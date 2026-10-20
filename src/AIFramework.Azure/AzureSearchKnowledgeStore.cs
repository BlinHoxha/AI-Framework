using System.Text.Json;
using AIFramework.Core.Abstractions;
using AIFramework.Core.Models;

namespace AIFramework.Azure;

public sealed class AzureSearchKnowledgeStore(AzureSearchOptions options, AzureTokenClient client, ITextEmbeddingModel openAi)
    : IKnowledgeIndex, IKnowledgeRetriever
{
    public AzureSearchKnowledgeStore(AzureAiOptions options, AzureTokenClient client, AzureOpenAiClient openAi)
        : this(new AzureSearchOptions(options.SearchEndpoint, options.SearchIndex), client, openAi) { }
    private const string Scope = "https://search.azure.com/.default";

    public async Task IndexAsync(KnowledgeDocument document, IReadOnlyCollection<string> chunks, CancellationToken cancellationToken)
    {
        List<object> entries = new(chunks.Count);
        int number = 0;
        foreach (string content in chunks)
        {
            number++;
            float[] vector = await openAi.EmbedAsync(content, cancellationToken);
            entries.Add(new Dictionary<string, object?>
            {
                ["@search.action"] = "upload",
                ["id"] = $"{document.Id:N}-{number}",
                ["documentId"] = document.Id.ToString("D"),
                ["chunkNumber"] = number,
                ["title"] = document.Title,
                ["content"] = content,
                ["profile"] = document.Profile,
                ["tenantId"] = document.TenantId.ToString("D"),
                ["scopeId"] = document.ScopeId?.ToString("D"),
                ["language"] = document.Language,
                ["sourceUri"] = document.SourceUri,
                ["vector"] = vector
            });
        }

        Uri uri = new(options.SearchEndpoint, $"indexes/{Uri.EscapeDataString(options.SearchIndex)}/docs/index?api-version=2025-09-01");
        foreach (object[] batch in entries.Chunk(1000))
        {
            using JsonDocument response = await client.PostAsync(uri, new { value = batch }, Scope, cancellationToken);
            foreach (JsonElement result in response.RootElement.GetProperty("value").EnumerateArray())
            {
                if (!result.GetProperty("status").GetBoolean())
                {
                    throw new InvalidOperationException($"Azure AI Search rejected chunk {result.GetProperty("key").GetString()}.");
                }
            }
        }
    }

    public async Task<IReadOnlyCollection<KnowledgeChunk>> SearchAsync(KnowledgeSearchQuery query, CancellationToken cancellationToken)
    {
        if (query.DocumentIds is { Count: 0 })
        {
            return [];
        }

        float[] vector = await openAi.EmbedAsync(query.Question, cancellationToken);
        string filter = $"tenantId eq '{query.TenantId:D}' and profile eq '{Escape(query.Profile)}'";
        filter += query.ScopeId.HasValue
            ? $" and (scopeId eq null or scopeId eq '{query.ScopeId.Value:D}')"
            : " and scopeId eq null";
        if (query.DocumentIds is { Count: > 0 })
        {
            filter += " and (" + string.Join(" or ", query.DocumentIds.Select(id => $"documentId eq '{id:D}'")) + ")";
        }

        Uri uri = new(options.SearchEndpoint, $"indexes/{Uri.EscapeDataString(options.SearchIndex)}/docs/search?api-version=2025-09-01");
        using JsonDocument response = await client.PostAsync(uri, new
        {
            search = query.Question,
            filter,
            vectorFilterMode = "preFilter",
            vectorQueries = new[] { new { kind = "vector", vector, fields = "vector", k = query.MaxResults } },
            top = query.MaxResults,
            select = "id,documentId,chunkNumber,title,content,profile,tenantId,scopeId,language,sourceUri"
        }, Scope, cancellationToken);

        return response.RootElement.GetProperty("value").EnumerateArray().Select(item => new KnowledgeChunk(
            item.GetProperty("id").GetString()!,
            Guid.Parse(item.GetProperty("documentId").GetString()!),
            item.GetProperty("chunkNumber").GetInt32(),
            item.GetProperty("title").GetString()!,
            item.GetProperty("content").GetString()!,
            item.GetProperty("profile").GetString()!,
            Guid.Parse(item.GetProperty("tenantId").GetString()!),
            item.TryGetProperty("scopeId", out JsonElement scope) && scope.ValueKind == JsonValueKind.String ? Guid.Parse(scope.GetString()!) : null,
            item.GetProperty("language").GetString()!,
            item.TryGetProperty("sourceUri", out JsonElement source) && source.ValueKind == JsonValueKind.String ? source.GetString() : null,
            item.TryGetProperty("@search.score", out JsonElement score) ? score.GetDouble() : 0)).ToArray();
    }

    private static string Escape(string value) => value.Replace("'", "''", StringComparison.Ordinal);
}
