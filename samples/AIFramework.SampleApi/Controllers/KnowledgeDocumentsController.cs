using AIFramework.Application.Services;
using AIFramework.Contracts;
using AIFramework.SampleApi.Contracts;
using AIFramework.SampleApi.Security;
using Microsoft.AspNetCore.Mvc;

namespace AIFramework.SampleApi.Controllers;

[ApiController]
[Route("api/v1/knowledge/documents")]
public sealed class KnowledgeDocumentsController(DocumentIngestionService documentIngestionService, IHostEnvironment environment, IConfiguration configuration) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<CommonResult<KnowledgeDocumentResponse>>> Create(
        [FromBody] CreateKnowledgeDocumentRequest request,
        CancellationToken cancellationToken)
    {
        if (!TenantAccess.IsAllowed(User, request.TenantId, request.ScopeId, environment.IsDevelopment() && configuration.GetValue<bool>("AI:AllowAnonymousDevelopment")))
        {
            return Forbid();
        }

        KnowledgeDocumentResponse response = await documentIngestionService.IngestAsync(request, cancellationToken);
        return StatusCode(
            StatusCodes.Status201Created,
            CommonResult<KnowledgeDocumentResponse>.Success(response));
    }
}

