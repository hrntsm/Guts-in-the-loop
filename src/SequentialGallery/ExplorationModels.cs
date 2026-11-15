using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace SequentialGallery;

internal sealed record HistoryItem(int Id, int? ParentId, string Title, BitmapSource Image, string EdgeLabel, string Details);

internal sealed record GeometryVariableRecord(string Name, Guid InstanceId, decimal Value, decimal Minimum, decimal Maximum,
    string NumericType, int Precision, double SpsCoordinate, double AppliedCoordinate);

internal sealed record GeometryCandidate(int SourceNodeId, int Cell, int Rows, int Columns, int Plane, int Step,
    byte[] ReferencePng, GeometryVariableRecord[] Variables) {
    public string Key => $"{SourceNodeId}:{Cell}";
}

internal interface IGeometryHost : IDisposable {
    int Dimensions { get; }
    Task<double[]> ApplyAsync(double[] normalized, CancellationToken cancellation);
    Task<double[]> ApplyRecordedAsync(GeometryVariableRecord[] variables, CancellationToken cancellation);
    BitmapSource Capture();
    GeometryVariableRecord[] GetVariables(double[] coordinates);
}
