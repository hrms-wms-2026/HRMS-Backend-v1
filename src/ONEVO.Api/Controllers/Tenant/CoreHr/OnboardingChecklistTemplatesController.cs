using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.CoreHr.Onboarding.Queries.GetOnboardingChecklistTemplateMatches;

namespace ONEVO.Api.Controllers.Tenant.CoreHr;

[ApiController]
[Route("api/v1/onboarding/checklist-templates")]
[Authorize(Policy = "TenantPolicy")]
public class OnboardingChecklistTemplatesController : ControllerBase
{
    private readonly IMediator _mediator;

    public OnboardingChecklistTemplatesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>Active onboarding-only checklist template matches for the Add Employee wizard,
    /// ordered position match, then department match, then company/default.</summary>
    [HttpGet("matches")]
    [RequirePermission("employees:read")]
    public async Task<IActionResult> Matches(
        [FromQuery] Guid legalEntityId, [FromQuery] Guid? departmentId, [FromQuery] Guid? positionId, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetOnboardingChecklistTemplateMatchesQuery(legalEntityId, departmentId, positionId), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
