using System.ComponentModel.DataAnnotations;

namespace AIFramework.Contracts;

public sealed record CreateKnowledgeDocumentRequest
{
    [Required]
    [MaxLength(300)]
    public required string Title { get; init; }

    [Required]
    [MinLength(20)]
    public required string Content { get; init; }

    [Required]
    [MaxLength(100)]
    public required string Profile { get; init; }

    public required Guid TenantId { get; init; }

    public Guid? ScopeId { get; init; }

    [Required]
    [MaxLength(10)]
    public string Language { get; init; } = "en";

    [MaxLength(2_000)]
    public string? SourceUri { get; init; }
}

