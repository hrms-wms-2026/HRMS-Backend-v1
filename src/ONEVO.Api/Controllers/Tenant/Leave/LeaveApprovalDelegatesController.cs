using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.Leave.Approvals;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.Leave.Approval.Commands;

namespace ONEVO.Api.Controllers.Tenant.Leave;

[ApiController]
[Route("api/v1/leave/approval-delegates")]
[Authorize(Policy = "TenantPolicy")]
public sealed class LeaveApprovalDelegatesController : ControllerBase
{
    private readonly IMediator _mediator;

    public LeaveApprovalDelegatesController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    [RequirePermission("leave:approve")]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var result = await _mediator.Send(new ListMyLeaveApprovalDelegatesQuery(), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost]
    [RequirePermission("leave:approve")]
    public async Task<IActionResult> Create([FromBody] CreateLeaveApprovalDelegateRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(
            new CreateLeaveApprovalDelegateCommand(request.DelegateEmployeeId, request.StartDate, request.EndDate), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpDelete("{id:guid}")]
    [RequirePermission("leave:approve")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new DeleteLeaveApprovalDelegateCommand(id), ct);
        return result.IsSuccess ? NoContent() : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
