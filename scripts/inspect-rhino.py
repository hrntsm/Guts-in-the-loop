import os
import clr
clr.AddReference("Grasshopper")
import Grasshopper
from Grasshopper.Kernel import GH_Component, GH_RuntimeMessageLevel
from Grasshopper.Kernel.Special import GH_NumberSlider

root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
doc = Grasshopper.Instances.ActiveCanvas.Document
with open(os.path.join(root, "build", "evidence", "definition.txt"), "w") as report:
    for obj in doc.Objects:
        report.write("{0} ({1})\n".format(obj.Name, obj.GetType().FullName))
        if isinstance(obj, GH_NumberSlider):
            report.write("  {0}, range {1}..{2}, value {3}\n".format(obj.Slider.Type, obj.Slider.Minimum, obj.Slider.Maximum, obj.Slider.Value))
        if isinstance(obj, GH_Component):
            for message in obj.RuntimeMessages(GH_RuntimeMessageLevel.Error):
                report.write("  ERROR: {0}\n".format(message))
            for message in obj.RuntimeMessages(GH_RuntimeMessageLevel.Warning):
                report.write("  WARNING: {0}\n".format(message))
            for param in obj.Params.Input:
                report.write("  {0} {1} <- {2}\n".format(param.Name, param.GetType().Name, ", ".join([s.Name for s in param.Sources])))
                report.write("    {0}\n".format(", ".join([str(x) for x in param.VolatileData.AllData(True)])))
