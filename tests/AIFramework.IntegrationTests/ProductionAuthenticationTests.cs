using System.Net;
using AIFramework.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;

namespace AIFramework.IntegrationTests.Api;

public sealed class ProductionAuthenticationTests
{
    [Fact]
    public async Task KnowledgeEndpoint_RejectsAnonymousProductionRequest()
    {
        using FrameworkApiFactory factory = new();
        using var production = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("Authentication:Authority", "https://login.microsoftonline.com/11111111-1111-1111-1111-111111111111/v2.0");
            builder.UseSetting("Authentication:Audience", "api://framework-test");
            builder.UseSetting("AI:Provider", "Azure");
            builder.UseSetting("AI:Azure:OpenAiEndpoint", "https://example.openai.azure.com/");
            builder.UseSetting("AI:Azure:ChatDeployment", "chat");
            builder.UseSetting("AI:Azure:EmbeddingDeployment", "embeddings");
            builder.UseSetting("AI:Azure:SearchEndpoint", "https://example.search.windows.net/");
            builder.UseSetting("AI:Azure:SearchIndex", "knowledge-chunks");
        });
        using HttpClient client = production.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/v1/ai/document-profiles");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
