using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Application.Features.Dashboard.Team.Queries;

namespace ONEVO.Api.Controllers.Tenant.Dashboard;

[ApiController]
[Route("api/v1/dashboard/team")]
[Authorize(Policy = "TenantPolicy")]
public sealed class TeamDashboardController(IMediator mediator) : ControllerBase
{
    [HttpGet("capabilities")]
    public async Task<IActionResult> Capabilities(CancellationToken ct = default)
    {
        var result = await mediator.Send(new GetMyTeamCapabilitiesQuery(), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpGet("action-items")]
    public async Task<IActionResult> ActionItems(
        [FromQuery] int topPerSource = 5,
        CancellationToken ct = default)
    {
        var result = await mediator.Send(new GetTeamActionItemsQuery(topPerSource), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
