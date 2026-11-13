using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using AIFramework.Core.Abstractions;
using AIFramework.Core.Metering;
using AIFramework.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace AIFramework.Ollama;

/// <summary>Development-only text and structured generation through a loopback Ollama server.</summary>
public sealed class OllamaGenerationModel : ITextGenerationModel, IStructuredGenerationModel
{
    private readonly OllamaOptions options;
    private readonly HttpClient client;

    public OllamaGenerationModel(OllamaOptions options, HttpClient client)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(client);
        options.Validate();
        this.options = options;
        this.client = client;
    }

    public async Task<TextGenerationResult> GenerateTextAsync(TextGenerationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();
        CheckContext(request.Instructions, request.Input, request.MaxOutputTokens);
        try
        {
            using var response = await PostBoundedAsync("api/chat", ChatBody(request.Instructions, request.Input,
                request.MaxOutputTokens), 1_048_576, cancellationToken);
            var root = response.RootElement;
            string reason = GetDoneReason(root);
            string content = GetContent(root);
            return new(content, "ollama")
            {
                Model = GetModel(root), Usage = GetUsage(root), Complete = reason == "stop"
            };
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new InvalidOperationException("Ollama returned an invalid text response.");
        }
    }

    public async Task<StructuredGenerationResult> GenerateStructuredAsync(
        StructuredGenerationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.SchemaName is null || !Regex.IsMatch(request.SchemaName, "^[A-Za-z0-9_-]{1,64}$") ||
            request.MaxOutputTokens is < 1 or > 4096 ||
            string.IsNullOrWhiteSpace(request.Instructions) || request.Instructions.Length > 32_000 ||
            string.IsNullOrWhiteSpace(request.Input) || request.Input.Length > 32_000 ||
            request.Schema.ValueKind != JsonValueKind.Object || request.Schema.GetRawText().Length > 32_000)
            throw new ArgumentException("Invalid structured-generation request bounds.");
        try
        {
            StructuredJson.ValidateSchema(request.Schema);
            if (request.Schema.GetProperty("type").ValueKind != JsonValueKind.String ||
                request.Schema.GetProperty("type").GetString() != "object")
                throw new ArgumentException("Structured generation requires an object root schema.");
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            throw new ArgumentException("Invalid structured-generation schema.");
        }
        CheckContext(request.Instructions, request.Input, request.MaxOutputTokens);
        var body = ChatBody(request.Instructions, request.Input, request.MaxOutputTokens);
        body["format"] = request.Schema;
        ((Dictionary<string, object>)body["options"]!)["temperature"] = 0;
        try
        {
            using var response = await PostBoundedAsync("api/chat", body, 131_072, cancellationToken);
            var root = response.RootElement;
            string reason = GetDoneReason(root);
            var usage = GetUsage(root);
            string? model = GetModel(root);
            string json = GetContent(root);
            if (reason == "length")
                return new(StructuredGenerationStatus.Incomplete, null, "ollama", usage) { Model = model };
            using var output = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
            if (!StructuredJson.Matches(output.RootElement, request.Schema))
                throw new StructuredGenerationException("invalid_response");
            return new(StructuredGenerationStatus.Completed, json, "ollama", usage) { Model = model };
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new StructuredGenerationException("invalid_response");
        }
    }

    public async Task<bool> IsModelAvailableAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(options.Endpoint, "api/show"))
        {
            Content = JsonContent.Create(new { model = options.Model })
        };
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return false;
        response.EnsureSuccessStatusCode();
        return true;
    }

    /// <summary>Uses a conservative upper estimate; a request that fits the model may be rejected.</summary>
    private void CheckContext(string instructions, string input, int maxOutputTokens)
    {
        if (AiCostCalculator.EstimateInputTokensUpperBound(instructions, input) + maxOutputTokens > options.ContextTokens)
            throw new ArgumentException("Request exceeds the conservative Ollama context bound.");
    }

    private Dictionary<string, object> ChatBody(string instructions, string input, int maxOutputTokens)
    {
        var body = new Dictionary<string, object>
        {
            ["model"] = options.Model,
            ["messages"] = new[] { new { role = "system", content = instructions }, new { role = "user", content = input } },
            ["stream"] = false,
            ["options"] = new Dictionary<string, object> { ["num_ctx"] = options.ContextTokens, ["num_predict"] = maxOutputTokens }
        };
        if (options.Think is { } think) body["think"] = think;
        return body;
    }

    private async Task<JsonDocument> PostBoundedAsync(string path, object body, int limit, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(options.Endpoint, path))
        {
            Content = JsonContent.Create(body)
        };
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        byte[] chunk = new byte[4096];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > limit) throw new StructuredGenerationException("response_too_large");
            buffer.Write(chunk, 0, read);
        }
        return JsonDocument.Parse(buffer.ToArray());
    }

    private static string GetDoneReason(JsonElement root)
    {
        if (!root.TryGetProperty("done", out var done) || done.ValueKind != JsonValueKind.True ||
            !root.TryGetProperty("done_reason", out var reason) || reason.ValueKind != JsonValueKind.String ||
            reason.GetString() is not ("stop" or "length"))
            throw new InvalidOperationException("Ollama did not complete generation.");
        return reason.GetString()!;
    }

    private static string GetContent(JsonElement root)
    {
        if (!root.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object ||
            !message.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.String)
            throw new InvalidOperationException("Ollama returned no content.");
        return content.GetString()!;
    }

    private static string? GetModel(JsonElement root) => root.TryGetProperty("model", out var model) &&
        model.ValueKind == JsonValueKind.String ? model.GetString() : null;

    private static GenerationUsage GetUsage(JsonElement root) => new(
        GetToken(root, "prompt_eval_count"), GetToken(root, "eval_count"));

    private static int? GetToken(JsonElement root, string name) => root.TryGetProperty(name, out var token) &&
        token.ValueKind == JsonValueKind.Number && token.TryGetInt32(out int value) && value >= 0 ? value : null;
}

internal sealed class OllamaHttpClient(HttpClient client)
{
    internal HttpClient Client => client;
}

public static class OllamaRegistration
{
    public static IServiceCollection AddOllamaModels(this IServiceCollection services, OllamaOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        services.AddSingleton(options);
        services.AddHttpClient<OllamaHttpClient>(client => client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false });
        services.AddSingleton<OllamaGenerationModel>(provider =>
            new(options, provider.GetRequiredService<OllamaHttpClient>().Client));
        services.AddSingleton<ITextGenerationModel>(provider => provider.GetRequiredService<OllamaGenerationModel>());
        services.AddSingleton<IStructuredGenerationModel>(provider => provider.GetRequiredService<OllamaGenerationModel>());
        return services;
    }
}
