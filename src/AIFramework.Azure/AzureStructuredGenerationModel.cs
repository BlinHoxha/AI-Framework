using System.Text.Json;
using System.Text.RegularExpressions;
using AIFramework.Core.Abstractions;
using AIFramework.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace AIFramework.Azure;

public sealed record AzureStructuredOptions(Uri Endpoint, string Deployment)
{
    public void Validate()
    {
        if (!Endpoint.IsAbsoluteUri || Endpoint.Scheme != "https" || Endpoint.UserInfo.Length > 0 ||
            Endpoint.Query.Length > 0 || Endpoint.Fragment.Length > 0 || Endpoint.AbsolutePath != "/" ||
            string.IsNullOrWhiteSpace(Deployment))
            throw new ArgumentException("Structured generation requires an HTTPS resource root and an explicit deployment.");
    }
}

public sealed class AzureStructuredGenerationModel(AzureStructuredOptions options, AzureTokenClient client) : IStructuredGenerationModel
{
    public async Task<StructuredGenerationResult> GenerateStructuredAsync(StructuredGenerationRequest request, CancellationToken cancellationToken)
    {
        options.Validate();
        if (!Regex.IsMatch(request.SchemaName, "^[A-Za-z0-9_-]{1,64}$") || request.MaxOutputTokens is < 1 or > 4096 ||
            string.IsNullOrWhiteSpace(request.Instructions) || request.Instructions.Length > 32_000 ||
            string.IsNullOrWhiteSpace(request.Input) || request.Input.Length > 32_000 || request.Schema.GetRawText().Length > 32_000)
            throw new ArgumentException("Invalid structured-generation request bounds.");
        StructuredJson.ValidateSchema(request.Schema);
        if (request.Schema.GetProperty("type").ValueKind != JsonValueKind.String || request.Schema.GetProperty("type").GetString() != "object")
            throw new ArgumentException("Azure strict generation requires an object root schema.");
        var uri = new Uri(options.Endpoint, $"openai/deployments/{Uri.EscapeDataString(options.Deployment)}/chat/completions?api-version=2024-10-21");
        using var response = await client.PostBoundedAsync(uri, new
        {
            messages = new[] { new { role = "system", content = request.Instructions }, new { role = "user", content = request.Input } },
            response_format = new { type = "json_schema", json_schema = new { name = request.SchemaName, strict = true, schema = request.Schema } },
            max_completion_tokens = request.MaxOutputTokens
        }, "https://cognitiveservices.azure.com/.default", 131_072, cancellationToken);
        try
        {
            var root = response.RootElement;
            int? Usage(string name) => root.TryGetProperty("usage", out var usage) && usage.TryGetProperty(name, out var count) && count.TryGetInt32(out int n) && n >= 0 ? n : null;
            var tokens = new GenerationUsage(Usage("prompt_tokens"), Usage("completion_tokens"));
            var choices = root.GetProperty("choices");
            if (choices.GetArrayLength() != 1) throw new StructuredGenerationException("invalid_response");
            var choice = choices[0];
            var message = choice.GetProperty("message");
            if (message.TryGetProperty("refusal", out var refusal) && refusal.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(refusal.GetString()))
                return new(StructuredGenerationStatus.Refused, null, "azure-openai", tokens);
            if (choice.GetProperty("finish_reason").GetString() != "stop")
                return new(StructuredGenerationStatus.Incomplete, null, "azure-openai", tokens);
            string json = message.GetProperty("content").GetString() ?? "";
            using var output = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
            if (!StructuredJson.Matches(output.RootElement, request.Schema)) throw new StructuredGenerationException("invalid_response");
            return new(StructuredGenerationStatus.Completed, json, "azure-openai", tokens);
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException)
        { throw new StructuredGenerationException("invalid_response"); }
    }
}

public static class StructuredGenerationRegistration
{
    public static IServiceCollection AddAzureStructuredGeneration(this IServiceCollection services, AzureStructuredOptions options)
    {
        options.Validate();
        services.AddSingleton(options);
        DependencyInjection.AddTransport(services);
        services.AddSingleton<IStructuredGenerationModel, AzureStructuredGenerationModel>();
        return services;
    }
}
