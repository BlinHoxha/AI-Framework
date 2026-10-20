namespace AIFramework.Core.Models;

public sealed record KnowledgeDocument(
    Guid Id,
    string Title,
    string Profile,
    Guid TenantId,
    Guid? ScopeId,
    string Language,
    string? SourceUri);

public sealed record KnowledgeChunk(
    string Id,
    Guid DocumentId,
    int ChunkNumber,
    string Title,
    string Content,
    string Profile,
    Guid TenantId,
    Guid? ScopeId,
    string Language,
    string? SourceUri,
    double Score = 0);

public sealed record KnowledgeSearchQuery(
    string Question,
    string Profile,
    Guid TenantId,
    Guid? ScopeId,
    IReadOnlyCollection<Guid>? DocumentIds,
    int MaxResults);

public sealed record GroundedGenerationRequest(
    string Question,
    DocumentProfile Profile,
    IReadOnlyCollection<KnowledgeChunk> Context);

public sealed record GroundedGenerationResult(string Answer, string Provider);

public sealed record DocumentProfile(
    string Name,
    string Description,
    IReadOnlyCollection<string> AllowedOperations,
    IReadOnlyCollection<string> SupportedLanguages,
    bool RequiresHumanReview);
