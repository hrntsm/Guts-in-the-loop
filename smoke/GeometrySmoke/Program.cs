using System;
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
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Startup += async (_, _) => {
            GeometryPane? pane = null;
            Window? window = null;
            try {
                SpsEngine.LibraryDirectory = Path.GetFullPath("build/native/out");
                var host = new FakeHost();
                pane = new GeometryPane(() => host, TimeSpan.FromMilliseconds(5));
                window = new Window { Content = pane, Width = 720, Height = 850 };
                bool finished = false, closed = false;
                window.Closed += (_, _) => closed = true;
                pane.Finished += () => { finished = true; pane.Shutdown(); window.Close(); };
                GeometryCandidate? handedOff = null;
                pane.HandoffRequested += source => handedOff = source;
                Require(host.Applies == 0, "Opening setup must not apply variables.");
                Click(pane, "StartButton"); await Wait(pane);
                Require(pane.HistoryItems.Count == 1 && host.Applies == 25, "Start must retain one complete grid. " + ((TextBlock)pane.FindName("Status")).Text);
                int calls = host.Applies;
                Select(pane, 12);
                Require(host.Applies == calls && pane.HistoryItems.Count == 1, "A geometry click must only select, without solving or advancing SPS.");
                Require(pane.SelectedCandidate?.SourceNodeId == 0 && pane.SelectedCandidate.Cell == 12, "Selected shape must identify its displayed source node and cell.");
                pane.SetPeerBusy(true);
                Require(((Button)pane.FindName("NextButton")).IsEnabled && !((Button)pane.FindName("HandoffButton")).IsEnabled
                    && !((Button)pane.FindName("FinishButton")).IsEnabled, "AI activity must block only handoff and Finish, not shape exploration.");
                pane.SetPeerBusy(false);
                Click(pane, "HandoffButton");
                Require(handedOff?.Key == "0:12" && host.Applies == calls && pane.HistoryItems.Count == 1, "Handoff must emit the selected cached shape without a solve or zoom.");
                Require(handedOff!.ReferencePng.Length > 0 && handedOff.Variables.Length == 2, "Handoff must retain its captured PNG and exact variable records.");
                Click(pane, "NextButton"); await Wait(pane);
                Require(pane.HistoryItems.Count == 2 && pane.SelectedCandidate == null, "Next must add a complete grid and clear the previous cell selection.");
                calls = host.Applies;
                pane.ResumeHistory(0);
                Require(pane.ActiveHistoryNodeId == 0 && host.Applies == calls, "History restoration must not apply sliders or capture images.");
                Select(pane, 12); Click(pane, "NextButton"); await Wait(pane);
                Require(pane.HistoryItems.Count == 2 && host.Applies == calls && pane.ActiveHistoryNodeId == 1, "Same-cell Next must reuse the saved child without another solve.");
                pane.ResumeHistory(0);
                Select(pane, 0); Click(pane, "NextButton"); await Wait(pane);
                Require(pane.HistoryItems.Count == 3 && pane.HistoryItems[1].ParentId == 0 && pane.HistoryItems[2].ParentId == 0, "A different selection must preserve both branches.");
                pane.ResumeHistory(1);
                for (int i = 0; i < 3; i++) { Select(pane, 12); Click(pane, "NextButton"); await Wait(pane); }
                Require(Field<int>(pane, "plane") == 2 && Field<int>(pane, "zoom") == 0, "Four committed selections must learn a new plane.");
                int complete = pane.HistoryItems.Count, source = pane.ActiveHistoryNodeId!.Value;
                host.Slow = true; calls = host.Applies;
                Select(pane, 12); Click(pane, "NextButton");
                for (int i = 0; i < 500 && host.Applies == calls; i++) await Task.Delay(10);
                Require(host.Applies > calls && pane.IsBusy, "Cancellation scenario must reach candidate application.");
                pane.ResumeHistory(0);
                Require(pane.ActiveHistoryNodeId == source, "Resume must be blocked during shape generation.");
                pane.Cancel(); await Wait(pane); host.Slow = false;
                Require(pane.HistoryItems.Count == complete, "Cancelled partial grids must not enter history.");
                pane.ResumeHistory(source);
                Require(!Field<bool>(pane, "stopped") && !Field<bool>(pane, "pendingPrepared"), "Resume must abandon cancelled pending work.");
                Select(pane, 12); Click(pane, "NextButton"); await Wait(pane);
                Require(pane.HistoryItems.Count == complete + 1, "Exploration must continue after cancellation and restoration.");
                Select(pane, 12);
                var finalCandidate = pane.SelectedCandidate!;
                Capture(window, "build/evidence/geometry-evaluate.png");
                string exportRoot = Path.GetFullPath("build/evidence/geometry-exports-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(exportRoot);
                string blocker = Path.Combine(exportRoot, "not-a-directory"); File.WriteAllText(blocker, "block export");
                ResultExport.RootDirectoryOverride = blocker;
                Click(pane, "FinishButton"); await Wait(pane);
                Require(!closed && !finished && ((Button)pane.FindName("FinishButton")).IsEnabled, "Save failure must retain the result for retry.");
                ResultExport.RootDirectoryOverride = exportRoot;
                Click(pane, "FinishButton"); await Wait(pane);
                Require(closed && finished && pane.LastExportPath != null && host.RecordedApplies == 2, "Successful geometry Finish must apply recorded values and notify the owning window.");
                Require(File.ReadAllBytes(Path.Combine(pane.LastExportPath!, "geometry.png")).SequenceEqual(finalCandidate.ReferencePng), "Exported shape image must be the selected candidate's source capture.");
                using var metadata = JsonDocument.Parse(File.ReadAllText(Path.Combine(pane.LastExportPath!, "parameters.json")));
                Require(metadata.RootElement.GetProperty("variables")[0].GetProperty("value").GetDecimal() == host.Values[0], "JSON must preserve exact applied decimals.");
                Require(metadata.RootElement.GetProperty("variables")[0].GetProperty("spsCoordinate").ValueKind == JsonValueKind.Number, "JSON must distinguish raw coordinates from applied values.");
                Require(metadata.RootElement.GetProperty("sourceHistoryNode").GetInt32() == finalCandidate.SourceNodeId, "Export source must be the selected displayed grid, not its preceding selection.");
                Require(pane.HistoryItems.Count == 0, "Shutdown must discard window-lifetime history.");
                Require(!Directory.EnumerateDirectories(exportRoot, ".pending-*").Any(), "Export failures must not leave valid-looking partial output.");
                Console.WriteLine("PASS: embeddable geometry pane with real SPS; click-only selection, explicit Next, cached handoff, independent busy gating, branch reuse, learning, cancellation recovery, and exact automatic export.");
                Console.WriteLine("Uses a fake geometry host; Rhino solver/capture are validated separately.");
            } catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
            finally { pane?.Shutdown(); window?.Close(); ResultExport.RootDirectoryOverride = null; app.Shutdown(); }
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
    private static void Click(GeometryPane window, string name) => ((Button)window.FindName(name)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void Select(GeometryPane window, int index) {
        var cell = Field<Button[]>(window, "cells")[index]; Require(cell.IsEnabled, "Chosen geometry cell must be enabled.");
        cell.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }
    private static async Task Wait(GeometryPane window) {
        for (int i = 0; i < 6000 && window.IsBusy; i++) await Task.Delay(10);
        Require(!window.IsBusy, "Geometry flow timed out.");
    }
    private static void Capture(Window window, string path) {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var content = (FrameworkElement)window.Content;
        content.Measure(new Size(1068, 810)); content.Arrange(new Rect(0, 0, 1068, 810)); content.UpdateLayout();
        var image = new RenderTargetBitmap(1068, 810, 96, 96, PixelFormats.Pbgra32); image.Render(content); ResultExport.SavePng(image, path);
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
