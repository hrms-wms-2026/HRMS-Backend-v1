using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.Leave.Calendar.Queries;

namespace ONEVO.Api.Controllers.Tenant.Leave;

[ApiController]
[Route("api/v1/leave/calendar")]
[Authorize(Policy = "TenantPolicy")]
public sealed class LeaveCalendarController : ControllerBase
{
    private readonly IMediator _mediator;

    public LeaveCalendarController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    [RequirePermission("calendar:read")]
    public async Task<IActionResult> Get(
        [FromQuery] int year,
        [FromQuery] int month,
        [FromQuery] Guid? departmentId,
        [FromQuery] bool? includeTentative,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new GetLeaveCalendarQuery(year, month, departmentId, includeTentative),
            ct);

        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
