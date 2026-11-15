using System;
using System.IO;
using System.Windows.Media.Imaging;

namespace SequentialGallery;

internal static class ResultExport {
    internal static string? RootDirectoryOverride { get; set; }

    public static string Save(string mode, Action<string> writeFiles) {
        string root = RootDirectoryOverride ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Guts-in-the-loop-results");
        Directory.CreateDirectory(root);
        string name = $"{mode}-{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}";
        string destination = Path.Combine(root, name);
        string staging = Path.Combine(root, ".pending-" + name);
        Directory.CreateDirectory(staging);
        try {
            writeFiles(staging);
            Directory.Move(staging, destination);
            return destination;
        } catch {
            // Only this call's unique staging directory is removed after a failed export.
            try { Directory.Delete(staging, true); } catch { }
            throw;
        }
    }

    public static void SavePng(BitmapSource image, string path) {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
