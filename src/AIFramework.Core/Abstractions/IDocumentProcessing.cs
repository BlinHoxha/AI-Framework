namespace AIFramework.Core.Abstractions;

// Content ownership, malware scanning, authorization, storage and job persistence belong to the host.
public sealed record DocumentText(string Text, string Language, string ExtractorVersion);
public interface IDocumentTextExtractor
{
    Task<DocumentText> ExtractAsync(Stream content, string contentType, CancellationToken cancellationToken);
}
