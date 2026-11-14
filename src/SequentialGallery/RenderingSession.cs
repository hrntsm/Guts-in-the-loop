using System;
using System.Linq;
using System.Text;

namespace SequentialGallery;

internal sealed record RenderingVariable(string Name, string[] Anchors) {
    public string Describe(double value) {
        double position = value * (Anchors.Length - 1);
        int lower = Math.Min((int)position, Anchors.Length - 1);
        if (lower == Anchors.Length - 1 || position == lower) return Anchors[lower];
        double upperWeight = position - lower;
        return FormattableString.Invariant($"{(1 - upperWeight) * 100:F1}% {Anchors[lower]} + {upperWeight * 100:F1}% {Anchors[lower + 1]}");
    }
}

internal sealed record RenderingBatch(string Prompt, double[][] Values, bool[] Valid, int Plane, int SelectionStage);

internal sealed class RenderingSession : IDisposable {
    public static readonly RenderingVariable[] Variables = {
        new("TimeOfDay", new[] { "Early morning", "Midday", "Night" }),
        new("RenderingStyle", new[] { "Pencil/line sketch", "Watercolor", "Architectural illustration", "Detailed CGI", "Photorealistic" }),
        new("Weather", new[] { "Clear skies", "Overcast skies", "Heavy rain" }),
        new("ColorTemperature", new[] { "Strong cool tones", "Natural color", "Strong warm tones" }),
        new("Activity", new[] { "No people, quiet", "Moderate foot traffic", "Crowded, lively" }),
        new("Vegetation", new[] { "Almost no planting", "Moderate planting", "Abundant planting" }),
        new("Season", new[] { "Spring", "Summer", "Autumn", "Winter" })
    };
    private readonly SpsEngine engine;
    private readonly int[] variableIndices;
    private int plane = 1, zoom;
    public int Rows { get; }
    public int Columns { get; }
    public RenderingBatch? Displayed { get; private set; }
    public RenderingBatch? Pending { get; private set; }
    public bool Faulted { get; private set; }

    public RenderingSession(int rows, int columns, int[] variableIndices) {
        if (variableIndices.Length < 2 || variableIndices.Length > Variables.Length
            || variableIndices.Distinct().Count() != variableIndices.Length
            || variableIndices.Any(i => i < 0 || i >= Variables.Length))
            throw new ArgumentException("Select at least two different rendering variables.");
        Rows = rows;
        Columns = columns;
        this.variableIndices = (int[])variableIndices.Clone();
        engine = new SpsEngine(variableIndices.Length, rows, columns);
        try { Pending = BuildBatch(); }
        catch { engine.Dispose(); throw; }
    }
    public void Advance(int index) {
        if (Faulted || Pending != null || Displayed == null || index < 0 || index >= Displayed.Valid.Length || !Displayed.Valid[index])
            throw new InvalidOperationException("Select a valid cell after the pending generation finishes.");
        try {
            engine.Select(index);
            if (++zoom == 4) { zoom = 0; plane++; }
            Pending = BuildBatch();
        } catch { Faulted = true; throw; }
    }
    public void Regenerate() {
        if (Faulted || Pending != null || Displayed == null) throw new InvalidOperationException("Finish or retry the pending generation first.");
        Pending = Displayed;
    }
    public void Accept(RenderingBatch batch) {
        if (!ReferenceEquals(Pending, batch)) throw new InvalidOperationException("This response no longer belongs to the pending generation.");
        Displayed = batch;
        Pending = null;
    }
    private RenderingBatch BuildBatch() {
        var grid = engine.Grid();
        var valid = grid.Select(values => values.All(v => double.IsFinite(v) && v >= 0 && v <= 1)).ToArray();
        var prompt = new StringBuilder();
        prompt.AppendLine($"Create ONE square image, an exact {Rows}-row by {Columns}-column contact sheet of architectural renderings.");
        prompt.AppendLine("Use the supplied facade reference in every valid cell. Preserve its building geometry, camera and composition; atmosphere comparison matters more than exact detail. All cells must be equal rectangles, edge-to-edge, with no outer margin or gutters. Do not draw text, labels or numbers. Conditions below are indexed top-to-bottom, left-to-right; indices are instructions only. Keep every cell in its specified position. Blend neighboring descriptions by the given weights; do not blend between cells. Season and weather are independent.");
        const string keys = "TDWCAVS";
        prompt.AppendLine("Cell keys: " + string.Join("; ", variableIndices.Select(i => $"{keys[i]}={Variables[i].Name}")));
        foreach (int i in Enumerable.Range(0, Variables.Length).Except(variableIndices))
            prompt.AppendLine($"Fixed in every valid cell: {Variables[i].Name} = {Variables[i].Describe(.5)}.");
        for (int i = 0; i < grid.Length; i++) {
            prompt.Append($"R{i / Columns + 1}C{i % Columns + 1}: ");
            if (!valid[i]) { prompt.AppendLine("Blank neutral placeholder. No rendering."); continue; }
            for (int j = 0; j < variableIndices.Length; j++) {
                var variable = Variables[variableIndices[j]];
                prompt.Append(keys[variableIndices[j]]).Append('=').Append(variable.Describe(grid[i][j])).Append("; ");
            }
            prompt.AppendLine();
        }
        if (prompt.Length > 32000) throw new InvalidOperationException("The matrix prompt exceeds the API prompt limit. Choose a smaller grid.");
        var values = grid.Select(vector => {
            var all = Enumerable.Repeat(.5, Variables.Length).ToArray();
            for (int j = 0; j < variableIndices.Length; j++) all[variableIndices[j]] = vector[j];
            return all;
        }).ToArray();
        return new RenderingBatch(prompt.ToString(), values, valid, plane, zoom + 1);
    }
    public void Dispose() => engine.Dispose();
}
