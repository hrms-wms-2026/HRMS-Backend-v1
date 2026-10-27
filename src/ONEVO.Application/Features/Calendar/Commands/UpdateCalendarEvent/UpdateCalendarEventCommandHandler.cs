using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.DTOs.Responses;
using ONEVO.Application.Features.Calendar.Helpers;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.Services;

namespace ONEVO.Application.Features.Calendar.Commands.UpdateCalendarEvent;

public sealed class UpdateCalendarEventCommandHandler(
    ICurrentUser currentUser,
    ICalendarEventRepository events,
    ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository employees,
    ICalendarNotificationSender notifications,
    IUnitOfWork unitOfWork,
    CalendarMeetingGuard? meetingGuard = null)
    : IRequestHandler<UpdateCalendarEventCommand, Result<CalendarEventItem>>
{
    public async Task<Result<CalendarEventItem>> Handle(UpdateCalendarEventCommand request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result<CalendarEventItem>.Forbidden();

        if (request.EndDate < request.StartDate)
            return Result<CalendarEventItem>.Failure("End date cannot be before start date.", 400);

        if (!CalendarEventValidation.IsValidMeetingLink(request.MeetingLink))
            return Result<CalendarEventItem>.Failure("Meeting link must be a valid http(s) URL.", 400);

        var tenantId = currentUser.TenantId;
        var existing = await events.GetTrackedByIdForTenantAsync(tenantId, request.Id, ct);
        if (existing is null)
            return Result<CalendarEventItem>.NotFound("Calendar event not found.");

        if (existing.CreatedById != currentUser.UserId)
            return Result<CalendarEventItem>.Forbidden("Only the event creator can edit this event.");
        if (meetingGuard is not null && !string.IsNullOrWhiteSpace(request.MeetingLink) && string.IsNullOrWhiteSpace(existing.MeetingLink)
            && await meetingGuard.RefusesBecomingMeetingAsync(tenantId, existing, ct))
            return CalendarMeetingGuard.Refusal<CalendarEventItem>();

        return await unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            existing.Title = request.Title.Trim();
            existing.Description = request.Description;
            existing.StartDate = request.StartDate;
            existing.EndDate = request.EndDate;
            existing.IsAllDay = request.IsAllDay;
            existing.Location = request.Location;
            existing.MeetingLink = request.MeetingLink;
            existing.Color = request.Color;
            existing.Recurrence = request.Recurrence;

            events.Update(existing);

            // The sender only stages outbox rows, so they must be staged before the save that writes them.
            var participantsByEvent = await events.GetParticipantsForEventsAsync(tenantId, [existing.Id], innerCt);
            var participantEmployeeIds = participantsByEvent.TryGetValue(existing.Id, out var p) ? p.Select(x => x.EmployeeId).ToList() : [];
            var guestEmails = await CalendarEventAudience.GuestEmailsAsync(events, tenantId, existing.Id, innerCt);
            if (participantEmployeeIds.Count > 0 || guestEmails.Count > 0)
            {
                var callerEmployee = await employees.GetDefaultForUserAsync(tenantId, currentUser.UserId, innerCt);
                var organizerName = callerEmployee is null ? "Someone" : $"{callerEmployee.FirstName} {callerEmployee.LastName}";
                if (participantEmployeeIds.Count > 0)
                    await notifications.NotifyEventUpdatedAsync(tenantId, existing.Title, participantEmployeeIds, organizerName, innerCt);
                // Participants and guests also get the updated invite by email, with its .ics (plan C1 P1-3 / P2-1).
                await notifications.EmailEventChangedAsync(tenantId, CalendarEventEmailKinds.Updated,
                    CalendarEventIcs.For(existing.Id, existing.EndDate, existing.RecurrenceRule, DateTimeOffset.UtcNow),
                    existing.Title, existing.StartDate, existing.Location, existing.MeetingLink, participantEmployeeIds, guestEmails, organizerName, innerCt);
            }
            await notifications.QueueIncidentEvaluationAsync(tenantId, participantEmployeeIds, innerCt);

            await unitOfWork.SaveChangesAsync(innerCt);

            return Result<CalendarEventItem>.Success(new CalendarEventItem(
                existing.Id, existing.Title, existing.Description, existing.StartDate, existing.EndDate,
                existing.SourceType, existing.Color, existing.Recurrence, existing.IsAllDay, existing.Timezone,
                existing.EventStatus, existing.IsPrivate, existing.Location, existing.MeetingLink,
                existing.ExternalSource, existing.CreatedById));
        }, ct);
    }
}
