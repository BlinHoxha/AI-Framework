namespace AIFramework.Azure;

public sealed record AzureModelOptions(Uri OpenAiEndpoint, string ChatDeployment, string EmbeddingDeployment, int ChatMaxOutputTokens = 4096)
{
    public void Validate()
    {
        if (!OpenAiEndpoint.IsAbsoluteUri || OpenAiEndpoint.Scheme != "https" ||
            string.IsNullOrWhiteSpace(ChatDeployment) || string.IsNullOrWhiteSpace(EmbeddingDeployment))
            throw new ArgumentException("Azure models require an HTTPS endpoint and chat/embedding deployments.");
        if (ChatMaxOutputTokens is < 1 or > 16384)
            throw new ArgumentException("ChatMaxOutputTokens must be between 1 and 16384.");
    }
}
public sealed record AzureSearchOptions(Uri SearchEndpoint, string SearchIndex)
{
    public void Validate()
    {
        if (!SearchEndpoint.IsAbsoluteUri || SearchEndpoint.Scheme != "https" || string.IsNullOrWhiteSpace(SearchIndex))
            throw new ArgumentException("Azure Search requires an HTTPS endpoint and index name.");
    }
}
