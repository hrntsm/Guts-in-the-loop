using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SequentialGallery;

internal static class Program {
    [STAThread] private static void Main() {
        var app = new Application();
        app.Startup += async (_, _) => {
            GalleryWindow? window = null;
            try {
                SpsEngine.LibraryDirectory = Path.GetFullPath("build/native/out");
                var host = new FakeHost();
                window = new GalleryWindow(() => host, TimeSpan.FromMilliseconds(5));
                bool closed = false; window.Closed += (_, _) => closed = true;
                var history = (HistoryView)window.FindName("History");
                Require(host.Applies == 0, "Opening setup must not apply variables.");
                Click(window, "StartButton"); await Wait(window);
                Require(NodeCount(window) == 1 && host.Applies == 25, "Start must complete and retain one 25-cell grid.");
                Select(window, 12); await Wait(window);
                Require(NodeCount(window) == 2, "Each completed grid must add one history node.");
                int calls = host.Applies;
                history.Preview(0); Require(host.Applies == calls, "History preview must not touch sliders.");
                history.ResumeSelected();
                Require(Field<int?>(window, "activeNodeId") == 0 && host.Applies == calls, "Resume must restore without geometry capture.");
                Select(window, 12); await Wait(window);
                Require(NodeCount(window) == 2 && host.Applies == calls && Field<int?>(window, "activeNodeId") == 1, "Same-cell revisit must reuse its stored child.");
                history.Preview(0); history.ResumeSelected();
                Select(window, 0); await Wait(window);
                Require(NodeCount(window) == 3 && NodeParent(window, 1) == 0 && NodeParent(window, 2) == 0, "A revised selection must retain both branches.");
                history.Preview(1); history.ResumeSelected();
                for (int i = 0; i < 3; i++) { Select(window, 12); await Wait(window); }
                Require(Field<int>(window, "plane") == 2 && Field<int>(window, "zoom") == 0, "History-aware exploration must still update planes after four selections.");
                int complete = NodeCount(window), source = Field<int?>(window, "activeNodeId")!.Value;
                host.Slow = true; calls = host.Applies;
                Select(window, 12);
                for (int i = 0; i < 500 && host.Applies == calls; i++) await Task.Delay(10);
                Require(host.Applies > calls, "Cancellation scenario must reach candidate application.");
                history.Preview(0); history.ResumeSelected();
                Require(Field<int?>(window, "activeNodeId") == source, "Resume must be disabled while generation is busy.");
                Click(window, "CancelButton"); await Wait(window); host.Slow = false;
                Require(NodeCount(window) == complete, "Cancelled partial grids must not become history nodes.");
                history.Preview(source); history.ResumeSelected();
                Require(!Field<bool>(window, "stopped") && !Field<bool>(window, "pendingPrepared"), "History resume must clear failed/cancelled pending work.");
                Select(window, 12); await Wait(window);
                Require(NodeCount(window) == complete + 1, "Exploration must continue after cancelling and resuming history.");
                Capture(window, "build/evidence/geometry-evaluate.png");
                ((TabControl)window.FindName("ExplorationTabs")).SelectedIndex = 1;
                history.Preview(0); Capture(window, "build/evidence/geometry-history.png");
                Require(!((Button)window.FindName("FinishButton")).IsEnabled, "History preview must not expose a Finish action for a different active selection.");
                ((TabControl)window.FindName("ExplorationTabs")).SelectedIndex = 0;
                string exportRoot = Path.GetFullPath("build/evidence/geometry-exports-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(exportRoot);
                string blocker = Path.Combine(exportRoot, "not-a-directory"); File.WriteAllText(blocker, "block export");
                ResultExport.RootDirectoryOverride = blocker;
                Click(window, "FinishButton"); await Wait(window);
                Require(!closed && ((Button)window.FindName("FinishButton")).IsEnabled, "Save failure must retain the result and allow another Finish attempt.");
                ResultExport.RootDirectoryOverride = exportRoot;
                Click(window, "FinishButton"); await Wait(window);
                Require(closed && window.LastExportPath != null && host.RecordedApplies == 2, "Finish must apply recorded values and close only after save success.");
                Require(File.Exists(Path.Combine(window.LastExportPath!, "geometry.png")), "Geometry result must include the selected image.");
                using var metadata = JsonDocument.Parse(File.ReadAllText(Path.Combine(window.LastExportPath!, "parameters.json")));
                Require(metadata.RootElement.GetProperty("variables")[0].GetProperty("value").GetDecimal() == host.Values[0], "JSON must preserve the exact applied decimal value.");
                Require(metadata.RootElement.GetProperty("variables")[0].GetProperty("spsCoordinate").ValueKind == JsonValueKind.Number, "JSON must distinguish raw SPS coordinates from applied values.");
                Require(!Directory.EnumerateDirectories(exportRoot, ".pending-*").Any(), "Failed exports must not leave staging folders.");
                Console.WriteLine("PASS: geometry WPF with real SPS; branch reuse, alternate branches, plane learning, cancellation recovery, preview isolation, and automatic export failure/retry.");
                Console.WriteLine("This check uses a fake geometry host; it does not establish Rhino capture or Grasshopper solver behavior.");
            } catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
            finally { window?.Close(); ResultExport.RootDirectoryOverride = null; app.Shutdown(); }
        };
        app.Run();
    }
    private sealed class FakeHost : IGeometryHost {
        public int Dimensions => 2;
        public int Applies, RecordedApplies;
        public bool Slow;
        public decimal[] Values = new decimal[2];
        private readonly Guid[] ids = { Guid.NewGuid(), Guid.NewGuid() };
        public async Task<double[]> ApplyAsync(double[] normalized, CancellationToken cancellation) {
            Applies++; if (Slow) await Task.Delay(1000, cancellation); else await Task.Yield();
            cancellation.ThrowIfCancellationRequested();
            Values = normalized.Select(v => decimal.Round((decimal)v * 10, 4)).ToArray();
            return Values.Select(v => (double)(v / 10)).ToArray();
        }
        public Task<double[]> ApplyRecordedAsync(GeometryVariableRecord[] variables, CancellationToken cancellation) {
            cancellation.ThrowIfCancellationRequested(); RecordedApplies++;
            Values = variables.Select(v => v.Value).ToArray();
            return Task.FromResult(Values.Select(v => (double)(v / 10)).ToArray());
        }
        public BitmapSource Capture() {
            var bytes = new[] { (byte)(Values[0] * 20), (byte)(Values[1] * 20), (byte)150, (byte)255 };
            var image = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, bytes, 4); image.Freeze(); return image;
        }
        public GeometryVariableRecord[] GetVariables(double[] coordinates) => Values.Select((v, i) => new GeometryVariableRecord("Slider " + i, ids[i], v, 0, 10, "Float", 4, coordinates[i], (double)(v / 10))).ToArray();
        public void Dispose() { }
    }
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    private static IList Nodes(GalleryWindow window) => Field<IList>(window, "history");
    private static int NodeCount(GalleryWindow window) => Nodes(window).Count;
    private static int? NodeParent(GalleryWindow window, int index) => (int?)Nodes(window)[index]!.GetType().GetProperty("ParentId")!.GetValue(Nodes(window)[index]);
    private static void Click(GalleryWindow window, string name) => ((Button)window.FindName(name)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void Select(GalleryWindow window, int index) {
        var cell = Field<Button[]>(window, "cells")[index]; Require(cell.IsEnabled, "Chosen geometry cell must be enabled.");
        cell.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }
    private static async Task Wait(GalleryWindow window) {
        for (int i = 0; i < 6000 && Field<bool>(window, "busy"); i++) await Task.Delay(10);
        Require(!Field<bool>(window, "busy"), "Geometry flow timed out.");
    }
    private static void Capture(Window window, string path) {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var content = (FrameworkElement)window.Content;
        content.Measure(new Size(1068, 810)); content.Arrange(new Rect(0, 0, 1068, 810)); content.UpdateLayout();
        var image = new RenderTargetBitmap(1068, 810, 96, 96, PixelFormats.Pbgra32); image.Render(content); ResultExport.SavePng(image, path);
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
