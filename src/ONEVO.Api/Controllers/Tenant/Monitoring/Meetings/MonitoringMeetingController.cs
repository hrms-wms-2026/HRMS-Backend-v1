using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.Monitoring.Meetings.Queries.GetMeetingSignals;

namespace ONEVO.Api.Controllers.Tenant.Monitoring.Meetings;

[ApiController]
[Route("api/v1/monitoring/meetings")]
[Authorize(Policy = "TenantPolicy")]
public class MonitoringMeetingController : ControllerBase
{
    private readonly IMediator _mediator;

    public MonitoringMeetingController(IMediator mediator) => _mediator = mediator;

    [HttpGet("signals")]
    [RequirePermission("monitoring:read")]
    public async Task<IActionResult> GetSignals(
        [FromQuery] Guid employeeId,
        [FromQuery] DateOnly date,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new GetMeetingSignalsQuery { EmployeeId = employeeId, Date = date, Page = page, PageSize = pageSize }, ct);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
