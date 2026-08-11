using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.CoreHr.WorkModes.Queries.ListActiveWorkModes;

namespace ONEVO.Api.Controllers.Tenant.CoreHr;

[ApiController]
[Route("api/v1/work-modes")]
[Authorize(Policy = "TenantPolicy")]
public sealed class WorkModesController : ControllerBase
{
    private readonly IMediator _mediator;

    public WorkModesController(IMediator mediator) => _mediator = mediator;

    /// <summary>Active work modes (global lookup, not tenant-scoped) for onboarding/employee
    /// work-mode selection - e.g. the Add Employee wizard.</summary>
    [HttpGet]
    [RequirePermission("employees:write")]
    public async Task<IActionResult> List(CancellationToken ct = default)
    {
        var result = await _mediator.Send(new ListActiveWorkModesQuery(), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
