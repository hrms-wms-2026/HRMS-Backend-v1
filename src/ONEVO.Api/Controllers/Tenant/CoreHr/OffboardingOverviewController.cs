using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.CoreHr.Offboarding.Queries.ListOffboardingOverview;

namespace ONEVO.Api.Controllers.Tenant.CoreHr;

[ApiController]
[Route("api/v1/employees/offboarding-overview")]
[Authorize(Policy = "TenantPolicy")]
public class OffboardingOverviewController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    [RequirePermission("employees:read")]
    public async Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default)
    {
        var result = await mediator.Send(new ListOffboardingOverviewQuery(page, pageSize), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
