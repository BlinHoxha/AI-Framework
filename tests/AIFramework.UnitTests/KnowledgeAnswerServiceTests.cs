using AIFramework.Core.Abstractions;
using AIFramework.Core.Models;
using AIFramework.Application.Services;
using AIFramework.Contracts;

namespace AIFramework.UnitTests.AI;

public sealed class KnowledgeAnswerServiceTests
{
    [Fact]
    public async Task AnswerAsync_WhenRetrievalIsEmpty_DoesNotCallModel()
    {
        StubChatModel chatModel = new();
        KnowledgeAnswerService service = new(
            new StubProfileCatalog(),
            new EmptyRetriever(),
            chatModel);

        GroundedAnswerResponse result = await service.AnswerAsync(new AskKnowledgeRequest
        {
            Question = "What is the deadline?",
            Profile = "test",
            TenantId = Guid.NewGuid()
        });

        Assert.False(result.Grounded);
        Assert.Empty(result.Citations);
        Assert.False(chatModel.WasCalled);
    }

    [Fact]
    public async Task AnswerAsync_WithEmptyTenant_RejectsRequestBeforeRetrieval()
    {
        KnowledgeAnswerService service = new(
            new StubProfileCatalog(),
            new EmptyRetriever(),
            new StubChatModel());

        await Assert.ThrowsAsync<ArgumentException>(() => service.AnswerAsync(new AskKnowledgeRequest
        {
            Question = "What is the deadline?",
            Profile = "test",
            TenantId = Guid.Empty
        }));
    }

    [Theory]
    [InlineData("tenant")]
    [InlineData("scope")]
    [InlineData("document")]
    [InlineData("profile")]
    public async Task AnswerAsync_WhenCustomRetrieverReturnsUnauthorizedEvidence_DoesNotCallModel(string boundary)
    {
        Guid tenantId = Guid.NewGuid();
        Guid scopeId = Guid.NewGuid();
        Guid documentId = Guid.NewGuid();
        KnowledgeChunk chunk = new(
            "unauthorized", boundary == "document" ? Guid.NewGuid() : documentId, 1,
            "Private manual", "The private deadline is Friday.",
            boundary == "profile" ? "other-profile" : "test",
            boundary == "tenant" ? Guid.NewGuid() : tenantId,
            boundary == "scope" ? Guid.NewGuid() : scopeId, "en", null);
        StubChatModel chatModel = new();
        KnowledgeAnswerService service = new(new StubProfileCatalog(), new UnfilteredRetriever(chunk), chatModel);

        GroundedAnswerResponse result = await service.AnswerAsync(new AskKnowledgeRequest
        {
            Question = "What is the deadline?",
            Profile = "test",
            TenantId = tenantId,
            ScopeId = scopeId,
            DocumentIds = [documentId]
        });

        Assert.False(result.Grounded);
        Assert.Empty(result.Citations);
        Assert.Equal("none", result.Provider);
        Assert.DoesNotContain("Friday", result.Answer);
        Assert.False(chatModel.WasCalled);
    }

    private sealed class UnfilteredRetriever(KnowledgeChunk chunk) : IKnowledgeRetriever
    {
        public Task<IReadOnlyCollection<KnowledgeChunk>> SearchAsync(
            KnowledgeSearchQuery query, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<KnowledgeChunk>>([chunk]);
    }

    private sealed class StubProfileCatalog : IDocumentProfileCatalog
    {
        private static readonly DocumentProfile Profile =
            new("test", "Test profile", ["question-answering"], ["en"], false);

        public IReadOnlyCollection<DocumentProfile> GetAll() => [Profile];

        public DocumentProfile GetRequired(string name) => Profile;
    }

    private sealed class EmptyRetriever : IKnowledgeRetriever
    {
        public Task<IReadOnlyCollection<KnowledgeChunk>> SearchAsync(
            KnowledgeSearchQuery query,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<KnowledgeChunk>>([]);
    }

    private sealed class StubChatModel : IChatModel
    {
        public bool WasCalled { get; private set; }

        public Task<GroundedGenerationResult> GenerateAsync(
            GroundedGenerationRequest request,
            CancellationToken cancellationToken)
        {
            WasCalled = true;
            return Task.FromResult(new GroundedGenerationResult("answer", "stub"));
        }
    }
}

