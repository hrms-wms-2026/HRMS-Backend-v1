using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Meetings.Commands;
using ONEVO.Application.Features.WorkManagement.Meetings.Commands.CancelMeeting;
using ONEVO.Application.Features.WorkManagement.Meetings.Commands.CreateMeeting;
using ONEVO.Application.Features.WorkManagement.Meetings.Commands.EditMeeting;
using ONEVO.Application.Features.WorkManagement.Meetings.Commands.MoveMeeting;
using ONEVO.Application.Features.WorkManagement.Meetings.Contracts;
using ONEVO.Application.Features.WorkManagement.Meetings.Queries;

namespace ONEVO.Api.Controllers.Tenant.WorkManagement;

/// <summary>
/// Meetings as work items (meetings-as-tasks Phase 2; contract: docs/api/meetings-v1.md). Who may do what is decided
/// in the application layer - organizer, attendee, the D4 module rule, calendar:write for Office meetings - so there is
/// no permission attribute here, and "not found" and "not allowed to see" share one 404 body. Every failure carries a
/// stable `code` extension.
/// </summary>
[ApiController]
[Route("api/v1/meetings")]
[Authorize(Policy = "TenantPolicy")]
public sealed class MeetingsController : ControllerBase
{
    private readonly IMediator _mediator;

    public MeetingsController(IMediator mediator) => _mediator = mediator;

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateMeetingRequest request, CancellationToken ct)
        => Respond(await _mediator.Send(new CreateMeetingCommand(request), ct), value => StatusCode(201, value));

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] DateTimeOffset from, [FromQuery] DateTimeOffset to, [FromQuery] Guid? projectId, CancellationToken ct)
        => Respond(await _mediator.Send(new ListMeetingsQuery(from, to, projectId), ct), Ok);

    [HttpGet("occurrences/{taskId:guid}")]
    public async Task<IActionResult> GetOccurrence(Guid taskId, CancellationToken ct)
        => Respond(await _mediator.Send(new GetMeetingOccurrenceQuery(taskId), ct), Ok);

    [HttpGet("series/{seriesId:guid}/occurrences/{originalStart}")]
    public async Task<IActionResult> GetOccurrenceAt(Guid seriesId, DateTimeOffset originalStart, CancellationToken ct)
        => Respond(await _mediator.Send(new GetMeetingOccurrenceAtQuery(seriesId, originalStart), ct), Ok);

    [HttpGet("series/{seriesId:guid}")]
    public async Task<IActionResult> GetSeries(Guid seriesId, CancellationToken ct)
        => Respond(await _mediator.Send(new GetMeetingSeriesQuery(seriesId), ct), Ok);

    [HttpPatch("series/{seriesId:guid}/occurrences/{originalStart}")]
    public async Task<IActionResult> Edit(Guid seriesId, DateTimeOffset originalStart, [FromBody] EditMeetingRequest request, CancellationToken ct)
        => Respond(await _mediator.Send(new EditMeetingCommand(seriesId, originalStart, request), ct), Ok);

    [HttpPost("series/{seriesId:guid}/occurrences/{originalStart}/cancel")]
    public async Task<IActionResult> Cancel(Guid seriesId, DateTimeOffset originalStart, [FromBody] CancelMeetingRequest request, CancellationToken ct)
        => Respond(await _mediator.Send(new CancelMeetingCommand(seriesId, originalStart, request.Scope), ct), _ => NoContent());

    [HttpPost("series/{seriesId:guid}/occurrences/{originalStart}/move")]
    public async Task<IActionResult> Move(Guid seriesId, DateTimeOffset originalStart, [FromBody] MoveMeetingRequest request, CancellationToken ct)
        => Respond(await _mediator.Send(new MoveMeetingCommand(seriesId, originalStart, request), ct), Ok);

    [HttpPost("series/{seriesId:guid}/attendees")]
    public async Task<IActionResult> AddSeriesAttendees(Guid seriesId, [FromBody] MeetingAttendeesRequest request, CancellationToken ct)
        => Respond(await _mediator.Send(new AddSeriesAttendeesCommand(seriesId, request), ct), Ok);

    [HttpDelete("series/{seriesId:guid}/attendees/employees/{employeeId:guid}")]
    public async Task<IActionResult> RemoveSeriesEmployee(Guid seriesId, Guid employeeId, CancellationToken ct)
        => Respond(await _mediator.Send(new RemoveSeriesEmployeeCommand(seriesId, employeeId), ct), _ => NoContent());

    [HttpDelete("series/{seriesId:guid}/attendees/guests/{email}")]
    public async Task<IActionResult> RemoveSeriesGuest(Guid seriesId, string email, CancellationToken ct)
        => Respond(await _mediator.Send(new RemoveSeriesGuestCommand(seriesId, email), ct), _ => NoContent());

    [HttpPost("series/{seriesId:guid}/occurrences/{originalStart}/attendees")]
    public async Task<IActionResult> AddOccurrenceAttendees(Guid seriesId, DateTimeOffset originalStart, [FromBody] MeetingAttendeesRequest request, CancellationToken ct)
        => Respond(await _mediator.Send(new AddOccurrenceAttendeesCommand(seriesId, originalStart, request), ct), Ok);

    [HttpDelete("series/{seriesId:guid}/occurrences/{originalStart}/attendees/employees/{employeeId:guid}")]
    public async Task<IActionResult> RemoveOccurrenceEmployee(Guid seriesId, DateTimeOffset originalStart, Guid employeeId, CancellationToken ct)
        => Respond(await _mediator.Send(new RemoveOccurrenceEmployeeCommand(seriesId, originalStart, employeeId), ct), _ => NoContent());

    [HttpDelete("series/{seriesId:guid}/occurrences/{originalStart}/attendees/guests/{email}")]
    public async Task<IActionResult> RemoveOccurrenceGuest(Guid seriesId, DateTimeOffset originalStart, string email, CancellationToken ct)
        => Respond(await _mediator.Send(new RemoveOccurrenceGuestCommand(seriesId, originalStart, email), ct), _ => NoContent());

    [HttpPost("series/{seriesId:guid}/respond")]
    public async Task<IActionResult> Respond(Guid seriesId, [FromBody] RespondToMeetingRequest request, CancellationToken ct)
        => Respond(await _mediator.Send(new RespondToMeetingCommand(seriesId, request), ct), _ => NoContent());

    [HttpGet("series/{seriesId:guid}/link")]
    public async Task<IActionResult> GetLink(Guid seriesId, CancellationToken ct)
        => Respond(await _mediator.Send(new GetMeetingLinkQuery(seriesId), ct), Ok);

    [HttpPost("series/{seriesId:guid}/link")]
    public async Task<IActionResult> CreateLink(Guid seriesId, [FromBody] MeetingLinkRequest request, CancellationToken ct)
        => Respond(await _mediator.Send(new CreateMeetingLinkCommand(seriesId, request), ct), Ok);

    [HttpPost("series/{seriesId:guid}/link/retry")]
    public async Task<IActionResult> RetryLink(Guid seriesId, CancellationToken ct)
        => Respond(await _mediator.Send(new RetryMeetingLinkCommand(seriesId), ct), Ok);

    [HttpDelete("series/{seriesId:guid}/link")]
    public async Task<IActionResult> RemoveLink(Guid seriesId, CancellationToken ct)
        => Respond(await _mediator.Send(new RemoveMeetingLinkCommand(seriesId), ct), link => link is null ? NoContent() : Ok(link));

    [HttpPost("series/{seriesId:guid}/link/cleanup-confirmed")]
    public async Task<IActionResult> ConfirmLinkCleanup(Guid seriesId, CancellationToken ct)
        => Respond(await _mediator.Send(new ConfirmMeetingLinkCleanupCommand(seriesId), ct), _ => NoContent());

    private IActionResult Respond<T>(Result<T> result, Func<T, IActionResult> onSuccess)
    {
        if (result.IsSuccess)
            return onSuccess(result.Value!);
        var problem = new ProblemDetails
        {
            Status = result.StatusCode ?? StatusCodes.Status400BadRequest,
            Title = "The meeting request could not be completed.",
            Detail = result.Error,
        };
        if (!string.IsNullOrWhiteSpace(result.ErrorCode))
            problem.Extensions["code"] = result.ErrorCode;
        return new ObjectResult(problem) { StatusCode = problem.Status };
    }
}
