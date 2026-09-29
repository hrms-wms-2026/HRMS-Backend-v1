using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.CoreHr.OnboardingWorkModes.Queries.ListOnboardingWorkModes;

namespace ONEVO.Api.Controllers.Tenant.CoreHr;

[ApiController]
[Route("api/v1/work-modes")]
[Authorize(Policy = "TenantPolicy")]
public sealed class WorkModesController : ControllerBase
{
    private readonly IMediator _mediator;

    public WorkModesController(IMediator mediator) => _mediator = mediator;

    /// <summary>Active work modes for a legal entity, for the Add Employee wizard's work-mode picker.</summary>
    [HttpGet]
    [RequirePermission("employees:write")]
    public async Task<IActionResult> List([FromQuery] Guid legalEntityId, CancellationToken ct)
    {
        var result = await _mediator.Send(new ListOnboardingWorkModesQuery(legalEntityId), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
