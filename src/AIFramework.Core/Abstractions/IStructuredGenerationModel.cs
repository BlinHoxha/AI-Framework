using AIFramework.Core.Models;

namespace AIFramework.Core.Abstractions;

/// <summary>Schema-constrained generation, independent of retrieval and product commands.</summary>
public interface IStructuredGenerationModel
{
    Task<StructuredGenerationResult> GenerateStructuredAsync(
        StructuredGenerationRequest request, CancellationToken cancellationToken);
}
