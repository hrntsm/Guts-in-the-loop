using System;
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
                int initialRequests = await RequestCount();
                var host = new FakeHost(); int viewportCaptures = 0;
                int closePrompts = 0;
                window = new IntegratedWindow(() => host, TimeSpan.FromMilliseconds(5), () => { viewportCaptures++; return Reference(80); }, Api,
                    () => { closePrompts++; return false; });
                bool closed = false; window.Closed += (_, _) => closed = true;
                var geometry = window.Geometry!; var rendering = window.Rendering;
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
                history.Preview(false, firstAiNode);
                Require(((ComboBox)history.FindName("CompletedChoice")).Items.Count == 2 && ((ComboBox)history.FindName("CompletedChoice")).SelectedItem == null,
                    "Multiple completed branches require explicit disambiguation.");
                Require(!history.SelectForFinal(false, firstAiNode, 0), "An arbitrary unchosen historical cell must not be eligible.");
                Require(history.SelectForFinal(true, sourceA.SourceNodeId, sourceA.Cell) && history.SelectForFinal(false, conditionsNode, 12), "Final rendering permits independent geometry A and conditions from geometry B.");
                int requestsBeforeFinal = await RequestCount();
                history.OpenFinal(); var final = window.FinalRendering;
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
    private sealed class FakeHost : IGeometryHost {
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
        public BitmapSource Capture() => Image((byte)(Values[0] * 20), (byte)(Values[1] * 20));
        public GeometryVariableRecord[] GetVariables(double[] coordinates) => Values.Select((v, i) => new GeometryVariableRecord("Slider " + i, ids[i], v, 0, 10, "Float", 4, coordinates[i], (double)(v / 10))).ToArray();
        public void Dispose() => Disposed = true;
    }
    private static BitmapSource Image(byte red, byte green) {
        var pixels = Enumerable.Range(0, 16 * 16).SelectMany(_ => new[] { (byte)150, green, red, (byte)255 }).ToArray();
        var image = BitmapSource.Create(16, 16, 96, 96, PixelFormats.Bgra32, null, pixels, 64); image.Freeze(); return image;
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
