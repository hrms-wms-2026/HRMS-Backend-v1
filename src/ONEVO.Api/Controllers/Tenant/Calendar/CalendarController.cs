using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.Calendar;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.Calendar.Commands.CancelRecurringOccurrence;
using ONEVO.Application.Features.Calendar.Commands.CreateCalendarEvent;
using ONEVO.Application.Features.Calendar.Commands.DeleteCalendarEvent;
using ONEVO.Application.Features.Calendar.Commands.EditRecurringOccurrence;
using ONEVO.Application.Features.Calendar.Commands.RespondToCalendarEvent;
using ONEVO.Application.Features.Calendar.Commands.UpdateCalendarEvent;
using ONEVO.Application.Features.Calendar.Queries.CheckCalendarConflicts;
using ONEVO.Application.Features.Calendar.Queries.GetCalendarEvents;

namespace ONEVO.Api.Controllers.Tenant.Calendar;

[ApiController]
[Route("api/v1/calendar")]
[Authorize(Policy = "TenantPolicy")]
public class CalendarController : ControllerBase
{
    private readonly IMediator _mediator;

    public CalendarController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    [RequirePermission("calendar:read")]
    public async Task<IActionResult> GetEvents([FromQuery] DateTimeOffset from, [FromQuery] DateTimeOffset to, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetCalendarEventsQuery(from, to), ct);
        return result.IsSuccess
            ? Ok(result.Value!.ToViewModel())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost]
    [RequirePermission("calendar:write")]
    public async Task<IActionResult> Create([FromBody] CreateCalendarEventRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new CreateCalendarEventCommand(
            request.Title, request.Description, request.StartDate, request.EndDate, request.IsAllDay,
            request.Timezone, request.Location, request.MeetingLink, request.Color, request.Recurrence,
            request.ParticipantEmployeeIds, request.RecurrenceRule), ct);

        return result.IsSuccess
            ? StatusCode(201, result.Value!.ToViewModel())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPut("{id:guid}")]
    [RequirePermission("calendar:write")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateCalendarEventRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new UpdateCalendarEventCommand(
            id, request.Title, request.Description, request.StartDate, request.EndDate, request.IsAllDay,
            request.Timezone, request.Location, request.MeetingLink, request.Color, request.Recurrence), ct);

        return result.IsSuccess
            ? Ok(result.Value!.ToViewModel())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpDelete("{id:guid}")]
    [RequirePermission("calendar:write")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new DeleteCalendarEventCommand(id), ct);
        return result.IsSuccess
            ? NoContent()
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPut("{id:guid}/occurrence")]
    [RequirePermission("calendar:write")]
    public async Task<IActionResult> EditOccurrence(Guid id, [FromBody] EditRecurringOccurrenceRequest request, CancellationToken ct)
    {
        if (!Enum.TryParse<RecurrenceEditScope>(request.Scope, ignoreCase: true, out var scope))
            return Problem("Invalid scope. Expected 'ThisEventOnly' or 'AllEvents'.", statusCode: 400);

        var result = await _mediator.Send(new EditRecurringOccurrenceCommand(
            id, request.OriginalStart, scope, request.Title, request.Description, request.StartDate,
            request.EndDate, request.IsAllDay, request.Timezone, request.Location, request.MeetingLink, request.Color), ct);

        return result.IsSuccess
            ? Ok(result.Value!.ToViewModel())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpDelete("{id:guid}/occurrence")]
    [RequirePermission("calendar:write")]
    public async Task<IActionResult> CancelOccurrence(Guid id, [FromQuery] DateTimeOffset originalStart, CancellationToken ct)
    {
        var result = await _mediator.Send(new CancelRecurringOccurrenceCommand(id, originalStart), ct);
        return result.IsSuccess
            ? NoContent()
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("{id:guid}/respond")]
    [RequirePermission("calendar:read")]
    public async Task<IActionResult> Respond(Guid id, [FromBody] RespondToCalendarEventRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new RespondToCalendarEventCommand(id, request.ResponseStatus), ct);
        return result.IsSuccess
            ? NoContent()
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("check-conflicts")]
    [RequirePermission("calendar:read")]
    public async Task<IActionResult> CheckConflicts([FromBody] CheckCalendarConflictsRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new CheckCalendarConflictsQuery(request.ParticipantEmployeeIds, request.StartDate, request.EndDate), ct);
        return result.IsSuccess
            ? Ok(result.Value!.ToViewModel())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
