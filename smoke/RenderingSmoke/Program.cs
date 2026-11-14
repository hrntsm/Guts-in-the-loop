using System;
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
                Capture(window, "build/evidence/rendering-setup.png");
                Click(window, "StartButton"); await Wait(window);
                Require(captures == 1, "Reference must be captured once.");
                var session = Field<RenderingSession>(window, "session");
                Require(session.Displayed != null && session.Pending == null, "Start must display one matrix. " + Status(window));
                Require(((FrameworkElement)window.FindName("SetupPanel")).Visibility == Visibility.Collapsed, "Setup must disappear.");
                Require(session.Displayed!.Valid.Length == 25 && session.Displayed.Values.All(v => v.Length == 7), "Default grid must map 25 seven-variable conditions.");
                if (!live) {
                    Require(RenderingSession.Variables[1].Describe(.25) == "Watercolor", "Watercolor anchor must be distinct.");
                    Require(RenderingSession.Variables[6].Describe(.5).Contains("Summer") && RenderingSession.Variables[6].Describe(.5).Contains("Autumn"), "Season midpoint must blend summer/autumn.");
                    foreach (int size in new[] {3,5,7,9}) {
                        using var large = new RenderingSession(size, 9, Enumerable.Range(0, 7).ToArray());
                        Require(large.Pending!.Prompt.Length <= 32000, "Supported grids must fit API prompt limits.");
                    }
                    using var subset = new RenderingSession(3, 7, new[] {0,1});
                    Require(subset.Pending!.Values.All(v => v.Skip(2).All(x => x == .5)), "Unselected variables must remain fixed.");
                    SelectCenter(window);
                    Require(Field<int>(window, "selected") == 12 && session.Pending == null, "Click must select without generation.");
                    var previous = Field<BitmapSource>(window, "displayedImage");
                    await SetMode("rate-limit"); Click(window, "NextButton"); await Wait(window);
                    Require(Status(window).Contains("429") && session.Pending?.SelectionStage == 2, "Failed Next must retain one pending search advance.");
                    Require(ReferenceEquals(previous, Field<BitmapSource>(window, "displayedImage")), "Failure must preserve previous image.");
                    string pendingPrompt = session.Pending!.Prompt;
                    Click(window, "RetryButton"); await Wait(window);
                    Require(session.Displayed!.Prompt == pendingPrompt && session.Displayed.SelectionStage == 2 && session.Pending == null, "Retry must reuse the pending conditions without advancing.");
                    Require(Field<int>(window, "selected") == -1, "New response must clear selection.");
                    SelectCenter(window); Click(window, "RegenerateButton"); await Wait(window);
                    Require(session.Displayed.Prompt == pendingPrompt && session.Displayed.SelectionStage == 2 && Field<int>(window, "selected") == -1, "Regenerate must not advance the search.");
                    SelectCenter(window); previous = Field<BitmapSource>(window, "displayedImage");
                    await SetMode("slow"); Click(window, "NextButton"); await Task.Delay(150);
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
                    using var stats = await JsonDocument.ParseAsync(await control.GetStreamAsync("stats"));
                    var requests = stats.RootElement.GetProperty("requests");
                    Require(requests.EnumerateArray().All(r => r.GetProperty("valid").GetBoolean()), "Every request must match the image edit contract.");
                    Require(requests[priorRequests + 1].GetProperty("prompt").GetString() == requests[priorRequests + 2].GetProperty("prompt").GetString(), "HTTP retry must preserve exact prompt.");
                    Console.WriteLine($"PASS: {requests.GetArrayLength() - priorRequests} real HTTP calls through the Postman stand-in; 429, HTML 502, cancellation, Retry, Regenerate, and plane update outcomes verified.");
                }
                Capture(window, live ? "build/evidence/rendering-live-gallery.png" : "build/evidence/rendering-gallery.png");
                SelectCenter(window); Click(window, "FinishButton");
                var crop = Field<BitmapSource>(window, "finalCrop");
                Require(crop.PixelWidth is 204 or 205 && crop.PixelHeight is 204 or 205, "Finish must crop original matrix pixels.");
                Require(((FrameworkElement)window.FindName("SaveButton")).Visibility == Visibility.Visible, "Final preview must offer PNG saving.");
                string cropPath = live ? "build/evidence/rendering-live-crop.png" : "build/evidence/rendering-crop.png";
                RenderingWindow.SavePng(crop, cropPath);
                var saved = RenderingWindow.Decode(File.ReadAllBytes(cropPath));
                Require(saved.PixelWidth == crop.PixelWidth && saved.PixelHeight == crop.PixelHeight, "PNG save must preserve crop dimensions.");
                var last = RenderingWindow.CellBounds(1024, 1024, 5, 5, 24);
                Require(last.X + last.Width == 1024 && last.Y + last.Height == 1024, "Integer crop edges must cover the final pixel.");
                Require(captures == 1, "Next/Retry/Regenerate must never recapture the reference.");
                Console.WriteLine(live ? "PASS: live OpenAI image edit -> production WPF gallery -> selected crop -> PNG, with one fixed reference capture." : "PASS: idle setup, fixed reference, variable mappings, supported grids, cell selection, final crop, PNG round-trip.");
            } catch (Exception error) { Console.Error.WriteLine(error.Message); Environment.ExitCode = 1; }
            finally { window?.Close(); control.Dispose(); app.Shutdown(); }
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
