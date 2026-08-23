using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.Leave.BalanceAudit.Queries.ListBalanceAudit;

namespace ONEVO.Api.Controllers.Tenant.Leave;

[ApiController]
[Route("api/v1/leave/balance-audit")]
[Authorize(Policy = "TenantPolicy")]
public class LeaveBalanceAuditController : ControllerBase
{
    private readonly IMediator _mediator;

    public LeaveBalanceAuditController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>Append-only balance audit trail. Filterable by employee, leave type, change
    /// type, and date range.</summary>
    [HttpGet]
    [RequirePermission("leave:read")]
    public async Task<IActionResult> List(
        [FromQuery] Guid? employeeId,
        [FromQuery] Guid? leaveTypeId,
        [FromQuery] string? changeType,
        [FromQuery] DateOnly? fromDate,
        [FromQuery] DateOnly? toDate,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new ListBalanceAuditQuery(employeeId, leaveTypeId, changeType, fromDate, toDate, page, pageSize), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
