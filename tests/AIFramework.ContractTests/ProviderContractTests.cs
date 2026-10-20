using AIFramework.Application;
using AIFramework.Application.Services;
using AIFramework.Contracts;
using AIFramework.Core.Abstractions;
using AIFramework.Local;
using Microsoft.Extensions.DependencyInjection;
namespace AIFramework.ContractTests;
public sealed class ProviderContractTests
{
    [Fact]
    public async Task Local_pipeline_includes_shared_evidence_but_excludes_other_resource_scopes()
    {
        using var provider = new ServiceCollection().AddAiApplication().AddLocalAi().BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        using var scope = provider.CreateScope();
        var ingestion = scope.ServiceProvider.GetRequiredService<DocumentIngestionService>();
        var answers = scope.ServiceProvider.GetRequiredService<KnowledgeAnswerService>();
        Guid tenant = Guid.NewGuid();
        Guid permittedScope = Guid.NewGuid();
        Guid otherScope = Guid.NewGuid();

        async Task<KnowledgeDocumentResponse> Ingest(string title, Guid? resourceScope) =>
            await ingestion.IngestAsync(new()
            {
                TenantId = tenant, ScopeId = resourceScope, Title = title,
                Content = $"Inspection procedure for {title}: review equipment every Monday.",
                Profile = "general-document"
            });

        var shared = await Ingest("Shared manual", null);
        var permitted = await Ingest("Permitted manual", permittedScope);
        var restricted = await Ingest("Restricted manual", otherScope);
        AskKnowledgeRequest request = new()
        {
            TenantId = tenant, ScopeId = permittedScope,
            Question = "Inspection procedure", Profile = "general-document"
        };

        var scoped = await answers.AnswerAsync(request);
        Assert.True(scoped.Grounded);
        Assert.Equal(2, scoped.Citations.Count);
        Assert.Contains(scoped.Citations, c => c.DocumentId == shared.DocumentId);
        Assert.Contains(scoped.Citations, c => c.DocumentId == permitted.DocumentId);
        Assert.DoesNotContain(scoped.Citations, c => c.DocumentId == restricted.DocumentId);
        Assert.DoesNotContain("Restricted manual", scoped.Answer);

        var unscoped = await answers.AnswerAsync(request with { ScopeId = null });
        Assert.Equal(shared.DocumentId, Assert.Single(unscoped.Citations).DocumentId);

        var allowlisted = await answers.AnswerAsync(request with { DocumentIds = [permitted.DocumentId] });
        Assert.Equal(permitted.DocumentId, Assert.Single(allowlisted.Citations).DocumentId);

        var denied = await answers.AnswerAsync(request with { DocumentIds = [restricted.DocumentId] });
        Assert.False(denied.Grounded);
        Assert.Empty(denied.Citations);
    }

    [Fact]
    public async Task Independent_container_can_ingest_and_answer_without_database_or_framework()
    {
        using var provider = new ServiceCollection().AddAiApplication().AddLocalAi().BuildServiceProvider();
        using var scope = provider.CreateScope();
        var tenant = Guid.NewGuid();
        var document = await scope.ServiceProvider.GetRequiredService<DocumentIngestionService>().IngestAsync(new()
        { TenantId = tenant, Title = "Manual", Content = "Energy meters are inspected every Monday morning.", Profile = "general-document" });
        var answers = scope.ServiceProvider.GetRequiredService<KnowledgeAnswerService>();
        var result = await answers.AnswerAsync(new() { TenantId = tenant, Question = "When are energy meters inspected?", Profile = "general-document", DocumentIds = [document.DocumentId] });
        Assert.True(result.Grounded); Assert.Single(result.Citations);
        Assert.False((await answers.AnswerAsync(new() { TenantId = Guid.NewGuid(), Question = "When are energy meters inspected?", Profile = "general-document" })).Grounded);
        Assert.False((await answers.AnswerAsync(new() { TenantId = tenant, Question = "When are energy meters inspected?", Profile = "general-document", DocumentIds = [] })).Grounded);
    }
}
