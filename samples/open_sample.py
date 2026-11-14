# Run in Rhino 8 with _-RunPythonScript. The original user file is never changed.
import os
import clr
import Rhino
import System

root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
Rhino.RhinoApp.RunScript("_Grasshopper", False)
clr.AddReference("Grasshopper")
clr.AddReference("GH_IO")
import Grasshopper
from GH_IO.Serialization import GH_Archive
from Grasshopper.Kernel.Special import GH_NumberSlider
plugin = os.path.join(root, "artifacts", "SequentialGallery", "SequentialGallery.gha")
server = Grasshopper.Instances.ComponentServer
if server.FindAssembly(System.Guid("1b335bd7-a5bb-4b6d-80dc-b3b139436f65")) is None:
    server.LoadExternalFile(Grasshopper.Kernel.GH_ExternalFile(plugin), False)
clr.AddReferenceToFileAndPath(plugin)
from SequentialGallery import GalleryComponent

reader = Grasshopper.Kernel.GH_DocumentIO()
if not reader.Open(os.path.join(root, "samples", "Facade.gh")):
    raise Exception("Could not open samples/Facade.gh")
doc = reader.Document
Grasshopper.Instances.DocumentServer.AddDocument(doc)
Grasshopper.Instances.ActiveCanvas.Document = doc
sliders = [o for o in doc.Objects if isinstance(o, GH_NumberSlider)
           and o.Slider.Maximum > o.Slider.Minimum]
galleries = [o for o in doc.Objects if isinstance(o, GalleryComponent)]
if galleries:
    gallery = galleries[0]
else:
    gallery = GalleryComponent()
    gallery.CreateAttributes()
    right = max([o.Attributes.Bounds.Right for o in doc.Objects] + [400])
    gallery.Attributes.Pivot = System.Drawing.PointF(right + 160, 200)
    doc.AddObject(gallery, False)
if gallery.Params.Input[0].SourceCount == 0:
    for slider in sliders:
        gallery.Params.Input[0].AddSource(slider)
doc.NewSolution(False)
archive = GH_Archive()
if not archive.AppendObject(doc, "Definition"):
    raise Exception("Could not serialize the prepared sample.")
if not archive.WriteToFile(os.path.join(root, "samples", "Facade.ghx"), True, False):
    raise Exception("Could not save the prepared sample.")

view = Rhino.RhinoDoc.ActiveDoc.Views.ActiveView
view.Maximized = True
view.ActiveViewport.DisplayMode = Rhino.Display.DisplayModeDescription.GetDisplayMode(Rhino.Display.DisplayModeDescription.ShadedId)
view.ActiveViewport.ZoomBoundingBox(Rhino.Geometry.BoundingBox(Rhino.Geometry.Point3d(0, 0, 0), Rhino.Geometry.Point3d(100, 100, 100)))
Rhino.RhinoDoc.ActiveDoc.Views.Redraw()
evidence = os.path.join(root, "build", "evidence")
if not os.path.exists(evidence):
    os.makedirs(evidence)
with open(os.path.join(evidence, "sample.txt"), "w") as report:
    report.write("Loaded user-provided Facade.gh\n")
    report.write("Runtime: {0}\n".format(System.Environment.Version))
    report.write("Gallery registered: {0}\n".format(Grasshopper.Instances.ComponentServer.FindAssemblyByObject(gallery) is not None))
    for slider in sliders:
        report.write("Slider {0}: [{1}, {2}], value {3}\n".format(slider.NickName, slider.Slider.Minimum, slider.Slider.Maximum, slider.Slider.Value))
    for obj in doc.Objects:
        if isinstance(obj, Grasshopper.Kernel.GH_Component):
            report.write("{0}: {1}\n".format(obj.Name, obj.RuntimeMessageLevel))
print("Loaded sample with {0} variables. Frame the model, then double-click Sequential Gallery.".format(len(sliders)))
