namespace AIFramework.Azure;

public sealed record AzureModelOptions(Uri OpenAiEndpoint, string ChatDeployment, string EmbeddingDeployment, int ChatMaxOutputTokens = 4096)
{
    public void Validate()
    {
        if (!OpenAiEndpoint.IsAbsoluteUri || OpenAiEndpoint.Scheme != "https" ||
            string.IsNullOrWhiteSpace(ChatDeployment) || string.IsNullOrWhiteSpace(EmbeddingDeployment) ||
            ChatMaxOutputTokens is < 1 or > 16384)
            throw new ArgumentException("Azure models require an HTTPS endpoint and chat/embedding deployments.");
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
