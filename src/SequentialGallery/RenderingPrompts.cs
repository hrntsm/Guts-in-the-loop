using System;
using System.Text.Json;

namespace SequentialGallery;

internal sealed record RenderingPrompts(
    string DefaultInitialPrompt, string Layout, string SceneRules, string FixedSceneRules,
    string InteractionRules, string BlankCell, string Rain, string Snow, RenderingVariable[] Variables) {
    public static readonly RenderingPrompts Current = Load();

    private static RenderingPrompts Load() {
        using var stream = typeof(RenderingPrompts).Assembly.GetManifestResourceStream("SequentialGallery.RenderingPrompts.json")
            ?? throw new InvalidOperationException("Rendering prompt resource is missing.");
        return JsonSerializer.Deserialize<RenderingPrompts>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("Rendering prompt resource is empty.");
    }
}
