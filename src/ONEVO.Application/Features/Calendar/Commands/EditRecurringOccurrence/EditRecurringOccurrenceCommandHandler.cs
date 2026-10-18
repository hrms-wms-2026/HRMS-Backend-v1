using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.DTOs.Responses;
using ONEVO.Application.Features.Calendar.Helpers;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Application.Features.Calendar.Commands.EditRecurringOccurrence;

public sealed class EditRecurringOccurrenceCommandHandler(
    ICurrentUser currentUser,
    ICalendarEventRepository events,
    ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository employees,
    ICalendarNotificationSender notifications,
    IUnitOfWork unitOfWork)
    : IRequestHandler<EditRecurringOccurrenceCommand, Result<CalendarEventItem>>
{
    public async Task<Result<CalendarEventItem>> Handle(EditRecurringOccurrenceCommand request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result<CalendarEventItem>.Forbidden();

        if (request.EndDate < request.StartDate)
            return Result<CalendarEventItem>.Failure("End date cannot be before start date.", 400);

        if (!CalendarEventValidation.IsValidMeetingLink(request.MeetingLink))
            return Result<CalendarEventItem>.Failure("Meeting link must be a valid http(s) URL.", 400);

        var tenantId = currentUser.TenantId;
        var master = await events.GetTrackedByIdForTenantAsync(tenantId, request.MasterId, ct);
        if (master is null)
            return Result<CalendarEventItem>.NotFound("Calendar event not found.");

        if (master.RecurrenceParentId != null || master.Recurrence == CalendarRecurrences.None)
            return Result<CalendarEventItem>.Failure("This event is not a recurring series.", 400);

        if (master.CreatedById != currentUser.UserId)
            return Result<CalendarEventItem>.Forbidden("Only the series creator can edit this event.");

        return request.Scope switch
        {
            RecurrenceEditScope.AllEvents => await EditAllEventsAsync(master, request, ct),
            RecurrenceEditScope.ThisEventOnly => await EditThisEventOnlyAsync(tenantId, master, request, ct),
            RecurrenceEditScope.ThisAndFollowing => await EditThisAndFollowingAsync(tenantId, master, request, ct),
            _ => Result<CalendarEventItem>.Failure("Unknown edit scope.", 400)
        };
    }

    private async Task<Result<CalendarEventItem>> EditAllEventsAsync(
        CalendarEvent master, EditRecurringOccurrenceCommand request, CancellationToken ct)
    {
        ApplyFields(master, request);
        events.Update(master);

        await NotifyAttendeesAsync(master.TenantId, attendeesOf: master.Id, changed: master,
            CalendarEventIcs.For(master.Id, master.EndDate, master.RecurrenceRule, DateTimeOffset.UtcNow), ct);

        await unitOfWork.SaveChangesAsync(ct);
        return Result<CalendarEventItem>.Success(ToItem(master, isOccurrence: false, masterId: null, originalStart: null));
    }

    /// <summary>Stages the in-app and email "updated" notices, plus the incident re-evaluation, for the
    /// attendees of <paramref name="attendeesOf"/>. The sender only stages outbox rows, so this must run
    /// before the save that writes them.</summary>
    private async Task NotifyAttendeesAsync(Guid tenantId, Guid attendeesOf, CalendarEvent changed, CalendarEventIcs ics, CancellationToken ct)
    {
        var participantsByEvent = await events.GetParticipantsForEventsAsync(tenantId, [attendeesOf], ct);
        var participantEmployeeIds = participantsByEvent.TryGetValue(attendeesOf, out var p) ? p.Select(x => x.EmployeeId).ToList() : [];
        var guestEmails = await CalendarEventAudience.GuestEmailsAsync(events, tenantId, attendeesOf, ct);
        await NotifyAsync(tenantId, participantEmployeeIds, guestEmails, changed, ics, ct);
    }

    private async Task NotifyAsync(
        Guid tenantId, IReadOnlyList<Guid> participantEmployeeIds, IReadOnlyList<string> guestEmails, CalendarEvent changed,
        CalendarEventIcs ics, CancellationToken ct)
    {
        if (participantEmployeeIds.Count > 0 || guestEmails.Count > 0)
        {
            var callerEmployee = await employees.GetDefaultForUserAsync(tenantId, currentUser.UserId, ct);
            var organizerName = callerEmployee is null ? "Someone" : $"{callerEmployee.FirstName} {callerEmployee.LastName}";
            if (participantEmployeeIds.Count > 0)
                await notifications.NotifyEventUpdatedAsync(tenantId, changed.Title, participantEmployeeIds, organizerName, ct);
            await notifications.EmailEventChangedAsync(tenantId, CalendarEventEmailKinds.Updated, ics,
                changed.Title, changed.StartDate, changed.Location, changed.MeetingLink, participantEmployeeIds, guestEmails, organizerName, ct);
        }
        await notifications.QueueIncidentEvaluationAsync(tenantId, participantEmployeeIds, ct);
    }

    private async Task<Result<CalendarEventItem>> EditThisEventOnlyAsync(
        Guid tenantId, CalendarEvent master, EditRecurringOccurrenceCommand request, CancellationToken ct)
    {
        var child = await events.GetTrackedChildByOriginalStartAsync(tenantId, master.Id, request.OriginalStart, ct);
        if (child is null)
        {
            child = new CalendarEvent
            {
                Id = Guid.NewGuid(), TenantId = tenantId, RecurrenceParentId = master.Id,
                RecurrenceOriginalStart = request.OriginalStart, Recurrence = CalendarRecurrences.None,
                SourceType = master.SourceType, Timezone = master.Timezone
            };
            ApplyFields(child, request);
            await events.AddAsync(child, ct);
        }
        else
        {
            child.IsRecurrenceCancelled = false;
            ApplyFields(child, request);
            events.Update(child);
        }

        // The occurrence has no attendee rows of its own: the series' attendees are its attendees.
        await NotifyAttendeesAsync(tenantId, attendeesOf: master.Id, changed: child,
            CalendarEventIcs.For(master.Id, child.EndDate, master.RecurrenceRule, DateTimeOffset.UtcNow) with { RecurrenceId = request.OriginalStart }, ct);

        await unitOfWork.SaveChangesAsync(ct);
        return Result<CalendarEventItem>.Success(ToItem(child, isOccurrence: true, masterId: master.Id, originalStart: request.OriginalStart));
    }

    private async Task<Result<CalendarEventItem>> EditThisAndFollowingAsync(
        Guid tenantId, CalendarEvent master, EditRecurringOccurrenceCommand request, CancellationToken ct)
    {
        return await unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var originalRule = master.RecurrenceRule!;
            master.RecurrenceRule = WithUntil(originalRule, request.OriginalStart.AddSeconds(-1));
            events.Update(master);

            var newMaster = new CalendarEvent
            {
                Id = Guid.NewGuid(), TenantId = tenantId, RecurrenceParentId = null,
                Recurrence = master.Recurrence, RecurrenceRule = originalRule, SourceType = master.SourceType,
                Timezone = master.Timezone
            };
            ApplyFields(newMaster, request);
            await events.AddAsync(newMaster, innerCt);

            // The new series keeps the same people and guests. Answers were given for a time, so they are
            // asked again only when the time or length changes; any other edit keeps them.
            var timeChanged = request.StartDate != request.OriginalStart
                || request.EndDate - request.StartDate != master.EndDate - master.StartDate;
            var participantsByEvent = await events.GetParticipantsForEventsAsync(tenantId, [master.Id], innerCt);
            var participants = participantsByEvent.TryGetValue(master.Id, out var p) ? p : [];
            if (participants.Count > 0)
            {
                await events.AddParticipantsAsync(participants.Select(x => new CalendarEventParticipant
                {
                    Id = Guid.NewGuid(), TenantId = tenantId, EventId = newMaster.Id, EmployeeId = x.EmployeeId,
                    ResponseStatus = timeChanged ? CalendarEventParticipantStatuses.Pending : x.ResponseStatus,
                    ResponseReason = timeChanged ? null : x.ResponseReason,
                }).ToList(), innerCt);
            }
            var guestEmails = await CalendarEventAudience.GuestEmailsAsync(events, tenantId, master.Id, innerCt);
            if (guestEmails.Count > 0)
            {
                await events.AddGuestsAsync(guestEmails.Select(email => new CalendarEventGuest
                {
                    Id = Guid.NewGuid(), TenantId = tenantId, EventId = newMaster.Id, Email = email,
                }).ToList(), innerCt);
            }

            var children = await events.GetChildrenForMasterAsync(tenantId, master.Id, innerCt);
            foreach (var child in children.Where(c => c.RecurrenceOriginalStart >= request.OriginalStart))
            {
                var tracked = await events.GetTrackedByIdForTenantAsync(tenantId, child.Id, innerCt);
                if (tracked is null) continue;
                tracked.RecurrenceParentId = newMaster.Id;
                events.Update(tracked);
            }

            // The copied attendee rows are not saved yet, so the new series' audience is passed directly.
            await NotifyAsync(tenantId, participants.Select(x => x.EmployeeId).ToList(), guestEmails, newMaster,
                CalendarEventIcs.For(newMaster.Id, newMaster.EndDate, newMaster.RecurrenceRule, DateTimeOffset.UtcNow), innerCt);

            await unitOfWork.SaveChangesAsync(innerCt);
            return Result<CalendarEventItem>.Success(ToItem(newMaster, isOccurrence: true, masterId: newMaster.Id, originalStart: request.StartDate));
        }, ct);
    }

    private static void ApplyFields(CalendarEvent target, EditRecurringOccurrenceCommand request)
    {
        target.Title = request.Title.Trim();
        target.Description = request.Description;
        target.StartDate = request.StartDate;
        target.EndDate = request.EndDate;
        target.IsAllDay = request.IsAllDay;
        target.Location = request.Location;
        target.MeetingLink = request.MeetingLink;
        target.Color = request.Color;
    }

    private static CalendarEventItem ToItem(CalendarEvent e, bool isOccurrence, Guid? masterId, DateTimeOffset? originalStart) => new(
        e.Id, e.Title, e.Description, e.StartDate, e.EndDate, e.SourceType, e.Color, e.Recurrence, e.IsAllDay,
        e.Timezone, e.EventStatus, e.IsPrivate, e.Location, e.MeetingLink, e.ExternalSource, e.CreatedById,
        isOccurrence, masterId, originalStart);

    private static string WithUntil(string recurrenceRule, DateTimeOffset until)
    {
        var parts = recurrenceRule.Split(';').Where(p => !p.StartsWith("UNTIL=", StringComparison.OrdinalIgnoreCase)).ToList();
        parts.Add($"UNTIL={until.UtcDateTime:yyyyMMddTHHmmssZ}");
        return string.Join(';', parts);
    }
}
