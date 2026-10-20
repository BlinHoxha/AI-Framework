using AIFramework.Aws;
using AIFramework.Core.Models;
using Amazon;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Amazon.Runtime;
namespace AIFramework.UnitTests;
public sealed class BedrockChatModelTests
{
    [Fact]
    public async Task Adapter_sends_evidence_and_cancellation_and_returns_text_with_review_notice()
    {
        using var client = new StubClient();
        using var cancellation = new CancellationTokenSource();
        var model = new BedrockChatModel(client, new("eu-central-1", "test-model"));
        var result = await model.GenerateAsync(new("When is review?", new("legal", "Legal evidence", ["question-answering"], ["en"], true),
            [new("chunk", Guid.NewGuid(), 1, "Policy", "Review is Monday.", "legal", Guid.NewGuid(), null, "en", null)]), cancellation.Token);
        Assert.Contains("Human verification", result.Answer);
        Assert.Equal("aws-bedrock", result.Provider);
        Assert.Contains("Review is Monday", client.Request!.Messages[0].Content[0].Text);
        Assert.Equal(cancellation.Token, client.Cancellation);
    }
    private sealed class StubClient() : AmazonBedrockRuntimeClient(new AnonymousAWSCredentials(), RegionEndpoint.EUCentral1)
    {
        public ConverseRequest? Request { get; private set; }
        public CancellationToken Cancellation { get; private set; }
        public override Task<ConverseResponse> ConverseAsync(ConverseRequest request, CancellationToken cancellationToken = default)
        {
            Request = request; Cancellation = cancellationToken;
            return Task.FromResult(new ConverseResponse { StopReason = StopReason.End_turn,
                Output = new ConverseOutput { Message = new Message { Role = ConversationRole.Assistant, Content = [new ContentBlock { Text = "Monday [1]." }] } } });
        }
    }
}
