using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SequentialGallery;

internal sealed class RenderingApi : IDisposable {
    internal const string Model = "gpt-image-2.6-flare";
    private readonly HttpClient client;
    public RenderingApi(string apiKey, Uri? endpoint = null) {
        if (string.IsNullOrWhiteSpace(apiKey)) throw new InvalidOperationException("Set OPENAI_API_KEY before starting Rhino, then reopen AI Rendering.");
        var target = endpoint ?? new Uri("https://api.openai.com/v1/images/edits");
        if (target.Scheme != "https" && !target.IsLoopback) throw new ArgumentException("Image requests require HTTPS.");
        client = new HttpClient { BaseAddress = target, Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
    }
    public async Task<byte[]> GenerateAsync(byte[] reference, string prompt, CancellationToken cancellation) {
        var payload = new {
            model = Model,
            images = new[] { new { image_url = "data:image/png;base64," + Convert.ToBase64String(reference) } },
            prompt, n = 1, size = "1024x1024", quality = "low", output_format = "png"
        };
        using var response = await client.PostAsJsonAsync("", payload, cancellation).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) {
            string guidance = (int)response.StatusCode switch {
                401 => "Check the API key and restart Rhino after updating it.",
                403 => "Check project permissions and image-model access.",
                404 => "Check whether this project can access " + Model + ".",
                429 => "Check API credits and rate limits, then retry manually.",
                _ => "Retry manually when the service is available."
            };
            // Do not echo provider response bodies: they can contain credentials or submitted data.
            throw new InvalidOperationException($"Image generation returned HTTP {(int)response.StatusCode}. {guidance}");
        }
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false), cancellationToken: cancellation).ConfigureAwait(false);
        if (!json.RootElement.TryGetProperty("data", out var data) || data.GetArrayLength() != 1
            || !data[0].TryGetProperty("b64_json", out var encoded) || string.IsNullOrWhiteSpace(encoded.GetString()))
            throw new InvalidOperationException("The API did not return one encoded image. Retry the same conditions.");
        return Convert.FromBase64String(encoded.GetString()!);
    }
    public void Dispose() => client.Dispose();
}
