using System.Net;
using System.Text.Json;
using AIFramework.Aws;
using AIFramework.Azure;
using AIFramework.Core.Abstractions;
using AIFramework.Core.Models;
using AIFramework.Local;
using Amazon;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Amazon.Runtime;
using Azure.Core;
using Microsoft.Extensions.DependencyInjection;

namespace AIFramework.UnitTests;

public sealed class TextGenerationTests
{
    private static readonly TextGenerationRequest Request = new("Write plainly.", "Hello", 321);

    [Theory]
    [InlineData("stop", true)]
    [InlineData("length", false)]
    public async Task Azure_text_generation_maps_request_and_result(string finishReason, bool complete)
    {
        using var handler = new Handler("""{"model":"chat-model","choices":[{"finish_reason":"REASON","message":{"content":"reply"}}],"usage":{"prompt_tokens":7,"completion_tokens":3}}""".Replace("REASON", finishReason));
        var result = await AzureModel(handler).GenerateTextAsync(Request, CancellationToken.None);
        Assert.Equal("reply", result.Text);
        Assert.Equal("azure-openai", result.Provider);
        Assert.Equal("chat-model", result.Model);
        Assert.Equal(new GenerationUsage(7, 3), result.Usage);
        Assert.Equal(complete, result.Complete);
        using var body = JsonDocument.Parse(handler.Body!);
        var root = body.RootElement;
        Assert.False(root.TryGetProperty("temperature", out _));
        Assert.Equal(321, root.GetProperty("max_completion_tokens").GetInt32());
        Assert.Equal("system", root.GetProperty("messages")[0].GetProperty("role").GetString());
        Assert.Equal("Write plainly.", root.GetProperty("messages")[0].GetProperty("content").GetString());
        Assert.Equal("user", root.GetProperty("messages")[1].GetProperty("role").GetString());
        Assert.Equal("Hello", root.GetProperty("messages")[1].GetProperty("content").GetString());
    }

    [Theory]
    [InlineData("tool_calls")]
    [InlineData("content_filter")]
    public async Task Azure_text_generation_rejects_other_finish_reasons(string reason)
    {
        using var handler = new Handler("""{"choices":[{"finish_reason":"REASON","message":{"content":"reply"}}]}""".Replace("REASON", reason));
        await Assert.ThrowsAsync<InvalidOperationException>(() => AzureModel(handler).GenerateTextAsync(Request, CancellationToken.None));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("\"\"")]
    public async Task Azure_text_generation_rejects_null_or_missing_content(string content)
    {
        using var handler = new Handler("""{"choices":[{"finish_reason":"stop","message":{"content":CONTENT}}]}""".Replace("CONTENT", content));
        if (content == "null")
            await Assert.ThrowsAsync<InvalidOperationException>(() => AzureModel(handler).GenerateTextAsync(Request, CancellationToken.None));
        else
            Assert.Equal("", (await AzureModel(handler).GenerateTextAsync(Request, CancellationToken.None)).Text);
    }

    [Fact]
    public async Task Azure_text_generation_rejects_missing_content_and_finish_reason()
    {
        using var missingContent = new Handler("""{"choices":[{"finish_reason":"stop","message":{}}]}""");
        await Assert.ThrowsAsync<InvalidOperationException>(() => AzureModel(missingContent).GenerateTextAsync(Request, CancellationToken.None));
        using var missingFinish = new Handler("""{"choices":[{"message":{"content":"reply"}}]}""");
        await Assert.ThrowsAsync<InvalidOperationException>(() => AzureModel(missingFinish).GenerateTextAsync(Request, CancellationToken.None));
    }

    [Fact]
    public async Task Azure_text_generation_rejects_oversized_response()
    {
        using var handler = new Handler(new string('x', 1_048_577));
        var error = await Assert.ThrowsAsync<StructuredGenerationException>(() => AzureModel(handler).GenerateTextAsync(Request, CancellationToken.None));
        Assert.Equal("response_too_large", error.Code);
    }

    [Theory]
    [InlineData("end_turn", true)]
    [InlineData("max_tokens", false)]
    public async Task Bedrock_text_generation_maps_request_and_result(string reason, bool complete)
    {
        using var client = new StubClient(reason, "reply", new TokenUsage { InputTokens = 8, OutputTokens = 4 });
        var result = await new BedrockChatModel(client, new("eu-central-1", "test-model"))
            .GenerateTextAsync(Request, CancellationToken.None);
        Assert.Equal("reply", result.Text);
        Assert.Equal("aws-bedrock", result.Provider);
        Assert.Equal("test-model", result.Model);
        Assert.Equal(new GenerationUsage(8, 4), result.Usage);
        Assert.Equal(complete, result.Complete);
        Assert.Equal(321, client.Request!.InferenceConfig.MaxTokens);
        Assert.Equal("Write plainly.", client.Request.System[0].Text);
        Assert.Equal("Hello", client.Request.Messages[0].Content[0].Text);
    }

    [Theory]
    [InlineData("tool_use", "reply")]
    [InlineData("end_turn", "")]
    public async Task Bedrock_text_generation_rejects_other_stops_or_empty_text(string reason, string answer)
    {
        using var client = new StubClient(reason, answer);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new BedrockChatModel(client, new("eu-central-1", "test-model")).GenerateTextAsync(Request, CancellationToken.None));
    }

    [Theory]
    [InlineData("", "input", 1024)]
    [InlineData(" ", "input", 1024)]
    [InlineData("instructions", "", 1024)]
    [InlineData("instructions", " ", 1024)]
    [InlineData("instructions", "input", 0)]
    [InlineData("instructions", "input", 16385)]
    public void Request_rejects_invalid_values(string instructions, string input, int maxTokens)
        => Assert.Throws<ArgumentException>(() => new TextGenerationRequest(instructions, input, maxTokens).Validate());

    [Fact]
    public void Request_checks_length_boundaries()
    {
        new TextGenerationRequest(new string('a', 32_000), new string('b', 200_000), 16_384).Validate();
        new TextGenerationRequest("a", "b", 1).Validate();
        Assert.Throws<ArgumentException>(() => new TextGenerationRequest(new string('a', 32_001), "b").Validate());
        Assert.Throws<ArgumentException>(() => new TextGenerationRequest("a", new string('b', 200_001)).Validate());
    }

    [Fact]
    public async Task Local_text_generation_is_deterministic_and_honors_cancellation()
    {
        var model = new LocalTextGenerationModel();
        var result = await model.GenerateTextAsync(new("instructions", new string('x', 201)), CancellationToken.None);
        Assert.Equal("Local text generation (no model): " + new string('x', 200), result.Text);
        Assert.Equal("local-echo", result.Provider);
        Assert.Null(result.Model);
        Assert.Null(result.Usage);
        Assert.True(result.Complete);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => model.GenerateTextAsync(Request, cancellation.Token));
    }

    [Fact]
    public void Registrations_resolve_text_generation_and_share_cloud_instances()
    {
        using var azure = new ServiceCollection().AddAzureModels(new(new("https://test.openai.azure.com/"), "chat", "embedding"))
            .BuildServiceProvider();
        Assert.Same(azure.GetRequiredService<IChatModel>(), azure.GetRequiredService<ITextGenerationModel>());

        using var bedrock = new ServiceCollection().AddBedrockChat(new("eu-central-1", "test-model"))
            .BuildServiceProvider();
        Assert.Same(bedrock.GetRequiredService<IChatModel>(), bedrock.GetRequiredService<ITextGenerationModel>());

        using var local = new ServiceCollection().AddLocalAi().BuildServiceProvider();
        Assert.IsType<LocalTextGenerationModel>(local.GetRequiredService<ITextGenerationModel>());
    }

    [Fact]
    public void Azure_options_report_specific_output_limit_message()
    {
        var error = Assert.Throws<ArgumentException>(() =>
            new AzureModelOptions(new("https://test.openai.azure.com/"), "chat", "embedding", 0).Validate());
        Assert.Contains("ChatMaxOutputTokens", error.Message);
    }

    private static AzureOpenAiClient AzureModel(Handler handler) => new(
        new AzureModelOptions(new("https://test.openai.azure.com/"), "chat", "embedding"),
        new(new HttpClient(handler), new Credential()));

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

    private sealed class StubClient(string reason, string answer, TokenUsage? usage = null)
        : AmazonBedrockRuntimeClient(new AnonymousAWSCredentials(), RegionEndpoint.EUCentral1)
    {
        public ConverseRequest? Request { get; private set; }
        public override Task<ConverseResponse> ConverseAsync(ConverseRequest request, CancellationToken cancellationToken = default)
        {
            Request = request;
            return Task.FromResult(new ConverseResponse { StopReason = StopReason.FindValue(reason), Usage = usage,
                Output = new ConverseOutput { Message = new Message { Role = ConversationRole.Assistant,
                    Content = [new ContentBlock { Text = answer }] } } });
        }
    }
}
