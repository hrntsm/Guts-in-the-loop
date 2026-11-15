using System;
using System.Diagnostics;
using System.Globalization;
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
    private RenderingBatch? displayedBatch, inspectorBatch;
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
        InitialPrompt.Text = RenderingSession.DefaultInitialPrompt;
        NegativePrompt.Text = RenderingSession.DefaultNegativePrompt;
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
        NegativePrompt.IsReadOnly = busy || session != null || final;
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
    }
    private async void StartClick(object sender, RoutedEventArgs e) {
        if (busy || session != null || variables.Count(v => v.IsChecked == true) < 2) return;
        int rows = Rows, columns = Columns;
        string initialPrompt = InitialPrompt.Text;
        string negativePrompt = NegativePrompt.Text;
        int[] indices = variables.Select((v, i) => (v, i)).Where(x => x.v.IsChecked == true).Select(x => x.i).ToArray();
        await RunAsync(async token => {
            activity = "Initializing rendering exploration";
            api = createApi();
            reference = captureReference();
            session = await Task.Run(() => new RenderingSession(rows, columns, indices, initialPrompt, negativePrompt));
            if (closed) token.ThrowIfCancellationRequested();
            frozenInitialPrompt = session.InitialPrompt;
            frozenNegativePrompt = session.NegativePrompt;
            exploredVariables = indices;
            SetupPanel.Visibility = Visibility.Collapsed;
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
        UpdateControls();
        activity = "Generating matrix";
        var bytes = await api!.GenerateAsync(reference!, pending.Prompt, token).WaitAsync(token);
        token.ThrowIfCancellationRequested();
        var image = Decode(bytes);
        if (image.PixelWidth != 1024 || image.PixelHeight != 1024)
            throw new InvalidOperationException("The API returned an unexpected image size. Expected 1024x1024. Retry the same conditions.");
        session.Accept(pending);
        displayedBatch = pending;
        displayedImage = image;
        MatrixImage.Source = image;
        selected = -1;
        PromptValueScroll.ScrollToTop();
        PromptValueScroll.ScrollToHorizontalOffset(0);
        StageLabel.Text = $"Search plane {pending.Plane} · Selection {pending.SelectionStage} of 4";
        Status.Text = $"Ready · {pending.Valid.Count(v => v)} valid cells · {elapsed.Elapsed.TotalSeconds:F1}s. Select a cell, then Next or Finish. Use Regenerate if the layout is unsuitable.";
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
        bool canSelect = !busy && !final && session?.Pending == null && session?.Faulted == false;
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
