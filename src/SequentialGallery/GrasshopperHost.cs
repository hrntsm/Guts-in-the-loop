using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Grasshopper;
using Grasshopper.GUI.Base;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;
using Rhino;
using Rhino.Display;
using Rhino.DocObjects;

namespace SequentialGallery;

internal sealed class GrasshopperHost : IDisposable
{
    private readonly GH_Document document;
    private readonly GH_NumberSlider[] sliders;
    private readonly decimal[] minimum, maximum;
    private readonly GH_SliderAccuracy[] accuracy;
    private readonly int[] digits;
    private readonly RhinoDoc rhino;
    private readonly RhinoView view;
    private readonly ViewportInfo projection;
    private readonly DisplayModeDescription mode;
    private readonly System.Drawing.Size captureSize;
    public int Dimensions => sliders.Length;
    public GrasshopperHost(GH_Document doc, IEnumerable<IGH_Param> sources) {
        document = doc;
        var inputs = sources.ToArray();
        if (inputs.Length < 2) throw new InvalidOperationException("Connect at least two Number Sliders.");
        if (inputs.Any(s => s is not GH_NumberSlider)) throw new InvalidOperationException("Variables must connect directly to Number Sliders.");
        sliders = inputs.Cast<GH_NumberSlider>().ToArray();
        if (sliders.Select(s => s.InstanceGuid).Distinct().Count() != sliders.Length)
            throw new InvalidOperationException("Each variable must reference a different slider.");
        if (sliders.Any(s => s.Slider.Maximum <= s.Slider.Minimum))
            throw new InvalidOperationException("Use sliders with a non-zero range.");
        minimum = sliders.Select(s => s.Slider.Minimum).ToArray();
        maximum = sliders.Select(s => s.Slider.Maximum).ToArray();
        accuracy = sliders.Select(s => s.Slider.Type).ToArray();
        digits = sliders.Select(s => s.Slider.DecimalPlaces).ToArray();
        rhino = RhinoDoc.ActiveDoc ?? throw new InvalidOperationException("No Rhino document is open.");
        view = rhino.Views.ActiveView ?? throw new InvalidOperationException("No Rhino viewport is available.");
        projection = new ViewportInfo(view.ActiveViewport);
        mode = DisplayModeDescription.GetDisplayMode(view.ActiveViewport.DisplayMode.Id);
        captureSize = view.ActiveViewport.Size;
        if (captureSize.Width <= 0 || captureSize.Height <= 0) throw new InvalidOperationException("The viewport has no drawable area.");
    }
    private void Validate() {
        if (!GH_Document.EnableSolutions || !document.Enabled) throw new InvalidOperationException("Enable the Grasshopper solver before continuing.");
        if (document.SolutionDepth != 0) throw new InvalidOperationException("Grasshopper is already computing. Try again when it finishes.");
        if (Instances.ActiveCanvas?.Document != document || RhinoDoc.ActiveDoc != rhino)
            throw new InvalidOperationException("Keep the original Rhino and Grasshopper documents active during exploration.");
        if (!rhino.Views.Any(v => v.ActiveViewportID == view.ActiveViewportID))
            throw new InvalidOperationException("The captured viewport was closed.");
        for (int i = 0; i < sliders.Length; i++)
            if (!document.Objects.Contains(sliders[i]) || sliders[i].Slider.Minimum != minimum[i] || sliders[i].Slider.Maximum != maximum[i]
                || sliders[i].Slider.Type != accuracy[i] || sliders[i].Slider.DecimalPlaces != digits[i])
                throw new InvalidOperationException("A registered slider was removed or changed. Reopen the gallery.");
    }
    public async Task<double[]> ApplyAsync(double[] normalized, CancellationToken cancellation) {
        cancellation.ThrowIfCancellationRequested();
        Validate();
        if (normalized.Length != sliders.Length || normalized.Any(x => !double.IsFinite(x) || x < 0 || x > 1))
            throw new InvalidOperationException("Candidate is out of range.");
        for (int i = 0; i < sliders.Length; i++) {
            decimal value = minimum[i] + (decimal)normalized[i] * (maximum[i] - minimum[i]);
            value = accuracy[i] switch {
                GH_SliderAccuracy.Integer => decimal.Round(value, 0, MidpointRounding.AwayFromZero),
                GH_SliderAccuracy.Even => 2 * decimal.Round(value / 2, 0, MidpointRounding.AwayFromZero),
                GH_SliderAccuracy.Odd => 2 * decimal.Round((value - 1) / 2, 0, MidpointRounding.AwayFromZero) + 1,
                _ => decimal.Round(value, Math.Clamp(digits[i], 0, 28), MidpointRounding.AwayFromZero)
            };
            bool raiseEvents = sliders[i].Slider.RaiseEvents;
            sliders[i].Slider.RaiseEvents = false;
            try {
                sliders[i].SetSliderValue(Math.Clamp(value, minimum[i], maximum[i]));
                sliders[i].ExpireSolution(false);
            } finally { sliders[i].Slider.RaiseEvents = raiseEvents; }
        }
        bool completed = false;
        void End(object sender, GH_SolutionEventArgs e) => completed = true;
        document.SolutionEnd += End;
        try { document.NewSolution(false); }
        finally { document.SolutionEnd -= End; }
        if (!completed || document.SolutionState == GH_ProcessStep.Aborted)
            throw new InvalidOperationException("Grasshopper computation did not complete.");
        // A completed solution may contain existing component errors; capture its visible preview.
        // Let the UI and Grasshopper preview invalidations run before the synchronous redraw.
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        cancellation.ThrowIfCancellationRequested();
        Validate();
        if (!view.ActiveViewport.SetViewProjection(projection, true)) throw new InvalidOperationException("Could not restore the captured camera.");
        view.ActiveViewport.DisplayMode = mode;
        view.Redraw();
        return sliders.Select((s, i) => (double)((s.Slider.Value - minimum[i]) / (maximum[i] - minimum[i]))).ToArray();
    }
    public BitmapSource Capture() {
        Validate();
        using var bitmap = view.CaptureToBitmap(captureSize, mode)
            ?? throw new InvalidOperationException("Viewport capture failed.");
        using var stream = new MemoryStream();
        bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
        stream.Position = 0;
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }
    public void Dispose() => projection.Dispose();
}
