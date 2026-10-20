using AIFramework.Application;
using AIFramework.Application.Services;
using AIFramework.Contracts;
using AIFramework.Local;
using Microsoft.Extensions.DependencyInjection;

// These IDs simulate scope already authorized by a consuming application.
// They are sample values, not users, memberships or production seed data.
Guid tenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
Guid scopeId = Guid.Parse("22222222-2222-2222-2222-222222222222");
Guid restrictedScopeId = Guid.Parse("33333333-3333-3333-3333-333333333333");
const string profile = "general-document";

using ServiceProvider provider = new ServiceCollection()
    .AddAiApplication()
    .AddLocalAi()
    .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
using IServiceScope scope = provider.CreateScope();
var ingestion = scope.ServiceProvider.GetRequiredService<DocumentIngestionService>();
var answers = scope.ServiceProvider.GetRequiredService<KnowledgeAnswerService>();

KnowledgeDocumentResponse document = await IngestAsync("Operations manual", "operations-manual.txt", scopeId);
KnowledgeDocumentResponse restricted = await IngestAsync("Restricted manual", "restricted-manual.txt", restrictedScopeId);
Console.WriteLine("Local AI demo: synthetic text, in-memory retrieval, extractive answers (no LLM).");
Console.WriteLine($"Ingested two documents; authorized document: {document.DocumentId}");

AskKnowledgeRequest question = new()
{
    Question = "How often are backup credentials rotated?",
    Profile = profile,
    TenantId = tenantId,
    ScopeId = scopeId,
    DocumentIds = [document.DocumentId]
};
GroundedAnswerResponse answer = await answers.AnswerAsync(question);
if (!answer.Grounded || answer.Provider != "local-extractive" || answer.Citations.Count != 1 ||
    answer.Citations.Single().DocumentId != document.DocumentId ||
    !answer.Answer.Contains("thirty days", StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("Expected evidence and citation from the authorized manual.");

Console.WriteLine(answer.Answer);
foreach (CitationResponse citation in answer.Citations)
    Console.WriteLine($"Source: {citation.Title}, chunk {citation.ChunkNumber}, {citation.SourceUri}");

await ExpectNoEvidenceAsync("Different tenant", question with { TenantId = Guid.NewGuid() });
await ExpectNoEvidenceAsync("Different scope", question with { ScopeId = restrictedScopeId });
await ExpectNoEvidenceAsync("Restricted document", question with { DocumentIds = [restricted.DocumentId] });
await ExpectNoEvidenceAsync("Empty permitted document list", question with { DocumentIds = [] });
await ExpectNoEvidenceAsync("No matching evidence", question with { Question = "quasar nebula pulsar" });
Console.WriteLine("All checks passed. Indexed data disappears when this process exits.");

async Task<KnowledgeDocumentResponse> IngestAsync(string title, string fileName, Guid documentScope)
{
    string content = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Documents", fileName));
    return await ingestion.IngestAsync(new CreateKnowledgeDocumentRequest
    {
        Title = title,
        Content = content,
        Profile = profile,
        TenantId = tenantId,
        ScopeId = documentScope,
        Language = "en",
        SourceUri = $"https://example.test/{fileName}"
    });
}

async Task ExpectNoEvidenceAsync(string scenario, AskKnowledgeRequest request)
{
    GroundedAnswerResponse result = await answers.AnswerAsync(request);
    if (result.Grounded || result.Citations.Count != 0 || result.Provider != "none")
        throw new InvalidOperationException($"Unexpected evidence: {scenario}.");
    Console.WriteLine($"PASS: {scenario}");
}
