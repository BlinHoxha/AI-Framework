using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using AIFramework.Core.Abstractions;
using AIFramework.Core.Models;

namespace AIFramework.Local;

public sealed partial class InMemoryKnowledgeStore : IKnowledgeIndex, IKnowledgeRetriever
{
    private readonly ConcurrentDictionary<string, KnowledgeChunk> _chunks = new(StringComparer.Ordinal);

    public Task IndexAsync(KnowledgeDocument document, IReadOnlyCollection<string> chunks, CancellationToken cancellationToken)
    {
        int chunkNumber = 0;
        foreach (string content in chunks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            chunkNumber++;
            string id = $"{document.Id:N}-{chunkNumber}";
            _chunks[id] = new KnowledgeChunk(
                id, document.Id, chunkNumber, document.Title, content, document.Profile,
                document.TenantId, document.ScopeId, document.Language, document.SourceUri);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyCollection<KnowledgeChunk>> SearchAsync(KnowledgeSearchQuery query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        HashSet<string> queryTerms = Tokenize(query.Question);
        HashSet<Guid>? documentIds = query.DocumentIds?.ToHashSet();

        KnowledgeChunk[] matches = _chunks.Values
            .Where(chunk => chunk.TenantId == query.TenantId)
            .Where(chunk => string.Equals(chunk.Profile, query.Profile, StringComparison.OrdinalIgnoreCase))
            .Where(chunk => IsVisibleInScope(chunk.ScopeId, query.ScopeId))
            .Where(chunk => documentIds is null || documentIds.Contains(chunk.DocumentId))
            .Select(chunk => chunk with { Score = CalculateScore(chunk, query.Question, queryTerms) })
            .Where(chunk => chunk.Score > 0)
            .OrderByDescending(chunk => chunk.Score)
            .ThenBy(chunk => chunk.DocumentId)
            .ThenBy(chunk => chunk.ChunkNumber)
            .Take(query.MaxResults)
            .ToArray();

        return Task.FromResult<IReadOnlyCollection<KnowledgeChunk>>(matches);
    }

    private static bool IsVisibleInScope(Guid? documentScopeId, Guid? requestedScopeId) =>
        requestedScopeId.HasValue ? documentScopeId is null || documentScopeId == requestedScopeId : documentScopeId is null;

    private static double CalculateScore(KnowledgeChunk chunk, string question, HashSet<string> queryTerms)
    {
        HashSet<string> contentTerms = Tokenize($"{chunk.Title} {chunk.Content}");
        int matchingTerms = queryTerms.Count(contentTerms.Contains);
        double phraseBonus = chunk.Content.Contains(question, StringComparison.OrdinalIgnoreCase) ? 2 : 0;
        return matchingTerms + phraseBonus;
    }

    private static HashSet<string> Tokenize(string value) =>
        WordRegex().Matches(value.ToLowerInvariant()).Select(match => match.Value)
            .Where(word => word.Length > 2).ToHashSet(StringComparer.Ordinal);

    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex WordRegex();
}
