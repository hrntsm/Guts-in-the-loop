using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SequentialGallery;

internal sealed record GeometryVariableRecord(string Name, Guid InstanceId, decimal Value, decimal Minimum, decimal Maximum,
    string NumericType, int Precision, double SpsCoordinate, double AppliedCoordinate);

internal interface IGeometryHost : IDisposable {
    int Dimensions { get; }
    Task<double[]> ApplyAsync(double[] normalized, CancellationToken cancellation);
    Task<double[]> ApplyRecordedAsync(GeometryVariableRecord[] variables, CancellationToken cancellation);
    BitmapSource Capture();
    GeometryVariableRecord[] GetVariables(double[] coordinates);
}

internal partial class GalleryWindow : Window {
    private sealed record GeometryNode(int Id, int? ParentId, int? Cell, int Plane, int Zoom, SpsEngine Snapshot,
        double[][] Coordinates, double[]?[] Applied, byte[]?[] Images, GeometryVariableRecord[]?[] Variables, BitmapSource Thumbnail,
        double[]? Selected, double[]? SelectedCoordinates, GeometryVariableRecord[]? SelectedVariables);
    private readonly Func<IGeometryHost> createHost;
    private readonly TimeSpan? lastSolution;
    private readonly List<GeometryNode> history = new();
    private IGeometryHost? host;
    private Button[] cells = Array.Empty<Button>();
    private double[]?[] applied = Array.Empty<double[]?>();
    private SpsEngine? engine;
    private CancellationTokenSource? cancellation;
    private double[]? selected, selectedCoordinates;
    private GeometryVariableRecord[]? selectedVariables;
    private bool busy, closeRequested, stopped, pendingPrepared;
    private int plane = 1, zoom;
    private int? activeNodeId, pendingParent, pendingCell;
    private int? selectedSourceNodeId, selectedSourceCell;
    internal string? LastExportPath { get; private set; }

    public GalleryWindow(Func<IGeometryHost> createHost, TimeSpan? lastSolution) {
        this.createHost = createHost;
        this.lastSolution = lastSolution;
        InitializeComponent();
        History.ResumeRequested += ResumeHistory;
        UpdateGridSummary();
        Closing += OnClosing;
        Closed += (_, _) => {
            engine?.Dispose(); foreach (var node in history) node.Snapshot.Dispose();
            host?.Dispose(); cancellation?.Dispose(); history.Clear();
        };
    }
    private void GridSizeChanged(object sender, SelectionChangedEventArgs e) => UpdateGridSummary();
    private void ModeChanged(object sender, SelectionChangedEventArgs e) {
        if (e.Source == ExplorationTabs && FinishButton != null) UpdateButtons();
    }
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
        Gallery.Rows = rows; Gallery.Columns = columns;
        cells = new Button[rows * columns]; applied = new double[cells.Length][];
        Progress.Maximum = cells.Length;
        for (int i = 0; i < cells.Length; i++) {
            int index = i;
            cells[i] = new Button { Content = "Waiting", IsEnabled = false, Padding = new Thickness(2) };
            cells[i].Click += async (_, _) => await SelectCandidateAsync(index);
            Gallery.Children.Add(cells[i]);
        }
        await RunAsync(async token => {
            host = createHost(); SetupPanel.Visibility = Visibility.Collapsed;
            ExplorationTabs.Visibility = GalleryInstructions.Visibility = Progress.Visibility
                = FinishButton.Visibility = SessionHint.Visibility = Visibility.Visible;
            PlaneLabel.Text = "Starting exploration";
            Status.Text = "Initializing Sequential Plane Search…";
            engine = await Task.Run(() => new SpsEngine(host.Dimensions, rows, columns));
            pendingPrepared = true;
            token.ThrowIfCancellationRequested();
            await GenerateAsync(token);
        });
    }
    private async Task SelectCandidateAsync(int index) {
        if (busy || stopped || activeNodeId == null || applied[index] == null) return;
        var source = history[activeNodeId.Value];
        var existing = history.FirstOrDefault(node => node.ParentId == source.Id && node.Cell == index);
        if (existing != null) { RestoreNode(existing); return; }
        await RunAsync(async token => {
            selected = (double[])source.Applied[index]!.Clone();
            selectedCoordinates = (double[])source.Coordinates[index].Clone();
            selectedVariables = source.Variables[index];
            selectedSourceNodeId = source.Id; selectedSourceCell = index;
            pendingParent = source.Id; pendingCell = index; pendingPrepared = false;
            Status.Text = zoom == 3 ? "Learning preferences and constructing the next plane…" : "Zooming around your selection…";
            await Task.Run(() => engine!.Select(index));
            if (++zoom == 4) { zoom = 0; plane++; }
            pendingPrepared = true;
            token.ThrowIfCancellationRequested();
            await GenerateAsync(token);
        });
    }
    private async Task GenerateAsync(CancellationToken token) {
        PlaneLabel.Text = $"Search plane {plane}   ·   Selection {zoom + 1} of 4";
        Progress.Value = 0;
        foreach (var cell in cells) { cell.Content = "Waiting"; cell.IsEnabled = false; }
        var parameters = await Task.Run(() => engine!.Grid());
        var nextApplied = new double[]?[cells.Length];
        var images = new BitmapSource?[cells.Length];
        var variableRecords = new GeometryVariableRecord[]?[cells.Length];
        var watch = Stopwatch.StartNew(); int outOfRange = 0;
        for (int i = 0; i < cells.Length; i++) {
            token.ThrowIfCancellationRequested();
            if (parameters[i].Any(v => !double.IsFinite(v) || v < 0 || v > 1)) {
                cells[i].Content = "Out of range"; outOfRange++;
            } else {
                Status.Text = $"Generating candidate {i + 1} / {cells.Length}…";
                nextApplied[i] = await host!.ApplyAsync(parameters[i], token);
                images[i] = host.Capture();
                variableRecords[i] = host.GetVariables(parameters[i]);
                cells[i].Content = new Image { Source = images[i], Stretch = Stretch.Uniform };
                cells[i].ToolTip = $"Candidate {i + 1}";
            }
            Progress.Value = i + 1;
        }
        token.ThrowIfCancellationRequested();
        var node = new GeometryNode(history.Count, pendingParent, pendingCell, plane, zoom, engine!.Clone(),
            parameters, nextApplied, images.Select(image => image == null ? null : EncodeImage(image)).ToArray(), variableRecords,
            BuildThumbnail(images), selected == null ? null : (double[])selected.Clone(),
            selectedCoordinates == null ? null : (double[])selectedCoordinates.Clone(), selectedVariables);
        history.Add(node); activeNodeId = node.Id; applied = nextApplied;
        pendingPrepared = false; pendingParent = pendingCell = null;
        stopped = false; RefreshHistory();
        Status.Text = $"Ready · {cells.Length - outOfRange} images · {outOfRange} out of range · {watch.Elapsed.TotalSeconds:F1}s. Select your preferred design.";
    }
    private BitmapSource BuildThumbnail(BitmapSource?[] images) {
        const int size = 600;
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen()) {
            drawing.DrawRectangle(new SolidColorBrush(Color.FromRgb(34, 43, 54)), null, new Rect(0, 0, size, size));
            double width = (double)size / Gallery.Columns, height = (double)size / Gallery.Rows;
            for (int i = 0; i < images.Length; i++) {
                var rectangle = new Rect(i % Gallery.Columns * width, i / Gallery.Columns * height, width, height);
                if (images[i] is BitmapSource image) {
                    double scale = Math.Min(width / image.PixelWidth, height / image.PixelHeight);
                    var target = new Rect(rectangle.X + (width - image.PixelWidth * scale) / 2, rectangle.Y + (height - image.PixelHeight * scale) / 2, image.PixelWidth * scale, image.PixelHeight * scale);
                    drawing.DrawImage(image, target);
                }
                drawing.DrawRectangle(null, new Pen(Brushes.SlateGray, 1), rectangle);
            }
        }
        var thumbnail = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32); thumbnail.Render(visual); thumbnail.Freeze(); return thumbnail;
    }
    private static byte[] EncodeImage(BitmapSource image) {
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = new MemoryStream(); encoder.Save(stream); return stream.ToArray();
    }
    private static BitmapSource DecodeImage(byte[] bytes) {
        using var stream = new MemoryStream(bytes);
        var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream; image.EndInit(); image.Freeze(); return image;
    }
    private void RefreshHistory() {
        var items = history.Select(node => {
            string values = string.Join("\n", node.Variables.Select((records, i) =>
                $"R{i / Gallery.Columns + 1}C{i % Gallery.Columns + 1}: " + (records == null ? "Out of range" :
                    string.Join(", ", records.Select(v => $"{v.Name}={v.Value} ({v.SpsCoordinate.ToString("G6", System.Globalization.CultureInfo.InvariantCulture)})")))));
            string details = $"{Gallery.Rows}×{Gallery.Columns} geometry candidates\nSearch plane {node.Plane}, selection {node.Zoom + 1} of 4\n"
                + $"{node.Applied.Count(v => v != null)} valid candidates\n"
                + "Resume restores this step without changing sliders or recapturing the grid.\n"
                + "Applied values (raw SPS coordinates in parentheses):\n" + values;
            return new HistoryItem(node.Id, node.ParentId, $"Plane {node.Plane} · Step {node.Zoom + 1}", node.Thumbnail,
                node.Cell is int cell ? $"R{cell / Gallery.Columns + 1} C{cell % Gallery.Columns + 1}" : "Start", details);
        });
        History.SetItems(items, activeNodeId, !busy && history.Count > 0);
    }
    private void ResumeHistory(int id) {
        if (busy) return;
        RestoreNode(history.Single(node => node.Id == id));
    }
    private void RestoreNode(GeometryNode node) {
        var restored = node.Snapshot.Clone(); engine?.Dispose(); engine = restored;
        plane = node.Plane; zoom = node.Zoom; applied = node.Applied;
        selected = node.Selected == null ? null : (double[])node.Selected.Clone();
        selectedCoordinates = node.SelectedCoordinates == null ? null : (double[])node.SelectedCoordinates.Clone();
        selectedVariables = node.SelectedVariables;
        selectedSourceNodeId = node.ParentId; selectedSourceCell = node.Cell;
        activeNodeId = node.Id; pendingParent = pendingCell = null; pendingPrepared = false; stopped = false;
        for (int i = 0; i < cells.Length; i++) cells[i].Content = node.Images[i] is byte[] bytes ? new Image { Source = DecodeImage(bytes), Stretch = Stretch.Uniform } : "Out of range";
        Progress.Value = cells.Length;
        PlaneLabel.Text = $"Search plane {plane}   ·   Selection {zoom + 1} of 4";
        Status.Text = "Restored saved candidates and optimizer state. No images were regenerated.";
        ExplorationTabs.SelectedIndex = 0; UpdateButtons();
    }
    private async Task RunAsync(Func<CancellationToken, Task> operation, bool export = false) {
        if (busy) return;
        busy = true; UpdateButtons(); cancellation?.Dispose(); cancellation = new CancellationTokenSource();
        try { await operation(cancellation.Token); }
        catch (OperationCanceledException) { if (!export) stopped = true; Status.Text = "Cancelled. Completed history is retained; retry or resume a history node."; }
        catch (Exception e) { if (!export) stopped = true; Status.Text = (export ? "Could not save result: " : "Stopped: ") + e.Message; }
        finally { busy = false; UpdateButtons(); if (closeRequested) Close(); }
    }
    private void UpdateButtons() {
        StartButton.IsEnabled = !busy && host == null && !stopped;
        RowsChoice.IsEnabled = ColumnsChoice.IsEnabled = StartButton.IsEnabled;
        bool evaluate = ExplorationTabs.SelectedIndex == 0;
        FinishButton.Visibility = host != null && evaluate ? Visibility.Visible : Visibility.Collapsed;
        FinishButton.IsEnabled = evaluate && !busy && selected != null;
        RetryButton.Visibility = evaluate && stopped && pendingPrepared ? Visibility.Visible : Visibility.Collapsed;
        RetryButton.IsEnabled = !busy && pendingPrepared;
        CancelButton.Content = busy ? "Cancel generation" : "Close";
        for (int i = 0; i < cells.Length; i++) cells[i].IsEnabled = !busy && !stopped && applied[i] != null;
        RefreshHistory();
    }
    private async void RetryClick(object sender, RoutedEventArgs e) {
        if (!busy && pendingPrepared) await RunAsync(GenerateAsync);
    }
    private async void FinishClick(object sender, RoutedEventArgs e) {
        if (!FinishButton.IsEnabled) return;
        await RunAsync(async token => {
            await host!.ApplyRecordedAsync(selectedVariables!, token);
            var image = host.Capture();
            var variables = host.GetVariables(selectedCoordinates!);
            var source = history[selectedSourceNodeId!.Value];
            token.ThrowIfCancellationRequested();
            LastExportPath = ResultExport.Save("geometry", directory => {
                ResultExport.SavePng(image, Path.Combine(directory, "geometry.png"));
                File.WriteAllText(Path.Combine(directory, "parameters.json"), JsonSerializer.Serialize(new {
                    schemaVersion = 1, mode = "geometry", savedAt = DateTimeOffset.Now,
                    sourceHistoryNode = source.Id, searchPlane = source.Plane, selectionStage = source.Zoom + 1,
                    selectedRow = selectedSourceCell!.Value / Gallery.Columns + 1,
                    selectedColumn = selectedSourceCell.Value % Gallery.Columns + 1,
                    gridRows = Gallery.Rows, gridColumns = Gallery.Columns, variables
                }, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            });
            Status.Text = "Saved result to " + LastExportPath; closeRequested = true;
        }, export: true);
    }
    private void CancelClick(object sender, RoutedEventArgs e) { if (busy) cancellation?.Cancel(); else Close(); }
    private void OnClosing(object? sender, CancelEventArgs e) {
        if (!busy) return;
        e.Cancel = true; closeRequested = true; cancellation?.Cancel(); Status.Text = "Closing after the current computation finishes…";
    }
    internal void RequestClose() => Close();
}
