using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SequentialGallery;

internal static class Program {
    private static readonly HttpClient control = new() { BaseAddress = new Uri("http://localhost:4900/openai/") };
    [STAThread] private static void Main(string[] args) {
        var app = new Application();
        app.Startup += async (_, _) => {
            RenderingWindow? window = null;
            try {
                SpsEngine.LibraryDirectory = Path.GetFullPath("build/native/out");
                Directory.CreateDirectory("build/evidence");
                ResultExport.RootDirectoryOverride = Path.GetFullPath("build/evidence/rendering-results");
                bool live = args.Length == 2 && args[0] == "--live";
                int priorRequests = 0;
                if (!live) {
                    using var initialStats = await JsonDocument.ParseAsync(await control.GetStreamAsync("stats"));
                    priorRequests = initialStats.RootElement.GetProperty("requests").GetArrayLength();
                }
                byte[] reference = live ? File.ReadAllBytes(args[1]) : MakeReference();
                int captures = 0;
                window = new RenderingWindow(() => { captures++; return reference; }, live
                    ? () => new RenderingApi(Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? "")
                    : () => new RenderingApi("simulation-only", new Uri("http://localhost:4900/openai/v1/images/edits")));
                window.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
                Require(captures == 0 && Field<RenderingSession?>(window, "session") == null, "Opening setup must stay idle.");
                const string fixedScene = "Keep the building single-storey. Preserve its massing and camera angle.";
                const string fixedNegative = "altered building geometry, added storeys, watermarks, extra text, Rhino viewport grid lines, axes";
                Require(((TextBox)window.FindName("NegativePrompt")).Text == RenderingSession.DefaultNegativePrompt, "Negative prompt must load its default from the catalog.");
                if (!live) {
                    ((TextBox)window.FindName("InitialPrompt")).Text = fixedScene;
                    ((TextBox)window.FindName("NegativePrompt")).Text = fixedNegative;
                    var options = Field<CheckBox[]>(window, "variables");
                    var choices = Field<ComboBox[]>(window, "fixedValueChoices");
                    for (int i = 0; i < choices.Length; i++) {
                        Require(choices[i].Items.Count == (i == 5 ? 4 : 5) && !choices[i].IsEnabled, "Explored variables must have disabled anchor choices; Season has four choices, others five.");
                        options[i].IsChecked = false;
                        Require(choices[i].IsEnabled, "Unchecking a variable must enable its fixed-value selector.");
                        options[i].IsChecked = true;
                        Require(!choices[i].IsEnabled, "Checking a variable must restore exploration and disable its fixed-value selector.");
                    }
                    int[] fixedChoiceIndices = { 2, 2, 4, 0, 3, 3 };
                    for (int i = 2; i < options.Length; i++) {
                        options[i].IsChecked = false;
                        choices[i].SelectedIndex = fixedChoiceIndices[i];
                    }
                    Require(captures == 0 && Field<RenderingSession?>(window, "session") == null, "Choosing fixed values must not capture or generate.");
                }
                Capture(window, "build/evidence/rendering-setup.png");
                Click(window, "StartButton"); await Wait(window);
                Require(captures == 1, "Reference must be captured once.");
                var session = Field<RenderingSession>(window, "session");
                Require(session.Displayed != null && session.Pending == null, "Start must display one matrix. " + Status(window));
                Require(((FrameworkElement)window.FindName("SetupPanel")).Visibility == Visibility.Collapsed, "Setup must disappear.");
                Require(session.Displayed!.Valid.Length == 25 && session.Displayed.Values.All(v => v.Length == 6), "Default grid must map 25 six-variable conditions.");
                if (!live) {
                    double[] expectedFixed = { 1, 0, .75, 1 };
                    Require(session.Displayed.Values.All(v => v.Skip(2).SequenceEqual(expectedFixed)), "Every displayed cell must use the chosen weather, activity, vegetation, and season values.");
                    Require(Field<ComboBox[]>(window, "fixedValueChoices").All(c => !c.IsEnabled), "All fixed-value choices must be locked after Start.");
                    Field<ComboBox[]>(window, "fixedValueChoices")[5].SelectedIndex = 0;
                    Require(session.InitialPrompt == fixedScene && session.Displayed.Prompt.Contains(fixedScene), "Initial prompt must be captured and sent in the first matrix request.");
                    Require(session.NegativePrompt == fixedNegative && session.Displayed.Prompt.Contains("[Negative prompt - avoid these results]" + Environment.NewLine + fixedNegative), "Negative results must be sent in a separately labeled avoidance block.");
                    Require(((TextBox)window.FindName("InitialPrompt")).IsReadOnly, "Initial prompt must be frozen after Start.");
                    Require(((TextBox)window.FindName("NegativePrompt")).IsReadOnly, "Negative prompt must be frozen after Start.");
                    ((TextBox)window.FindName("InitialPrompt")).Text = "Do not use this changed text in the running session.";
                    ((TextBox)window.FindName("NegativePrompt")).Text = "Do not use this changed negative text.";
                    Click(window, "ShowPromptButton");
                    Require(((FrameworkElement)window.FindName("PromptPanel")).Visibility == Visibility.Visible, "Show Prompt must open the right-hand panel.");
                    Require(((TextBox)window.FindName("InspectorInitialPrompt")).Text == fixedScene, "Inspector must show the captured initial instructions, not changed setup text.");
                    Require(((TextBox)window.FindName("InspectorNegativePrompt")).Text == fixedNegative, "Inspector must separately show the captured negative instructions.");
                    var valueGrid = (System.Windows.Controls.Primitives.UniformGrid)window.FindName("PromptValueGrid");
                    Require(valueGrid.Rows == 5 && valueGrid.Columns == 5 && valueGrid.Children.Count == 25, "Inspector must mirror the image's 5x5 arrangement.");
                    for (int i = 0; i < valueGrid.Children.Count; i++)
                        for (int j = 0; j < RenderingSession.Variables.Length; j++)
                            Require(((Button)valueGrid.Children[i]).ToolTip.ToString()!.Contains(RenderingSession.Variables[j].Name + ": "
                                + session.Displayed.Values[i][j].ToString("G17", CultureInfo.InvariantCulture)), "Each box must contain exact values for all six variables in its details.");
                    Require(((TextBox)window.FindName("FullPrompt")).Text == session.Displayed.Prompt, "Full prompt must be the exact request for the displayed image.");
                    Click(window, "ShowPromptButton");
                    Require(((FrameworkElement)window.FindName("PromptPanel")).Visibility == Visibility.Collapsed, "Hide Prompt must restore the full image area.");
                    Click(window, "ShowPromptButton");
                    using (var toggleStats = await JsonDocument.ParseAsync(await control.GetStreamAsync("stats")))
                        Require(toggleStats.RootElement.GetProperty("requests").GetArrayLength() == priorRequests + 1, "Toggling prompt details must not generate images.");
                    Require(RenderingSession.Variables.Select(v => v.Name).SequenceEqual(new[] { "TimeOfDay", "RenderingStyle", "Weather", "Activity", "Vegetation", "Season" }), "Color temperature must be removed from the search and inspector vector.");
                    Require(RenderingSession.Variables[1].Describe(.25).Contains("translucent watercolor"), "Light watercolor must include hand-drawn linework and pale washes.");
                    Require(RenderingSession.Variables[0].Describe(.75).Contains("after sunset"), "Twilight must have its own anchor.");
                    Require(RenderingSession.Variables[5].Describe(.5).Contains("Summer") && RenderingSession.Variables[5].Describe(.5).Contains("Autumn"), "Season midpoint must blend summer/autumn.");
                    Require(RenderingSession.DescribePrecipitation(0) == "rain" && RenderingSession.DescribePrecipitation(1.0 / 3) == "rain"
                        && RenderingSession.DescribePrecipitation(2.0 / 3) == "rain" && RenderingSession.DescribePrecipitation(1) == "snow", "Season must control rain versus snow without making every winter cell snowy.");
                    Require(RenderingSession.DescribePrecipitation(5.0 / 6) == "50.0% rain + 50.0% snow", "Autumn-to-winter precipitation must blend continuously.");
                    var winter = new[] { .5, .5, 1.0, .5, .5, 1.0 };
                    Require(RenderingSession.DescribeValue(winter, 2).EndsWith("Precipitation: snow."), "Inspector must resolve heavy winter weather to snow.");
                    winter[2] = 0;
                    Require(!RenderingSession.DescribeValue(winter, 2).Contains("Precipitation:"), "Clear winter must not request precipitation.");
                    foreach (var variable in RenderingSession.Variables) {
                        Require(variable.Anchors.Length == variable.Descriptions.Length, "Every anchor must have a prompt definition.");
                        for (int i = 0; i < variable.Descriptions.Length; i++)
                            Require(session.Displayed.Prompt.Contains(variable.Key + i + ": " + variable.Descriptions[i]), "Every referenced anchor must be defined in the actual request.");
                    }
                    foreach (int size in new[] {3,5,7,9}) {
                        using var large = new RenderingSession(size, 9, Enumerable.Range(0, 6).ToArray(),
                            new string('x', RenderingSession.InitialPromptLimit), new string('y', RenderingSession.NegativePromptLimit));
                        Require(large.Pending!.Prompt.Length <= 32000, "Supported grids must fit API prompt limits.");
                        foreach (int i in Enumerable.Range(0, large.Pending.Values.Length).Where(i => large.Pending.Valid[i] && large.Pending.Values[i][2] > .5)) {
                            string cell = large.Pending.Prompt.Split('\n').Single(line => line.StartsWith($"R{i / 9 + 1}C{i % 9 + 1}: "));
                            Require(cell.Contains("Precipitation=" + RenderingSession.DescribePrecipitation(large.Pending.Values[i][5])), "Each wet-weather cell must carry the precipitation type for its own season.");
                        }
                    }
                    using var subset = new RenderingSession(3, 7, new[] {0,1});
                    Require(subset.Pending!.Values.All(v => v.Skip(2).SequenceEqual(RenderingSession.Variables.Skip(2).Select(variable => variable.DefaultFixedValue))), "Unselected defaults must be actual anchors, including Autumn for Season.");
                    for (int fixedIndex = 0; fixedIndex < RenderingSession.Variables.Length; fixedIndex++) {
                        var variable = RenderingSession.Variables[fixedIndex];
                        for (int anchor = 0; anchor < variable.Anchors.Length; anchor++) {
                            var fixedValues = RenderingSession.Variables.Select(v => v.DefaultFixedValue).ToArray();
                            double expected = fixedValues[fixedIndex] = (double)anchor / (variable.Anchors.Length - 1);
                            using var fixedSession = new RenderingSession(3, 3, Enumerable.Range(0, 6).Where(i => i != fixedIndex).Take(2).ToArray(), fixedValues: fixedValues);
                            fixedValues[fixedIndex] = -1;
                            Require(fixedSession.Pending!.Values.All(v => v[fixedIndex] == expected), "Every fixed anchor must map to its own normalized value, independently of the input array.");
                            Require(fixedSession.Pending.Prompt.Contains($"Fixed in every valid cell: {variable.Key} = {variable.Key}{anchor}."), "The request must use the chosen fixed anchor instead of a midpoint blend.");
                        }
                    }
                    using var noScenePrompts = new RenderingSession(3, 3, new[] {0,1}, "", "");
                    Require(!noScenePrompts.Pending!.Prompt.Contains("[Initial prompt]") && !noScenePrompts.Pending.Prompt.Contains("[Negative prompt"), "Empty optional fields must not fall back to default prompt blocks.");
                    using var negativeOnly = new RenderingSession(3, 3, new[] {0,1}, "", fixedNegative);
                    Require(!negativeOnly.Pending!.Prompt.Contains("[Initial prompt]") && negativeOnly.Pending.Prompt.Contains(fixedNegative), "Negative prompt must apply even when Initial prompt is empty.");
                    SelectCenter(window);
                    Require(Field<int>(window, "selected") == 12 && session.Pending == null, "Click must select without generation.");
                    Require(((Button)valueGrid.Children[12]).BorderBrush == Brushes.Orange, "Image selection must highlight the corresponding value box.");
                    Require(((TextBox)window.FindName("SelectedCellValues")).Text.Contains(session.Displayed.Values[12][0].ToString("G17", CultureInfo.InvariantCulture)), "Selected details must show exact continuous values.");
                    int firstValid = Array.FindIndex(session.Displayed.Valid, v => v);
                    ((Button)valueGrid.Children[firstValid]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Require(Field<int>(window, "selected") == firstValid, "Selecting a valid value box must highlight its image cell.");
                    SelectCenter(window);
                    var previous = Field<BitmapSource>(window, "displayedImage");
                    string displayedPrompt = session.Displayed.Prompt;
                    await SetMode("rate-limit"); Click(window, "NextButton"); await Wait(window);
                    Require(Status(window).Contains("429") && session.Pending?.SelectionStage == 2, "Failed Next must retain one pending search advance.");
                    Require(ReferenceEquals(previous, Field<BitmapSource>(window, "displayedImage")), "Failure must preserve previous image.");
                    Require(((TextBox)window.FindName("FullPrompt")).Text == displayedPrompt
                        && ((TextBlock)window.FindName("PromptContext")).Text.Contains("pending"), "Failed pending requests must not replace the displayed image's inspector data.");
                    string pendingPrompt = session.Pending!.Prompt;
                    Click(window, "RetryButton"); await Wait(window);
                    Require(session.Displayed!.Prompt == pendingPrompt && session.Displayed.SelectionStage == 2 && session.Pending == null, "Retry must reuse the pending conditions without advancing.");
                    Require(((TextBox)window.FindName("FullPrompt")).Text == pendingPrompt, "Successful replacement must update the inspector to its actual request.");
                    Require(Field<int>(window, "selected") == -1, "New response must clear selection.");
                    SelectCenter(window); Click(window, "RegenerateButton"); await Wait(window);
                    Require(session.Displayed.Prompt == pendingPrompt && session.Displayed.SelectionStage == 2 && Field<int>(window, "selected") == -1, "Regenerate must not advance the search.");
                    SelectCenter(window); previous = Field<BitmapSource>(window, "displayedImage");
                    await SetMode("slow"); Click(window, "NextButton"); await Task.Delay(150);
                    var tree = (HistoryView)window.FindName("HistoryTree");
                    var originalNode = Field<RenderingHistoryNode>(window, "activeNode");
                    tree.Preview(0); tree.ResumeSelected();
                    Require(ReferenceEquals(originalNode, Field<RenderingHistoryNode>(window, "activeNode")), "Busy history preview must not resume or redirect generation.");
                    Click(window, "CancelButton"); await Wait(window);
                    Require(session.Pending?.SelectionStage == 3 && ReferenceEquals(previous, Field<BitmapSource>(window, "displayedImage")), "Cancel must preserve previous image and pending next conditions.");
                    Click(window, "RetryButton"); await Wait(window);
                    var retried = Field<BitmapSource>(window, "displayedImage");
                    await Task.Delay(1800);
                    Require(ReferenceEquals(retried, Field<BitmapSource>(window, "displayedImage")), "Late cancelled response must not replace the retried image.");
                    Require(session.Displayed!.SelectionStage == 3 && session.Pending == null, "Cancelled Next must not be counted twice.");
                    for (int i = 0; i < 2; i++) { SelectCenter(window); Click(window, "NextButton"); await Wait(window); }
                    Require(session.Displayed!.Plane == 2 && session.Displayed.SelectionStage == 1, "Fourth committed selection must learn the next plane.");
                    SelectCenter(window); previous = Field<BitmapSource>(window, "displayedImage");
                    await SetMode("html-error"); Click(window, "RegenerateButton"); await Wait(window);
                    Require(Status(window).Contains("502") && ReferenceEquals(previous, Field<BitmapSource>(window, "displayedImage")), "Non-JSON gateway failures must preserve the image.");
                    Click(window, "RetryButton"); await Wait(window);
                    var nodes = Field<List<RenderingHistoryNode>>(window, "history");
                    var learnedNode = Field<RenderingHistoryNode>(window, "activeNode");
                    var learnedGrid = learnedNode.State.Engine.Grid();
                    Require(nodes.All(n => n.State.Batch != null) && nodes.Count == 7, "Only seven successfully completed matrices must become history nodes in the ten-call flow.");
                    foreach (var variant in nodes.Where(n => n.EdgeLabel == "Regenerate")) {
                        var parent = nodes.Single(n => n.Id == variant.ParentId);
                        Require(variant.State.Batch.Prompt == parent.State.Batch.Prompt && variant.State.Plane == parent.State.Plane
                            && variant.State.Zoom == parent.State.Zoom, "Regenerated matrices must remain separate variants at the same optimizer stage.");
                    }
                    var root = nodes[0];
                    var firstChild = nodes.Single(n => n.ParentId == root.Id && n.Cell == 12);
                    ((TabControl)window.FindName("ModeTabs")).SelectedIndex = 1;
                    Require(new[] { "NextButton", "RegenerateButton", "FinishButton", "ShowPromptButton", "RetryButton" }
                        .All(name => !((Button)window.FindName(name)).IsEnabled), "Evaluate actions must be disabled while previewing History.");
                    tree.Preview(root.Id);
                    Require(((TextBox)tree.FindName("PreviewDetails")).Text.Contains("R1 C1")
                        && ((TextBox)tree.FindName("PreviewDetails")).Text.Contains("TimeOfDay="), "History preview must expose per-cell explored values without resuming.");
                    Capture(window, "build/evidence/rendering-history.png");
                    tree.ResumeSelected();
                    Require(ReferenceEquals(session.Displayed, root.State.Batch) && ((TabControl)window.FindName("ModeTabs")).SelectedIndex == 0,
                        "Explicit Resume must restore the saved batch and switch to Evaluate.");
                    SelectCenter(window); Click(window, "NextButton"); await Wait(window);
                    Require(ReferenceEquals(Field<RenderingHistoryNode>(window, "activeNode"), firstChild), "Repeating the same source cell must reuse its child.");
                    using (var reuseStats = await JsonDocument.ParseAsync(await control.GetStreamAsync("stats")))
                        Require(reuseStats.RootElement.GetProperty("requests").GetArrayLength() == priorRequests + 10, "History resume and existing-path reuse must not call the image provider.");
                    tree.Preview(root.Id); tree.ResumeSelected();
                    int alternative = Array.FindIndex(root.State.Batch.Valid, valid => valid);
                    Field<Button[]>(window, "cells")[alternative].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Click(window, "NextButton"); await Wait(window);
                    Require(nodes.Count(n => n.ParentId == root.Id && n.Cell != null) == 2, "A different choice must preserve the original child and add a branch.");
                    ((TabControl)window.FindName("ModeTabs")).SelectedIndex = 1;
                    tree.Preview(root.Id);
                    Capture(window, "build/evidence/rendering-history.png");
                    tree.Preview(learnedNode.Id); tree.ResumeSelected();
                    Require(ReferenceEquals(session.Displayed, learnedNode.State.Batch)
                        && learnedGrid.Zip(learnedNode.State.Engine.Grid()).All(pair => pair.First.SequenceEqual(pair.Second)), "Returning from another branch must preserve the learned node's exact grid.");
                    SelectCenter(window); await SetMode("slow"); Click(window, "NextButton"); await Task.Delay(150);
                    tree.Preview(root.Id); Click(window, "CancelButton"); await Wait(window);
                    tree.ResumeSelected();
                    Require(session.Pending == null && ReferenceEquals(session.Displayed, root.State.Batch), "Resuming after cancellation must abandon the unresolved pending request.");
                    await Task.Delay(1800);
                    Require(ReferenceEquals(session.Displayed, root.State.Batch), "Late cancelled replies must not attach to the restored branch.");
                    using var stats = await JsonDocument.ParseAsync(await control.GetStreamAsync("stats"));
                    var requests = stats.RootElement.GetProperty("requests");
                    Require(requests.EnumerateArray().All(r => r.GetProperty("valid").GetBoolean()), "Every request must match the image edit contract.");
                    Require(requests.EnumerateArray().Skip(priorRequests).All(r => r.GetProperty("prompt").GetString()!.Contains(fixedScene)
                        && r.GetProperty("prompt").GetString()!.Contains(fixedNegative)
                        && !r.GetProperty("prompt").GetString()!.Contains("Do not use this changed")), "Both prompt categories must stay unchanged through Next, Retry, Regenerate, and cancellation.");
                    Require(session.Displayed.Values.All(v => v.Skip(2).SequenceEqual(expectedFixed)), "Fixed values must persist after zoom and plane learning, even if hidden setup controls change.");
                    Require(requests.EnumerateArray().Skip(priorRequests).All(r => new[] {
                        "Fixed in every valid cell: W = W4.", "Fixed in every valid cell: A = A0.",
                        "Fixed in every valid cell: V = V3.", "Fixed in every valid cell: S = S3."
                    }.All(r.GetProperty("prompt").GetString()!.Contains)), "Actual HTTP requests must retain all chosen fixed conditions across retries and regeneration.");
                    Require(requests[priorRequests + 1].GetProperty("prompt").GetString() == requests[priorRequests + 2].GetProperty("prompt").GetString(), "HTTP retry must preserve exact prompt.");
                    Console.WriteLine($"PASS: {requests.GetArrayLength() - priorRequests} real HTTP calls through the Postman stand-in; 429, HTML 502, cancellation, Retry, Regenerate, and plane update outcomes verified.");
                }
                Capture(window, live ? "build/evidence/rendering-live-gallery.png" : "build/evidence/rendering-gallery.png");
                SelectCenter(window);
                string exportedPrompt = session.Displayed!.Prompt;
                double[] exportedValues = session.Displayed.Values[12];
                if (!live) {
                    string blockedDestination = Path.GetFullPath("build/evidence/rendering-export-blocker");
                    File.WriteAllText(blockedDestination, "Not a directory.");
                    ResultExport.RootDirectoryOverride = blockedDestination;
                    Click(window, "FinishButton");
                    Require(Status(window).Contains("Could not save") && !Field<bool>(window, "closed")
                        && Field<List<RenderingHistoryNode>>(window, "history").Count > 0, "Save failure must retain the window and history for retry.");
                    ResultExport.RootDirectoryOverride = Path.GetFullPath("build/evidence/rendering-results");
                }
                Click(window, "FinishButton");
                var crop = Field<BitmapSource>(window, "finalCrop");
                Require(crop.PixelWidth is 204 or 205 && crop.PixelHeight is 204 or 205, "Finish must crop original matrix pixels.");
                Require(Field<bool>(window, "closed") && Field<List<RenderingHistoryNode>>(window, "history").Count == 0, "Successful Finish must close and release session history.");
                string exportDirectory = window.LastExportDirectory ?? throw new InvalidOperationException("Finish did not save a result directory.");
                Require(File.ReadAllBytes(Path.Combine(exportDirectory, "reference.png")).SequenceEqual(reference), "Export must preserve the original reference image bytes.");
                Require(File.ReadAllText(Path.Combine(exportDirectory, "prompt.txt")) == exportedPrompt, "Export must record the exact source matrix prompt.");
                using (var metadata = JsonDocument.Parse(File.ReadAllText(Path.Combine(exportDirectory, "metadata.json")))) {
                    Require(metadata.RootElement.GetProperty("selectedCell").GetProperty("index").GetInt32() == 12, "Export must identify the selected cell.");
                    Require(metadata.RootElement.GetProperty("variables").EnumerateArray().Select(v => v.GetProperty("value").GetDouble()).SequenceEqual(exportedValues), "Export must include the selected cell's exact parameter vector.");
                    if (!live) Require(metadata.RootElement.GetProperty("initialPrompt").GetString() == fixedScene
                        && metadata.RootElement.GetProperty("negativePrompt").GetString() == fixedNegative, "Export must retain both frozen prompt categories.");
                }
                string cropPath = live ? "build/evidence/rendering-live-crop.png" : "build/evidence/rendering-crop.png";
                RenderingWindow.SavePng(crop, cropPath);
                var saved = RenderingWindow.Decode(File.ReadAllBytes(cropPath));
                Require(saved.PixelWidth == crop.PixelWidth && saved.PixelHeight == crop.PixelHeight, "PNG save must preserve crop dimensions.");
                var last = RenderingWindow.CellBounds(1024, 1024, 5, 5, 24);
                Require(last.X + last.Width == 1024 && last.Y + last.Height == 1024, "Integer crop edges must cover the final pixel.");
                Require(captures == 1, "Next/Retry/Regenerate must never recapture the reference.");
                Console.WriteLine(live ? "PASS: live OpenAI image edit -> production WPF gallery -> selected crop -> PNG, with one fixed reference capture." : "PASS: idle setup, fixed reference, variable mappings, supported grids, cell selection, final crop, PNG round-trip.");
            } catch (Exception error) { Console.Error.WriteLine(error.Message); Environment.ExitCode = 1; }
            finally { window?.Close(); ResultExport.RootDirectoryOverride = null; control.Dispose(); app.Shutdown(); }
        };
        app.Run();
    }
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    private static string Status(RenderingWindow window) => ((TextBlock)window.FindName("Status")).Text;
    private static void Click(RenderingWindow window, string name) {
        var button = (Button)window.FindName(name);
        Require(button.IsEnabled, name + " must be enabled. " + Status(window));
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }
    private static void SelectCenter(RenderingWindow window) {
        var cells = Field<Button[]>(window, "cells"); var cell = cells[cells.Length / 2];
        Require(cell.IsEnabled, "Center cell must be selectable. " + Status(window));
        cell.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }
    private static async Task Wait(RenderingWindow window) {
        for (int i = 0; i < 18000 && Field<bool>(window, "busy"); i++) await Task.Delay(20);
        Require(!Field<bool>(window, "busy"), "Timed out waiting for the UI. " + Status(window));
    }
    private static async Task SetMode(string mode) {
        using var response = await control.PostAsJsonAsync("control", new { mode }); response.EnsureSuccessStatusCode();
    }
    private static byte[] MakeReference() {
        var image = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, Enumerable.Repeat((byte)255, 16).ToArray(), 8);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = new MemoryStream(); encoder.Save(stream); return stream.ToArray();
    }
    private static void Capture(RenderingWindow window, string path) {
        var content = (FrameworkElement)window.Content;
        content.Measure(new Size(1068, 810)); content.Arrange(new Rect(0, 0, 1068, 810)); content.UpdateLayout();
        var image = new RenderTargetBitmap(1068, 810, 96, 96, PixelFormats.Pbgra32); image.Render(content);
        RenderingWindow.SavePng(image, path);
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
