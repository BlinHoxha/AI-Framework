using System.Net;
using System.Net.Http.Json;
using AIFramework.Contracts;
using AIFramework.SampleApi.Contracts;
using AIFramework.IntegrationTests.Infrastructure;

namespace AIFramework.IntegrationTests.Api;

public sealed class AiBlueprintEndpointsTests : IClassFixture<FrameworkApiFactory>
{
    private readonly HttpClient _client;

    public AiBlueprintEndpointsTests(FrameworkApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task IngestThenAsk_ReturnsGroundedAnswerWithCitation()
    {
        Guid tenantId = Guid.NewGuid();
        CreateKnowledgeDocumentRequest document = new()
        {
            Title = "Operations Manual",
            Content = "Backup credentials must be rotated every thirty days. Administrators review the access log every evening.",
            Profile = "general-document",
            TenantId = tenantId,
            Language = "en",
            SourceUri = "https://example.test/operations-manual"
        };

        HttpResponseMessage createResponse = await _client.PostAsJsonAsync("/api/v1/knowledge/documents", document);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        CommonResult<KnowledgeDocumentResponse>? created =
            await createResponse.Content.ReadFromJsonAsync<CommonResult<KnowledgeDocumentResponse>>();
        Assert.NotNull(created?.Data);

        AskKnowledgeRequest question = new()
        {
            Question = "How often are backup credentials rotated?",
            Profile = "general-document",
            TenantId = tenantId
        };

        HttpResponseMessage answerResponse = await _client.PostAsJsonAsync("/api/v1/ai/answers", question);
        Assert.Equal(HttpStatusCode.OK, answerResponse.StatusCode);

        CommonResult<GroundedAnswerResponse>? result =
            await answerResponse.Content.ReadFromJsonAsync<CommonResult<GroundedAnswerResponse>>();

        Assert.True(result?.Data?.Grounded);
        Assert.Equal("local-extractive", result?.Data?.Provider);
        CitationResponse citation = Assert.Single(result!.Data!.Citations);
        Assert.Equal(created.Data.DocumentId, citation.DocumentId);
    }

    [Fact]
    public async Task Ask_WithDifferentTenant_DoesNotLeakDocument()
    {
        Guid ownerTenantId = Guid.NewGuid();
        await _client.PostAsJsonAsync("/api/v1/knowledge/documents", new CreateKnowledgeDocumentRequest
        {
            Title = "Private procedure",
            Content = "The confidential closing procedure uses the north entrance after midnight.",
            Profile = "general-document",
            TenantId = ownerTenantId,
            Language = "en"
        });

        HttpResponseMessage response = await _client.PostAsJsonAsync("/api/v1/ai/answers", new AskKnowledgeRequest
        {
            Question = "Which entrance is used after midnight?",
            Profile = "general-document",
            TenantId = Guid.NewGuid()
        });

        CommonResult<GroundedAnswerResponse>? result =
            await response.Content.ReadFromJsonAsync<CommonResult<GroundedAnswerResponse>>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(result?.Data?.Grounded);
        Assert.Empty(result!.Data!.Citations);
    }

    [Fact]
    public async Task GetDocumentProfiles_ReturnsReusableProfiles()
    {
        CommonResult<IReadOnlyCollection<DocumentProfileResponse>>? result =
            await _client.GetFromJsonAsync<CommonResult<IReadOnlyCollection<DocumentProfileResponse>>>(
                "/api/v1/ai/document-profiles");

        DocumentProfileResponse profile = Assert.Single(result!.Data!);
        Assert.Equal("general-document", profile.Name);
    }
}

