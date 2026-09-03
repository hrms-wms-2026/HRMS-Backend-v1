using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.WorkManagement.CalendarEvents;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.WorkManagement.CalendarEvents.Commands.CloseCalendarEvent;
using ONEVO.Application.Features.WorkManagement.CalendarEvents.Commands.CreateCalendarEvent;
using ONEVO.Application.Features.WorkManagement.CalendarEvents.Commands.UpdateCalendarEvent;
using ONEVO.Application.Features.WorkManagement.CalendarEvents.Queries.GetProjectCalendar;

namespace ONEVO.Api.Controllers.Tenant.WorkManagement;

[ApiController]
[Route("api/v1/work")]
[Authorize(Policy = "TenantPolicy")]
public sealed class CalendarController : ControllerBase
{
    private readonly IMediator _mediator;

    public CalendarController(IMediator mediator) => _mediator = mediator;

    [HttpGet("projects/{projectId:guid}/calendar")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> GetProjectCalendar(Guid projectId, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetProjectCalendarQuery(projectId), ct);
        return result.IsSuccess
            ? Ok(result.Value!.ToViewModel())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("projects/{projectId:guid}/calendar-events")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> CreateEvent(
        Guid projectId, [FromBody] CreateCalendarEventRequest request, CancellationToken ct)
    {
        var command = new CreateCalendarEventCommand(
            projectId, request.Name, request.Color, request.StartDate, request.EndDate,
            request.ObjectiveIds, request.TaskIds);
        var result = await _mediator.Send(command, ct);
        return result.IsSuccess
            ? StatusCode(201, result.Value!.ToViewModel())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPatch("calendar-events/{id:guid}")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> UpdateEvent(
        Guid id, [FromBody] UpdateCalendarEventRequest request, CancellationToken ct)
    {
        var command = new UpdateCalendarEventCommand(
            id, request.Name, request.Color, request.StartDate, request.EndDate,
            request.ObjectiveIds, request.TaskIds);
        var result = await _mediator.Send(command, ct);
        return result.IsSuccess
            ? Ok(result.Value!.ToViewModel())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("calendar-events/{id:guid}/close")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> CloseEvent(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new CloseCalendarEventCommand(id), ct);
        return result.IsSuccess
            ? NoContent()
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
