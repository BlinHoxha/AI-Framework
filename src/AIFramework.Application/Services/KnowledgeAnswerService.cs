using AIFramework.Core.Abstractions;
using AIFramework.Core.Models;
using AIFramework.Contracts;

namespace AIFramework.Application.Services;

public sealed class KnowledgeAnswerService(
    IDocumentProfileCatalog profileCatalog,
    IKnowledgeRetriever knowledgeRetriever,
    IChatModel chatModel)
{
    private const string NoEvidenceAnswer = "I could not find sufficient evidence in the authorized documents to answer this question.";

    public async Task<GroundedAnswerResponse> AnswerAsync(
        AskKnowledgeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Question) || request.Question.Trim().Length is < 2 or > 2000 ||
            string.IsNullOrWhiteSpace(request.Profile) || request.MaxResults is < 1 or > 20 || request.ScopeId == Guid.Empty)
            throw new ArgumentException("Question, profile, result limit or scope is invalid.");
        if (request.TenantId == Guid.Empty)
        {
            throw new ArgumentException("TenantId is required.", nameof(request));
        }

        DocumentProfile profile = profileCatalog.GetRequired(request.Profile);
        if (!profile.AllowedOperations.Contains("question-answering", StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Profile '{profile.Name}' does not allow question answering.");
        }

        KnowledgeSearchQuery query = new(
            request.Question.Trim(),
            profile.Name,
            request.TenantId,
            request.ScopeId,
            request.DocumentIds,
            request.MaxResults);

        // Defense in depth for custom retrievers; authorization still belongs to the host.
        IReadOnlyCollection<KnowledgeChunk> chunks = (await knowledgeRetriever.SearchAsync(query, cancellationToken))
            .Where(c => c.TenantId == query.TenantId && c.Profile == query.Profile &&
                (c.ScopeId is null || c.ScopeId == query.ScopeId) &&
                (query.DocumentIds is null || query.DocumentIds.Contains(c.DocumentId)))
            .Take(query.MaxResults).ToArray();
        if (chunks.Count == 0)
        {
            return new GroundedAnswerResponse(NoEvidenceAnswer, false, "none", []);
        }

        GroundedGenerationResult generation = await chatModel.GenerateAsync(
            new GroundedGenerationRequest(request.Question.Trim(), profile, chunks),
            cancellationToken);

        CitationResponse[] citations = chunks
            .Select(chunk => new CitationResponse(
                chunk.DocumentId,
                chunk.Title,
                CreateExcerpt(chunk.Content),
                chunk.SourceUri,
                chunk.ChunkNumber))
            .ToArray();

        return new GroundedAnswerResponse(generation.Answer, true, generation.Provider, citations);
    }

    private static string CreateExcerpt(string content) =>
        content.Length <= 240 ? content : $"{content[..237]}...";
}

