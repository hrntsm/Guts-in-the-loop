using System;
using System.Linq;
using System.Text;

namespace SequentialGallery;

internal sealed record RenderingVariable(string Name, string Label, string Key, string[] Anchors, string[] Descriptions) {
    public double DefaultFixedValue => (double)(Anchors.Length / 2) / (Anchors.Length - 1);
    public string Describe(double value) => Blend(value, i => Descriptions[i]);
    public string Reference(double value) => Blend(value, i => Key + i);
    private string Blend(double value, Func<int, string> text) {
        double position = value * (Anchors.Length - 1);
        int lower = Math.Min((int)position, Anchors.Length - 1);
        if (lower == Anchors.Length - 1 || position == lower) return text(lower);
        double upperWeight = position - lower;
        return FormattableString.Invariant($"{(1 - upperWeight) * 100:F1}% {text(lower)} + {upperWeight * 100:F1}% {text(lower + 1)}");
    }
}

internal sealed record RenderingBatch(string Prompt, double[][] Values, bool[] Valid, int Plane, int SelectionStage);

internal sealed record RenderingState(SpsEngine Engine, RenderingBatch Batch, int Plane, int Zoom) : IDisposable {
    public void Dispose() => Engine.Dispose();
}

internal sealed class RenderingSession : IDisposable {
    public const int InitialPromptLimit = 2000;
    public const int NegativePromptLimit = 2000;
    public static readonly string DefaultInitialPrompt = RenderingPrompts.Current.DefaultInitialPrompt;
    public static readonly string DefaultNegativePrompt = RenderingPrompts.Current.DefaultNegativePrompt;
    public static readonly RenderingVariable[] Variables = RenderingPrompts.Current.Variables;
    private SpsEngine engine;
    private readonly int[] variableIndices;
    private readonly double[] fixedValues;
    private int plane = 1, zoom;
    public int Rows { get; }
    public int Columns { get; }
    public string InitialPrompt { get; }
    public string NegativePrompt { get; }
    public RenderingBatch? Displayed { get; private set; }
    public RenderingBatch? Pending { get; private set; }
    public bool Faulted { get; private set; }

    public RenderingSession(int rows, int columns, int[] variableIndices, string? initialPrompt = null, string? negativePrompt = null, double[]? fixedValues = null) {
        initialPrompt ??= DefaultInitialPrompt;
        negativePrompt ??= DefaultNegativePrompt;
        if (variableIndices.Length < 2 || variableIndices.Length > Variables.Length
            || variableIndices.Distinct().Count() != variableIndices.Length
            || variableIndices.Any(i => i < 0 || i >= Variables.Length))
            throw new ArgumentException("Select at least two different rendering variables.");
        if (initialPrompt.Length > InitialPromptLimit)
            throw new ArgumentException($"Initial prompt must be at most {InitialPromptLimit} characters.");
        if (negativePrompt.Length > NegativePromptLimit)
            throw new ArgumentException($"Negative prompt must be at most {NegativePromptLimit} characters.");
        this.fixedValues = fixedValues == null ? Variables.Select(v => v.DefaultFixedValue).ToArray() : (double[])fixedValues.Clone();
        if (this.fixedValues.Length != Variables.Length || this.fixedValues.Any(v => !double.IsFinite(v) || v < 0 || v > 1))
            throw new ArgumentException("Provide one finite fixed value between 0 and 1 for every rendering variable.");
        Rows = rows;
        Columns = columns;
        InitialPrompt = initialPrompt.Trim();
        NegativePrompt = negativePrompt.Trim();
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
    public RenderingState CaptureState() {
        if (Faulted || Pending != null || Displayed == null)
            throw new InvalidOperationException("Only completed rendering states can be recorded.");
        return new RenderingState(engine.Clone(), Displayed, plane, zoom);
    }
    public void RestoreState(RenderingState state) {
        var restored = state.Engine.Clone();
        engine.Dispose();
        engine = restored;
        Displayed = state.Batch;
        Pending = null;
        plane = state.Plane;
        zoom = state.Zoom;
        Faulted = false;
    }
    private RenderingBatch BuildBatch() {
        var grid = engine.Grid();
        var valid = grid.Select(values => values.All(v => double.IsFinite(v) && v >= 0 && v <= 1)).ToArray();
        var values = grid.Select(vector => {
            var all = (double[])fixedValues.Clone();
            for (int j = 0; j < variableIndices.Length; j++) all[variableIndices[j]] = vector[j];
            return all;
        }).ToArray();
        var definitions = RenderingPrompts.Current;
        var prompt = new StringBuilder();
        prompt.AppendLine(definitions.Layout.Replace("{rows}", Rows.ToString()).Replace("{columns}", Columns.ToString()));
        prompt.AppendLine(definitions.SceneRules);
        if (InitialPrompt.Length > 0 || NegativePrompt.Length > 0) {
            prompt.AppendLine(definitions.FixedSceneRules);
            if (InitialPrompt.Length > 0)
                prompt.AppendLine("[Initial prompt]").AppendLine(InitialPrompt).AppendLine("[/Initial prompt]");
            if (NegativePrompt.Length > 0)
                prompt.AppendLine("[Negative prompt - avoid these results]").AppendLine(NegativePrompt).AppendLine("[/Negative prompt]");
        }
        prompt.AppendLine(definitions.InteractionRules);
        prompt.AppendLine("Cell keys: " + string.Join("; ", Variables.Select(v => $"{v.Key}={v.Name}")));
        foreach (var variable in Variables)
            for (int j = 0; j < variable.Descriptions.Length; j++)
                prompt.AppendLine($"{variable.Key}{j}: {variable.Descriptions[j]}");
        foreach (int i in Enumerable.Range(0, Variables.Length).Except(variableIndices))
            prompt.AppendLine($"Fixed in every valid cell: {Variables[i].Key} = {Variables[i].Reference(fixedValues[i])}.");
        for (int i = 0; i < grid.Length; i++) {
            prompt.Append($"R{i / Columns + 1}C{i % Columns + 1}: ");
            if (!valid[i]) { prompt.AppendLine(definitions.BlankCell); continue; }
            for (int j = 0; j < variableIndices.Length; j++) {
                var variable = Variables[variableIndices[j]];
                prompt.Append(variable.Key).Append('=').Append(variable.Reference(grid[i][j])).Append("; ");
            }
            if (values[i][2] > .5) prompt.Append("Precipitation=").Append(DescribePrecipitation(values[i][5])).Append(';');
            prompt.AppendLine();
        }
        if (prompt.Length > 32000) throw new InvalidOperationException("The matrix prompt exceeds the API prompt limit. Choose a smaller grid.");
        return new RenderingBatch(prompt.ToString(), values, valid, plane, zoom + 1);
    }
    internal static string DescribePrecipitation(double season) {
        var definitions = RenderingPrompts.Current;
        double snow = Math.Clamp((season - 2.0 / 3) * 3, 0, 1);
        if (snow == 0) return definitions.Rain;
        if (snow == 1) return definitions.Snow;
        return FormattableString.Invariant($"{(1 - snow) * 100:F1}% {definitions.Rain} + {snow * 100:F1}% {definitions.Snow}");
    }
    internal static string DescribeValue(double[] values, int index) {
        string description = Variables[index].Describe(values[index]);
        return index == 2 && values[2] > .5 ? description + " Precipitation: " + DescribePrecipitation(values[5]) + "." : description;
    }
    public void Dispose() => engine.Dispose();
}
