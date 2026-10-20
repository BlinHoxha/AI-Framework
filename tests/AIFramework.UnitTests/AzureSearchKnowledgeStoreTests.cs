using System.Net;
using System.Text.Json;
using Azure.Core;
using AIFramework.Azure;
using AIFramework.Core.Models;

namespace AIFramework.UnitTests.AI;

public sealed class AzureSearchKnowledgeStoreTests
{
    private static readonly Guid Tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Scope = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task Search_FiltersTenantProfileAndScopeBeforeVectorSearch()
    {
        RecordingHandler handler = new();
        AzureSearchKnowledgeStore store = CreateStore(handler);
        KnowledgeSearchQuery query = new("Where is the manual?", "general-document", Tenant, Scope, null, 5);

        await store.SearchAsync(query, CancellationToken.None);

        JsonElement searchRequest = JsonDocument.Parse(handler.Bodies[1]).RootElement;
        string filter = searchRequest.GetProperty("filter").GetString()!;
        Assert.Contains($"tenantId eq '{Tenant:D}'", filter);
        Assert.Contains("profile eq 'general-document'", filter);
        Assert.Contains($"(scopeId eq null or scopeId eq '{Scope:D}')", filter);
        Assert.Equal("preFilter", searchRequest.GetProperty("vectorFilterMode").GetString());
        Assert.Equal(1, searchRequest.GetProperty("vectorQueries").GetArrayLength());
    }

    [Fact]
    public async Task Index_SendsAzureSearchUploadAction()
    {
        RecordingHandler handler = new();
        AzureSearchKnowledgeStore store = CreateStore(handler);
        KnowledgeDocument document = new(Guid.NewGuid(), "Manual", "general-document", Tenant, null, "en", null);

        await store.IndexAsync(document, ["Example content"], CancellationToken.None);

        JsonElement indexed = JsonDocument.Parse(handler.Bodies[1]).RootElement.GetProperty("value")[0];
        Assert.Equal("upload", indexed.GetProperty("@search.action").GetString());
        Assert.Equal(Tenant.ToString("D"), indexed.GetProperty("tenantId").GetString());
    }

    private static AzureSearchKnowledgeStore CreateStore(RecordingHandler handler)
    {
        AzureAiOptions options = AzureAiOptions.FromValues(
            "https://example.openai.azure.com/", "chat", "embeddings",
            "https://example.search.windows.net/", "knowledge-chunks");
        AzureTokenClient client = new(new HttpClient(handler), new FakeCredential());
        AzureOpenAiClient openAi = new(options, client);
        return new AzureSearchKnowledgeStore(options, client, openAi);
    }

    private sealed class FakeCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            new("test-token", DateTimeOffset.UtcNow.AddMinutes(5));

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            ValueTask.FromResult(GetToken(requestContext, cancellationToken));
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            string response = request.RequestUri!.AbsolutePath switch
            {
                string path when path.EndsWith("/embeddings", StringComparison.Ordinal) => "{\"data\":[{\"embedding\":[0.1,0.2]}]}",
                string path when path.EndsWith("/docs/index", StringComparison.Ordinal) => "{\"value\":[{\"key\":\"chunk-1\",\"status\":true}]}",
                _ => "{\"value\":[]}"
            };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response) };
        }
    }
}
