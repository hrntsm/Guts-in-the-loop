using System;
using System.IO;
using Rhino;

namespace SequentialGallery;

internal static class RenderingHost {
    public static byte[] CaptureReference() {
        var rhino = RhinoDoc.ActiveDoc ?? throw new InvalidOperationException("No Rhino document is open.");
        var view = rhino.Views.ActiveView ?? throw new InvalidOperationException("No Rhino viewport is available.");
        view.Redraw();
        using var bitmap = view.CaptureToBitmap()
            ?? throw new InvalidOperationException("Viewport capture failed.");
        using var stream = new MemoryStream();
        bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
        return stream.ToArray();
    }
}
