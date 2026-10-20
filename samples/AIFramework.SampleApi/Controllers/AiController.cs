using AIFramework.Core.Abstractions;
using AIFramework.Core.Models;
using AIFramework.Application.Services;
using AIFramework.Contracts;
using AIFramework.SampleApi.Contracts;
using AIFramework.SampleApi.Security;
using Microsoft.AspNetCore.Mvc;

namespace AIFramework.SampleApi.Controllers;

[ApiController]
[Route("api/v1/ai")]
public sealed class AiController(
    KnowledgeAnswerService knowledgeAnswerService,
    IDocumentProfileCatalog profileCatalog,
    IHostEnvironment environment, IConfiguration configuration) : ControllerBase
{
    [HttpPost("answers")]
    public async Task<ActionResult<CommonResult<GroundedAnswerResponse>>> Answer(
        [FromBody] AskKnowledgeRequest request,
        CancellationToken cancellationToken)
    {
        if (!TenantAccess.IsAllowed(User, request.TenantId, request.ScopeId, environment.IsDevelopment() && configuration.GetValue<bool>("AI:AllowAnonymousDevelopment")))
        {
            return Forbid();
        }

        GroundedAnswerResponse response = await knowledgeAnswerService.AnswerAsync(request, cancellationToken);
        return Ok(CommonResult<GroundedAnswerResponse>.Success(response));
    }

    [HttpGet("document-profiles")]
    public ActionResult<CommonResult<IReadOnlyCollection<DocumentProfileResponse>>> GetDocumentProfiles()
    {
        DocumentProfileResponse[] profiles = profileCatalog.GetAll()
            .Select(MapProfile)
            .OrderBy(profile => profile.Name)
            .ToArray();

        return Ok(CommonResult<IReadOnlyCollection<DocumentProfileResponse>>.Success(profiles));
    }

    private static DocumentProfileResponse MapProfile(DocumentProfile profile) =>
        new(
            profile.Name,
            profile.Description,
            profile.AllowedOperations,
            profile.SupportedLanguages,
            profile.RequiresHumanReview);
}

