using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.Attendance.WorkModes;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.TimeAttendance.Commands.CreateWorkMode;
using ONEVO.Application.Features.TimeAttendance.Commands.DeactivateWorkMode;
using ONEVO.Application.Features.TimeAttendance.Commands.UpdateWorkMode;
using ONEVO.Application.Features.TimeAttendance.Queries.ListWorkModes;

namespace ONEVO.Api.Controllers.Tenant.Attendance;

[ApiController]
[Route("api/v1/attendance/legal-entities/{legalEntityId:guid}/work-modes")]
[Authorize(Policy = "TenantPolicy")]
public class LegalEntityWorkModesController : ControllerBase
{
    private readonly IMediator _mediator;

    public LegalEntityWorkModesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    [RequirePermission("attendance:read")]
    public async Task<IActionResult> List(
        Guid legalEntityId, [FromQuery] bool includeInactive = false, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new ListWorkModesQuery(legalEntityId, includeInactive), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost]
    [RequirePermission("attendance:write")]
    public async Task<IActionResult> Create(
        Guid legalEntityId, [FromBody] UpsertWorkModeRequest request, CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new CreateWorkModeCommand(
                legalEntityId, request.Name, request.BiometricEnabled, request.WebEnabled,
                request.TrayEnabled, request.PhotoRequired,
                request.SelfRegistersLocation, request.AllowsDailyLocationChoice),
            ct);
        return result.IsSuccess
            ? CreatedAtAction(nameof(List), new { legalEntityId }, result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPut("{id:guid}")]
    [RequirePermission("attendance:write")]
    public async Task<IActionResult> Update(
        Guid legalEntityId, Guid id, [FromBody] UpsertWorkModeRequest request, CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new UpdateWorkModeCommand(
                id, request.Name, request.BiometricEnabled, request.WebEnabled,
                request.TrayEnabled, request.PhotoRequired,
                request.SelfRegistersLocation, request.AllowsDailyLocationChoice),
            ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("{id:guid}/deactivate")]
    [RequirePermission("attendance:write")]
    public async Task<IActionResult> Deactivate(Guid legalEntityId, Guid id, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new DeactivateWorkModeCommand(id), ct);
        return result.IsSuccess ? NoContent() : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
