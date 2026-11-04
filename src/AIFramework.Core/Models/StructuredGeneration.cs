using System.Text.Json;

namespace AIFramework.Core.Models;

public sealed record StructuredGenerationRequest(string SchemaName, string Instructions,
    string Input, JsonElement Schema, int MaxOutputTokens = 1024);
public enum StructuredGenerationStatus { Completed, Refused, Incomplete }
public sealed record GenerationUsage(int? InputTokens, int? OutputTokens);
public sealed record StructuredGenerationResult(StructuredGenerationStatus Status,
    string? OutputJson, string Provider, GenerationUsage Usage);
public sealed class StructuredGenerationException(string code) : Exception("Structured generation failed.")
{
    public string Code { get; } = code;
}

/// <summary>Defence-in-depth validation for the deliberately small schema subset used here.
/// Supports object/array/scalar types, nullable type arrays, required properties and enums.
/// This is not a general-purpose JSON Schema implementation.</summary>
public static class StructuredJson
{
    private static readonly HashSet<string> Keywords = ["type", "properties", "required", "additionalProperties", "items", "enum", "description"];

    public static void ValidateSchema(JsonElement schema)
    {
        if (schema.ValueKind != JsonValueKind.Object || schema.EnumerateObject().Any(p => !Keywords.Contains(p.Name)))
            throw new ArgumentException("Unsupported structured-output schema keyword or shape.");
        if (!schema.TryGetProperty("type", out var type)) throw new ArgumentException("Explicit schema types are required.");
        string[] types = type.ValueKind == JsonValueKind.Array ? type.EnumerateArray().Select(t => t.GetString()!).ToArray() : [type.GetString()!];
        if (types.Length == 0 || types.Any(t => t is not ("object" or "array" or "string" or "integer" or "number" or "boolean" or "null")))
            throw new ArgumentException("Unsupported structured-output type.");
        if (types.Contains("object"))
        {
            if (!schema.TryGetProperty("properties", out var properties) || properties.ValueKind != JsonValueKind.Object ||
                !schema.TryGetProperty("additionalProperties", out var extra) || extra.ValueKind != JsonValueKind.False ||
                !schema.TryGetProperty("required", out var required) || required.ValueKind != JsonValueKind.Array)
                throw new ArgumentException("Objects must be closed and every property required; use nullable types for missing values.");
            var names = properties.EnumerateObject().Select(p => p.Name).Order().ToArray();
            if (!names.SequenceEqual(required.EnumerateArray().Select(p => p.GetString()).Order()))
                throw new ArgumentException("Every property must be required exactly once.");
            foreach (var property in properties.EnumerateObject()) ValidateSchema(property.Value);
        }
        if (types.Contains("array")) ValidateSchema(schema.GetProperty("items"));
    }

    public static bool Matches(JsonElement value, JsonElement schema)
    {
        var type = schema.GetProperty("type");
        string actual = value.ValueKind switch
        {
            JsonValueKind.Object => "object", JsonValueKind.Array => "array", JsonValueKind.String => "string",
            JsonValueKind.Number => "number", JsonValueKind.True or JsonValueKind.False => "boolean", _ => "null"
        };
        bool IsType(string? t) => t == actual || t == "integer" && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _);
        if (!(type.ValueKind == JsonValueKind.Array ? type.EnumerateArray().Any(t => IsType(t.GetString())) : IsType(type.GetString()))) return false;
        if (schema.TryGetProperty("enum", out var choices) && !choices.EnumerateArray().Any(c =>
            c.ValueKind == value.ValueKind && (c.ValueKind == JsonValueKind.String ? c.GetString() == value.GetString() : c.GetRawText() == value.GetRawText()))) return false;
        if (value.ValueKind == JsonValueKind.Object)
        {
            var properties = schema.GetProperty("properties");
            var members = value.EnumerateObject().ToArray();
            if (members.Select(p => p.Name).Distinct().Count() != members.Length ||
                schema.GetProperty("required").EnumerateArray().Any(p => !value.TryGetProperty(p.GetString()!, out _))) return false;
            foreach (var property in members)
                if (!properties.TryGetProperty(property.Name, out var child) || !Matches(property.Value, child)) return false;
        }
        if (value.ValueKind == JsonValueKind.Array && value.EnumerateArray().Any(v => !Matches(v, schema.GetProperty("items")))) return false;
        return true;
    }
}
