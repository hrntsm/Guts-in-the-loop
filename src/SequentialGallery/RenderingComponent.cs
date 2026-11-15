using System;
using System.Drawing;
using System.Windows.Forms;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;

namespace SequentialGallery;

public sealed class RenderingComponent : GH_Component {
    private static readonly Lazy<Bitmap> icon = new(() => GalleryComponent.LoadIcon("Rendering"));
    private IntegratedWindow? window;
    public RenderingComponent() : base("AI Rendering", "AI Render",
        "Explore the atmosphere of a fixed viewport capture with AI. Double-click to set up.", "Params", "Util") { }
    public override Guid ComponentGuid => new("52a8d957-fb2f-47f5-bf86-79abda2b904e");
    public override GH_Exposure Exposure => GH_Exposure.hidden;
    protected override Bitmap Icon => icon.Value;
    protected override void RegisterInputParams(GH_InputParamManager p) { }
    protected override void RegisterOutputParams(GH_OutputParamManager p) { }
    protected override void SolveInstance(IGH_DataAccess da) { }
    public override void CreateAttributes() => m_attributes = new RenderingAttributes(this);
    protected override void AppendAdditionalComponentMenuItems(ToolStripDropDown menu) {
        base.AppendAdditionalComponentMenuItems(menu);
        Menu_AppendItem(menu, "Open AI Rendering", (_, _) => OpenRendering());
    }
    internal void OpenRendering() {
        if (window != null) { window.Activate(); return; }
        var library = Grasshopper.Instances.ComponentServer.FindAssemblyByObject(this);
        if (library != null) SpsEngine.LibraryDirectory = System.IO.Path.GetDirectoryName(library.Location);
        window = new IntegratedWindow(null, null, RenderingHost.CaptureReference);
        window.Closed += (_, _) => window = null;
        window.Show();
    }
    public override void RemovedFromDocument(GH_Document document) {
        window?.RequestClose();
        base.RemovedFromDocument(document);
    }
    public override void DocumentContextChanged(GH_Document document, GH_DocumentContext context) {
        if (context == GH_DocumentContext.Close || context == GH_DocumentContext.Unloaded) window?.RequestClose();
        base.DocumentContextChanged(document, context);
    }
    private sealed class RenderingAttributes(RenderingComponent owner) : GH_ComponentAttributes(owner) {
        public override GH_ObjectResponse RespondToMouseDoubleClick(GH_Canvas sender, GH_CanvasMouseEvent e) {
            owner.OpenRendering();
            return GH_ObjectResponse.Handled;
        }
    }
}
