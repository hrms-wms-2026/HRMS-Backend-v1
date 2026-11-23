using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Filters;
using ONEVO.Application.Common.Authorization;
using ONEVO.Application.Features.Leave.Balance.Queries.GetMyBalances;
using ONEVO.Application.Features.Leave.Balance.Queries.GetMyLeaveWorkWindow;
using ONEVO.Application.Features.Leave.Balance.Queries.ListAllBalances;
using ONEVO.Application.Features.Leave.Balance.Queries.ListTeamBalances;

namespace ONEVO.Api.Controllers.Tenant.Leave;

[ApiController]
[Route("api/v1/leave/balances")]
[Authorize(Policy = "TenantPolicy")]
public class LeaveBalancesController : ControllerBase
{
    private readonly IMediator _mediator;

    public LeaveBalancesController(IMediator mediator) => _mediator = mediator;

    [HttpGet("my")]
    [RequirePermission(PermissionCatalog.Leave.ReadOwn)]
    public async Task<IActionResult> My([FromQuery] int year, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetMyBalancesQuery(year), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpGet("work-window")]
    [RequireAnyPermission(PermissionCatalog.Leave.ReadOwn, PermissionCatalog.Leave.Manage)]
    public async Task<IActionResult> WorkWindow(CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetMyLeaveWorkWindowQuery(), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpGet("team")]
    [RequirePermission(PermissionCatalog.Leave.ReadTeam)]
    public async Task<IActionResult> Team(
        [FromQuery] int year,
        [FromQuery] Guid? departmentId,
        [FromQuery] Guid? leaveTypeId,
        [FromQuery] string? search,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(new ListTeamBalancesQuery(year, departmentId, leaveTypeId, search), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpGet("all")]
    [RequirePermission(PermissionCatalog.Leave.Read)]
    public async Task<IActionResult> All(
        [FromQuery] int year,
        [FromQuery] Guid? legalEntityId,
        [FromQuery] Guid? departmentId,
        [FromQuery] Guid? leaveTypeId,
        [FromQuery] int? employmentStatusId,
        [FromQuery] string? search,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new ListAllBalancesQuery(year, legalEntityId, departmentId, leaveTypeId, employmentStatusId, search), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
