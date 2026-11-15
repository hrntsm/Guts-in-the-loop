using System;
using System.Globalization;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace SequentialGallery;

internal sealed class FinalRenderingPane : UserControl {
    private sealed record Generation(GeometryCandidate? Geometry, RenderingChoice Conditions, byte[] Reference,
        string Prompt, string Size, string Quality, byte[] Png, DateTimeOffset CreatedAt);
    internal readonly TextBox PromptEditor = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MinHeight = 200 };
    internal readonly ComboBox SizeChoice = new() { ItemsSource = new[] { "1024x1024", "1536x1024", "1024x1536" }, SelectedIndex = 0, MinWidth = 140 };
    internal readonly ComboBox QualityChoice = new() { ItemsSource = new[] { "low", "medium", "high" }, SelectedIndex = 2, MinWidth = 100 };
    internal readonly Button RenderButton = new() { Content = "Render final image" };
    internal readonly Button FinishButton = new() { Content = "Finish · save and close", IsEnabled = false };
    private readonly Button back = new() { Content = "Back to History" }, cancel = new() { Content = "Cancel generation" };
    private readonly TextBlock provenance = new() { TextWrapping = TextWrapping.Wrap }, status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Image preview = new() { Stretch = Stretch.Uniform }, referenceImage = new() { Height = 100, Stretch = Stretch.Uniform };
    private readonly ProgressBar progress = new() { Height = 5, IsIndeterminate = true, Visibility = Visibility.Collapsed };
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly Stopwatch elapsed = new();
    private readonly Func<RenderingApi> createApi;
    private readonly Func<GeometryCandidate, CancellationToken, Task>? applyGeometry;
    private GeometryCandidate? geometry;
    private RenderingChoice? conditions;
    private Generation? accepted;
    private CancellationTokenSource? cancellation;
    private bool busy, closed;
    public bool IsBusy => busy;
    internal string? LastExportDirectory { get; private set; }
    public event Action? StateChanged, BackRequested, Finished;

    public FinalRenderingPane(Func<RenderingApi> createApi, Func<GeometryCandidate, CancellationToken, Task>? applyGeometry) {
        this.createApi = createApi; this.applyGeometry = applyGeometry;
        var root = new Grid { Margin = new Thickness(12) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star), MinWidth = 330 });
        var left = new DockPanel { Margin = new Thickness(0, 0, 16, 0) };
        var heading = new TextBlock { Text = "FINAL RENDERING", FontSize = 22, Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(heading, Dock.Top); left.Children.Add(heading);
        DockPanel.SetDock(status, Dock.Bottom); left.Children.Add(status);
        DockPanel.SetDock(progress, Dock.Bottom); left.Children.Add(progress); left.Children.Add(preview); root.Children.Add(left);
        var right = new DockPanel(); Grid.SetColumn(right, 1); root.Children.Add(right);
        var actions = new WrapPanel();
        foreach (var button in new[] { back, RenderButton, cancel, FinishButton }) {
            button.Margin = new Thickness(0, 6, 8, 0); button.Padding = new Thickness(10, 8, 10, 8);
            button.Background = new SolidColorBrush(Color.FromRgb(40, 51, 65)); button.Foreground = Brushes.White; actions.Children.Add(button);
        }
        DockPanel.SetDock(actions, Dock.Bottom); right.Children.Add(actions);
        var details = new StackPanel();
        details.Children.Add(new TextBlock { Text = "Selected sources and conditions (read-only)", FontWeight = FontWeights.SemiBold });
        details.Children.Add(referenceImage); details.Children.Add(provenance);
        var options = new WrapPanel { Margin = new Thickness(0, 12, 0, 12) };
        options.Children.Add(new TextBlock { Text = "Size ", VerticalAlignment = VerticalAlignment.Center }); options.Children.Add(SizeChoice);
        options.Children.Add(new TextBlock { Text = "  Quality ", VerticalAlignment = VerticalAlignment.Center }); options.Children.Add(QualityChoice);
        details.Children.Add(options);
        details.Children.Add(new TextBlock { Text = "Final prompt · edit or replace the complete request", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) });
        PromptEditor.Background = new SolidColorBrush(Color.FromRgb(34, 43, 54)); PromptEditor.Foreground = Brushes.White; PromptEditor.Padding = new Thickness(8);
        details.Children.Add(PromptEditor);
        right.Children.Add(new ScrollViewer { Content = details, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); Content = root;
        back.Click += (_, _) => { if (!busy) BackRequested?.Invoke(); };
        cancel.Click += (_, _) => Cancel(); RenderButton.Click += async (_, _) => await RenderAsync();
        FinishButton.Click += async (_, _) => await FinishAsync(); UpdateControls();
        timer.Tick += (_, _) => status.Text = $"Working on final rendering · {elapsed.Elapsed.TotalSeconds:F0}s elapsed. API usage may apply.";
    }

    public void SetSelection(GeometryCandidate? selectedGeometry, RenderingChoice selectedConditions) {
        if (busy || closed) return;
        if (geometry?.Key == selectedGeometry?.Key && conditions?.Key == selectedConditions.Key) return;
        geometry = selectedGeometry; conditions = selectedConditions; accepted = null; preview.Source = null;
        LastExportDirectory = null;
        byte[] input = selectedGeometry?.ReferencePng ?? selectedConditions.ReferencePng;
        referenceImage.Source = RenderingPane.Decode(input);
        provenance.Text = (selectedGeometry == null ? "Input: original viewport reference" :
            $"Geometry #{selectedGeometry.SourceNodeId + 1} · R{selectedGeometry.Cell / selectedGeometry.Columns + 1} C{selectedGeometry.Cell % selectedGeometry.Columns + 1}\n"
            + string.Join(", ", selectedGeometry.Variables.Select(v => v.Name + "=" + v.Value.ToString(CultureInfo.InvariantCulture))))
            + "\n" + selectedConditions.Label + "\n"
            + string.Join("\n", RenderingSession.Variables.Select((v, i) => v.Name + "=" + selectedConditions.Values[i].ToString("G6", CultureInfo.InvariantCulture)
                + " · " + RenderingSession.DescribeValue(selectedConditions.Values, i)));
        PromptEditor.Text = BuildPrompt(selectedConditions);
        status.Text = "Ready to render one image. No request has been sent. Finish saves the displayed generation; later draft edits are not saved as its prompt.";
        UpdateControls();
    }

    internal static string BuildPrompt(RenderingChoice choice) {
        var catalog = RenderingPrompts.Current;
        // Remove built-in exploration layout wording while retaining custom scene constraints verbatim.
        string initial = choice.InitialPrompt.Replace("Use the same building in every cell.", "Use the supplied building.")
            .Replace("Keep the same camera angle and composition across cells.", "Keep the reference camera angle and composition.")
            .Replace("Present a clean architectural rendering in an evenly divided grid.", "Present a clean architectural rendering.");
        var prompt = new StringBuilder(catalog.FinalLayout).AppendLine().AppendLine(catalog.FinalSceneRules)
            .AppendLine().AppendLine("Scene constraints:").AppendLine(initial)
            .AppendLine().AppendLine("Avoid:").AppendLine(choice.NegativePrompt)
            .AppendLine().AppendLine("Selected rendering conditions:");
        for (int i = 0; i < RenderingSession.Variables.Length; i++)
            prompt.Append(RenderingSession.Variables[i].Name).Append(": ").AppendLine(RenderingSession.DescribeValue(choice.Values, i));
        return prompt.AppendLine().AppendLine(catalog.InteractionRules).ToString();
    }

    internal async Task RenderAsync() {
        if (busy || closed || conditions == null) return;
        string prompt = PromptEditor.Text;
        if (string.IsNullOrWhiteSpace(prompt) || prompt.Length > 32000) { status.Text = "Enter a final prompt of 1–32000 characters."; return; }
        var chosen = conditions with { Values = (double[])conditions.Values.Clone(), ExploredVariables = (int[])conditions.ExploredVariables.Clone(), ReferencePng = (byte[])conditions.ReferencePng.Clone() };
        var shape = geometry == null ? null : geometry with { ReferencePng = (byte[])geometry.ReferencePng.Clone(), Variables = (GeometryVariableRecord[])geometry.Variables.Clone() };
        byte[] reference = (byte[])(shape?.ReferencePng ?? chosen.ReferencePng).Clone();
        string size = (string)SizeChoice.SelectedItem, quality = (string)QualityChoice.SelectedItem;
        await RunAsync(async token => {
            status.Text = "Generating final image. API usage may apply. The previous preview remains available if this request fails.";
            using var api = createApi();
            var png = await api.GenerateAsync(reference, prompt, size, quality, token).WaitAsync(token);
            token.ThrowIfCancellationRequested(); if (closed) return;
            var image = RenderingPane.Decode(png); var dimensions = size.Split('x');
            if (image.PixelWidth != int.Parse(dimensions[0]) || image.PixelHeight != int.Parse(dimensions[1]))
                throw new InvalidOperationException("The API returned an unexpected image size. The previous preview was kept.");
            accepted = new Generation(shape, chosen, reference, prompt, size, quality, png, DateTimeOffset.UtcNow);
            preview.Source = image; status.Text = $"Preview ready · {size} · {quality}. Finish saves this generation. Edit and Render again to generate a different image.";
        });
    }

    internal async Task FinishAsync() {
        if (busy || closed || accepted == null) return;
        var result = accepted;
        await RunAsync(async token => {
            if (result.Geometry != null) {
                if (applyGeometry == null) throw new InvalidOperationException("The selected final geometry cannot be applied.");
                await applyGeometry(result.Geometry, token);
            }
            token.ThrowIfCancellationRequested(); if (closed) return;
            LastExportDirectory = ResultExport.Save(result.Geometry == null ? "rendering" : "combined", directory => {
                File.WriteAllBytes(Path.Combine(directory, "rendering.png"), result.Png);
                File.WriteAllBytes(Path.Combine(directory, "reference.png"), result.Reference);
                File.WriteAllText(Path.Combine(directory, "prompt.txt"), result.Prompt);
                if (result.Geometry != null) GeometryPane.WriteCandidateFiles(result.Geometry, directory);
                File.WriteAllText(Path.Combine(directory, "metadata.json"), JsonSerializer.Serialize(new {
                    formatVersion = 2, kind = result.Geometry == null ? "ai-rendering" : "combined", createdAt = result.CreatedAt,
                    image = new { file = "rendering.png", kind = "standalone final rendering" }, referenceImage = "reference.png", promptFile = "prompt.txt",
                    generation = new { model = RenderingApi.Model, size = result.Size, quality = result.Quality, outputFormat = "png", count = 1 },
                    sourceGeometry = Source(result.Geometry),
                    renderingConditions = new { node = result.Conditions.SourceNodeId, cell = result.Conditions.Cell,
                        row = result.Conditions.Cell / result.Conditions.Columns + 1, column = result.Conditions.Cell % result.Conditions.Columns + 1,
                        rows = result.Conditions.Rows, columns = result.Conditions.Columns, plane = result.Conditions.Plane, step = result.Conditions.Step,
                        originalGeometry = Source(result.Conditions.OriginalGeometry) },
                    initialPrompt = result.Conditions.InitialPrompt, negativePrompt = result.Conditions.NegativePrompt, finalPrompt = result.Prompt,
                    variables = RenderingSession.Variables.Select((v, i) => new { name = v.Name, value = result.Conditions.Values[i],
                        explored = result.Conditions.ExploredVariables.Contains(i), description = RenderingSession.DescribeValue(result.Conditions.Values, i) }).ToArray()
                }, new JsonSerializerOptions { WriteIndented = true }));
            });
            status.Text = "Saved to " + LastExportDirectory; Finished?.Invoke();
        });
    }
    private static object? Source(GeometryCandidate? source) => source == null ? null : new {
        node = source.SourceNodeId, cell = source.Cell, row = source.Cell / source.Columns + 1, column = source.Cell % source.Columns + 1,
        rows = source.Rows, columns = source.Columns, plane = source.Plane, step = source.Step };
    private async Task RunAsync(Func<CancellationToken, Task> action) {
        if (busy || closed) return;
        busy = true; cancellation?.Dispose(); cancellation = new CancellationTokenSource(); elapsed.Restart(); timer.Start(); UpdateControls();
        try { await action(cancellation.Token); }
        catch (OperationCanceledException) { if (!closed) status.Text = "Cancelled. The previous preview and its generation settings were kept."; }
        catch (Exception error) { if (!closed) status.Text = "Could not complete final rendering: " + error.Message; }
        finally { timer.Stop(); busy = false; if (closed) cancellation?.Dispose(); UpdateControls(); }
    }
    private void UpdateControls() {
        back.IsEnabled = !busy && !closed; RenderButton.IsEnabled = !busy && !closed && conditions != null;
        FinishButton.IsEnabled = !busy && !closed && accepted != null; cancel.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        PromptEditor.IsReadOnly = busy; SizeChoice.IsEnabled = QualityChoice.IsEnabled = !busy && !closed; StateChanged?.Invoke();
        progress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
    }
    public void Cancel() => cancellation?.Cancel();
    public void Shutdown() { closed = true; Cancel(); timer.Stop(); if (!busy) cancellation?.Dispose(); }
}
