namespace AIFramework.Azure;

public sealed class AzureAiOptions
{
    public const string SectionName = "AI:Azure";

    public required Uri OpenAiEndpoint { get; init; }
    public required string ChatDeployment { get; init; }
    public required string EmbeddingDeployment { get; init; }
    public required Uri SearchEndpoint { get; init; }
    public required string SearchIndex { get; init; }

    public static AzureAiOptions FromValues(
        string? openAiEndpoint,
        string? chatDeployment,
        string? embeddingDeployment,
        string? searchEndpoint,
        string? searchIndex)
    {
        if (!Uri.TryCreate(openAiEndpoint, UriKind.Absolute, out Uri? openAiUri) || openAiUri.Scheme != Uri.UriSchemeHttps ||
            !Uri.TryCreate(searchEndpoint, UriKind.Absolute, out Uri? searchUri) || searchUri.Scheme != Uri.UriSchemeHttps ||
            string.IsNullOrWhiteSpace(chatDeployment) || string.IsNullOrWhiteSpace(embeddingDeployment) || string.IsNullOrWhiteSpace(searchIndex))
        {
            throw new InvalidOperationException("AI:Azure requires HTTPS OpenAiEndpoint and SearchEndpoint plus ChatDeployment, EmbeddingDeployment, and SearchIndex.");
        }

        return new AzureAiOptions
        {
            OpenAiEndpoint = openAiUri,
            ChatDeployment = chatDeployment,
            EmbeddingDeployment = embeddingDeployment,
            SearchEndpoint = searchUri,
            SearchIndex = searchIndex
        };
    }
}
