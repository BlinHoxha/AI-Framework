namespace AIFramework.Contracts;

public sealed record KnowledgeDocumentResponse(
    Guid DocumentId,
    string Status,
    int ChunkCount,
    string Profile);

public sealed record CitationResponse(
    Guid DocumentId,
    string Title,
    string Excerpt,
    string? SourceUri,
    int ChunkNumber);

public sealed record GroundedAnswerResponse(
    string Answer,
    bool Grounded,
    string Provider,
    IReadOnlyCollection<CitationResponse> Citations);

public sealed record DocumentProfileResponse(
    string Name,
    string Description,
    IReadOnlyCollection<string> AllowedOperations,
    IReadOnlyCollection<string> SupportedLanguages,
    bool RequiresHumanReview);

