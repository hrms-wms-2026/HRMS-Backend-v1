using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.WorkManagement.Leadership.Queries.GetLedWorkProgress;

namespace ONEVO.Api.Controllers.Tenant.WorkManagement;

/// <summary>My Team - Work I Lead (spec §8.3). Relationship-based like the rest of Work
/// Management: the module gate only, no RequirePermission; the handler computes ownership.</summary>
[ApiController]
[Route("api/v1/work")]
[Authorize(Policy = "TenantPolicy")]
[RequireAnyModule("worksync_foundation", "projects", "objectives_milestones", "tasks", "boards", "planning_sprints")]
public sealed class WorkLeadershipController(IMediator mediator) : ControllerBase
{
    [HttpGet("led-progress")]
    public async Task<IActionResult> LedProgress([FromQuery] int overdueLimit = 10, CancellationToken ct = default)
    {
        var result = await mediator.Send(new GetLedWorkProgressQuery(overdueLimit), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
