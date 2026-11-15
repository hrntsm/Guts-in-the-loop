using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace SequentialGallery;

internal partial class RenderingWindow : Window {
    private readonly Func<byte[]> captureReference;
    private readonly Func<RenderingApi> createApi;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly Stopwatch elapsed = new();
    private CheckBox[] variables = Array.Empty<CheckBox>();
    private Button[] cells = Array.Empty<Button>();
    private RenderingSession? session;
    private RenderingApi? api;
    private byte[]? reference;
    private BitmapSource? displayedImage, finalCrop;
    private CancellationTokenSource? cancellation;
    private bool busy, closed, final;
    private int selected = -1;
    private string activity = "Generating matrix";

    public RenderingWindow(Func<byte[]> captureReference, Func<RenderingApi>? createApi = null) {
        this.captureReference = captureReference;
        this.createApi = createApi ?? (() => new RenderingApi(Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? ""));
        InitializeComponent();
        InitialPrompt.Text = RenderingSession.DefaultInitialPrompt;
        variables = RenderingSession.Variables.Select(variable => {
            var option = new CheckBox {
                Content = $"{variable.Name}   ·   {variable.Anchors[0]} → {variable.Anchors[^1]}",
                IsChecked = true, Foreground = Brushes.White, Margin = new Thickness(0, 5, 0, 5)
            };
            option.Checked += (_, _) => UpdateControls();
            option.Unchecked += (_, _) => UpdateControls();
            VariableOptions.Children.Add(option);
            return option;
        }).ToArray();
        timer.Tick += (_, _) => Status.Text = $"{activity} · {elapsed.Elapsed.TotalSeconds:F0}s elapsed. Cancel stops waiting; API usage may still apply.";
        Closed += (_, _) => {
            closed = true;
            cancellation?.Cancel();
            timer.Stop();
            if (!busy) DisposeResources();
        };
        UpdateControls();
    }
    private int Rows => int.Parse((string)((ComboBoxItem)RowsChoice.SelectedItem).Content);
    private int Columns => int.Parse((string)((ComboBoxItem)ColumnsChoice.SelectedItem).Content);
    private void SetupChanged(object sender, SelectionChangedEventArgs e) {
        if (GridSummary != null && RowsChoice?.SelectedItem != null && ColumnsChoice?.SelectedItem != null) UpdateControls();
    }
    private void UpdateControls() {
        bool ready = !busy && !closed;
        StartButton.IsEnabled = ready && session == null && variables.Count(v => v.IsChecked == true) >= 2;
        GridSummary.Text = $"{Rows * Columns} candidate slots · one image";
        bool hasImage = session?.Displayed != null && displayedImage != null;
        bool canExplore = ready && !final && hasImage && session?.Pending == null && session?.Faulted == false;
        NextButton.IsEnabled = canExplore && selected >= 0;
        RegenerateButton.IsEnabled = canExplore;
        RetryButton.IsEnabled = ready && !final && session?.Pending != null && session.Faulted == false;
        RetryButton.Visibility = session?.Pending != null && !final ? Visibility.Visible : Visibility.Collapsed;
        FinishButton.IsEnabled = ready && !final && hasImage && selected >= 0;
        CancelButton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        BusyProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        foreach (var variable in variables) variable.IsEnabled = ready && session == null;
        RowsChoice.IsEnabled = ColumnsChoice.IsEnabled = ready && session == null;
        InitialPrompt.IsReadOnly = busy || session != null || final;
        for (int i = 0; i < cells.Length; i++) {
            bool valid = session?.Displayed?.Valid[i] == true;
            cells[i].IsEnabled = canExplore && valid;
            cells[i].Background = valid ? Brushes.Transparent : new SolidColorBrush(Color.FromArgb(245, 35, 43, 54));
            cells[i].Content = hasImage && !valid ? "Out of range" : null;
            cells[i].BorderBrush = i == selected ? Brushes.Orange : new SolidColorBrush(Color.FromArgb(150, 255, 255, 255));
            cells[i].BorderThickness = new Thickness(i == selected ? 6 : 1);
        }
    }
    private async void StartClick(object sender, RoutedEventArgs e) {
        if (busy || session != null || variables.Count(v => v.IsChecked == true) < 2) return;
        int rows = Rows, columns = Columns;
        string initialPrompt = InitialPrompt.Text;
        int[] indices = variables.Select((v, i) => (v, i)).Where(x => x.v.IsChecked == true).Select(x => x.i).ToArray();
        await RunAsync(async token => {
            activity = "Initializing rendering exploration";
            api = createApi();
            reference = captureReference();
            session = await Task.Run(() => new RenderingSession(rows, columns, indices, initialPrompt));
            if (closed) token.ThrowIfCancellationRequested();
            SetupPanel.Visibility = Visibility.Collapsed;
            MatrixView.Visibility = VariableSummary.Visibility = Visibility.Visible;
            NextButton.Visibility = RegenerateButton.Visibility = FinishButton.Visibility = Visibility.Visible;
            VariableSummary.Text = string.Join(" · ", indices.Select(i => RenderingSession.Variables[i].Name));
            CellOverlay.Rows = rows;
            CellOverlay.Columns = columns;
            cells = Enumerable.Range(0, rows * columns).Select(i => {
                var cell = new Button { Style = (Style)FindResource("CellStyle"), IsEnabled = false, ToolTip = $"Row {i / columns + 1}, column {i % columns + 1}" };
                cell.Click += (_, _) => { selected = i; UpdateControls(); };
                CellOverlay.Children.Add(cell);
                return cell;
            }).ToArray();
            token.ThrowIfCancellationRequested();
            await GenerateAsync(token);
        });
    }
    private async void NextClick(object sender, RoutedEventArgs e) {
        if (!NextButton.IsEnabled) return;
        int index = selected;
        await RunAsync(async token => {
            activity = "Updating the search plane";
            await Task.Run(() => session!.Advance(index));
            token.ThrowIfCancellationRequested();
            await GenerateAsync(token);
        });
    }
    private async void RegenerateClick(object sender, RoutedEventArgs e) {
        if (!RegenerateButton.IsEnabled) return;
        await RunAsync(async token => {
            session!.Regenerate();
            await GenerateAsync(token);
        });
    }
    private async void RetryClick(object sender, RoutedEventArgs e) {
        if (RetryButton.IsEnabled) await RunAsync(GenerateAsync);
    }
    private async Task GenerateAsync(CancellationToken token) {
        var pending = session!.Pending ?? throw new InvalidOperationException("No generation is pending.");
        activity = "Generating matrix";
        var bytes = await api!.GenerateAsync(reference!, pending.Prompt, token).WaitAsync(token);
        token.ThrowIfCancellationRequested();
        var image = Decode(bytes);
        if (image.PixelWidth != 1024 || image.PixelHeight != 1024)
            throw new InvalidOperationException("The API returned an unexpected image size. Expected 1024x1024. Retry the same conditions.");
        session.Accept(pending);
        displayedImage = image;
        MatrixImage.Source = image;
        selected = -1;
        StageLabel.Text = $"Search plane {pending.Plane} · Selection {pending.SelectionStage} of 4";
        Status.Text = $"Ready · {pending.Valid.Count(v => v)} valid cells · {elapsed.Elapsed.TotalSeconds:F1}s. Select a cell, then Next or Finish. Use Regenerate if the layout is unsuitable.";
    }
    private async Task RunAsync(Func<CancellationToken, Task> operation) {
        if (busy || closed || final) return;
        busy = true;
        cancellation?.Dispose();
        cancellation = new CancellationTokenSource();
        elapsed.Restart();
        timer.Start();
        UpdateControls();
        try { await operation(cancellation.Token); }
        catch (OperationCanceledException) {
            Status.Text = cancellation.IsCancellationRequested
                ? "Generation cancelled. The previous image was kept. Retry uses the same pending conditions."
                : "Image request timed out. Retry uses the same pending conditions.";
        }
        catch (Exception e) {
            Status.Text = session?.Faulted == true ? "Search update failed. Finish the previous selection or close and reopen. " + e.Message
                : "Stopped: " + e.Message + (session?.Pending != null ? " Retry keeps the same candidate conditions." : "");
        }
        finally {
            timer.Stop();
            busy = false;
            if (session == null) { api?.Dispose(); api = null; reference = null; }
            if (closed) DisposeResources(); else UpdateControls();
        }
    }
    internal static BitmapSource Decode(byte[] bytes) {
        using var stream = new MemoryStream(bytes);
        var image = new BitmapImage();
        image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = stream; image.EndInit(); image.Freeze();
        return image;
    }
    internal static Int32Rect CellBounds(int width, int height, int rows, int columns, int index) {
        int row = index / columns, column = index % columns;
        int x = column * width / columns, y = row * height / rows;
        return new Int32Rect(x, y, (column + 1) * width / columns - x, (row + 1) * height / rows - y);
    }
    private void FinishClick(object sender, RoutedEventArgs e) {
        if (!FinishButton.IsEnabled) return;
        finalCrop = new CroppedBitmap(displayedImage!, CellBounds(displayedImage!.PixelWidth, displayedImage.PixelHeight, session!.Rows, session.Columns, selected));
        finalCrop.Freeze();
        final = true;
        MatrixView.Visibility = NextButton.Visibility = RegenerateButton.Visibility = RetryButton.Visibility = FinishButton.Visibility = Visibility.Collapsed;
        FinalImage.Source = finalCrop;
        FinalImage.Visibility = SaveButton.Visibility = Visibility.Visible;
        StageLabel.Text = "Final rendering preview";
        Status.Text = $"Selected crop · {finalCrop.PixelWidth}×{finalCrop.PixelHeight} pixels. Save PNG keeps the original generated pixels.";
        session.Dispose(); session = null;
        api?.Dispose(); api = null;
        reference = null;
        UpdateControls();
    }
    private void SaveClick(object sender, RoutedEventArgs e) {
        if (finalCrop == null) return;
        var dialog = new SaveFileDialog { Filter = "PNG image (*.png)|*.png", DefaultExt = ".png", FileName = "Rendering.png", AddExtension = true };
        if (dialog.ShowDialog(this) != true) return;
        try {
            SavePng(finalCrop, dialog.FileName);
            Status.Text = "Saved selected rendering as PNG.";
        } catch (Exception error) { Status.Text = "Could not save PNG: " + error.Message; }
    }
    internal static void SavePng(BitmapSource image, string path) {
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(path); encoder.Save(stream);
    }
    private void CancelClick(object sender, RoutedEventArgs e) => cancellation?.Cancel();
    private void CloseClick(object sender, RoutedEventArgs e) => Close();
    private void DisposeResources() { timer.Stop(); session?.Dispose(); session = null; api?.Dispose(); api = null; cancellation?.Dispose(); reference = null; }
}
