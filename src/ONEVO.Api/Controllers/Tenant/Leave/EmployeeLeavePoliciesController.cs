using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.Leave.Policies;
using ONEVO.Api.Filters;
using ONEVO.Application.Common.Authorization;
using ONEVO.Application.Features.Leave.Policy.Commands.EmployeeLeavePolicy;
using ONEVO.Application.Features.Leave.Policy.Queries.GetEmployeeLeavePolicies;

namespace ONEVO.Api.Controllers.Tenant.Leave;

/// <summary>Which leave policy applies to one employee per leave type, and the employee-level
/// override that beats position, department and company.</summary>
[ApiController]
[Route("api/v1/employees/{employeeId:guid}/leave-policies")]
[Authorize(Policy = "TenantPolicy")]
public class EmployeeLeavePoliciesController : ControllerBase
{
    private readonly IMediator _mediator;

    public EmployeeLeavePoliciesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    [RequireAnyPermission(PermissionCatalog.Leave.Read, PermissionCatalog.Leave.ReadOwn, PermissionCatalog.Leave.ReadTeam, PermissionCatalog.Leave.Manage, PermissionCatalog.Leave.Create)]
    [RequireVisibleEmployee("employeeId")]
    public async Task<IActionResult> List(Guid employeeId, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetEmployeeLeavePoliciesQuery(employeeId), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPut("{leaveTypeId:guid}")]
    [RequireAnyPermission(PermissionCatalog.Leave.Manage, PermissionCatalog.Leave.Create)]
    [RequireVisibleEmployee("employeeId")]
    public async Task<IActionResult> Set(
        Guid employeeId, Guid leaveTypeId, [FromBody] SetEmployeeLeavePolicyRequest request, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new SetEmployeeLeavePolicyCommand(employeeId, leaveTypeId, request.PolicyId), ct);
        return result.IsSuccess ? NoContent() : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpDelete("{leaveTypeId:guid}")]
    [RequireAnyPermission(PermissionCatalog.Leave.Manage, PermissionCatalog.Leave.Create)]
    [RequireVisibleEmployee("employeeId")]
    public async Task<IActionResult> Reset(Guid employeeId, Guid leaveTypeId, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new ResetEmployeeLeavePolicyCommand(employeeId, leaveTypeId), ct);
        return result.IsSuccess ? NoContent() : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
