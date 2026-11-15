using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
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

internal partial class GeometryPane : UserControl {
    private sealed record GeometryNode(int Id, int? ParentId, int? Cell, int Plane, int Zoom, SpsEngine Snapshot,
        double[][] Coordinates, byte[]?[] Images, GeometryVariableRecord[]?[] Variables, BitmapSource Thumbnail);
    private readonly Func<IGeometryHost> createHost;
    private readonly TimeSpan? lastSolution;
    private readonly List<GeometryNode> history = new();
    private readonly List<GeometryCandidate> completedChoices = new();
    public IReadOnlyList<GeometryCandidate> CompletedChoices => completedChoices;
    private IGeometryHost? host;
    private Button[] cells = Array.Empty<Button>();
    private SpsEngine? engine;
    private CancellationTokenSource? cancellation;
    private GeometryCandidate? selectedCandidate;
    private bool busy, stopped, pendingPrepared, peerBusy, shutdown, released;
    private int plane = 1, zoom;
    private int? activeNodeId, pendingParent, pendingCell;

    public event Action? StateChanged;
    public event Action<GeometryCandidate>? HandoffRequested;
    public event Action? Finished;
    public bool IsBusy => busy;
    public bool CanResumeHistory => !busy && !shutdown && history.Count > 0;
    public GeometryCandidate? SelectedCandidate => selectedCandidate;
    public IReadOnlyList<HistoryItem> HistoryItems { get; private set; } = Array.Empty<HistoryItem>();
    public int? ActiveHistoryNodeId => activeNodeId;
    internal string? LastExportPath { get; private set; }

    public GeometryPane(Func<IGeometryHost> createHost, TimeSpan? lastSolution) {
        this.createHost = createHost;
        this.lastSolution = lastSolution;
        InitializeComponent();
        UpdateGridSummary();
        UpdateButtons();
    }
    public void SetPeerBusy(bool value) {
        if (peerBusy == value) return;
        peerBusy = value;
        UpdateButtons();
    }
    private void GridSizeChanged(object sender, SelectionChangedEventArgs e) => UpdateGridSummary();
    private void UpdateGridSummary() {
        if (RowsChoice?.SelectedItem is not ComboBoxItem row || ColumnsChoice?.SelectedItem is not ComboBoxItem column || GridSummary == null || TimeEstimate == null) return;
        int count = int.Parse((string)row.Content) * int.Parse((string)column.Content);
        GridSummary.Text = $"{count} slots";
        TimeEstimate.Text = lastSolution is { TotalSeconds: > 0 } duration
            ? $"Last solution: {FormatDuration(duration.TotalSeconds)}. Estimated computation: ~{FormatDuration(duration.TotalSeconds * count)}."
            : "Time estimate unavailable. Complete a Grasshopper solution, then reopen.";
    }
    private static string FormatDuration(double seconds) => seconds < 1 ? $"{seconds * 1000:F0} ms"
        : seconds < 60 ? $"{seconds:F1} s" : seconds < 3600 ? $"{seconds / 60:F1} min" : $"{seconds / 3600:F1} h";
    private async void StartClick(object sender, RoutedEventArgs e) {
        if (busy || host != null || stopped || shutdown) return;
        Gallery.Rows = int.Parse((string)((ComboBoxItem)RowsChoice.SelectedItem).Content);
        Gallery.Columns = int.Parse((string)((ComboBoxItem)ColumnsChoice.SelectedItem).Content);
        cells = new Button[Gallery.Rows * Gallery.Columns];
        Progress.Maximum = cells.Length;
        for (int i = 0; i < cells.Length; i++) {
            int index = i;
            cells[i] = new Button { Content = "Waiting", IsEnabled = false, Padding = new Thickness(2), BorderThickness = new Thickness(1) };
            cells[i].Click += (_, _) => SelectCandidate(index);
            Gallery.Children.Add(cells[i]);
        }
        await RunAsync(async token => {
            host = createHost();
            SetupPanel.Visibility = Visibility.Collapsed;
            Gallery.Visibility = GalleryInstructions.Visibility = Progress.Visibility = Visibility.Visible;
            PlaneLabel.Text = "Starting exploration";
            Status.Text = "Initializing Sequential Plane Search…";
            int dimensions = host.Dimensions, rows = Gallery.Rows, columns = Gallery.Columns;
            engine = await Task.Run(() => new SpsEngine(dimensions, rows, columns));
            pendingPrepared = true;
            token.ThrowIfCancellationRequested();
            await GenerateAsync(token);
        });
    }
    private void SelectCandidate(int index) {
        if (busy || stopped || shutdown || activeNodeId == null) return;
        var node = history[activeNodeId.Value];
        if (index < 0 || index >= node.Images.Length || node.Images[index] == null || node.Variables[index] == null) return;
        selectedCandidate = new GeometryCandidate(node.Id, index, Gallery.Rows, Gallery.Columns, node.Plane, node.Zoom + 1,
            node.Images[index]!, (GeometryVariableRecord[])node.Variables[index]!.Clone());
        UpdateButtons();
    }
    private async void NextClick(object sender, RoutedEventArgs e) {
        if (!NextButton.IsEnabled || selectedCandidate == null) return;
        var candidate = selectedCandidate;
        var existing = history.FirstOrDefault(node => node.ParentId == candidate.SourceNodeId && node.Cell == candidate.Cell);
        if (existing != null) { RecordChoice(candidate); RestoreNode(existing); return; }
        await RunAsync(async token => {
            pendingParent = candidate.SourceNodeId; pendingCell = candidate.Cell; pendingPrepared = false;
            Status.Text = zoom == 3 ? "Learning preferences and constructing the next plane…" : "Zooming around your selection…";
            await Task.Run(() => engine!.Select(candidate.Cell));
            RecordChoice(candidate);
            if (++zoom == 4) { zoom = 0; plane++; }
            pendingPrepared = true;
            token.ThrowIfCancellationRequested();
            await GenerateAsync(token);
        });
    }
    private void HandoffClick(object sender, RoutedEventArgs e) {
        if (HandoffButton.IsEnabled && selectedCandidate != null) HandoffRequested?.Invoke(selectedCandidate);
    }
    private void RecordChoice(GeometryCandidate candidate) {
        if (!completedChoices.Any(c => c.Key == candidate.Key)) completedChoices.Add(candidate);
    }
    private void ConfirmClick(object sender, RoutedEventArgs e) {
        if (!ConfirmButton.IsEnabled || selectedCandidate == null) return;
        RecordChoice(selectedCandidate);
        Status.Text = "Selection confirmed for final rendering. Choose its source node in History.";
        UpdateButtons();
    }
    private async Task GenerateAsync(CancellationToken token) {
        PlaneLabel.Text = $"Search plane {plane} · Selection {zoom + 1} of 4";
        Progress.Value = 0;
        foreach (var cell in cells) { cell.Content = "Waiting"; cell.IsEnabled = false; }
        var parameters = await Task.Run(() => engine!.Grid());
        var images = new BitmapSource?[cells.Length];
        var variableRecords = new GeometryVariableRecord[]?[cells.Length];
        var watch = Stopwatch.StartNew(); int outOfRange = 0;
        for (int i = 0; i < cells.Length; i++) {
            token.ThrowIfCancellationRequested();
            if (parameters[i].Any(v => !double.IsFinite(v) || v < 0 || v > 1)) {
                cells[i].Content = "Out of range"; outOfRange++;
            } else {
                Status.Text = $"Generating candidate {i + 1} / {cells.Length}…";
                await host!.ApplyAsync(parameters[i], token);
                images[i] = host.Capture();
                variableRecords[i] = host.GetVariables(parameters[i]);
                cells[i].Content = new Image { Source = images[i], Stretch = Stretch.Uniform };
                cells[i].ToolTip = $"Row {i / Gallery.Columns + 1}, column {i % Gallery.Columns + 1}";
            }
            Progress.Value = i + 1;
        }
        token.ThrowIfCancellationRequested();
        var node = new GeometryNode(history.Count, pendingParent, pendingCell, plane, zoom, engine!.Clone(), parameters,
            images.Select(image => image == null ? null : EncodeImage(image)).ToArray(), variableRecords, BuildThumbnail(images));
        history.Add(node); activeNodeId = node.Id; selectedCandidate = null;
        pendingPrepared = false; pendingParent = pendingCell = null; stopped = false;
        RefreshHistory();
        Status.Text = $"Ready · {cells.Length - outOfRange} images · {outOfRange} out of range · {watch.Elapsed.TotalSeconds:F1}s. Select a shape.";
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
                    drawing.DrawImage(image, new Rect(rectangle.X + (width - image.PixelWidth * scale) / 2,
                        rectangle.Y + (height - image.PixelHeight * scale) / 2, image.PixelWidth * scale, image.PixelHeight * scale));
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
        HistoryItems = history.Select(node => {
            string values = string.Join("\n", node.Variables.Select((records, i) =>
                $"R{i / Gallery.Columns + 1}C{i % Gallery.Columns + 1}: " + (records == null ? "Out of range" :
                    string.Join(", ", records.Select(v => $"{v.Name}={v.Value.ToString(CultureInfo.InvariantCulture)} ({v.SpsCoordinate.ToString("G6", CultureInfo.InvariantCulture)})")))));
            string details = $"{Gallery.Rows}×{Gallery.Columns} geometry candidates\nPlane {node.Plane}, step {node.Zoom + 1} of 4\n"
                + "Applied values (raw SPS coordinates in parentheses):\n" + values;
            return new HistoryItem(node.Id, node.ParentId, $"Plane {node.Plane} · Step {node.Zoom + 1}", node.Thumbnail,
                node.Cell is int cell ? $"R{cell / Gallery.Columns + 1} C{cell % Gallery.Columns + 1}" : "Start", details);
        }).ToArray();
    }
    public void ResumeHistory(int id) {
        if (!CanResumeHistory) return;
        var node = history.FirstOrDefault(node => node.Id == id);
        if (node != null) RestoreNode(node);
    }
    private void RestoreNode(GeometryNode node) {
        var restored = node.Snapshot.Clone(); engine?.Dispose(); engine = restored;
        plane = node.Plane; zoom = node.Zoom; selectedCandidate = null;
        activeNodeId = node.Id; pendingParent = pendingCell = null; pendingPrepared = false; stopped = false;
        DisplayNode(node);
        PlaneLabel.Text = $"Search plane {plane} · Selection {zoom + 1} of 4";
        Status.Text = "Restored saved candidates. No sliders were changed and no images regenerated.";
        UpdateButtons();
    }
    private void DisplayNode(GeometryNode node) {
        for (int i = 0; i < cells.Length; i++) cells[i].Content = node.Images[i] is byte[] bytes
            ? new Image { Source = DecodeImage(bytes), Stretch = Stretch.Uniform } : "Out of range";
        Progress.Value = cells.Length;
    }
    private async Task RunAsync(Func<CancellationToken, Task> operation, bool export = false) {
        if (busy || shutdown) return;
        busy = true; UpdateButtons(); cancellation?.Dispose(); cancellation = new CancellationTokenSource();
        try { await operation(cancellation.Token); }
        catch (OperationCanceledException) {
            if (!export) stopped = true;
            if (activeNodeId is int id) DisplayNode(history[id]);
            Status.Text = "Cancelled. Completed history remains available.";
        }
        catch (Exception e) {
            if (!export) stopped = true;
            if (activeNodeId is int id) DisplayNode(history[id]);
            Status.Text = (export ? "Could not save result: " : "Stopped: ") + e.Message;
        }
        finally { busy = false; if (shutdown) ReleaseResources(); UpdateButtons(); }
    }
    private void UpdateButtons() {
        StartButton.IsEnabled = !busy && !shutdown && host == null && !stopped;
        RowsChoice.IsEnabled = ColumnsChoice.IsEnabled = StartButton.IsEnabled;
        bool ready = !busy && !shutdown && !stopped && selectedCandidate != null;
        NextButton.Visibility = HandoffButton.Visibility = FinishButton.Visibility = host != null ? Visibility.Visible : Visibility.Collapsed;
        ConfirmButton.Visibility = NextButton.Visibility;
        ConfirmButton.IsEnabled = ready;
        NextButton.IsEnabled = ready;
        HandoffButton.IsEnabled = ready && !peerBusy;
        FinishButton.IsEnabled = !busy && !shutdown && !peerBusy && selectedCandidate != null;
        RetryButton.Visibility = stopped && pendingPrepared ? Visibility.Visible : Visibility.Collapsed;
        RetryButton.IsEnabled = !busy && !shutdown && pendingPrepared;
        CancelButton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        for (int i = 0; i < cells.Length; i++) {
            bool valid = activeNodeId is int id && history[id].Images[i] != null;
            cells[i].IsEnabled = !busy && !shutdown && !stopped && valid;
            bool selected = selectedCandidate is { } candidate && candidate.Cell == i && candidate.SourceNodeId == activeNodeId;
            cells[i].BorderBrush = selected ? Brushes.Orange : Brushes.SlateGray;
            cells[i].BorderThickness = new Thickness(selected ? 3 : 1);
        }
        StateChanged?.Invoke();
    }
    private async void RetryClick(object sender, RoutedEventArgs e) {
        if (!busy && pendingPrepared && !shutdown) await RunAsync(GenerateAsync);
    }
    private async void FinishClick(object sender, RoutedEventArgs e) {
        if (!FinishButton.IsEnabled || selectedCandidate == null) return;
        var candidate = selectedCandidate;
        await RunAsync(async token => {
            await host!.ApplyRecordedAsync(candidate.Variables, token);
            token.ThrowIfCancellationRequested();
            LastExportPath = ResultExport.Save("geometry", directory => WriteCandidateFiles(candidate, directory));
            Status.Text = "Saved result to " + LastExportPath;
            Finished?.Invoke();
        }, export: true);
    }
    public async Task ApplyCandidateAsync(GeometryCandidate candidate, CancellationToken token) {
        if (busy || shutdown || host == null) throw new InvalidOperationException("Wait for shape generation before finishing.");
        busy = true; UpdateButtons(); cancellation?.Dispose();
        cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        try { await host.ApplyRecordedAsync(candidate.Variables, cancellation.Token); }
        finally { busy = false; if (shutdown) ReleaseResources(); UpdateButtons(); }
    }
    public static void WriteCandidateFiles(GeometryCandidate candidate, string directory) {
        File.WriteAllBytes(Path.Combine(directory, "geometry.png"), candidate.ReferencePng);
        File.WriteAllText(Path.Combine(directory, "parameters.json"), JsonSerializer.Serialize(new {
            schemaVersion = 1, mode = "geometry", savedAt = DateTimeOffset.Now,
            sourceHistoryNode = candidate.SourceNodeId, searchPlane = candidate.Plane, selectionStage = candidate.Step,
            selectedRow = candidate.Cell / candidate.Columns + 1, selectedColumn = candidate.Cell % candidate.Columns + 1,
            gridRows = candidate.Rows, gridColumns = candidate.Columns, variables = candidate.Variables
        }, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    }
    private void CancelClick(object sender, RoutedEventArgs e) => Cancel();
    public void Cancel() => cancellation?.Cancel();
    public void Shutdown() {
        if (shutdown) return;
        shutdown = true; cancellation?.Cancel();
        if (!busy) ReleaseResources();
        UpdateButtons();
    }
    private void ReleaseResources() {
        if (released) return;
        released = true; engine?.Dispose(); engine = null;
        foreach (var node in history) node.Snapshot.Dispose();
        history.Clear(); HistoryItems = Array.Empty<HistoryItem>(); activeNodeId = null; selectedCandidate = null;
        completedChoices.Clear();
        host?.Dispose(); host = null; cancellation?.Dispose(); cancellation = null;
    }
}
