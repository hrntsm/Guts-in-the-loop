using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SequentialGallery;

internal static class Program {
    private static readonly HttpClient control = new() { BaseAddress = new Uri("http://localhost:4900/openai/") };
    private static RenderingApi Api() => new("simulation-only", new Uri("http://localhost:4900/openai/v1/images/edits"));
    [STAThread] private static void Main(string[] args) {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Startup += async (_, _) => {
            IntegratedWindow? window = null;
            try {
                SpsEngine.LibraryDirectory = Path.GetFullPath("build/native/out");
                ResultExport.RootDirectoryOverride = Path.GetFullPath("build/evidence/integrated-results-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(ResultExport.RootDirectoryOverride);
                if (args.Contains("--close-only")) { await VerifyCloseAsync(); return; }
                if (args.Contains("--view-only")) { await VerifyViewAsync(); return; }
                if (args.Contains("--history-only")) { await VerifyHistoryAsync(); return; }
                int initialRequests = await RequestCount();
                var host = new FakeHost(); int viewportCaptures = 0;
                int closePrompts = 0;
                window = new IntegratedWindow(() => host, TimeSpan.FromMilliseconds(5), () => { viewportCaptures++; return Reference(80); }, Api,
                    () => { closePrompts++; return false; });
                bool closed = false; window.Closed += (_, _) => closed = true;
                var geometry = window.Geometry!; var rendering = window.Rendering;
                ShowOffscreen(window);
                Choice(geometry, "RowsChoice", 0); Choice(geometry, "ColumnsChoice", 0);
                ((TextBox)rendering.FindName("InitialPrompt")).Text = "Preserve the same one-storey building.";
                Click(geometry, "StartButton"); await Wait(window);
                Require(host.Applies == 9 && geometry.HistoryItems.Count == 1 && rendering.HistoryItems.Count == 0, "Shape Start must generate only its own grid.");
                SelectGeometry(geometry, 4);
                var sourceA = geometry.SelectedCandidate!;
                Click(geometry, "ConfirmButton"); Click(geometry, "ConfirmButton");
                Require(geometry.CompletedChoices.Count == 1 && host.Applies == 9 && geometry.HistoryItems.Count == 1, "Geometry confirmation must deduplicate without solving or adding a grid.");
                Require(host.Applies == 9 && geometry.HistoryItems.Count == 1, "A shape click must select without advancing, solving, or requesting AI.");
                Click(geometry, "HandoffButton"); await Wait(window);
                Require(rendering.CurrentSource?.Key == sourceA.Key && rendering.HistoryItems.Count == 1 && viewportCaptures == 0, "Handoff must use the cached shape, not capture the live viewport.");
                Require(await RequestCount() == initialRequests + 1, "New handoff must start exactly one image request.");
                int firstAiNode = rendering.ActiveHistoryNodeId!.Value;
                Layout(window);
                var matrixView = (ImageViewport)rendering.FindName("MatrixView");
                await VerifyViewportAsync(matrixView);
                matrixView.SetZoom(1); Layout(window);
                matrixView.Scroll.ScrollToRightEnd(); matrixView.Scroll.ScrollToBottom(); Layout(window);
                HitCell(matrixView, Field<Button[]>(rendering, "cells")[24]);
                Require(Field<int>(rendering, "selected") == 24, "Scrolled image hit testing must select the displayed cell's original index.");
                double manualZoom = matrixView.Zoom;
                Click(rendering, "ShowPromptButton"); Layout(window);
                Require(matrixView.Zoom == manualZoom && !matrixView.IsFit && matrixView.Scroll.ScrollableWidth > 0,
                    "Show Prompt must preserve manual image zoom while resizing the viewport.");
                Capture(window, "build/evidence/image-scroll-matrix.png");
                Click(rendering, "ShowPromptButton"); matrixView.FitToWindow(); Layout(window);
                Require(await RequestCount() == initialRequests + 1, "Image viewing controls must not make another API request.");
                Click(geometry, "HandoffButton"); await Wait(window);
                Require(await RequestCount() == initialRequests + 1 && rendering.ActiveHistoryNodeId == firstAiNode, "Repeating a handoff must revisit its existing run without an API request.");

                await SetMode("slow"); Click(rendering, "RegenerateButton");
                Require(rendering.IsBusy, "Delayed AI operation must be running.");
                SelectGeometry(geometry, 0);
                Require(((Button)geometry.FindName("NextButton")).IsEnabled && !((Button)geometry.FindName("HandoffButton")).IsEnabled, "AI waiting must permit shape Next but block a new handoff.");
                Click(geometry, "NextButton");
                for (int i = 0; i < 500 && geometry.IsBusy; i++) await Task.Delay(5);
                Require(geometry.HistoryItems.Count == 2 && rendering.IsBusy && rendering.CurrentSource?.Key == sourceA.Key,
                    "Geometry must advance during AI generation without changing the AI source.");
                await Wait(window);
                SelectGeometry(geometry, 4); var sourceB = geometry.SelectedCandidate!;
                Click(geometry, "ConfirmButton");
                Require(sourceB.Key != sourceA.Key && !sourceB.ReferencePng.SequenceEqual(sourceA.ReferencePng), "Different geometry inputs must have distinct source identities and reference bytes.");
                ((TextBox)rendering.FindName("InitialPrompt")).Text = "Do not use this changed setup text.";
                Click(geometry, "HandoffButton"); await Wait(window);
                Require(rendering.CurrentSource?.Key == sourceB.Key && rendering.HistoryItems.Count == 3, "A different shape must retain old history and start a new AI run.");
                int conditionsNode = rendering.ActiveHistoryNodeId!.Value;
                SelectRendering(rendering, 12); Click(rendering, "ConfirmButton"); Click(rendering, "ConfirmButton");
                var finalConditions = rendering.CompletedChoices.Single();
                Require(finalConditions.SourceNodeId == conditionsNode && finalConditions.OriginalGeometry!.Key == sourceB.Key && rendering.HistoryItems.Count == 3, "Latest rendering grid becomes eligible without another generation.");
                int leftNode = geometry.ActiveHistoryNodeId!.Value;
                int requestsBeforeResume = await RequestCount();
                var history = (IntegratedHistoryView)window.FindName("History");
                ((TabControl)window.FindName("ModeTabs")).SelectedIndex = 1;
                history.Preview(false, firstAiNode);
                Require(geometry.ActiveHistoryNodeId == leftNode && rendering.CurrentSource?.Key == sourceB.Key, "Preview must change neither side's active state.");
                Capture(window, "build/evidence/integrated-history.png");
                Require(history.HandoffCount == 2 && history.NodeCount == 5, "Shared History must connect two shape sources to their AI roots.");
                history.ResumeSelected();
                Require(rendering.CurrentSource?.Key == sourceA.Key && geometry.ActiveHistoryNodeId == leftNode && geometry.SelectedCandidate?.Key == sourceB.Key,
                    "AI history resume must restore source A while leaving current shape B unchanged.");
                Require(await RequestCount() == requestsBeforeResume, "History resume must not regenerate.");
                Capture(window, "build/evidence/integrated-evaluate.png");
                SelectRendering(rendering, 12); Click(rendering, "ConfirmButton");
                SelectRendering(rendering, 11); Click(rendering, "ConfirmButton");
                SelectRendering(rendering, 12); Click(rendering, "ConfirmButton");
                Require(rendering.CompletedChoices.Count == 3 && rendering.CompletedChoices.Last().SourceNodeId == firstAiNode
                    && rendering.CompletedChoices.Last().Cell == 12, "Reconfirmation must update the last choice without adding a duplicate.");
                var flatMenu = NodeMenu(history, false, firstAiNode);
                Require(flatMenu.Items.Count == 2 && flatMenu.Items.Cast<MenuItem>().All(item => item.Items.Count == 0),
                    "History nodes must have only the two flat context actions, even with multiple confirmed choices.");
                Require(!history.SelectForFinal(false, firstAiNode, 0), "An arbitrary unchosen historical cell must not be eligible.");
                geometry.ResumeHistory(sourceA.SourceNodeId); SelectGeometry(geometry, sourceA.Cell); Click(geometry, "ConfirmButton");
                MenuClick(NodeMenu(history, true, sourceA.SourceNodeId), 1);
                MenuClick(NodeMenu(history, false, conditionsNode), 1);
                int requestsBeforeFinal = await RequestCount();
                Click(history, "OpenFinalButton"); var final = window.FinalRendering;
                Require(await RequestCount() == requestsBeforeFinal && final.PromptEditor.Text.Contains("Preserve the same one-storey building.")
                    && !final.PromptEditor.Text.Contains("contact sheet of architectural renderings"), "Opening final settings must not generate, and must preserve custom scene constraints.");
                var defaultChoice = finalConditions with { InitialPrompt = RenderingSession.DefaultInitialPrompt };
                string defaultPrompt = FinalRenderingPane.BuildPrompt(defaultChoice);
                Require(!defaultPrompt.Contains("evenly divided grid") && !defaultPrompt.Contains("across cells") && defaultPrompt.Contains("number of storeys"), "Default final prompt must retain building constraints without positive matrix layout.");
                Require((string)final.SizeChoice.SelectedItem == "1024x1024" && (string)final.QualityChoice.SelectedItem == "high", "Final output defaults must be square and high quality.");
                final.PromptEditor.Text = "Render the selected one-storey building as ONE image. Exact accepted instructions.";
                string acceptedPrompt = final.PromptEditor.Text; final.SizeChoice.SelectedIndex = 1;
                await final.RenderAsync();
                Require(final.FinishButton.IsEnabled && !closed, "A standalone result must remain open for review before Finish.");
                Layout(window); await VerifyViewportAsync(final.PreviewView);
                final.PreviewView.SetZoom(1); Layout(window);
                Require(final.PreviewView.Scene!.Width == 1536 && final.PreviewView.Scene.Height == 1024,
                    "Final image inspection must use the generated PNG's full dimensions.");
                final.PreviewView.Scroll.ScrollToRightEnd(); final.PreviewView.Scroll.ScrollToBottom(); Layout(window);
                Capture(window, "build/evidence/image-scroll-final.png");
                final.PreviewView.FitToWindow(); Layout(window);
                window.Close();
                Require(closePrompts == 1 && !closed && final.FinishButton.IsEnabled && !Field<bool>(window, "closing"),
                    "Declining close must preserve the unsaved final preview and session.");
                Capture(window, "build/evidence/integrated-final.png");
                final.PromptEditor.Text = "Unsent draft must never replace the accepted prompt.";
                final.SizeChoice.SelectedIndex = 2; final.QualityChoice.SelectedIndex = 0;
                await SetMode("rate-limit"); await final.RenderAsync();
                Require(final.FinishButton.IsEnabled, "Failed regeneration must preserve the accepted preview.");
                int beforeCancelledRequest = await RequestCount();
                await SetMode("slow"); var cancelled = final.RenderAsync();
                for (int i = 0; i < 100 && await RequestCount() == beforeCancelledRequest && !cancelled.IsCompleted; i++) await Task.Delay(20);
                Require(await RequestCount() == beforeCancelledRequest + 1, "Cancellation must exercise a request already received by the stand-in.");
                final.Cancel(); await cancelled;
                Require(final.FinishButton.IsEnabled, "Cancelled regeneration must preserve the accepted preview.");
                string blocker = Path.Combine(ResultExport.RootDirectoryOverride!, "blocker"); File.WriteAllText(blocker, "not a directory");
                string resultsRoot = ResultExport.RootDirectoryOverride!; ResultExport.RootDirectoryOverride = blocker;
                await final.FinishAsync(); await Wait(window);
                Require(!closed && rendering.HistoryItems.Count == 3 && final.FinishButton.IsEnabled, "Failed export must retain the preview and both histories for retry.");
                Require(host.Values.SequenceEqual(sourceA.Variables.Select(v => v.Value)), "Final Finish applies explicitly chosen A, despite conditions originating from B.");
                ResultExport.RootDirectoryOverride = resultsRoot;
                await final.FinishAsync(); await Wait(window);
                Require(closed && final.LastExportDirectory != null && closePrompts == 1, "Successful final Finish saves and closes without another confirmation.");
                string directory = final.LastExportDirectory!;
                foreach (string name in new[] { "geometry.png", "parameters.json", "rendering.png", "reference.png", "prompt.txt", "metadata.json" })
                    Require(File.Exists(Path.Combine(directory, name)), "Combined result must include " + name);
                Require(File.ReadAllBytes(Path.Combine(directory, "reference.png")).SequenceEqual(sourceA.ReferencePng), "Final reference must be chosen geometry A, not conditions' original B.");
                Require(File.ReadAllText(Path.Combine(directory, "prompt.txt")) == acceptedPrompt, "Saved instructions must match the displayed generation, not a later draft.");
                var standalone = RenderingPane.Decode(File.ReadAllBytes(Path.Combine(directory, "rendering.png")));
                Require(standalone.PixelWidth == 1536 && standalone.PixelHeight == 1024, "Finish must save the complete requested standalone image without cropping.");
                using (var metadata = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "metadata.json")))) {
                    var record = metadata.RootElement;
                    Require(record.GetProperty("finalPrompt").GetString() == acceptedPrompt, "Metadata must retain the accepted image's actual final prompt.");
                    Require(record.GetProperty("generation").GetProperty("size").GetString() == "1536x1024" && record.GetProperty("generation").GetProperty("quality").GetString() == "high", "Saved output settings must match accepted generation, not unsent draft.");
                    Require(record.GetProperty("sourceGeometry").GetProperty("node").GetInt32() == sourceA.SourceNodeId && record.GetProperty("sourceGeometry").GetProperty("cell").GetInt32() == sourceA.Cell, "Metadata must preserve chosen geometry A.");
                    Require(record.GetProperty("renderingConditions").GetProperty("originalGeometry").GetProperty("node").GetInt32() == sourceB.SourceNodeId && record.GetProperty("renderingConditions").GetProperty("node").GetInt32() == conditionsNode, "Metadata must independently preserve conditions provenance from B.");
                    Require(record.GetProperty("variables").EnumerateArray().Select(v => v.GetProperty("value").GetDouble()).SequenceEqual(finalConditions.Values), "Final metadata preserves the exact six-condition vector.");
                }
                await Task.Delay(1600);
                var reference = Reference(120); int captures = 0;
                window = new IntegratedWindow(null, null, () => { captures++; return reference; }, Api,
                    () => throw new Exception("A successful AI-only Finish must not request close confirmation."));
                Require(window.Geometry == null && ((ColumnDefinition)window.FindName("GeometryColumn")).Width.Value == 0, "AI-only mode must reserve no empty geometry column.");
                bool aiClosed = false; window.Closed += (_, _) => aiClosed = true;
                Click(window.Rendering, "StartButton"); await Wait(window);
                Require(captures == 1 && window.Rendering.HistoryItems.Count == 1, "AI-only Start must capture exactly one viewport reference.");
                SelectRendering(window.Rendering, 12); Click(window.Rendering, "ConfirmButton");
                history = (IntegratedHistoryView)window.FindName("History");
                Require(history.SelectForFinal(false, window.Rendering.ActiveHistoryNodeId!.Value, 12), "AI-only confirmed conditions must be eligible.");
                history.OpenFinal(); final = window.FinalRendering; final.SizeChoice.SelectedIndex = 2; final.QualityChoice.SelectedIndex = 1;
                await final.RenderAsync(); await final.FinishAsync(); await Wait(window);
                Require(aiClosed && !File.Exists(Path.Combine(final.LastExportDirectory!, "parameters.json")), "AI-only final rendering saves without fabricated geometry records.");
                using var stats = await JsonDocument.ParseAsync(await control.GetStreamAsync("stats"));
                var requests = stats.RootElement.GetProperty("requests").EnumerateArray().Skip(initialRequests).ToArray();
                Require(requests.Length == 8 && requests.All(r => r.GetProperty("valid").GetBoolean()), "Integrated scenarios must make eight valid real HTTP calls.");
                string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
                Require(requests[0].GetProperty("referenceSha256").GetString() == Hash(sourceA.ReferencePng)
                    && requests[1].GetProperty("referenceSha256").GetString() == Hash(sourceA.ReferencePng)
                    && requests[2].GetProperty("referenceSha256").GetString() == Hash(sourceB.ReferencePng)
                    && requests.Skip(3).Take(3).All(r => r.GetProperty("referenceSha256").GetString() == Hash(sourceA.ReferencePng))
                    && requests.Skip(6).All(r => r.GetProperty("referenceSha256").GetString() == Hash(reference)), "Actual HTTP image inputs must match each source shape and the AI-only viewport.");
                Require(requests.Take(3).All(r => r.GetProperty("prompt").GetString()!.Contains("Preserve the same one-storey building.")
                    && !r.GetProperty("prompt").GetString()!.Contains("Do not use this changed setup text")), "All shape runs must reuse the captured AI configuration.");
                Console.WriteLine("PASS: integrated geometry/AI flow, cached handoff hashes, independent progress, source-specific history reuse, connected graph, source-correct export, failure/retry, and AI-only mode and independent standalone final rendering through eight real HTTP calls.");
            } catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
            finally { window?.ForceClose(); ResultExport.RootDirectoryOverride = null; control.Dispose(); app.Shutdown(); }
        };
        app.Run();
    }
    private static async Task VerifyHistoryAsync() {
        IntegratedWindow? window = null;
        Window? historyWindow = null;
        try {
            var host = new FakeHost(); int apiCalls = 0;
            window = new IntegratedWindow(() => host, null, () => Reference(80),
                () => { apiCalls++; throw new Exception("History actions must not request an image."); }, () => true);
            ShowOffscreen(window);
            var geometry = window.Geometry!;
            Choice(geometry, "RowsChoice", 0); Choice(geometry, "ColumnsChoice", 0);
            Click(geometry, "StartButton"); await Wait(window);
            var liveHistory = (IntegratedHistoryView)window.FindName("History");
            Require(!((MenuItem)NodeMenu(liveHistory, true, 0).Items[1]).IsEnabled, "An unconfirmed source node cannot be used for final rendering.");
            SelectGeometry(geometry, 4); Click(geometry, "ConfirmButton");
            var first = geometry.SelectedCandidate!;
            SelectGeometry(geometry, 0); Click(geometry, "ConfirmButton");
            SelectGeometry(geometry, 4); Click(geometry, "ConfirmButton");
            Require(geometry.CompletedChoices.Count == 2 && geometry.CompletedChoices.Last().Cell == 4 && host.Applies == 9,
                "Reconfirming an earlier geometry choice must update recency without duplicates or computation.");
            Click(geometry, "NextButton"); await Wait(window);
            SelectGeometry(geometry, 8); Click(geometry, "ConfirmButton");
            var second = geometry.SelectedCandidate!;
            int applies = host.Applies;
            ((TabControl)window.FindName("ModeTabs")).SelectedIndex = 1;
            liveHistory.Preview(true, 1);
            MenuClick(NodeMenu(liveHistory, true, 0), 0);
            Require(geometry.ActiveHistoryNodeId == 0 && host.Applies == applies
                && ((TabControl)window.FindName("ModeTabs")).SelectedIndex == 0,
                "Context Resume must restore its clicked node, not the other preview, without solves.");

            var history = new IntegratedHistoryView();
            historyWindow = new Window { Content = history, Width = 1100, Height = 740, ShowActivated = false,
                WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000 };
            historyWindow.Show();
            var image = RenderingPane.Decode(first.ReferencePng);
            var renderItems = Enumerable.Range(0, 3).Select(id => new HistoryItem(id, null, "Plane 1 · Step 1", image, "Start", "Stored conditions")).ToArray();
            var conditionsA = new RenderingChoice(0, 4, 3, 3, 1, 1, Enumerable.Repeat(.5, 6).ToArray(), new[] { 0, 1 }, "", "", first.ReferencePng, first);
            var conditionsB = conditionsA with { SourceNodeId = 1, Cell = 8, OriginalGeometry = second };
            GeometryCandidate[] shapes = { first, second };
            RenderingChoice[] conditions = { conditionsA, conditionsB };
            var sources = new Dictionary<int, GeometryCandidate?> { [0] = first, [1] = second, [2] = second };
            history.SetCompletedChoices(shapes, conditions, true);
            history.SetItems(geometry.HistoryItems, 0, true, renderItems, 0, true, sources, true);
            Require(!((Button)history.FindName("OpenFinalButton")).IsEnabled, "Final must start disabled without both pins.");
            var geometryMenu = NodeMenu(history, true, 0);
            Require(geometryMenu.Items.Count == 2 && (string)((MenuItem)geometryMenu.Items[0]).Header == "Resume from here"
                && (string)((MenuItem)geometryMenu.Items[1]).Header == "Use this node for final"
                && geometryMenu.Items.Cast<MenuItem>().All(item => item.Items.Count == 0), "Each node must have exactly two flat context actions.");
            history.Preview(false, 2); MenuClick(geometryMenu, 1);
            Require(FinalLabel(history).Contains("Geometry: G1 R2 C2") && !((Button)history.FindName("OpenFinalButton")).IsEnabled,
                "Context selection must use the clicked source node and keep Final disabled with one pin.");
            MenuClick(NodeMenu(history, false, 1), 1);
            Require(FinalLabel(history).Contains("Rendering: AI2 R3 C3") && ((Button)history.FindName("OpenFinalButton")).IsEnabled,
                "Independent geometry and rendering pins must be identified in the footer and enable Final.");
            GeometryCandidate? chosenShape = null; RenderingChoice? chosenConditions = null;
            history.FinalRequested += (shape, choice) => { chosenShape = shape; chosenConditions = choice; };
            Click(history, "OpenFinalButton");
            Require(chosenShape?.Key == first.Key && chosenConditions?.Key == conditionsB.Key,
                "Final must open with the independently selected source records.");
            string label = FinalLabel(history);
            history.Preview(false, 0);
            history.SetItems(geometry.HistoryItems, 1, true, renderItems, 2, true, sources, true);
            Require(FinalLabel(history) == label && ((MenuItem)NodeMenu(history, true, 0).Items[1]).IsChecked,
                "Preview changes and graph redraws must retain the selected pair and context checkmarks.");
            MenuClick(NodeMenu(history, true, 1), 1);
            Require(FinalLabel(history).Contains("Geometry: G2 R3 C3") && FinalLabel(history).Contains("Rendering: AI2 R3 C3"),
                "Selecting another node must overwrite only the same-type pin.");
            shapes = new[] { first, second, second with { Cell = 0 } };
            conditions = new[] { conditionsA, conditionsB, conditionsB with { Cell = 4 } };
            history.SetCompletedChoices(shapes, conditions, true);
            Require(FinalLabel(history).Contains("Geometry: G2 R1 C1") && FinalLabel(history).Contains("Rendering: AI2 R2 C2"),
                "Pinned nodes must follow their current last-confirmed candidate without a submenu.");
            var noChoice = (MenuItem)NodeMenu(history, false, 2).Items[1];
            Require(!noChoice.IsEnabled && noChoice.ToolTip.ToString()!.Contains("Confirm"), "Unconfirmed nodes must explain their disabled final action.");
            history.SetCompletedChoices(shapes, conditions, false);
            history.SetItems(geometry.HistoryItems, 1, false, renderItems, 2, false, sources, true);
            Require(!((Button)history.FindName("OpenFinalButton")).IsEnabled && NodeMenu(history, true, 1).Items.Cast<MenuItem>().All(item => !item.IsEnabled),
                "Busy state must disable context actions and Final without losing the pair.");
            history.SetCompletedChoices(shapes, conditions, true);
            history.SetItems(geometry.HistoryItems, 1, true, renderItems, 2, true, sources, true);
            (bool Geometry, int Id)? resumed = null;
            history.ResumeRequested += (shape, id) => resumed = (shape, id);
            history.Preview(false, 2); MenuClick(NodeMenu(history, true, 0), 0);
            Require(resumed == (true, 0) && FinalLabel(history).Contains("Geometry: G2 R1 C1"),
                "Context Resume must target its own node without overwriting the final pair.");
            historyWindow.UpdateLayout(); Capture(historyWindow, "build/evidence/history-context-final.png");

            var aiOnly = new IntegratedHistoryView();
            var viewportChoice = conditionsA with { OriginalGeometry = null };
            aiOnly.SetCompletedChoices(null, new[] { viewportChoice }, true);
            aiOnly.SetItems(null, null, false, renderItems, 0, true, new Dictionary<int, GeometryCandidate?>(), false);
            Require(!((Button)aiOnly.FindName("OpenFinalButton")).IsEnabled, "AI-only Final requires a rendering pin.");
            MenuClick(NodeMenu(aiOnly, false, 0), 1);
            aiOnly.FinalRequested += (shape, choice) => { chosenShape = shape; chosenConditions = choice; };
            Click(aiOnly, "OpenFinalButton");
            Require(chosenShape == null && chosenConditions == viewportChoice && FinalLabel(aiOnly).Contains("original viewport reference"),
                "AI-only Final must require only rendering conditions and preserve the viewport reference.");
            Require(host.Applies == applies && apiCalls == 0, "Context actions must not solve, generate images, or advance exploration.");
            Console.WriteLine("PASS: flat History context actions, exact-node resume, last-confirmed deduplication, final pair overwrite/footer IDs, busy and eligibility guards, AI-only final selection. No HTTP requests.");
        } finally { historyWindow?.Close(); window?.ForceClose(); }
    }
    private static ContextMenu NodeMenu(IntegratedHistoryView history, bool geometry, int node) =>
        ((Button)Field<Dictionary<string, Border>>(history, "cards")[(geometry ? "g:" : "r:") + node].Child).ContextMenu;
    private static void MenuClick(ContextMenu menu, int index) {
        var item = (MenuItem)menu.Items[index]; Require(item.IsEnabled, "Context action must be enabled.");
        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
    }
    private static string FinalLabel(IntegratedHistoryView history) => ((TextBlock)history.FindName("FinalSelectionLabel")).Text;
    private static async Task VerifyCloseAsync() {
        IntegratedWindow? window = null;
        FakeHost? host = null;
        try {
            int prompts = 0, captures = 0;
            bool allow = false, closed = false;
            host = new FakeHost();
            window = new IntegratedWindow(() => host, null, () => { captures++; return Reference(80); }, Api,
                () => { prompts++; return allow; });
            window.Closed += (_, _) => closed = true;
            window.Close();
            Require(prompts == 1 && !closed && !Field<bool>(window, "closing") && captures == 0 && host.Applies == 0,
                "X-style close before generation must ask and No must leave setup untouched.");
            Click(window, "CloseButton");
            Require(prompts == 2 && !closed, "The visible Close action must use the same confirmation.");
            window.ForceClose();
            Require(closed && prompts == 2, "Document/component disposal must close without a blocking confirmation.");

            closed = false; prompts = 0;
            window = new IntegratedWindow(null, null, () => { captures++; return Reference(80); }, Api,
                () => { prompts++; return true; });
            window.Closed += (_, _) => closed = true;
            typeof(RenderingPane).GetMethod("CloseClick", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(window.Rendering, new object[] { window.Rendering, new RoutedEventArgs() });
            Require(closed && prompts == 1 && captures == 0, "AI-only pane Close must ask once even before a final image exists.");

            closed = false; prompts = 0; allow = false;
            host = new FakeHost { PendingApply = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
            window = new IntegratedWindow(() => host, null, () => Reference(80), Api,
                () => { prompts++; return allow; });
            window.Closed += (_, _) => closed = true;
            Choice(window.Geometry!, "RowsChoice", 0); Choice(window.Geometry!, "ColumnsChoice", 0);
            Click(window.Geometry!, "StartButton");
            for (int i = 0; i < 1000 && host.Applies == 0; i++) await Task.Delay(10);
            Require(window.Geometry!.IsBusy && host.Applies == 1, "Busy-close check must reach an in-flight host computation.");
            window.Close();
            Require(prompts == 1 && !closed && !Field<bool>(window, "closing")
                && !Field<CancellationTokenSource>(window.Geometry, "cancellation").IsCancellationRequested,
                "No during generation must not cancel work or mark the session closing.");
            allow = true; window.RequestClose();
            Require(prompts == 2 && !closed && Field<bool>(window, "closing")
                && Field<CancellationTokenSource>(window.Geometry, "cancellation").IsCancellationRequested,
                "Yes must cancel waiting and defer closure until active work can stop.");
            window.Close();
            Require(prompts == 2 && !closed, "Repeated busy closure must not ask again after approval.");
            host.PendingApply.SetResult(); await Wait(window);
            Require(closed && host.Disposed && prompts == 2, "Deferred closure must release the host and ask only once.");

            closed = false;
            host = new FakeHost();
            window = new IntegratedWindow(() => host, null, () => Reference(80), Api,
                () => throw new Exception("A successful geometry Finish must not request close confirmation."));
            window.Closed += (_, _) => closed = true;
            Choice(window.Geometry!, "RowsChoice", 0); Choice(window.Geometry!, "ColumnsChoice", 0);
            Click(window.Geometry!, "StartButton"); await Wait(window);
            SelectGeometry(window.Geometry!, 4); Click(window.Geometry!, "FinishButton"); await Wait(window);
            Require(closed && Directory.Exists(window.Geometry!.LastExportPath), "A successful geometry-only Finish must save and close without a prompt.");
            Console.WriteLine("PASS: unsaved close confirmation before generation, No preserves work, Yes defers busy closure once, lifecycle bypass, and saved geometry Finish bypass. No HTTP requests.");
        } finally { host?.PendingApply?.TrySetResult(); window?.ForceClose(); }
    }
    private static async Task VerifyViewAsync() {
        IntegratedWindow? window = null;
        try {
            var host = new FakeHost { CaptureWidth = 1024, CaptureHeight = 768 };
            window = new IntegratedWindow(() => host, null, () => Reference(80), Api, () => true);
            ShowOffscreen(window);
            Choice(window.Geometry!, "RowsChoice", 0); Choice(window.Geometry!, "ColumnsChoice", 0);
            Click(window.Geometry!, "StartButton"); await Wait(window); Layout(window);
            var view = (ImageViewport)window.Geometry!.FindName("GalleryView");
            Require(view.Scene!.Width == 3072 && view.Scene.Height == 2304, "Geometry zoom must retain original capture dimensions, not its History thumbnail size.");
            await VerifyViewportAsync(view);
            double fitScale = view.Zoom, width = window.Width, height = window.Height;
            window.Width = 1100; window.Height = 700; await Task.Delay(40); window.UpdateLayout();
            Require(view.IsFit && view.Zoom < fitScale && view.Scroll.ScrollableWidth < 1 && view.Scroll.ScrollableHeight < 1,
                "Fit must follow real window resizing without introducing overflow.");
            view.SetZoom(1); window.Width = 1250; window.Height = 780; await Task.Delay(40); window.UpdateLayout();
            Require(!view.IsFit && view.Zoom == 1, "Manual image zoom must remain fixed when the window is resized.");
            window.Width = width; window.Height = height; await Task.Delay(40); Layout(window);
            int applies = host.Applies;
            await VerifyPointerAsync(view, window.Geometry!, host);
            view.SetZoom(1); Layout(window); view.Scroll.ScrollToRightEnd(); view.Scroll.ScrollToBottom(); Layout(window);
            HitCell(view, Field<Button[]>(window.Geometry, "cells")[8]);
            Require(window.Geometry.SelectedCandidate?.Cell == 8 && host.Applies == applies,
                "A zoomed and scrolled geometry cell must retain its index without another solve.");
            Capture(window, "build/evidence/image-scroll-geometry.png");
            view.FitToWindow(); Layout(window); Capture(window, "build/evidence/image-fit-geometry.png");
            ((TabControl)window.FindName("ModeTabs")).SelectedIndex = 1; Layout(window);
            var history = (IntegratedHistoryView)window.FindName("History"); history.Preview(true, 0); Layout(window);
            var historyView = (ImageViewport)history.FindName("PreviewView"); historyView.SetZoom(2); Layout(window);
            Require(historyView.Scroll.ScrollableWidth > 0 && historyView.Scroll.ScrollableHeight > 0 && host.Applies == applies,
                "History image preview must scroll independently without changing the graph or solving.");
            var tiny = new ImageViewport { Scene = new Image() };
            tiny.SetSceneSize(1, 1); tiny.Measure(new Size(500, 400)); tiny.Arrange(new Rect(0, 0, 500, 400)); tiny.UpdateLayout();
            double tinyFit = tiny.Zoom; tiny.SetZoom(tinyFit * 1.25);
            Require(tiny.Zoom >= tinyFit, "Plus must not zoom out when Fit exceeds the normal manual limit.");
            tiny.SetSceneSize(100000, 100000); tiny.UpdateLayout(); double largeFit = tiny.Zoom; tiny.SetZoom(largeFit / 1.25);
            Require(tiny.Zoom <= largeFit, "Minus must not zoom in when Fit is below the normal manual limit.");
            Console.WriteLine("PASS: default Fit, two-axis scrolling, resize, wheel routing and anchoring, right-pan coordinate/lifecycle checks, original image dimensions, cell hit testing, and independent History preview without HTTP. Physical mouse capture was not exercised.");
        } finally { window?.ForceClose(); }
    }
    private static async Task VerifyPointerAsync(ImageViewport view, GeometryPane geometry, FakeHost host) {
        var cells = Field<Button[]>(geometry, "cells");
        int applies = host.Applies, historyCount = geometry.HistoryItems.Count;
        view.SetZoom(1); view.UpdateLayout();
        view.Scroll.ScrollToHorizontalOffset(view.Scroll.ScrollableWidth / 3);
        view.Scroll.ScrollToVerticalOffset(view.Scroll.ScrollableHeight / 3); view.UpdateLayout();
        var position = new Point(view.Scroll.ViewportWidth / 2, view.Scroll.ViewportHeight / 2);
        var anchor = view.Scroll.TranslatePoint(position, view.Scene!);
        view.ZoomAt(view.Zoom * 1.25, position); view.UpdateLayout(); await Task.Delay(20); view.UpdateLayout();
        Require((view.Scene!.TranslatePoint(anchor, view.Scroll) - position).Length < 1e-6,
            "Wheel zoom must preserve the point under the pointer when scroll bounds permit.");
        view.ZoomAt(view.Zoom / 1.25, position); view.UpdateLayout(); await Task.Delay(20); view.UpdateLayout();
        Require((view.Scene.TranslatePoint(anchor, view.Scroll) - position).Length < 1e-6,
            "Zooming back out must preserve the pointer anchor as well.");
        double scale = view.Zoom;
        var wheel = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, 120) { RoutedEvent = Mouse.PreviewMouseWheelEvent };
        cells[4].RaiseEvent(wheel);
        Require(wheel.Handled && view.Zoom > scale, "Routed wheel input over a cell must zoom in and suppress ordinary scrolling.");
        scale = view.Zoom;
        wheel = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120) { RoutedEvent = Mouse.PreviewMouseWheelEvent };
        cells[4].RaiseEvent(wheel);
        Require(wheel.Handled && view.Zoom < scale, "Routed wheel input must also zoom out.");
        wheel = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, 120) { RoutedEvent = Mouse.PreviewMouseWheelEvent };
        view.Scroll.RaiseEvent(wheel);
        Require(!wheel.Handled, "Viewport chrome must not be treated as an image wheel target.");

        view.SetZoom(1); view.UpdateLayout();
        view.Scroll.ScrollToHorizontalOffset(view.Scroll.ScrollableWidth / 3);
        view.Scroll.ScrollToVerticalOffset(view.Scroll.ScrollableHeight / 3); view.UpdateLayout();
        var down = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Right) { RoutedEvent = Mouse.PreviewMouseDownEvent };
        cells[4].RaiseEvent(down);
        // Offscreen inactive windows may have no native mouse provider to grant capture.
        // Exercise the same coordinate core without moving the user's physical pointer.
        Require(down.Handled ? Mouse.Captured == view.Scroll : Field<Point?>(view, "panOrigin") == null,
            "Right down must either capture the pointer or leave drag state untouched.");
        if (!down.Handled) view.BeginPanCore(position);
        Require(view.Scroll.Cursor == Cursors.SizeAll, "Panning must show a movement cursor.");
        var origin = Field<Point?>(view, "panOrigin")!.Value;
        double x = view.Scroll.HorizontalOffset, y = view.Scroll.VerticalOffset;
        view.PanTo(origin - new Vector(75, 55)); view.UpdateLayout();
        Require(Math.Abs(view.Scroll.HorizontalOffset - x - 75) < 1e-6 && Math.Abs(view.Scroll.VerticalOffset - y - 55) < 1e-6,
            "Right dragging must move the image in both scroll directions.");
        var up = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Right) { RoutedEvent = Mouse.PreviewMouseUpEvent };
        view.Scroll.RaiseEvent(up);
        Require(up.Handled && Mouse.Captured != view.Scroll && Field<Point?>(view, "panOrigin") == null && view.Scroll.Cursor != Cursors.SizeAll,
            "Right up must release pointer capture and restore the cursor.");
        Require(geometry.SelectedCandidate == null && host.Applies == applies && geometry.HistoryItems.Count == historyCount,
            "Image wheel and right dragging must not select cells, solve, or advance exploration.");
        HitCell(view, cells[4]);
        Require(geometry.SelectedCandidate?.Cell == 4 && host.Applies == applies,
            "Left hit testing after a right pan must still select the visible cell's original index.");
        view.BeginPanCore(position);
        var move = new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = Mouse.PreviewMouseMoveEvent };
        view.Scroll.RaiseEvent(move);
        if (move.RightButton == MouseButtonState.Released)
            Require(Field<Point?>(view, "panOrigin") == null, "A move after Right has been released must clear a missed drag release.");
        else view.EndPan();
        view.BeginPanCore(position);
        view.Scroll.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = Mouse.LostMouseCaptureEvent });
        Require(Field<Point?>(view, "panOrigin") == null && view.Scroll.Cursor != Cursors.SizeAll, "Capture loss must stop panning.");
        view.BeginPanCore(position); view.SetSceneSize(view.Scene.Width, view.Scene.Height);
        Require(Mouse.Captured != view.Scroll && Field<Point?>(view, "panOrigin") == null, "Replacing displayed image dimensions must stop panning.");
        view.BeginPanCore(position); view.FitToWindow();
        Require(Field<Point?>(view, "panOrigin") == null, "Fit must end an active pan.");
        view.BeginPanCore(position); view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        Require(Mouse.Captured != view.Scroll && Field<Point?>(view, "panOrigin") == null, "Unloading or closing a view must release its drag.");
    }
    private static async Task VerifyViewportAsync(ImageViewport view) {
        view.UpdateLayout();
        Require(view.IsFit && view.Scroll.ScrollableWidth < 1 && view.Scroll.ScrollableHeight < 1, "Images must initially fit with no scroll overflow.");
        view.SetZoom(1); view.UpdateLayout(); await Task.Delay(30); view.UpdateLayout();
        Require(view.Scroll.ScrollableWidth > 0 && view.Scroll.ScrollableHeight > 0, "Native image zoom must provide both scroll directions.");
        view.Scroll.ScrollToRightEnd(); view.Scroll.ScrollToBottom(); view.UpdateLayout(); await Task.Delay(30); view.UpdateLayout();
        Require(view.Scroll.HorizontalOffset > 0 && view.Scroll.VerticalOffset > 0, "Both scrollbars must move to the far image edges.");
        view.FitToWindow(); view.UpdateLayout(); await Task.Delay(30); view.UpdateLayout();
        Require(view.IsFit && view.Scroll.ScrollableWidth < 1 && view.Scroll.ScrollableHeight < 1
            && view.Scroll.HorizontalOffset == 0 && view.Scroll.VerticalOffset == 0, "Fit must remove both overflows and reset scroll offsets after manual inspection.");
    }
    private static void Layout(IntegratedWindow window) {
        var content = (FrameworkElement)window.Content; content.Measure(new Size(1500, 900));
        content.Arrange(new Rect(0, 0, 1500, 900)); content.UpdateLayout();
    }
    private static void ShowOffscreen(IntegratedWindow window) {
        window.ShowActivated = false; window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = window.Top = -20000; window.Show();
    }
    private static void HitCell(ImageViewport view, Button cell) {
        var visible = cell.TransformToAncestor(view.Scroll).TransformBounds(new Rect(cell.RenderSize));
        visible.Intersect(new Rect(0, 0, view.Scroll.ViewportWidth, view.Scroll.ViewportHeight));
        Require(!visible.IsEmpty, "The test target must have a visible area after scrolling.");
        var point = new Point(visible.X + visible.Width / 2, visible.Y + visible.Height / 2);
        DependencyObject? hit = view.Scroll.InputHitTest(point) as DependencyObject;
        while (hit != null && hit is not Button) hit = VisualTreeHelper.GetParent(hit);
        Require(ReferenceEquals(hit, cell), $"The scaled overlay must stay aligned with its visible image cell. Point={point}; cell={cell.ActualWidth}x{cell.ActualHeight}; viewport={view.Scroll.ViewportWidth}x{view.Scroll.ViewportHeight}; offsets={view.Scroll.HorizontalOffset},{view.Scroll.VerticalOffset}; hit={hit?.GetType().Name ?? "null"}");
        ((Button)hit!).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }
    private sealed class FakeHost : IGeometryHost {
        public int CaptureWidth = 16, CaptureHeight = 16;
        public int Dimensions => 2;
        public int Applies;
        public TaskCompletionSource? PendingApply;
        public bool Disposed;
        public decimal[] Values = new decimal[2];
        private readonly Guid[] ids = { Guid.NewGuid(), Guid.NewGuid() };
        public async Task<double[]> ApplyAsync(double[] normalized, CancellationToken token) {
            token.ThrowIfCancellationRequested(); Applies++; await Task.Yield();
            if (PendingApply != null) await PendingApply.Task;
            token.ThrowIfCancellationRequested();
            Values = normalized.Select(v => decimal.Round((decimal)v * 10, 4)).ToArray();
            return Values.Select(v => (double)(v / 10)).ToArray();
        }
        public Task<double[]> ApplyRecordedAsync(GeometryVariableRecord[] variables, CancellationToken token) {
            token.ThrowIfCancellationRequested(); Values = variables.Select(v => v.Value).ToArray();
            return Task.FromResult(Values.Select(v => (double)(v / 10)).ToArray());
        }
        public BitmapSource Capture() => Image((byte)(Values[0] * 20), (byte)(Values[1] * 20), CaptureWidth, CaptureHeight);
        public GeometryVariableRecord[] GetVariables(double[] coordinates) => Values.Select((v, i) => new GeometryVariableRecord("Slider " + i, ids[i], v, 0, 10, "Float", 4, coordinates[i], (double)(v / 10))).ToArray();
        public void Dispose() => Disposed = true;
    }
    private static BitmapSource Image(byte red, byte green, int width = 16, int height = 16) {
        var pixels = Enumerable.Range(0, width * height).SelectMany(_ => new[] { (byte)150, green, red, (byte)255 }).ToArray();
        var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4); image.Freeze(); return image;
    }
    private static byte[] Reference(byte red) {
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(Image(red, 100)));
        using var stream = new MemoryStream(); encoder.Save(stream); return stream.ToArray();
    }
    private static void Choice(FrameworkElement pane, string name, int value) => ((ComboBox)pane.FindName(name)).SelectedIndex = value;
    private static void Click(FrameworkElement pane, string name) {
        var button = (Button)pane.FindName(name); Require(button.IsEnabled, name + " must be enabled."); button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }
    private static void SelectGeometry(GeometryPane pane, int cell) => Field<Button[]>(pane, "cells")[cell].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void SelectRendering(RenderingPane pane, int cell) => Field<Button[]>(pane, "cells")[cell].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    private static async Task Wait(IntegratedWindow window) {
        for (int i = 0; i < 6000 && (window.Geometry?.IsBusy == true || window.Rendering.IsBusy || window.FinalRendering.IsBusy); i++) await Task.Delay(10);
        Require(window.Geometry?.IsBusy != true && !window.Rendering.IsBusy && !window.FinalRendering.IsBusy, "Integrated operation timed out."); await Task.Delay(20);
    }
    private static async Task<int> RequestCount() { using var stats = await JsonDocument.ParseAsync(await control.GetStreamAsync("stats")); return stats.RootElement.GetProperty("requests").GetArrayLength(); }
    private static async Task SetMode(string mode) { using var response = await control.PostAsJsonAsync("control", new { mode }); response.EnsureSuccessStatusCode(); }
    private static void Capture(Window window, string path) {
        var content = (FrameworkElement)window.Content; content.Measure(new Size(1500, 900)); content.Arrange(new Rect(0, 0, 1500, 900)); content.UpdateLayout();
        var image = new RenderTargetBitmap(1500, 900, 96, 96, PixelFormats.Pbgra32); image.Render(content); ResultExport.SavePng(image, path);
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
