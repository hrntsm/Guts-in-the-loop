using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace SequentialGallery;

internal sealed record RenderingHistoryNode(int Id, int? ParentId, int? Cell, string EdgeLabel, RenderingState State, BitmapSource Image);

internal partial class RenderingWindow : Window {
    private readonly Func<byte[]> captureReference;
    private readonly Func<RenderingApi> createApi;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly Stopwatch elapsed = new();
    private CheckBox[] variables = Array.Empty<CheckBox>();
    private ComboBox[] fixedValueChoices = Array.Empty<ComboBox>();
    private Button[] cells = Array.Empty<Button>();
    private RenderingSession? session;
    private RenderingApi? api;
    private byte[]? reference;
    private BitmapSource? displayedImage, finalCrop;
    private RenderingBatch? displayedBatch, inspectorBatch;
    private readonly List<RenderingHistoryNode> history = new();
    private RenderingHistoryNode? activeNode;
    private int? pendingParentId, pendingCell;
    private string pendingEdge = "";
    internal string? LastExportDirectory { get; private set; }
    private string frozenInitialPrompt = "";
    private string frozenNegativePrompt = "";
    private int[] exploredVariables = Array.Empty<int>();
    private bool showingPrompt;
    private Button[] promptCells = Array.Empty<Button>();
    private CancellationTokenSource? cancellation;
    private bool busy, closed, final;
    private int selected = -1;
    private string activity = "Generating matrix";

    public RenderingWindow(Func<byte[]> captureReference, Func<RenderingApi>? createApi = null) {
        this.captureReference = captureReference;
        this.createApi = createApi ?? (() => new RenderingApi(Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? ""));
        InitializeComponent();
        ModeTabs.SelectionChanged += (_, e) => { if (ReferenceEquals(e.Source, ModeTabs)) UpdateControls(); };
        HistoryTree.ResumeRequested += ResumeHistory;
        InitialPrompt.Text = RenderingSession.DefaultInitialPrompt;
        NegativePrompt.Text = RenderingSession.DefaultNegativePrompt;
        fixedValueChoices = RenderingSession.Variables.Select(variable => new ComboBox {
            ItemsSource = variable.Anchors, SelectedIndex = variable.Anchors.Length / 2,
            Width = 220, Margin = new Thickness(8, 3, 0, 3), IsEnabled = false,
            ToolTip = "Fixed value when this variable is not explored."
        }).ToArray();
        variables = RenderingSession.Variables.Select((variable, i) => {
            var option = new CheckBox {
                Content = variable.Name, ToolTip = $"Explore {variable.Anchors[0]} → {variable.Anchors[^1]}",
                IsChecked = true, Foreground = Brushes.White, Margin = new Thickness(0, 5, 12, 5), VerticalAlignment = VerticalAlignment.Center
            };
            option.Checked += (_, _) => UpdateControls();
            option.Unchecked += (_, _) => UpdateControls();
            var row = new DockPanel();
            DockPanel.SetDock(fixedValueChoices[i], Dock.Right);
            row.Children.Add(fixedValueChoices[i]);
            row.Children.Add(option);
            VariableOptions.Children.Add(row);
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
        bool evaluating = ModeTabs.SelectedIndex == 0;
        bool canExplore = ready && evaluating && !final && hasImage && session?.Pending == null && session?.Faulted == false;
        NextButton.IsEnabled = canExplore && selected >= 0;
        RegenerateButton.IsEnabled = canExplore;
        RetryButton.IsEnabled = ready && evaluating && !final && session?.Pending != null && session.Faulted == false;
        RetryButton.Visibility = session?.Pending != null && !final ? Visibility.Visible : Visibility.Collapsed;
        FinishButton.IsEnabled = ready && evaluating && !final && hasImage && selected >= 0;
        CancelButton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        BusyProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        for (int i = 0; i < variables.Length; i++) {
            variables[i].IsEnabled = ready && session == null && !final;
            fixedValueChoices[i].IsEnabled = ready && session == null && !final && variables[i].IsChecked != true;
        }
        RowsChoice.IsEnabled = ColumnsChoice.IsEnabled = ready && session == null;
        InitialPrompt.IsReadOnly = busy || session != null || final;
        NegativePrompt.IsReadOnly = busy || session != null || final;
        ShowPromptButton.IsEnabled = evaluating && (displayedBatch != null || session?.Pending != null);
        for (int i = 0; i < cells.Length; i++) {
            bool valid = session?.Displayed?.Valid[i] == true;
            cells[i].IsEnabled = canExplore && valid;
            cells[i].Background = valid ? Brushes.Transparent : new SolidColorBrush(Color.FromArgb(245, 35, 43, 54));
            cells[i].Content = hasImage && !valid ? "Out of range" : null;
            cells[i].BorderBrush = i == selected ? Brushes.Orange : new SolidColorBrush(Color.FromArgb(150, 255, 255, 255));
            cells[i].BorderThickness = new Thickness(i == selected ? 6 : 1);
        }
        UpdatePromptInspector();
        UpdateHistory();
    }
    private async void StartClick(object sender, RoutedEventArgs e) {
        if (busy || session != null || variables.Count(v => v.IsChecked == true) < 2) return;
        int rows = Rows, columns = Columns;
        string initialPrompt = InitialPrompt.Text;
        string negativePrompt = NegativePrompt.Text;
        int[] indices = variables.Select((v, i) => (v, i)).Where(x => x.v.IsChecked == true).Select(x => x.i).ToArray();
        double[] fixedValues = fixedValueChoices.Select((choice, i) => (double)choice.SelectedIndex / (RenderingSession.Variables[i].Anchors.Length - 1)).ToArray();
        await RunAsync(async token => {
            activity = "Initializing rendering exploration";
            api = createApi();
            reference = captureReference();
            session = await Task.Run(() => new RenderingSession(rows, columns, indices, initialPrompt, negativePrompt, fixedValues));
            if (closed) token.ThrowIfCancellationRequested();
            frozenInitialPrompt = session.InitialPrompt;
            frozenNegativePrompt = session.NegativePrompt;
            exploredVariables = indices;
            SetupPanel.Visibility = Visibility.Collapsed;
            ModeTabs.Visibility = Visibility.Visible;
            RenderingLayout.Visibility = MatrixView.Visibility = VariableSummary.Visibility = ShowPromptButton.Visibility = Visibility.Visible;
            NextButton.Visibility = RegenerateButton.Visibility = FinishButton.Visibility = Visibility.Visible;
            VariableSummary.Text = string.Join(" · ", indices.Select(i => RenderingSession.Variables[i].Name));
            CellOverlay.Rows = rows;
            CellOverlay.Columns = columns;
            cells = Enumerable.Range(0, rows * columns).Select(i => {
                var cell = new Button { Style = (Style)FindResource("CellStyle"), IsEnabled = false, ToolTip = $"Row {i / columns + 1}, column {i % columns + 1}" };
                cell.Click += (_, _) => SelectCell(i);
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
        var existing = history.FirstOrDefault(n => n.ParentId == activeNode?.Id && n.Cell == index);
        if (existing != null) { ResumeHistory(existing.Id); return; }
        pendingParentId = activeNode?.Id;
        pendingCell = index;
        pendingEdge = $"R{index / session!.Columns + 1} C{index % session.Columns + 1}";
        await RunAsync(async token => {
            activity = "Updating the search plane";
            await Task.Run(() => session!.Advance(index));
            token.ThrowIfCancellationRequested();
            await GenerateAsync(token);
        });
    }
    private async void RegenerateClick(object sender, RoutedEventArgs e) {
        if (!RegenerateButton.IsEnabled) return;
        pendingParentId = activeNode?.Id;
        pendingCell = null;
        pendingEdge = "Regenerate";
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
        UpdateControls();
        activity = "Generating matrix";
        var bytes = await api!.GenerateAsync(reference!, pending.Prompt, token).WaitAsync(token);
        token.ThrowIfCancellationRequested();
        var image = Decode(bytes);
        if (image.PixelWidth != 1024 || image.PixelHeight != 1024)
            throw new InvalidOperationException("The API returned an unexpected image size. Expected 1024x1024. Retry the same conditions.");
        session.Accept(pending);
        activeNode = new RenderingHistoryNode(history.Count, pendingParentId, pendingCell, pendingEdge, session.CaptureState(), image);
        history.Add(activeNode);
        displayedBatch = pending;
        displayedImage = image;
        MatrixImage.Source = image;
        selected = -1;
        PromptValueScroll.ScrollToTop();
        PromptValueScroll.ScrollToHorizontalOffset(0);
        StageLabel.Text = $"Search plane {pending.Plane} · Selection {pending.SelectionStage} of 4";
        Status.Text = $"Ready · {pending.Valid.Count(v => v)} valid cells · {elapsed.Elapsed.TotalSeconds:F1}s. Select a cell, then Next or Finish. Use Regenerate if the layout is unsuitable.";
    }
    private void UpdateHistory() {
        HistoryTree.SetItems(history.Select(node => new HistoryItem(node.Id, node.ParentId,
            $"Plane {node.State.Batch.Plane} · Step {node.State.Batch.SelectionStage}", node.Image, node.EdgeLabel,
            HistoryDetails(node))), activeNode?.Id, !busy && !closed && !final);
    }
    private string HistoryDetails(RenderingHistoryNode node) {
        string summary = $"Node {node.Id + 1}\nInitial prompt:\n{frozenInitialPrompt}\n\nNegative prompt:\n{frozenNegativePrompt}\n\nExplored: "
            + string.Join(", ", exploredVariables.Select(i => RenderingSession.Variables[i].Name))
            + "\nFixed:\n" + string.Join("\n", Enumerable.Range(0, RenderingSession.Variables.Length).Except(exploredVariables)
                .Select(i => RenderingSession.Variables[i].Name + ": " + RenderingSession.Variables[i].Describe(node.State.Batch.Values[0][i])));
        var details = new System.Text.StringBuilder(summary).AppendLine().AppendLine().AppendLine("Candidate values (normalized):");
        var batch = node.State.Batch;
        for (int cell = 0; cell < batch.Values.Length; cell++) {
            details.Append($"R{cell / CellOverlay.Columns + 1} C{cell % CellOverlay.Columns + 1}");
            if (!batch.Valid[cell]) details.Append(" · Out of range");
            details.Append(": ").AppendLine(string.Join("; ", exploredVariables.Select(i => RenderingSession.Variables[i].Name
                + "=" + batch.Values[cell][i].ToString("G6", CultureInfo.InvariantCulture))));
        }
        return details.ToString();
    }
    private void ResumeHistory(int id) {
        if (busy || closed || final || session == null) return;
        var node = history.Single(n => n.Id == id);
        try { session.RestoreState(node.State); }
        catch (Exception error) { Status.Text = "Could not restore history: " + error.Message; return; }
        activeNode = node;
        pendingParentId = pendingCell = null;
        pendingEdge = "";
        displayedBatch = node.State.Batch;
        displayedImage = node.Image;
        MatrixImage.Source = displayedImage;
        selected = -1;
        inspectorBatch = null;
        ModeTabs.SelectedIndex = 0;
        StageLabel.Text = $"Search plane {displayedBatch.Plane} · Selection {displayedBatch.SelectionStage} of 4";
        Status.Text = "Restored saved candidates. Select a cell to continue; no image request was made.";
        UpdateControls();
    }
    private void SelectCell(int index) {
        if (busy || final || session?.Pending != null || session?.Faulted != false || session.Displayed == null
            || index < 0 || index >= session.Displayed.Valid.Length || !session.Displayed.Valid[index]) return;
        selected = index;
        UpdateControls();
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
        bool canSelect = ModeTabs.SelectedIndex == 0 && !busy && !final && session?.Pending == null && session?.Faulted == false;
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
        try {
            finalCrop = new CroppedBitmap(displayedImage!, CellBounds(displayedImage!.PixelWidth, displayedImage.PixelHeight, session!.Rows, session.Columns, selected));
            finalCrop.Freeze();
            var batch = displayedBatch!;
            LastExportDirectory = ResultExport.Save("rendering", directory => {
                SavePng(finalCrop, Path.Combine(directory, "rendering.png"));
                File.WriteAllBytes(Path.Combine(directory, "reference.png"), reference!);
                File.WriteAllText(Path.Combine(directory, "prompt.txt"), batch.Prompt);
                var metadata = new {
                    formatVersion = 1, kind = "ai-rendering", createdAt = DateTimeOffset.UtcNow,
                    initialPrompt = frozenInitialPrompt, negativePrompt = frozenNegativePrompt,
                    rows = session.Rows, columns = session.Columns,
                    selectedCell = new { row = selected / session.Columns + 1, column = selected % session.Columns + 1, index = selected, indexBase = 0 },
                    sourceNode = activeNode!.Id, plane = batch.Plane, step = batch.SelectionStage,
                    image = new { file = "rendering.png", kind = "selected-cell crop of source matrix", width = finalCrop.PixelWidth, height = finalCrop.PixelHeight },
                    sourceMatrix = new { width = displayedImage.PixelWidth, height = displayedImage.PixelHeight, promptFile = "prompt.txt" },
                    referenceImage = "reference.png",
                    generation = new { model = RenderingApi.Model, size = "1024x1024", quality = "low", outputFormat = "png", count = 1 },
                    variables = RenderingSession.Variables.Select((variable, i) => new {
                        name = variable.Name, value = batch.Values[selected][i], explored = exploredVariables.Contains(i), isFixed = !exploredVariables.Contains(i),
                        description = RenderingSession.DescribeValue(batch.Values[selected], i)
                    }).ToArray()
                };
                File.WriteAllText(Path.Combine(directory, "metadata.json"), JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }));
            });
            final = true;
            Close();
        } catch (Exception error) {
            Status.Text = "Could not save rendering result: " + error.Message + " The result and history were kept. Retry Finish or Close without saving.";
            UpdateControls();
        }
    }
    internal static void SavePng(BitmapSource image, string path) => ResultExport.SavePng(image, path);
    private void CancelClick(object sender, RoutedEventArgs e) => cancellation?.Cancel();
    private void CloseClick(object sender, RoutedEventArgs e) => Close();
    private void DisposeResources() {
        timer.Stop(); session?.Dispose(); session = null; api?.Dispose(); api = null; cancellation?.Dispose(); reference = null;
        foreach (var node in history) node.State.Dispose();
        history.Clear(); activeNode = null;
    }
}
