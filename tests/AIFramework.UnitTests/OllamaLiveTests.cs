using System.Net.Http.Json;
using System.Text.Json;
using AIFramework.Core.Models;
using AIFramework.Ollama;
using Xunit.Abstractions;

namespace AIFramework.UnitTests;

public sealed class OllamaLiveTests(ITestOutputHelper output)
{
    private const string Variable = "AIFRAMEWORK_OLLAMA_LIVE_MODEL";
    private static readonly Uri Endpoint = new("http://127.0.0.1:11434/");

    [LiveOllamaFact]
    public async Task Model_is_available()
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
        Assert.True(await new OllamaGenerationModel(Options(), client).IsModelAvailableAsync(default));
    }

    [LiveOllamaFact]
    public async Task Generates_plain_text()
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
        var start = DateTime.UtcNow;
        var result = await new OllamaGenerationModel(Options(), client).GenerateTextAsync(
            new("Reply briefly in English.", "Say hello.", 80), default);
        output.WriteLine($"Text elapsed_ms={(DateTime.UtcNow - start).TotalMilliseconds:F0}; input_tokens={result.Usage?.InputTokens}; output_tokens={result.Usage?.OutputTokens}");
        Assert.False(string.IsNullOrWhiteSpace(result.Text));
        Assert.True(result.Complete);
    }

    [LiveOllamaFact]
    public async Task Generates_nullable_enum_schema()
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
        using var schema = JsonDocument.Parse("""
            {"type":"object","properties":{"choice":{"type":"string","enum":["yes","no"]},"note":{"type":["string","null"]}},"required":["choice","note"],"additionalProperties":false}
            """);
        var start = DateTime.UtcNow;
        var result = await new OllamaGenerationModel(Options(), client).GenerateStructuredAsync(
            new("answer", "Answer using the supplied JSON schema. Set choice to yes and note to null.",
                "Is water wet?", schema.RootElement, 120), default);
        output.WriteLine($"Structured elapsed_ms={(DateTime.UtcNow - start).TotalMilliseconds:F0}; input_tokens={result.Usage.InputTokens}; output_tokens={result.Usage.OutputTokens}");
        Assert.Equal(StructuredGenerationStatus.Completed, result.Status);
        Assert.NotNull(result.OutputJson);
    }

    [LiveOllamaFact]
    public async Task Raw_small_context_probe_completes_and_reports_behavior()
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
        string input = string.Join(' ', Enumerable.Repeat("The quick brown fox jumps over the lazy dog near the river bank.", 250));
        var start = DateTime.UtcNow;
        using var response = await client.PostAsJsonAsync(new Uri(Endpoint, "api/chat"), new
        {
            model = Environment.GetEnvironmentVariable(Variable),
            messages = new[] { new { role = "user", content = input } },
            stream = false,
            think = false,
            options = new { num_ctx = 512, num_predict = 16 }
        });
        int? promptCount = null;
        string responseText = await response.Content.ReadAsStringAsync();
        try
        {
            using var body = JsonDocument.Parse(responseText);
            if (body.RootElement.ValueKind == JsonValueKind.Object &&
                body.RootElement.TryGetProperty("prompt_eval_count", out var value) &&
                value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var count))
                promptCount = count;
        }
        catch (JsonException) { /* The probe records HTTP errors without requiring a JSON body. */ }
        output.WriteLine($"Raw truncation probe status={(int)response.StatusCode}; prompt_eval_count={promptCount?.ToString() ?? "absent"}; elapsed_ms={(DateTime.UtcNow - start).TotalMilliseconds:F0}");
    }

    private static OllamaOptions Options() => new(Endpoint, Environment.GetEnvironmentVariable(Variable)!);
}

public sealed class LiveOllamaFactAttribute : FactAttribute
{
    public LiveOllamaFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AIFRAMEWORK_OLLAMA_LIVE_MODEL")))
            Skip = "Set AIFRAMEWORK_OLLAMA_LIVE_MODEL for loopback Ollama tests.";
    }
}
