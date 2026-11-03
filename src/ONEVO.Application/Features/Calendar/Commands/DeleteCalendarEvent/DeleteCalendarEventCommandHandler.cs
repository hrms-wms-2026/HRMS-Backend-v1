using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.OutboxHandlers;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Application.Features.Calendar.Commands.DeleteCalendarEvent;

public sealed class DeleteCalendarEventCommandHandler(
    ICurrentUser currentUser,
    ICalendarEventRepository events,
    ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository employees,
    ICalendarNotificationSender notifications,
    ICalendarEventMeetingRepository meetings,
    IOutboxWriter outbox,
    IUnitOfWork unitOfWork)
    : IRequestHandler<DeleteCalendarEventCommand, Result>
{
    public async Task<Result> Handle(DeleteCalendarEventCommand request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result.Forbidden();

        var tenantId = currentUser.TenantId;
        var existing = await events.GetTrackedByIdForTenantAsync(tenantId, request.Id, ct);
        if (existing is null)
            return Result.NotFound("Calendar event not found.");

        if (existing.CreatedById != currentUser.UserId)
            return Result.Forbidden("Only the event creator can delete this event.");

        return await unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            // X-2: delete locally first. The remote cancel is a self-contained outbox message inserted in
            // THIS transaction, so it exists only if the delete commits.
            // Decide from the database, not the tracked row: if the execution strategy re-runs this lambda
            // after a failed save, the tracked row still carries the rolled-back attempt's "cancelled".
            var meeting = await meetings.GetByCalendarEventAsync(tenantId, existing.Id, innerCt);
            if (meeting is not null)
            {
                var providerName = CalendarMeetingProviderNames.Display(meeting.Provider);
                if (meeting.Status == CalendarEventMeetingStatuses.Active)
                {
                    var organizerEmployee = await employees.GetDefaultForUserAsync(tenantId, currentUser.UserId, innerCt);
                    await outbox.EnqueueKeyedAsync(
                        OutboxMessageTypes.CalendarMeetingCancel,
                        new CalendarMeetingCancelPayload(
                            tenantId, meeting.Provider, meeting.ExternalMeetingId, meeting.ExternalCalendarConnectionId,
                            existing.CreatedById, organizerEmployee?.Id ?? Guid.Empty, existing.Title),
                        $"meeting-cancel:{meeting.Id}:{meeting.LinkGeneration}", tenantId, innerCt);
                    var tracked = (await meetings.GetTrackedByCalendarEventAsync(tenantId, existing.Id, innerCt))!;
                    tracked.Status = CalendarEventMeetingStatuses.Cancelled;
                    meetings.Update(tracked);
                }
                else if (meeting.Status is CalendarEventMeetingStatuses.Provisioning
                    or CalendarEventMeetingStatuses.ProvisioningUnknown or CalendarEventMeetingStatuses.Unresolved)
                {
                    // We don't know the remote meeting's ID: the organizer has to check by hand.
                    await notifications.NotifyOrganizerAboutMeetingAsync(
                        tenantId, existing.CreatedById, CalendarMeetingNoticeCodes.Unresolved, existing.Title, providerName, innerCt);
                }
            }

            // Participants and guests are read before the event is removed, and every notice is staged before the save.
            var participantsByEvent = await events.GetParticipantsForEventsAsync(tenantId, [existing.Id], innerCt);
            var participantEmployeeIds = participantsByEvent.TryGetValue(existing.Id, out var p) ? p.Select(x => x.EmployeeId).ToList() : [];
            var guestEmails = await CalendarEventAudience.GuestEmailsAsync(events, tenantId, existing.Id, innerCt);
            if (participantEmployeeIds.Count > 0 || guestEmails.Count > 0)
            {
                var callerEmployee = await employees.GetDefaultForUserAsync(tenantId, currentUser.UserId, innerCt);
                var organizerName = callerEmployee is null ? "Someone" : $"{callerEmployee.FirstName} {callerEmployee.LastName}";
                if (participantEmployeeIds.Count > 0)
                    await notifications.NotifyEventCancelledAsync(tenantId, existing.Title, participantEmployeeIds, organizerName, innerCt);
                // The cancellation email removes the entry from participants' and guests' own calendars (CANCEL .ics).
                await notifications.EmailEventChangedAsync(tenantId, CalendarEventEmailKinds.Cancelled,
                    CalendarEventIcs.For(existing.Id, existing.EndDate, existing.RecurrenceRule, DateTimeOffset.UtcNow),
                    existing.Title, existing.StartDate, existing.Location, null, participantEmployeeIds, guestEmails, organizerName, innerCt);
            }
            await notifications.QueueIncidentEvaluationAsync(tenantId, participantEmployeeIds, innerCt);

            events.Remove(existing);
            await unitOfWork.SaveChangesAsync(innerCt);
            return Result.Success();
        }, ct);
    }
}
