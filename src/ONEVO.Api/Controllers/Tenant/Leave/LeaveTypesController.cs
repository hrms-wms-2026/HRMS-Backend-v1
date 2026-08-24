using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.Leave.Types;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.Leave.Type.Commands.CreateLeaveType;
using ONEVO.Application.Features.Leave.Type.Commands.DeactivateLeaveType;
using ONEVO.Application.Features.Leave.Type.Commands.UpdateLeaveType;
using ONEVO.Application.Features.Leave.Type.Queries.GetLeaveType;
using ONEVO.Application.Features.Leave.Type.Queries.ListLeaveTypes;

namespace ONEVO.Api.Controllers.Tenant.Leave;

[ApiController]
[Route("api/v1/leave/types")]
[Authorize(Policy = "TenantPolicy")]
public class LeaveTypesController : ControllerBase
{
    private readonly IMediator _mediator;

    public LeaveTypesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>List leave types for this tenant.</summary>
    [HttpGet]
    [RequirePermission("leave:read")]
    public async Task<IActionResult> List([FromQuery] bool includeInactive = false, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new ListLeaveTypesQuery(includeInactive), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Get a single leave type.</summary>
    [HttpGet("{leaveTypeId:guid}")]
    [RequirePermission("leave:read")]
    public async Task<IActionResult> Get(Guid leaveTypeId, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetLeaveTypeQuery(leaveTypeId), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Create a leave type. Requires leave:manage.</summary>
    [HttpPost]
    [RequirePermission("leave:manage")]
    public async Task<IActionResult> Create([FromBody] CreateLeaveTypeRequest request, CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new CreateLeaveTypeCommand(
                request.Name, request.Code, request.Description, request.Category,
                request.IsPaid, request.RequiresApproval, request.RequiresDocument,
                request.DocumentRequiredAfterDays, request.AcceptedDocumentTypes,
                request.MaxConsecutiveDays, request.DefaultDaysPerYear,
                request.CarryForwardAllowed, request.MaxCarryForwardDays, request.CarryForwardExpiryMonths,
                request.ProRataForNewJoiners, request.ApplicableGender, request.MinimumNoticeDays),
            ct);

        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Update a leave type. Code is immutable and not accepted here.</summary>
    [HttpPut("{leaveTypeId:guid}")]
    [RequirePermission("leave:manage")]
    public async Task<IActionResult> Update(Guid leaveTypeId, [FromBody] UpdateLeaveTypeRequest request, CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new UpdateLeaveTypeCommand(
                leaveTypeId, request.Name, request.Description, request.Category,
                request.IsPaid, request.RequiresApproval, request.RequiresDocument,
                request.DocumentRequiredAfterDays, request.AcceptedDocumentTypes,
                request.MaxConsecutiveDays, request.DefaultDaysPerYear,
                request.CarryForwardAllowed, request.MaxCarryForwardDays, request.CarryForwardExpiryMonths,
                request.ProRataForNewJoiners, request.ApplicableGender, request.MinimumNoticeDays),
            ct);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Deactivate a leave type. Returns 409 with the pending-request count if any exist
    /// and confirmed=false; resubmit with confirmed=true to proceed.</summary>
    [HttpPost("{leaveTypeId:guid}/deactivate")]
    [RequirePermission("leave:manage")]
    public async Task<IActionResult> Deactivate(Guid leaveTypeId, [FromQuery] bool confirmed = false, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new DeactivateLeaveTypeCommand(leaveTypeId, confirmed), ct);
        return result.IsSuccess ? NoContent() : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
