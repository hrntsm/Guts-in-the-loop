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
    [STAThread] private static void Main() {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Startup += async (_, _) => {
            IntegratedWindow? window = null;
            try {
                SpsEngine.LibraryDirectory = Path.GetFullPath("build/native/out");
                ResultExport.RootDirectoryOverride = Path.GetFullPath("build/evidence/integrated-results-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(ResultExport.RootDirectoryOverride);
                int initialRequests = await RequestCount();
                var host = new FakeHost(); int viewportCaptures = 0;
                window = new IntegratedWindow(() => host, TimeSpan.FromMilliseconds(5), () => { viewportCaptures++; return Reference(80); }, Api);
                bool closed = false; window.Closed += (_, _) => closed = true;
                var geometry = window.Geometry!; var rendering = window.Rendering;
                Choice(geometry, "RowsChoice", 0); Choice(geometry, "ColumnsChoice", 0);
                ((TextBox)rendering.FindName("InitialPrompt")).Text = "Preserve the same one-storey building.";
                Click(geometry, "StartButton"); await Wait(window);
                Require(host.Applies == 9 && geometry.HistoryItems.Count == 1 && rendering.HistoryItems.Count == 0, "Shape Start must generate only its own grid.");
                SelectGeometry(geometry, 4);
                var sourceA = geometry.SelectedCandidate!;
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
                Require(sourceB.Key != sourceA.Key && !sourceB.ReferencePng.SequenceEqual(sourceA.ReferencePng), "Different geometry inputs must have distinct source identities and reference bytes.");
                ((TextBox)rendering.FindName("InitialPrompt")).Text = "Do not use this changed setup text.";
                Click(geometry, "HandoffButton"); await Wait(window);
                Require(rendering.CurrentSource?.Key == sourceB.Key && rendering.HistoryItems.Count == 3, "A different shape must retain old history and start a new AI run.");
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
                SelectRendering(rendering, 12);
                string blocker = Path.Combine(ResultExport.RootDirectoryOverride!, "blocker"); File.WriteAllText(blocker, "not a directory");
                string resultsRoot = ResultExport.RootDirectoryOverride!; ResultExport.RootDirectoryOverride = blocker;
                Click(rendering, "FinishButton"); await Wait(window);
                Require(!closed && rendering.HistoryItems.Count == 3 && ((Button)rendering.FindName("FinishButton")).IsEnabled,
                    "Failed combined export must retain both histories and allow retry.");
                Require(host.Values.SequenceEqual(sourceA.Variables.Select(v => v.Value)), "Combined Finish must apply source A, not current selection B.");
                ResultExport.RootDirectoryOverride = resultsRoot;
                Click(rendering, "FinishButton"); await Wait(window);
                Require(closed && rendering.LastExportDirectory != null, "Successful combined Finish must save and close the whole window.");
                string directory = rendering.LastExportDirectory!;
                foreach (string name in new[] { "geometry.png", "parameters.json", "rendering.png", "reference.png", "prompt.txt", "metadata.json" })
                    Require(File.Exists(Path.Combine(directory, name)), "Combined result must include " + name);
                Require(File.ReadAllBytes(Path.Combine(directory, "reference.png")).SequenceEqual(sourceA.ReferencePng), "Export reference must be the selected AI source's original bytes.");
                using (var parameters = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "parameters.json"))))
                    Require(parameters.RootElement.GetProperty("variables")[0].GetProperty("value").GetDecimal() == sourceA.Variables[0].Value, "Exported geometry values must match source A exactly.");
                using (var metadata = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "metadata.json")))) {
                    Require(metadata.RootElement.GetProperty("kind").GetString() == "combined", "Combined result must identify its mode.");
                    Require(metadata.RootElement.GetProperty("sourceGeometry").GetProperty("node").GetInt32() == sourceA.SourceNodeId, "Rendering provenance must identify the source shape node.");
                }

                var reference = Reference(120); int captures = 0;
                window = new IntegratedWindow(null, null, () => { captures++; return reference; }, Api);
                Require(window.Geometry == null && ((ColumnDefinition)window.FindName("GeometryColumn")).Width.Value == 0, "AI-only mode must reserve no empty geometry column.");
                bool aiClosed = false; window.Closed += (_, _) => aiClosed = true;
                Click(window.Rendering, "StartButton"); await Wait(window);
                Require(captures == 1 && window.Rendering.HistoryItems.Count == 1, "AI-only Start must capture exactly one viewport reference.");
                SelectRendering(window.Rendering, 12); Click(window.Rendering, "FinishButton"); await Wait(window);
                Require(aiClosed && !File.Exists(Path.Combine(window.Rendering.LastExportDirectory!, "parameters.json")), "AI-only Finish must save without geometry records.");
                using var stats = await JsonDocument.ParseAsync(await control.GetStreamAsync("stats"));
                var requests = stats.RootElement.GetProperty("requests").EnumerateArray().Skip(initialRequests).ToArray();
                Require(requests.Length == 4 && requests.All(r => r.GetProperty("valid").GetBoolean()), "Integrated scenarios must make four valid real HTTP calls.");
                string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
                Require(requests[0].GetProperty("referenceSha256").GetString() == Hash(sourceA.ReferencePng)
                    && requests[1].GetProperty("referenceSha256").GetString() == Hash(sourceA.ReferencePng)
                    && requests[2].GetProperty("referenceSha256").GetString() == Hash(sourceB.ReferencePng)
                    && requests[3].GetProperty("referenceSha256").GetString() == Hash(reference), "Actual HTTP image inputs must match each source shape and the AI-only viewport.");
                Require(requests.Take(3).All(r => r.GetProperty("prompt").GetString()!.Contains("Preserve the same one-storey building.")
                    && !r.GetProperty("prompt").GetString()!.Contains("Do not use this changed setup text")), "All shape runs must reuse the captured AI configuration.");
                Console.WriteLine("PASS: integrated geometry/AI flow, cached handoff hashes, independent progress, source-specific history reuse, connected graph, source-correct export, failure/retry, and AI-only mode through four real HTTP calls.");
            } catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
            finally { window?.RequestClose(); ResultExport.RootDirectoryOverride = null; control.Dispose(); app.Shutdown(); }
        };
        app.Run();
    }
    private sealed class FakeHost : IGeometryHost {
        public int Dimensions => 2;
        public int Applies;
        public decimal[] Values = new decimal[2];
        private readonly Guid[] ids = { Guid.NewGuid(), Guid.NewGuid() };
        public async Task<double[]> ApplyAsync(double[] normalized, CancellationToken token) {
            token.ThrowIfCancellationRequested(); Applies++; await Task.Yield();
            Values = normalized.Select(v => decimal.Round((decimal)v * 10, 4)).ToArray();
            return Values.Select(v => (double)(v / 10)).ToArray();
        }
        public Task<double[]> ApplyRecordedAsync(GeometryVariableRecord[] variables, CancellationToken token) {
            token.ThrowIfCancellationRequested(); Values = variables.Select(v => v.Value).ToArray();
            return Task.FromResult(Values.Select(v => (double)(v / 10)).ToArray());
        }
        public BitmapSource Capture() => Image((byte)(Values[0] * 20), (byte)(Values[1] * 20));
        public GeometryVariableRecord[] GetVariables(double[] coordinates) => Values.Select((v, i) => new GeometryVariableRecord("Slider " + i, ids[i], v, 0, 10, "Float", 4, coordinates[i], (double)(v / 10))).ToArray();
        public void Dispose() { }
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
        for (int i = 0; i < 6000 && (window.Geometry?.IsBusy == true || window.Rendering.IsBusy); i++) await Task.Delay(10);
        Require(window.Geometry?.IsBusy != true && !window.Rendering.IsBusy, "Integrated operation timed out."); await Task.Delay(20);
    }
    private static async Task<int> RequestCount() { using var stats = await JsonDocument.ParseAsync(await control.GetStreamAsync("stats")); return stats.RootElement.GetProperty("requests").GetArrayLength(); }
    private static async Task SetMode(string mode) { using var response = await control.PostAsJsonAsync("control", new { mode }); response.EnsureSuccessStatusCode(); }
    private static void Capture(Window window, string path) {
        var content = (FrameworkElement)window.Content; content.Measure(new Size(1500, 900)); content.Arrange(new Rect(0, 0, 1500, 900)); content.UpdateLayout();
        var image = new RenderTargetBitmap(1500, 900, 96, 96, PixelFormats.Pbgra32); image.Render(content); ResultExport.SavePng(image, path);
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
