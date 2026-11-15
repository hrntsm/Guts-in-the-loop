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
using System.Windows.Threading;

namespace SequentialGallery;

internal sealed record RenderingHistoryNode(int Id, int? ParentId, int? Cell, string EdgeLabel, RenderingState State, BitmapSource Image, string RunKey);

internal partial class RenderingPane : UserControl {
    private sealed class RenderingRun(string key, GeometryCandidate? source, byte[] reference, RenderingSession session) {
        public string Key { get; } = key;
        public GeometryCandidate? Source { get; } = source;
        public byte[] Reference { get; } = reference;
        public RenderingSession Session { get; } = session;
        public RenderingHistoryNode? ActiveNode { get; set; }
        public int? PendingParent { get; set; }
        public int? PendingCell { get; set; }
        public string PendingEdge { get; set; } = "";
    }
    private sealed record RenderingConfiguration(int Rows, int Columns, int[] Indices, double[] FixedValues, string Initial, string Negative);
    private readonly Func<byte[]> captureReference;
    private readonly Func<RenderingApi> createApi;
    private readonly bool integratedMode;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly Stopwatch elapsed = new();
    private readonly List<RenderingHistoryNode> history = new();
    private readonly List<RenderingChoice> completedChoices = new();
    public IReadOnlyList<RenderingChoice> CompletedChoices => completedChoices;
    internal RenderingApi CreateFinalApi() => createApi();
    private readonly Dictionary<string, RenderingRun> runs = new();
    private RenderingRun? currentRun;
    private RenderingConfiguration? configuration;
    private RenderingSession? session => currentRun?.Session;
    private byte[]? reference => currentRun?.Reference;
    private RenderingHistoryNode? activeNode { get => currentRun?.ActiveNode; set { if (currentRun != null) currentRun.ActiveNode = value; } }
    private int? pendingParentId { get => currentRun?.PendingParent; set { if (currentRun != null) currentRun.PendingParent = value; } }
    private int? pendingCell { get => currentRun?.PendingCell; set { if (currentRun != null) currentRun.PendingCell = value; } }
    private string pendingEdge { get => currentRun?.PendingEdge ?? ""; set { if (currentRun != null) currentRun.PendingEdge = value; } }
    private CheckBox[] variables = Array.Empty<CheckBox>();
    private ComboBox[] fixedValueChoices = Array.Empty<ComboBox>();
    private Button[] cells = Array.Empty<Button>();
    private Button[] promptCells = Array.Empty<Button>();
    private RenderingApi? api;
    private BitmapSource? displayedImage;
    private RenderingBatch? displayedBatch, inspectorBatch;
    private string frozenInitialPrompt = "", frozenNegativePrompt = "";
    private int[] exploredVariables = Array.Empty<int>();
    private bool showingPrompt, busy, closed, peerBusy, disposed;
    private CancellationTokenSource? cancellation;
    private int selected = -1;
    private string activity = "Generating matrix";
    public event Action? StateChanged;
    public event Action? CloseRequested;
    public bool IsBusy => busy;
    public bool CanResume => !busy && !closed && history.Count > 0;
    public int? ActiveHistoryNodeId => activeNode?.Id;
    public GeometryCandidate? CurrentSource => currentRun?.Source;
    public IReadOnlyDictionary<int, GeometryCandidate?> HistorySources => history.ToDictionary(node => node.Id, node => runs[node.RunKey].Source);
    public IReadOnlyList<HistoryItem> HistoryItems => history.Select(node => new HistoryItem(node.Id, node.ParentId,
        $"Plane {node.State.Batch.Plane} · Step {node.State.Batch.SelectionStage}", node.Image, node.EdgeLabel, HistoryDetails(node))).ToArray();

    public RenderingPane(Func<byte[]> captureReference, Func<RenderingApi>? createApi = null, bool integratedMode = false) {
        this.captureReference = captureReference;
        this.createApi = createApi ?? (() => new RenderingApi(Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? ""));
        this.integratedMode = integratedMode;
        InitializeComponent();
        InitialPrompt.Text = RenderingSession.DefaultInitialPrompt;
        NegativePrompt.Text = RenderingSession.DefaultNegativePrompt;
        SourcePanel.Visibility = integratedMode ? Visibility.Visible : Visibility.Collapsed;
        StartButton.Visibility = integratedMode ? Visibility.Collapsed : Visibility.Visible;
        if (integratedMode) {
            StartHint.Text = "Prepare the AI settings, then select a geometry candidate on the left and press Use this shape. The first handoff freezes these settings for all shapes.";
            Status.Text = "Waiting for a shape. Geometry selection alone does not generate an image.";
        }
        fixedValueChoices = RenderingSession.Variables.Select(variable => new ComboBox {
            ItemsSource = variable.Anchors, SelectedIndex = variable.Anchors.Length / 2,
            Width = 220, Margin = new Thickness(8, 3, 0, 3), IsEnabled = false,
            ToolTip = "Fixed value when this variable is not explored."
        }).ToArray();
        variables = RenderingSession.Variables.Select((variable, i) => {
            var option = new CheckBox { Content = variable.Name,
                ToolTip = $"Explore {variable.Anchors[0]} → {variable.Anchors[^1]}", IsChecked = true,
                Foreground = Brushes.White, Margin = new Thickness(0, 5, 12, 5), VerticalAlignment = VerticalAlignment.Center };
            option.Checked += (_, _) => UpdateControls(); option.Unchecked += (_, _) => UpdateControls();
            var row = new DockPanel(); DockPanel.SetDock(fixedValueChoices[i], Dock.Right);
            row.Children.Add(fixedValueChoices[i]); row.Children.Add(option); VariableOptions.Children.Add(row);
            return option;
        }).ToArray();
        timer.Tick += (_, _) => Status.Text = $"{activity} · {elapsed.Elapsed.TotalSeconds:F0}s elapsed. Cancel stops waiting; API usage may still apply.";
        UpdateControls();
    }
    private int Rows => int.Parse((string)((ComboBoxItem)RowsChoice.SelectedItem).Content);
    private int Columns => int.Parse((string)((ComboBoxItem)ColumnsChoice.SelectedItem).Content);
    private void SetupChanged(object sender, SelectionChangedEventArgs e) {
        if (GridSummary != null && RowsChoice?.SelectedItem != null && ColumnsChoice?.SelectedItem != null) UpdateControls();
    }
    public void SetPeerBusy(bool value) { if (peerBusy == value) return; peerBusy = value; UpdateControls(); }
    private void UpdateControls() {
        bool ready = !busy && !closed;
        bool configurable = ready && configuration == null;
        StartButton.IsEnabled = !integratedMode && configurable && variables.Count(v => v.IsChecked == true) >= 2;
        GridSummary.Text = $"{Rows * Columns} candidate slots · one image";
        bool hasImage = session?.Displayed != null && displayedImage != null;
        bool canExplore = ready && hasImage && session?.Pending == null && session?.Faulted == false;
        NextButton.IsEnabled = canExplore && selected >= 0;
        RegenerateButton.IsEnabled = canExplore;
        RetryButton.IsEnabled = ready && session?.Pending != null && session.Faulted == false;
        RetryButton.Visibility = session?.Pending != null ? Visibility.Visible : Visibility.Collapsed;
        ConfirmButton.IsEnabled = canExplore && selected >= 0;
        CancelButton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        BusyProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        for (int i = 0; i < variables.Length; i++) {
            variables[i].IsEnabled = configurable;
            fixedValueChoices[i].IsEnabled = configurable && variables[i].IsChecked != true;
        }
        RowsChoice.IsEnabled = ColumnsChoice.IsEnabled = configurable;
        InitialPrompt.IsReadOnly = NegativePrompt.IsReadOnly = !configurable;
        ShowPromptButton.IsEnabled = displayedBatch != null || session?.Pending != null;
        for (int i = 0; i < cells.Length; i++) {
            bool valid = session?.Displayed?.Valid[i] == true;
            cells[i].IsEnabled = canExplore && valid;
            cells[i].Background = valid ? Brushes.Transparent : new SolidColorBrush(Color.FromArgb(245, 35, 43, 54));
            cells[i].Content = hasImage && !valid ? "Out of range" : null;
            cells[i].BorderBrush = i == selected ? Brushes.Orange : new SolidColorBrush(Color.FromArgb(150, 255, 255, 255));
            cells[i].BorderThickness = new Thickness(i == selected ? 6 : 1);
        }
        UpdatePromptInspector();
        StateChanged?.Invoke();
    }
    private RenderingConfiguration ReadConfiguration() {
        var indices = variables.Select((v, i) => (v, i)).Where(x => x.v.IsChecked == true).Select(x => x.i).ToArray();
        if (indices.Length < 2) throw new InvalidOperationException("Select at least two rendering variables before using a shape.");
        return new RenderingConfiguration(Rows, Columns, indices,
            fixedValueChoices.Select((choice, i) => (double)choice.SelectedIndex / (RenderingSession.Variables[i].Anchors.Length - 1)).ToArray(),
            InitialPrompt.Text, NegativePrompt.Text);
    }
    private async void StartClick(object sender, RoutedEventArgs e) {
        if (!StartButton.IsEnabled) return;
        await StartRunAsync("viewport", null);
    }
    public async Task UseShapeAsync(GeometryCandidate source) {
        if (!integratedMode || busy || closed) return;
        if (runs.TryGetValue(source.Key, out var existing)) {
            ActivateRun(existing);
            Status.Text = existing.Session.Pending != null
                ? "Returned to this shape's pending conditions. Retry to generate; no request was made."
                : "Returned to this shape's saved exploration. No image request was made.";
            UpdateControls(); return;
        }
        await StartRunAsync(source.Key, source);
    }
    private async Task StartRunAsync(string key, GeometryCandidate? source) {
        await RunAsync(async token => {
            activity = "Initializing rendering exploration";
            api ??= createApi();
            var settings = configuration ?? ReadConfiguration();
            var referenceBytes = source == null ? captureReference() : (byte[])source.ReferencePng.Clone();
            var sourceSnapshot = source == null ? null : source with {
                ReferencePng = referenceBytes, Variables = (GeometryVariableRecord[])source.Variables.Clone()
            };
            var newSession = await Task.Run(() => new RenderingSession(settings.Rows, settings.Columns, settings.Indices, settings.Initial, settings.Negative, settings.FixedValues));
            if (closed || token.IsCancellationRequested) { newSession.Dispose(); token.ThrowIfCancellationRequested(); return; }
            configuration ??= settings;
            frozenInitialPrompt = newSession.InitialPrompt; frozenNegativePrompt = newSession.NegativePrompt;
            exploredVariables = settings.Indices;
            var run = new RenderingRun(key, sourceSnapshot, referenceBytes, newSession);
            runs.Add(key, run);
            ActivateRun(run);
            token.ThrowIfCancellationRequested();
            await GenerateAsync(token);
        });
    }
    private void ActivateRun(RenderingRun run) {
        currentRun = run;
        selected = -1; inspectorBatch = null;
        displayedBatch = run.Session.Displayed;
        displayedImage = run.ActiveNode?.Image;
        MatrixImage.Source = displayedImage;
        MatrixView.FitToWindow();
        SetupPanel.Visibility = Visibility.Collapsed;
        RenderingLayout.Visibility = MatrixView.Visibility = VariableSummary.Visibility = ShowPromptButton.Visibility = Visibility.Visible;
        if (displayedImage == null) MatrixView.Visibility = Visibility.Collapsed;
        NextButton.Visibility = RegenerateButton.Visibility = ConfirmButton.Visibility = Visibility.Visible;
        VariableSummary.Text = string.Join(" · ", exploredVariables.Select(i => RenderingSession.Variables[i].Name));
        if (cells.Length == 0) {
            CellOverlay.Rows = run.Session.Rows; CellOverlay.Columns = run.Session.Columns;
            cells = Enumerable.Range(0, run.Session.Rows * run.Session.Columns).Select(i => {
                var cell = new Button { Style = (Style)FindResource("CellStyle"), IsEnabled = false,
                    ToolTip = $"Row {i / run.Session.Columns + 1}, column {i % run.Session.Columns + 1}" };
                cell.Click += (_, _) => SelectCell(i); CellOverlay.Children.Add(cell); return cell;
            }).ToArray();
        }
        if (run.Source is GeometryCandidate source) {
            SourceImage.Source = Decode(run.Reference);
            SourceLabel.Text = $"Input shape · Geometry #{source.SourceNodeId + 1}, R{source.Cell / source.Columns + 1} C{source.Cell % source.Columns + 1}\nPlane {source.Plane} · Step {source.Step}";
        }
        StageLabel.Text = displayedBatch == null ? "First matrix pending" : $"Search plane {displayedBatch.Plane} · Selection {displayedBatch.SelectionStage} of 4";
        UpdateControls();
    }
    private async void NextClick(object sender, RoutedEventArgs e) {
        if (!NextButton.IsEnabled) return;
        int index = selected;
        var existing = history.FirstOrDefault(n => n.ParentId == activeNode?.Id && n.Cell == index);
        if (existing != null) { RecordChoice(); ResumeHistory(existing.Id); return; }
        pendingParentId = activeNode?.Id; pendingCell = index;
        pendingEdge = $"R{index / session!.Columns + 1} C{index % session.Columns + 1}";
        await RunAsync(async token => {
            activity = "Updating the search plane"; await Task.Run(() => session!.Advance(index));
            RecordChoice();
            token.ThrowIfCancellationRequested(); await GenerateAsync(token);
        });
    }
    private async void RegenerateClick(object sender, RoutedEventArgs e) {
        if (!RegenerateButton.IsEnabled) return;
        pendingParentId = activeNode?.Id; pendingCell = null; pendingEdge = "Regenerate";
        await RunAsync(async token => { session!.Regenerate(); await GenerateAsync(token); });
    }
    private async void RetryClick(object sender, RoutedEventArgs e) { if (RetryButton.IsEnabled) await RunAsync(GenerateAsync); }
    private async Task GenerateAsync(CancellationToken token) {
        var run = currentRun ?? throw new InvalidOperationException("No rendering reference is active.");
        var pending = run.Session.Pending ?? throw new InvalidOperationException("No generation is pending.");
        UpdateControls(); activity = "Generating matrix";
        var bytes = await api!.GenerateAsync(run.Reference, pending.Prompt, token).WaitAsync(token);
        token.ThrowIfCancellationRequested();
        if (closed || !ReferenceEquals(currentRun, run)) return;
        var image = Decode(bytes);
        if (image.PixelWidth != 1024 || image.PixelHeight != 1024)
            throw new InvalidOperationException("The API returned an unexpected image size. Expected 1024x1024. Retry the same conditions.");
        run.Session.Accept(pending);
        activeNode = new RenderingHistoryNode(history.Count, pendingParentId, pendingCell, pendingEdge, run.Session.CaptureState(), image, run.Key);
        history.Add(activeNode);
        displayedBatch = pending; displayedImage = image; MatrixImage.Source = image;
        MatrixView.SetSceneSize(image.PixelWidth, image.PixelHeight); MatrixView.Visibility = Visibility.Visible;
        selected = -1; PromptValueScroll.ScrollToTop(); PromptValueScroll.ScrollToHorizontalOffset(0);
        StageLabel.Text = $"Search plane {pending.Plane} · Selection {pending.SelectionStage} of 4";
        Status.Text = $"Ready · {pending.Valid.Count(v => v)} valid cells · {elapsed.Elapsed.TotalSeconds:F1}s. Select a cell, then Next or Confirm selection.";
    }
    private string HistoryDetails(RenderingHistoryNode node) {
        var run = runs[node.RunKey];
        string source = run.Source == null ? "Viewport reference" : $"Geometry #{run.Source.SourceNodeId + 1}, R{run.Source.Cell / run.Source.Columns + 1} C{run.Source.Cell % run.Source.Columns + 1}";
        string summary = $"Node {node.Id + 1}\nInput: {source}\nInitial prompt:\n{frozenInitialPrompt}\n\nNegative prompt:\n{frozenNegativePrompt}\n\nExplored: "
            + string.Join(", ", exploredVariables.Select(i => RenderingSession.Variables[i].Name))
            + "\nFixed:\n" + string.Join("\n", Enumerable.Range(0, RenderingSession.Variables.Length).Except(exploredVariables)
                .Select(i => RenderingSession.Variables[i].Name + ": " + RenderingSession.Variables[i].Describe(node.State.Batch.Values[0][i])));
        var details = new System.Text.StringBuilder(summary).AppendLine().AppendLine().AppendLine("Candidate values (normalized):");
        var batch = node.State.Batch;
        for (int cell = 0; cell < batch.Values.Length; cell++) {
            details.Append($"R{cell / run.Session.Columns + 1} C{cell % run.Session.Columns + 1}");
            if (!batch.Valid[cell]) details.Append(" · Out of range");
            details.Append(": ").AppendLine(string.Join("; ", exploredVariables.Select(i => RenderingSession.Variables[i].Name + "=" + batch.Values[cell][i].ToString("G6", CultureInfo.InvariantCulture))));
        }
        return details.ToString();
    }
    public void ResumeHistory(int id) {
        if (!CanResume) return;
        var node = history.Single(n => n.Id == id);
        var run = runs[node.RunKey];
        try { run.Session.RestoreState(node.State); }
        catch (Exception error) { Status.Text = "Could not restore history: " + error.Message; return; }
        currentRun = run; activeNode = node; pendingParentId = pendingCell = null; pendingEdge = "";
        ActivateRun(run);
        Status.Text = "Restored saved candidates. No image request or geometry change was made.";
        UpdateControls();
    }
    private void SelectCell(int index) {
        if (busy || closed || session?.Pending != null || session?.Faulted != false || session.Displayed == null || index < 0 || index >= session.Displayed.Valid.Length || !session.Displayed.Valid[index]) return;
        selected = index; UpdateControls();
    }
    private void ShowPromptClick(object sender, RoutedEventArgs e) {
        showingPrompt = !showingPrompt;
        PromptPanel.Visibility = PromptSplitter.Visibility = showingPrompt ? Visibility.Visible : Visibility.Collapsed;
        PromptColumn.MinWidth = showingPrompt ? 300 : 0;
        PromptColumn.Width = showingPrompt ? new GridLength(3, GridUnitType.Star) : new GridLength(0);
        PromptGutter.Width = new GridLength(showingPrompt ? 10 : 0);
        ShowPromptButton.Content = showingPrompt ? "Hide Prompt" : "Show Prompt";
        UpdatePromptInspector();
    }
    private void UpdatePromptInspector() {
        if (!showingPrompt) return;
        var batch = displayedBatch ?? session?.Pending;
        if (batch == null) return;
        PromptContext.Text = displayedBatch == null
            ? $"Pending request · search plane {batch.Plane}, selection {batch.SelectionStage}. No image is available yet."
            : $"Displayed image · search plane {batch.Plane}, selection {batch.SelectionStage}."
                + (session?.Pending != null ? " A generation is pending; these values still match the image on the left." : "");
        if (!ReferenceEquals(inspectorBatch, batch)) {
            InspectorInitialPrompt.Text = frozenInitialPrompt.Length == 0 ? "(No initial prompt)" : frozenInitialPrompt;
            InspectorNegativePrompt.Text = frozenNegativePrompt.Length == 0 ? "(No negative prompt)" : frozenNegativePrompt;
            FullPrompt.Text = batch.Prompt;
            int columns = session?.Columns ?? CellOverlay.Columns;
            PromptValueGrid.Rows = session?.Rows ?? CellOverlay.Rows;
            PromptValueGrid.Columns = columns;
            PromptValueGrid.MinWidth = columns * 90;
            PromptValueGrid.MinHeight = PromptValueGrid.Rows * 130;
            PromptValueGrid.Children.Clear();
            promptCells = Enumerable.Range(0, batch.Values.Length).Select(i => {
                var content = new StackPanel();
                content.Children.Add(new TextBlock { Text = $"R{i / columns + 1} C{i % columns + 1}", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
                if (!batch.Valid[i]) content.Children.Add(new TextBlock { Text = "Out of range", Foreground = Brushes.LightSalmon, FontSize = 9 });
                for (int j = 0; j < RenderingSession.Variables.Length; j++) {
                    var line = new Grid();
                    line.ColumnDefinitions.Add(new ColumnDefinition());
                    line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    line.Children.Add(new TextBlock { Text = RenderingSession.Variables[j].Label, Foreground = Brushes.LightSteelBlue, Margin = new Thickness(0, 0, 3, 0) });
                    var value = new TextBlock { Text = batch.Values[i][j].ToString("G4", CultureInfo.InvariantCulture) };
                    Grid.SetColumn(value, 1); line.Children.Add(value); content.Children.Add(line);
                }
                var tile = new Button {
                    Content = content, ToolTip = DescribeCell(batch, i), Style = (Style)FindResource("CellStyle"),
                    MinWidth = 88, MinHeight = 128, Margin = new Thickness(1), Padding = new Thickness(4), FontSize = 11,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Top
                };
                ToolTipService.SetShowOnDisabled(tile, true);
                tile.Click += (_, _) => SelectCell(i);
                PromptValueGrid.Children.Add(tile);
                return tile;
            }).ToArray();
            inspectorBatch = batch;
        }
        bool canSelect = !busy && session?.Pending == null && session?.Faulted == false;
        for (int i = 0; i < promptCells.Length; i++) {
            promptCells[i].IsEnabled = canSelect && batch.Valid[i];
            promptCells[i].Background = new SolidColorBrush(batch.Valid[i] ? Color.FromRgb(34, 43, 54) : Color.FromRgb(25, 30, 38));
            promptCells[i].BorderBrush = i == selected ? Brushes.Orange : Brushes.SlateGray;
            promptCells[i].BorderThickness = new Thickness(i == selected ? 3 : 1);
        }
        SelectedCellValues.Text = selected < 0 ? "Select a valid box on the image or value grid to inspect its exact values and prompt descriptions." : DescribeCell(batch, selected);
        if (selected >= 0) promptCells[selected].BringIntoView();
    }
    private string DescribeCell(RenderingBatch batch, int index) {
        int columns = session?.Columns ?? CellOverlay.Columns;
        var details = new System.Text.StringBuilder($"Row {index / columns + 1}, column {index % columns + 1}\n");
        for (int j = 0; j < RenderingSession.Variables.Length; j++) {
            double value = batch.Values[index][j];
            var variable = RenderingSession.Variables[j];
            details.Append(variable.Name).Append(": ").Append(value.ToString("G17", CultureInfo.InvariantCulture));
            if (!exploredVariables.Contains(j)) details.Append(" (fixed)");
            details.Append(" · ").AppendLine(batch.Valid[index] ? RenderingSession.DescribeValue(batch.Values[index], j) : "Out of range");
        }
        return details.ToString();
    }
    private async Task RunAsync(Func<CancellationToken, Task> operation) {
        if (busy || closed) return;
        busy = true; cancellation?.Dispose(); cancellation = new CancellationTokenSource();
        elapsed.Restart(); timer.Start(); UpdateControls();
        try { await operation(cancellation.Token); }
        catch (OperationCanceledException) { if (!closed) Status.Text = "Generation cancelled. Completed history was kept. Retry uses the same pending conditions."; }
        catch (Exception error) { if (!closed) Status.Text = (activity == "Saving rendering result" ? "Could not save rendering result: " : "Stopped: ") + error.Message + (session?.Pending != null ? " Retry keeps the same pending conditions." : ""); }
        finally {
            timer.Stop(); busy = false;
            if (closed) DisposeResources(); else UpdateControls();
        }
    }
    internal static BitmapSource Decode(byte[] bytes) {
        using var stream = new MemoryStream(bytes);
        var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream; image.EndInit(); image.Freeze(); return image;
    }
    internal static Int32Rect CellBounds(int width, int height, int rows, int columns, int index) {
        int row = index / columns, column = index % columns, x = column * width / columns, y = row * height / rows;
        return new Int32Rect(x, y, (column + 1) * width / columns - x, (row + 1) * height / rows - y);
    }
    private void RecordChoice() {
        if (activeNode == null || displayedBatch == null || selected < 0 || currentRun == null) return;
        completedChoices.RemoveAll(c => c.SourceNodeId == activeNode.Id && c.Cell == selected);
        var batch = displayedBatch;
        completedChoices.Add(new RenderingChoice(activeNode.Id, selected, session!.Rows, session.Columns,
            batch.Plane, batch.SelectionStage, (double[])batch.Values[selected].Clone(), (int[])exploredVariables.Clone(),
            frozenInitialPrompt, frozenNegativePrompt, (byte[])currentRun.Reference.Clone(), currentRun.Source));
    }
    private void ConfirmClick(object sender, RoutedEventArgs e) {
        if (!ConfirmButton.IsEnabled) return;
        RecordChoice();
        Status.Text = "Selection confirmed for final rendering. Choose its source node in History.";
        UpdateControls();
    }
    internal static void SavePng(BitmapSource image, string path) => ResultExport.SavePng(image, path);
    public void Cancel() => cancellation?.Cancel();
    private void CancelClick(object sender, RoutedEventArgs e) => Cancel();
    private void CloseClick(object sender, RoutedEventArgs e) => CloseRequested?.Invoke();
    public void Shutdown() {
        if (closed) return;
        closed = true; cancellation?.Cancel(); timer.Stop();
        if (!busy) DisposeResources();
    }
    private void DisposeResources() {
        if (disposed) return;
        disposed = true; timer.Stop(); api?.Dispose(); api = null; cancellation?.Dispose();
        foreach (var run in runs.Values) run.Session.Dispose();
        foreach (var node in history) node.State.Dispose();
        runs.Clear(); history.Clear(); completedChoices.Clear(); currentRun = null;
    }
}
