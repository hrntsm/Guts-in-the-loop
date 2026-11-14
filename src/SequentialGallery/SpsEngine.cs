using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace SequentialGallery;

internal sealed class SpsEngine : SafeHandleZeroOrMinusOneIsInvalid
{
    private const string Library = "SequentialGallery.Native.dll";
    public static string? LibraryDirectory { get; set; }
    static SpsEngine()
    {
        NativeLibrary.SetDllImportResolver(typeof(SpsEngine).Assembly, (name, assembly, path) =>
            name == Library ? NativeLibrary.Load(Path.Combine(LibraryDirectory ?? Path.GetDirectoryName(assembly.Location)!, Library)) : IntPtr.Zero);
    }
    public SpsEngine(int dimensions, int rows = 5, int columns = 5) : base(true)
    {
        Dimensions = dimensions;
        CandidateCount = rows * columns;
        SetHandle(Create(dimensions, rows, columns));
        if (IsInvalid) throw new InvalidOperationException(LastError());
    }
    public int Dimensions { get; }
    public int CandidateCount { get; }
    public double[][] Grid()
    {
        var flat = new double[CandidateCount * Dimensions];
        Check(GetGrid(this, flat, flat.Length));
        var result = new double[CandidateCount][];
        for (int i = 0; i < CandidateCount; i++) {
            result[i] = new double[Dimensions];
            Array.Copy(flat, i * Dimensions, result[i], 0, Dimensions);
        }
        return result;
    }
    public void Select(int index) => Check(SelectNative(this, index));
    protected override bool ReleaseHandle() { Destroy(handle); return true; }
    private static string LastError() => Marshal.PtrToStringUTF8(GetError()) ?? "SPS failed.";
    private static void Check(int result) { if (result != 0) throw new InvalidOperationException(LastError()); }
    [DllImport(Library, EntryPoint = "sg_create", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr Create(int dimensions, int rows, int columns);
    [DllImport(Library, EntryPoint = "sg_destroy", CallingConvention = CallingConvention.Cdecl)]
    private static extern void Destroy(IntPtr session);
    [DllImport(Library, EntryPoint = "sg_last_error", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr GetError();
    [DllImport(Library, EntryPoint = "sg_grid", CallingConvention = CallingConvention.Cdecl)]
    private static extern int GetGrid(SpsEngine session, [Out] double[] values, int length);
    [DllImport(Library, EntryPoint = "sg_select", CallingConvention = CallingConvention.Cdecl)]
    private static extern int SelectNative(SpsEngine session, int index);
}
