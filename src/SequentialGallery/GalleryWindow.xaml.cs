using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SequentialGallery;

internal partial class GalleryWindow : Window
{
    private readonly Func<GrasshopperHost> createHost;
    private readonly TimeSpan? lastSolution;
    private GrasshopperHost? host;
    private Button[] cells = Array.Empty<Button>();
    private double[]?[] applied = Array.Empty<double[]?>();
    private SpsEngine? engine;
    private CancellationTokenSource? cancellation;
    private double[]? selected;
    private bool busy, closeRequested, stopped;
    private int plane = 1, zoom;
    public GalleryWindow(Func<GrasshopperHost> createHost, TimeSpan? lastSolution) {
        this.createHost = createHost;
        this.lastSolution = lastSolution;
        InitializeComponent();
        UpdateGridSummary();
        Closing += OnClosing;
        Closed += (_, _) => { engine?.Dispose(); host?.Dispose(); cancellation?.Dispose(); };
    }
    private void GridSizeChanged(object sender, SelectionChangedEventArgs e) => UpdateGridSummary();
    private void UpdateGridSummary() {
        if (RowsChoice?.SelectedItem is not ComboBoxItem row || ColumnsChoice?.SelectedItem is not ComboBoxItem column || GridSummary == null || TimeEstimate == null) return;
        int count = int.Parse((string)row.Content) * int.Parse((string)column.Content);
        GridSummary.Text = $"{count} candidate slots";
        TimeEstimate.Text = lastSolution is { TotalSeconds: > 0 } duration
            ? $"Last Grasshopper solution: {FormatDuration(duration.TotalSeconds)}. Estimated computation for {count} images: ~{FormatDuration(duration.TotalSeconds * count)}."
            : "Time estimate unavailable. Complete a Grasshopper solution, then reopen this window.";
    }
    private static string FormatDuration(double seconds) => seconds < 1 ? $"{seconds * 1000:F0} ms"
        : seconds < 60 ? $"{seconds:F1} s" : seconds < 3600 ? $"{seconds / 60:F1} min" : $"{seconds / 3600:F1} h";
    private async void StartClick(object sender, RoutedEventArgs e) {
        if (busy || host != null || stopped) return;
        int rows = int.Parse((string)((ComboBoxItem)RowsChoice.SelectedItem).Content);
        int columns = int.Parse((string)((ComboBoxItem)ColumnsChoice.SelectedItem).Content);
        Gallery.Rows = rows;
        Gallery.Columns = columns;
        cells = new Button[rows * columns];
        applied = new double[cells.Length][];
        Progress.Maximum = cells.Length;
        for (int i = 0; i < cells.Length; i++) {
            int index = i;
            cells[i] = new Button { Content = "Waiting", IsEnabled = false, Padding = new Thickness(2) };
            cells[i].Click += async (_, _) => await RunAsync(async token => {
                selected = (double[])applied[index]!.Clone();
                Status.Text = zoom == 3 ? "Learning preferences and constructing the next plane…" : "Zooming around your selection…";
                await Task.Run(() => engine!.Select(index));
                if (++zoom == 4) { zoom = 0; plane++; }
                token.ThrowIfCancellationRequested();
                await GenerateAsync(token);
            });
            Gallery.Children.Add(cells[i]);
        }
        await RunAsync(async token => {
            host = createHost();
            SetupPanel.Visibility = Visibility.Collapsed;
            Gallery.Visibility = GalleryInstructions.Visibility = Progress.Visibility
                = FinishButton.Visibility = SessionHint.Visibility = Visibility.Visible;
            PlaneLabel.Text = "Starting exploration";
            Status.Text = "Initializing Sequential Plane Search…";
            engine = await Task.Run(() => new SpsEngine(host.Dimensions, rows, columns));
            token.ThrowIfCancellationRequested();
            await GenerateAsync(token);
        });
    }
    private async Task GenerateAsync(CancellationToken token) {
        PlaneLabel.Text = $"Search plane {plane}   ·   Selection {zoom + 1} of 4";
        Progress.Value = 0;
        Array.Clear(applied);
        foreach (var cell in cells) { cell.Content = "Waiting"; cell.IsEnabled = false; }
        var parameters = await Task.Run(() => engine!.Grid());
        var watch = Stopwatch.StartNew();
        int outOfRange = 0;
        for (int i = 0; i < cells.Length; i++) {
            token.ThrowIfCancellationRequested();
            if (parameters[i].Any(v => !double.IsFinite(v) || v < 0 || v > 1)) {
                cells[i].Content = "Out of range";
                outOfRange++;
            } else {
                Status.Text = $"Generating candidate {i + 1} / {cells.Length}…";
                applied[i] = await host!.ApplyAsync(parameters[i], token);
                cells[i].Content = new Image { Source = host.Capture(), Stretch = Stretch.Uniform };
                cells[i].ToolTip = $"Candidate {i + 1}";
            }
            Progress.Value = i + 1;
        }
        Status.Text = $"Ready · {cells.Length - outOfRange} images · {outOfRange} out of range · {watch.Elapsed.TotalSeconds:F1}s. Select your preferred design.";
    }
    private async Task RunAsync(Func<CancellationToken, Task> operation) {
        if (busy) return;
        busy = true;
        UpdateButtons();
        cancellation?.Dispose();
        cancellation = new CancellationTokenSource();
        try { await operation(cancellation.Token); }
        catch (OperationCanceledException) { stopped = true; Status.Text = "Cancelled. Current slider values were kept."; }
        catch (Exception e) {
            stopped = true;
            Status.Text = "Stopped: " + e.Message;
            MessageBox.Show(this, e.Message, "Sequential Gallery", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally {
            busy = false;
            UpdateButtons();
            if (closeRequested) Close();
        }
    }
    private void UpdateButtons() {
        StartButton.IsEnabled = !busy && host == null && !stopped;
        RowsChoice.IsEnabled = ColumnsChoice.IsEnabled = StartButton.IsEnabled;
        FinishButton.IsEnabled = !busy && selected != null;
        CancelButton.Content = busy ? "Cancel generation" : "Close";
        for (int i = 0; i < cells.Length; i++) cells[i].IsEnabled = !busy && !stopped && applied[i] != null;
    }
    private async void FinishClick(object sender, RoutedEventArgs e) {
        await RunAsync(async token => {
            await host!.ApplyAsync(selected!, token);
            closeRequested = true;
        });
    }
    private void CancelClick(object sender, RoutedEventArgs e) {
        if (busy) cancellation?.Cancel(); else Close();
    }
    private void OnClosing(object? sender, CancelEventArgs e) {
        if (!busy) return;
        e.Cancel = true;
        closeRequested = true;
        cancellation?.Cancel();
        Status.Text = "Closing after the current computation finishes…";
    }
    internal void RequestClose() => Close();
}
