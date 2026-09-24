using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.CoreHr.Offboarding;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.CoreHr.Offboarding.Commands.ApproveBypassRequest;
using ONEVO.Application.Features.CoreHr.Offboarding.Commands.RejectBypassRequest;
using ONEVO.Application.Features.CoreHr.Offboarding.Queries.ListMyPendingBypassRequests;

namespace ONEVO.Api.Controllers.Tenant.CoreHr;

[ApiController]
[Route("api/v1/offboarding-bypass-requests")]
[Authorize(Policy = "TenantPolicy")]
public class OffboardingBypassRequestsController(IMediator mediator) : ControllerBase
{
    /// <summary>Always scoped to the caller as approver - there is no arbitrary approverId
    /// override, per design spec §6.</summary>
    [HttpGet]
    [RequirePermission("employees:read")]
    public async Task<IActionResult> ListMine(CancellationToken ct = default)
    {
        var result = await mediator.Send(new ListMyPendingBypassRequestsQuery(), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("{id:guid}/approve")]
    [RequirePermission("employees:write")]
    [Idempotent]
    public async Task<IActionResult> Approve(Guid id, CancellationToken ct = default)
    {
        var result = await mediator.Send(new ApproveBypassRequestCommand(id), ct);
        return result.IsSuccess ? NoContent() : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("{id:guid}/reject")]
    [RequirePermission("employees:write")]
    [Idempotent]
    public async Task<IActionResult> Reject(Guid id, [FromBody] RejectBypassRequestRequest request, CancellationToken ct = default)
    {
        var result = await mediator.Send(new RejectBypassRequestCommand(id, request.DecisionComment), ct);
        return result.IsSuccess ? NoContent() : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
