using System.Net;
using System.Text.Json;
using AIFramework.Azure;
using AIFramework.Core.Models;
using AIFramework.Core.Abstractions;
using Azure.Core;
using Microsoft.Extensions.DependencyInjection;

namespace AIFramework.UnitTests;

public sealed class StructuredGenerationTests
{
    [Fact]
    public void Registration_needs_no_embeddings_search_or_grounded_answer_model()
    {
        var services = new ServiceCollection();
        services.AddAzureStructuredGeneration(new(new("https://test.openai.azure.com/"), "explicit-deployment"));
        using var provider = services.BuildServiceProvider();
        Assert.IsType<AzureStructuredGenerationModel>(provider.GetRequiredService<IStructuredGenerationModel>());
        Assert.Null(provider.GetService<ITextEmbeddingModel>());
        Assert.Null(provider.GetService<IKnowledgeRetriever>());
        Assert.Null(provider.GetService<IChatModel>());
    }
    private static JsonElement Schema => JsonDocument.Parse("""{"type":"object","properties":{"scene":{"type":["string","null"],"enum":["Orion",null]}},"required":["scene"],"additionalProperties":false}""").RootElement.Clone();
    private static StructuredGenerationRequest Request => new("test_proposal", "Return only a proposal.", "Orion", Schema);

    [Fact]
    public async Task Sends_strict_schema_without_retrieval_and_reports_usage()
    {
        using var handler = new Handler(Reply("""{"scene":"Orion"}"""));
        var result = await Model(handler).GenerateStructuredAsync(Request, CancellationToken.None);
        Assert.Equal(StructuredGenerationStatus.Completed, result.Status);
        Assert.Equal(12, result.Usage.InputTokens);
        using var sent = JsonDocument.Parse(handler.Body!);
        var format = sent.RootElement.GetProperty("response_format");
        Assert.Equal("json_schema", format.GetProperty("type").GetString());
        Assert.True(format.GetProperty("json_schema").GetProperty("strict").GetBoolean());
        Assert.Equal(1024, sent.RootElement.GetProperty("max_completion_tokens").GetInt32());
        Assert.Contains("api-version=2024-10-21", handler.Uri!.Query);
        Assert.Equal("Bearer", handler.Authentication);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"scene\":\"Mars\"}")]
    [InlineData("{\"scene\":4}")]
    [InlineData("{\"scene\":null,\"execute\":true}")]
    [InlineData("{\"scene\":null,\"scene\":\"Orion\"}")]
    [InlineData("```json\n{}\n```")]
    public async Task Rejects_invalid_or_schema_violating_provider_output(string content)
    {
        using var handler = new Handler(Reply(content));
        var error = await Assert.ThrowsAsync<StructuredGenerationException>(() => Model(handler).GenerateStructuredAsync(Request, CancellationToken.None));
        Assert.Equal("invalid_response", error.Code);
    }

    [Theory]
    [InlineData("length", null, StructuredGenerationStatus.Incomplete)]
    [InlineData("tool_calls", null, StructuredGenerationStatus.Incomplete)]
    [InlineData("stop", "refused", StructuredGenerationStatus.Refused)]
    public async Task Never_returns_partial_or_refused_content(string finish, string? refusal, StructuredGenerationStatus status)
    {
        using var handler = new Handler(Reply("partial", finish, refusal));
        var result = await Model(handler).GenerateStructuredAsync(Request, CancellationToken.None);
        Assert.Equal(status, result.Status);
        Assert.Null(result.OutputJson);
    }

    [Fact]
    public async Task Bounds_response_and_propagates_cancellation()
    {
        using var handler = new Handler(new string('x', 140_000));
        var error = await Assert.ThrowsAsync<StructuredGenerationException>(() => Model(handler).GenerateStructuredAsync(Request, CancellationToken.None));
        Assert.Equal("response_too_large", error.Code);
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Model(handler).GenerateStructuredAsync(Request, cancel.Token));
    }

    [Fact]
    public void Schema_subset_and_endpoint_validation_fail_before_provider_calls()
    {
        Assert.Throws<ArgumentException>(() => StructuredJson.ValidateSchema(JsonDocument.Parse("""{"type":"string","pattern":"x"}""").RootElement));
        Assert.Throws<ArgumentException>(() => new AzureStructuredOptions(new("http://example.com"), "model").Validate());
        Assert.Throws<ArgumentException>(() => new AzureStructuredOptions(new("https://example.com/?secret=x"), "model").Validate());
    }

    private static string Reply(string content, string finish = "stop", string? refusal = null) => JsonSerializer.Serialize(new
    {
        choices = new[] { new { finish_reason = finish, message = new { content, refusal } } },
        usage = new { prompt_tokens = 12, completion_tokens = 9 }
    });
    private static AzureStructuredGenerationModel Model(Handler handler) => new(new(new("https://test.openai.azure.com/"), "explicit-deployment"),
        new(new HttpClient(handler), new Credential()));
    private sealed class Credential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) => new("test-token", DateTimeOffset.UtcNow.AddMinutes(1));
        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) => ValueTask.FromResult(GetToken(requestContext, cancellationToken));
    }
    private sealed class Handler(string response) : HttpMessageHandler
    {
        public string? Body { get; private set; }
        public Uri? Uri { get; private set; }
        public string? Authentication { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            Uri = request.RequestUri;
            Authentication = request.Headers.Authorization?.Scheme;
            return new(HttpStatusCode.OK) { Content = new StringContent(response) };
        }
    }
}
