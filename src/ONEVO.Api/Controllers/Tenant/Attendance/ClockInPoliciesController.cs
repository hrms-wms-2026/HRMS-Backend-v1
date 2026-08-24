using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.TimeAttendance.Queries.GetClockInPolicyById;
using ONEVO.Application.Features.TimeAttendance.Queries.ListClockInPolicies;

namespace ONEVO.Api.Controllers.Tenant.Attendance;

/// <summary>
/// Tenant-level Clock-in Policy reads matching the suggested
/// GET /api/v1/attendance/clock-in-policies shape. List requires legalEntityId
/// because Clock-in Policy is always company-context scoped.
/// </summary>
[ApiController]
[Route("api/v1/attendance/clock-in-policies")]
[Authorize(Policy = "TenantPolicy")]
public class ClockInPoliciesController : ControllerBase
{
    private readonly IMediator _mediator;

    public ClockInPoliciesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    [RequirePermission("attendance:read")]
    public async Task<IActionResult> List(
        [FromQuery] Guid legalEntityId,
        [FromQuery] bool includeInactive = false,
        CancellationToken ct = default)
    {
        if (legalEntityId == Guid.Empty)
            return Problem("legalEntityId query parameter is required.", statusCode: 400);

        var result = await _mediator.Send(
            new ListClockInPoliciesQuery(legalEntityId, includeInactive), ct);

        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpGet("{id:guid}")]
    [RequirePermission("attendance:read")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetClockInPolicyByIdQuery(id), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
