using System;
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
    private GalleryWindow? window;
    public GalleryComponent() : base("Sequential Gallery", "Gallery",
        "Explore designs with Sequential Plane Search. Double-click to open the gallery.", "Params", "Util") { }
    public override Guid ComponentGuid => new("bb44f5ae-9d96-4f92-88ce-411d02711427");
    protected override void RegisterInputParams(GH_InputParamManager p) {
        p.AddNumberParameter("Variables", "V", "Connect at least two Number Sliders directly. Values are rounded to each slider's precision.", GH_ParamAccess.list);
        p[0].Optional = true;
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p) { }
    protected override void SolveInstance(IGH_DataAccess da) {
        Message = $"{Params.Input[0].SourceCount} variables";
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
            var host = new GrasshopperHost(doc, Params.Input[0].Sources);
            window = new GalleryWindow(host);
            window.Closed += (_, _) => window = null;
            window.Show();
        } catch (Exception e) {
            System.Windows.MessageBox.Show(e.Message, "Sequential Gallery", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }
    public override void RemovedFromDocument(GH_Document document) {
        window?.RequestClose();
        base.RemovedFromDocument(document);
    }
    public override void DocumentContextChanged(GH_Document document, GH_DocumentContext context) {
        if (context == GH_DocumentContext.Close || context == GH_DocumentContext.Unloaded) window?.RequestClose();
        base.DocumentContextChanged(document, context);
    }
    private sealed class GalleryAttributes(GalleryComponent owner) : GH_ComponentAttributes(owner) {
        public override GH_ObjectResponse RespondToMouseDoubleClick(GH_Canvas sender, GH_CanvasMouseEvent e) {
            owner.OpenGallery();
            return GH_ObjectResponse.Handled;
        }
    }
}

public sealed class GalleryInfo : GH_AssemblyInfo {
    public override string Name => "Sequential Gallery";
    public override string Description => "Sequential Plane Search for Grasshopper — hackathon PoC";
    public override Guid Id => new("1b335bd7-a5bb-4b6d-80dc-b3b139436f65");
    public override string AuthorName => "Guts-in-the-loop";
    public override string AuthorContact => "";
}
