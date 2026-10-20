using System.ComponentModel.DataAnnotations;

namespace AIFramework.Contracts;

public sealed record AskKnowledgeRequest
{
    [Required]
    [MinLength(2)]
    [MaxLength(2_000)]
    public required string Question { get; init; }

    [Required]
    [MaxLength(100)]
    public required string Profile { get; init; }

    public required Guid TenantId { get; init; }

    public Guid? ScopeId { get; init; }

    public IReadOnlyCollection<Guid>? DocumentIds { get; init; }

    [Range(1, 20)]
    public int MaxResults { get; init; } = 5;
}

