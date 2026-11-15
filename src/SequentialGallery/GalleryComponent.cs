using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using Grasshopper.Kernel.Special;

namespace SequentialGallery;

public sealed class GalleryComponent : GH_Component
{
    private static readonly Lazy<Bitmap> sharedIcon = new(() => LoadIcon("Gallery"));
    internal static Bitmap LoadIcon(string name) {
        using var stream = typeof(GalleryComponent).Assembly.GetManifestResourceStream($"SequentialGallery.{name}.png")
            ?? throw new InvalidOperationException("The gallery icon resource is missing.");
        using var image = new Bitmap(stream);
        return new Bitmap(image);
    }
    internal static Bitmap SharedIcon => sharedIcon.Value;
    protected override Bitmap Icon => SharedIcon;
    private IntegratedWindow? window;
    public GalleryComponent() : base("Sequential Gallery", "Gallery",
        "Explore shape and AI rendering together. Leave Variables empty for AI rendering only. Double-click to set up.", "Params", "Util") { }
    public override Guid ComponentGuid => new("bb44f5ae-9d96-4f92-88ce-411d02711427");
    protected override void RegisterInputParams(GH_InputParamManager p) {
        p.AddNumberParameter("Variables", "V", "Leave empty for AI rendering only, or connect at least two Number Sliders for combined shape and rendering exploration.", GH_ParamAccess.list);
        p[0].Optional = true;
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p) { }
    protected override void SolveInstance(IGH_DataAccess da) {
        int count = Params.Input[0].SourceCount;
        Message = count == 0 ? "AI only" : count == 1 ? "Requires 2+ variables" : $"{count} shape variables + AI";
    }
    public override void CreateAttributes() => m_attributes = new GalleryAttributes(this);
    protected override void AppendAdditionalComponentMenuItems(ToolStripDropDown menu) {
        base.AppendAdditionalComponentMenuItems(menu);
        Menu_AppendItem(menu, "Connect Selected Sliders", (_, _) => {
            if (window != null) return;
            var doc = OnPingDocument();
            if (doc == null) return;
            RecordUndoEvent("Connect Selected Sliders");
            foreach (var slider in doc.Objects.OfType<GH_NumberSlider>().Where(s => s.Attributes.Selected))
                if (!Params.Input[0].Sources.Contains(slider)) Params.Input[0].AddSource(slider);
            ExpireSolution(true);
        }, true, false);
        Menu_AppendItem(menu, "Open Gallery", (_, _) => OpenGallery());
    }
    internal void OpenGallery() {
        if (window != null) { window.Activate(); return; }
        try {
            var library = Grasshopper.Instances.ComponentServer.FindAssemblyByObject(this);
            if (library != null) SpsEngine.LibraryDirectory = System.IO.Path.GetDirectoryName(library.Location);
            var doc = OnPingDocument() ?? throw new InvalidOperationException("No Grasshopper document is open.");
            int count = Params.Input[0].SourceCount;
            if (count == 1) throw new InvalidOperationException("Connect at least two Number Sliders for shape exploration, or disconnect Variables for AI rendering only.");
            if (Params.Input[0].Sources.Any(source => source is not GH_NumberSlider))
                throw new InvalidOperationException("Variables must connect directly to Number Sliders.");
            TimeSpan? lastSolution = doc.SolutionDepth == 0 && doc.SolutionState != GH_ProcessStep.Aborted
                && doc.SolutionSpan > TimeSpan.Zero ? doc.SolutionSpan : null;
            Func<IGeometryHost>? geometry = count == 0 ? null : () => new GrasshopperHost(doc, Params.Input[0].Sources);
            window = new IntegratedWindow(geometry, lastSolution, RenderingHost.CaptureReference);
            window.Closed += (_, _) => window = null;
            window.Show();
        } catch (Exception e) {
            System.Windows.MessageBox.Show(e.Message, "Sequential Gallery", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }
    public override void RemovedFromDocument(GH_Document document) {
        window?.ForceClose();
        base.RemovedFromDocument(document);
    }
    public override void DocumentContextChanged(GH_Document document, GH_DocumentContext context) {
        if (context == GH_DocumentContext.Close || context == GH_DocumentContext.Unloaded) window?.ForceClose();
        base.DocumentContextChanged(document, context);
    }
    private sealed class GalleryAttributes(GalleryComponent owner) : GH_ComponentAttributes(owner) {
        protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel) {
            if (channel != GH_CanvasChannel.Wires) {
                base.Render(canvas, graphics, channel);
                return;
            }
            var input = owner.Params.Input[0];
            var state = graphics.Save();
            try {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                foreach (var source in input.Sources) {
                    bool selected = Selected || source.Attributes.Selected;
                    if (input.WireDisplay == GH_ParamWireDisplay.hidden && !selected) continue;
                    var start = source.Attributes.OutputGrip;
                    var end = input.Attributes.InputGrip;
                    if (!canvas.Painter.ConnectionVisible(start, end)) continue;
                    bool emphasized = selected && input.WireDisplay != GH_ParamWireDisplay.faint;
                    using var path = GH_Painter.ConnectionPath(start, end, GH_WireDirection.right, GH_WireDirection.left);
                    if (path == null) continue;
                    using var pen = new Pen(Color.FromArgb(emphasized ? 255 : 70, 155, 27, 48), emphasized ? 3f : 2f);
                    graphics.DrawPath(pen, path);
                }
            } finally {
                graphics.Restore(state);
            }
        }
        public override GH_ObjectResponse RespondToMouseDoubleClick(GH_Canvas sender, GH_CanvasMouseEvent e) {
            owner.OpenGallery();
            return GH_ObjectResponse.Handled;
        }
    }
}

public sealed class GalleryInfo : GH_AssemblyInfo {
    public override Bitmap Icon => GalleryComponent.SharedIcon;
    public override string Name => "Sequential Gallery";
    public override string Description => "Sequential Plane Search for Grasshopper — hackathon PoC";
    public override Guid Id => new("1b335bd7-a5bb-4b6d-80dc-b3b139436f65");
    public override string AuthorName => "Guts-in-the-loop";
    public override string AuthorContact => "";
}
