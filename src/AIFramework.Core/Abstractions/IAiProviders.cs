using AIFramework.Core.Models;

namespace AIFramework.Core.Abstractions;

public interface ITextChunker
{
    IReadOnlyCollection<string> Split(string content);
}

public interface IKnowledgeIndex
{
    Task IndexAsync(
        KnowledgeDocument document,
        IReadOnlyCollection<string> chunks,
        CancellationToken cancellationToken);
}

public interface IKnowledgeRetriever
{
    Task<IReadOnlyCollection<KnowledgeChunk>> SearchAsync(
        KnowledgeSearchQuery query,
        CancellationToken cancellationToken);
}

public interface IChatModel
{
    Task<GroundedGenerationResult> GenerateAsync(
        GroundedGenerationRequest request,
        CancellationToken cancellationToken);
}

public interface ITextEmbeddingModel
{
    Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken);
}

public interface IDocumentProfileCatalog
{
    IReadOnlyCollection<DocumentProfile> GetAll();

    DocumentProfile GetRequired(string name);
}
