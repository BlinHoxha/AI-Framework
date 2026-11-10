using System.Text.Json;
using AIFramework.Core.Models;

namespace AIFramework.Azure;

internal static class AzureResponseMetadata
{
    internal static string? GetModel(JsonElement root)
        => root.TryGetProperty("model", out var model) && model.ValueKind == JsonValueKind.String ? model.GetString() : null;

    internal static int? GetToken(JsonElement usage, string name)
        => usage.TryGetProperty(name, out var count) && count.ValueKind == JsonValueKind.Number &&
            count.TryGetInt32(out int value) && value >= 0 ? value : null;

    internal static GenerationUsage? GetOptionalUsage(JsonElement root)
        => root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object
            ? new(GetToken(usage, "prompt_tokens"), GetToken(usage, "completion_tokens")) : null;

    internal static GenerationUsage GetStructuredUsage(JsonElement root)
    {
        int? Usage(string name) => root.TryGetProperty("usage", out var usage) && usage.TryGetProperty(name, out var count) &&
            count.TryGetInt32(out int value) && value >= 0 ? value : null;
        return new(Usage("prompt_tokens"), Usage("completion_tokens"));
    }
}
