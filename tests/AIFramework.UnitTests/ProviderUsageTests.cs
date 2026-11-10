using System.Net;
using System.Text.Json;
using AIFramework.Azure;
using AIFramework.Core.Abstractions;
using AIFramework.Core.Models;
using Azure.Core;

namespace AIFramework.UnitTests;

public sealed class ProviderUsageTests
{
    [Fact]
    public async Task Azure_chat_bounds_output_and_reports_model_usage_and_truncation()
    {
        using var handler = new Handler("""
            {"model":"chat-model","choices":[{"finish_reason":"length","message":{"content":"partial answer"}}],"usage":{"prompt_tokens":12,"completion_tokens":9}}
            """);
        var result = await Model(handler).GenerateAsync(Request, CancellationToken.None);

        Assert.Equal("partial answer", result.Answer);
        Assert.Equal("azure-openai", result.Provider);
        Assert.Equal("chat-model", result.Model);
        Assert.Equal(new GenerationUsage(12, 9), result.Usage);
        Assert.False(result.Complete);
        using var sent = JsonDocument.Parse(handler.Body!);
        Assert.Equal(4096, sent.RootElement.GetProperty("max_completion_tokens").GetInt32());
    }

    [Fact]
    public async Task Azure_chat_tolerates_missing_or_invalid_usage()
    {
        using var missing = new Handler("""{"choices":[{"message":{"content":"answer"}}]}""");
        var result = await Model(missing).GenerateAsync(Request, CancellationToken.None);
        Assert.True(result.Complete);
        Assert.Null(result.Model);
        Assert.Null(result.Usage);

        using var invalid = new Handler("""
            {"choices":[{"message":{"content":"answer"}}],"usage":{"prompt_tokens":-1,"completion_tokens":"unknown"}}
            """);
        result = await Model(invalid).GenerateAsync(Request, CancellationToken.None);
        Assert.Equal(new GenerationUsage(null, null), result.Usage);
    }

    [Fact]
    public async Task Azure_chat_rejects_oversized_response_with_existing_exception()
    {
        using var handler = new Handler(new string('x', 1_048_577));
        var error = await Assert.ThrowsAsync<StructuredGenerationException>(
            () => Model(handler).GenerateAsync(Request, CancellationToken.None));
        Assert.Equal("response_too_large", error.Code);
    }

    [Fact]
    public async Task Azure_embeddings_report_metadata_and_keep_legacy_vector()
    {
        using var handler = new Handler("""
            {"model":"embedding-model","data":[{"embedding":[0.1,0.2]}],"usage":{"prompt_tokens":5}}
            """);
        var model = Model(handler);
        var result = await model.EmbedWithMetadataAsync("hello", CancellationToken.None);
        Assert.Equal("azure-openai", result.Provider);
        Assert.Equal("embedding-model", result.Model);
        Assert.Equal(new GenerationUsage(5, null), result.Usage);
        Assert.Equal(new[] { 0.1f, 0.2f }, result.Vector);
        Assert.Equal(result.Vector, await model.EmbedAsync("hello", CancellationToken.None));
    }

    [Fact]
    public async Task Default_embedding_metadata_preserves_custom_implementations()
    {
        ITextEmbeddingModel model = new MinimalEmbedding();
        var result = await model.EmbedWithMetadataAsync("hello", CancellationToken.None);
        Assert.Equal("unknown", result.Provider);
        Assert.Equal(new[] { 1f }, result.Vector);
        Assert.Null(result.Model);
        Assert.Null(result.Usage);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(16385)]
    public void Azure_chat_output_limit_rejects_out_of_range_values(int limit)
        => Assert.Throws<ArgumentException>(() => Options(limit).Validate());

    [Fact]
    public void Azure_chat_output_limit_keeps_three_argument_constructor()
    {
        var options = new AzureModelOptions(new("https://test.openai.azure.com/"), "chat", "embedding");
        options.Validate();
        Assert.Equal(4096, options.ChatMaxOutputTokens);
    }

    private static GroundedGenerationRequest Request => new("question",
        new("general", "General", ["question-answering"], ["en"], false), []);

    private static AzureModelOptions Options(int limit) => new(new("https://test.openai.azure.com/"),
        "chat", "embedding", limit);

    private static AzureOpenAiClient Model(Handler handler) => new(Options(4096),
        new(new HttpClient(handler), new Credential()));

    private sealed class MinimalEmbedding : ITextEmbeddingModel
    {
        public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken)
            => Task.FromResult(new[] { 1f });
    }

    private sealed class Credential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new("test-token", DateTimeOffset.UtcNow.AddMinutes(1));
        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => ValueTask.FromResult(GetToken(requestContext, cancellationToken));
    }

    private sealed class Handler(string response) : HttpMessageHandler
    {
        public string? Body { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new(HttpStatusCode.OK) { Content = new StringContent(response) };
        }
    }
}
