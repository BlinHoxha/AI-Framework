using AIFramework.Core.Abstractions;
using AIFramework.Core.Models;

namespace AIFramework.Application.DocumentProfiles;

public sealed class DefaultDocumentProfileCatalog : IDocumentProfileCatalog
{
    private static readonly IReadOnlyDictionary<string, DocumentProfile> Profiles =
        new Dictionary<string, DocumentProfile>(StringComparer.OrdinalIgnoreCase)
        {
            ["general-document"] = new(
                "general-document",
                "A domain-neutral profile for grounded document ingestion and question answering.",
                ["ingest", "question-answering"],
                ["*"],
                false)
        };

    public IReadOnlyCollection<DocumentProfile> GetAll() => Profiles.Values.ToArray();

    public DocumentProfile GetRequired(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || !Profiles.TryGetValue(name.Trim(), out DocumentProfile? profile))
        {
            throw new ArgumentException($"Unknown document profile '{name}'.", nameof(name));
        }

        return profile;
    }
}
