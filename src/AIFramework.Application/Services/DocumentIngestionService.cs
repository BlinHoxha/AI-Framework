using AIFramework.Core.Abstractions;
using AIFramework.Core.Models;
using AIFramework.Contracts;

namespace AIFramework.Application.Services;

public sealed class DocumentIngestionService(
    IDocumentProfileCatalog profileCatalog,
    ITextChunker textChunker,
    IKnowledgeIndex knowledgeIndex)
{
    public async Task<KnowledgeDocumentResponse> IngestAsync(
        CreateKnowledgeDocumentRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Length > 300 ||
            string.IsNullOrWhiteSpace(request.Content) || request.Content.Length < 20 || request.Content.Length > 1_000_000 ||
            string.IsNullOrWhiteSpace(request.Profile) || string.IsNullOrWhiteSpace(request.Language) || request.ScopeId == Guid.Empty)
            throw new ArgumentException("Document metadata is invalid or content exceeds the synchronous ingestion limit.");
        EnsureTenant(request.TenantId);
        DocumentProfile profile = profileCatalog.GetRequired(request.Profile);
        EnsureOperation(profile, "ingest");

        Guid documentId = Guid.NewGuid();
        KnowledgeDocument document = new(
            documentId,
            request.Title.Trim(),
            profile.Name,
            request.TenantId,
            request.ScopeId,
            request.Language.Trim().ToLowerInvariant(),
            request.SourceUri);

        IReadOnlyCollection<string> chunks = textChunker.Split(request.Content);
        if (chunks.Count == 0)
        {
            throw new ArgumentException("The document does not contain indexable text.", nameof(request));
        }

        await knowledgeIndex.IndexAsync(document, chunks, cancellationToken);
        return new KnowledgeDocumentResponse(documentId, "ready", chunks.Count, profile.Name);
    }

    private static void EnsureTenant(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("TenantId is required.", nameof(tenantId));
        }
    }

    private static void EnsureOperation(DocumentProfile profile, string operation)
    {
        if (!profile.AllowedOperations.Contains(operation, StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Profile '{profile.Name}' does not allow '{operation}'.");
        }
    }
}

