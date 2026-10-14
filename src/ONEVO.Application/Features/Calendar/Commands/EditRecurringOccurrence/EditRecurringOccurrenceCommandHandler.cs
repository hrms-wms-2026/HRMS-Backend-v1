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

    private Task<Result<CalendarEventItem>> EditAllEventsAsync(
        CalendarEvent master, EditRecurringOccurrenceCommand request, CancellationToken ct)
        => unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            ApplyFields(master, request);
            master.Revision++;
            events.Update(master);

            await NotifyAttendeesOfAsync(master.TenantId, attendeesOf: master.Id, changed: master, innerCt);
            await unitOfWork.SaveChangesAsync(innerCt);
            return Result<CalendarEventItem>.Success(ToItem(master, isOccurrence: false, masterId: null, originalStart: null));
        }, ct);

    private Task<Result<CalendarEventItem>> EditThisEventOnlyAsync(
        Guid tenantId, CalendarEvent master, EditRecurringOccurrenceCommand request, CancellationToken ct)
        => unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var child = await events.GetTrackedChildByOriginalStartAsync(tenantId, master.Id, request.OriginalStart, innerCt);
            if (child is null)
            {
                child = new CalendarEvent
                {
                    Id = Guid.NewGuid(), TenantId = tenantId, RecurrenceParentId = master.Id,
                    RecurrenceOriginalStart = request.OriginalStart, Recurrence = CalendarRecurrences.None,
                    SourceType = master.SourceType, Timezone = master.Timezone
                };
                ApplyFields(child, request);
                child.Revision++;
                await events.AddAsync(child, innerCt);
            }
            else
            {
                child.IsRecurrenceCancelled = false;
                ApplyFields(child, request);
                child.Revision++;
                events.Update(child);
            }

            // The occurrence has no attendee rows of its own: the series' attendees are its attendees.
            await NotifyAttendeesOfAsync(tenantId, attendeesOf: master.Id, changed: child, innerCt);
            await unitOfWork.SaveChangesAsync(innerCt);
            return Result<CalendarEventItem>.Success(ToItem(child, isOccurrence: true, masterId: master.Id, originalStart: request.OriginalStart));
        }, ct);

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
                Timezone = master.Timezone, Revision = 1
            };
            ApplyFields(newMaster, request);
            await events.AddAsync(newMaster, innerCt);

            // P0-3: the new series keeps the series' participants (with their responses) and guests.
            var (participants, guests) = await CopyAttendeesAsync(tenantId, master.Id, newMaster.Id, innerCt);

            var children = await events.GetChildrenForMasterAsync(tenantId, master.Id, innerCt);
            foreach (var child in children.Where(c => c.RecurrenceOriginalStart >= request.OriginalStart))
            {
                var tracked = await events.GetTrackedByIdForTenantAsync(tenantId, child.Id, innerCt);
                if (tracked is null) continue;
                tracked.RecurrenceParentId = newMaster.Id;
                events.Update(tracked);
            }

            await NotifyRecipientsAsync(tenantId, newMaster, participants, guests, innerCt);
            await unitOfWork.SaveChangesAsync(innerCt);
            return Result<CalendarEventItem>.Success(ToItem(newMaster, isOccurrence: true, masterId: newMaster.Id, originalStart: request.StartDate));
        }, ct);
    }

    private async Task<(IReadOnlyList<CalendarEventParticipant> Participants, IReadOnlyList<CalendarEventGuest> Guests)> CopyAttendeesAsync(
        Guid tenantId, Guid fromEventId, Guid toEventId, CancellationToken ct)
    {
        var participants = ((await events.GetParticipantsForEventsAsync(tenantId, [fromEventId], ct)).GetValueOrDefault(fromEventId) ?? [])
            .Select(p => new CalendarEventParticipant
            {
                Id = Guid.NewGuid(), TenantId = tenantId, EventId = toEventId, EmployeeId = p.EmployeeId,
                ResponseStatus = p.ResponseStatus, ResponseReason = p.ResponseReason
            })
            .ToList();
        if (participants.Count > 0)
            await events.AddParticipantsAsync(participants, ct);

        var guests = ((await events.GetGuestsForEventsAsync(tenantId, [fromEventId], ct)).GetValueOrDefault(fromEventId) ?? [])
            .Select(g => new CalendarEventGuest { Id = Guid.NewGuid(), TenantId = tenantId, EventId = toEventId, Email = g.Email })
            .ToList();
        if (guests.Count > 0)
            await events.AddGuestsAsync(guests, ct);

        return (participants, guests);
    }

    private async Task NotifyAttendeesOfAsync(Guid tenantId, Guid attendeesOf, CalendarEvent changed, CancellationToken ct)
    {
        var participants = (await events.GetParticipantsForEventsAsync(tenantId, [attendeesOf], ct)).GetValueOrDefault(attendeesOf) ?? [];
        var guests = (await events.GetGuestsForEventsAsync(tenantId, [attendeesOf], ct)).GetValueOrDefault(attendeesOf) ?? [];
        await NotifyRecipientsAsync(tenantId, changed, participants, guests, ct);
    }

    private async Task NotifyRecipientsAsync(
        Guid tenantId, CalendarEvent changed, IReadOnlyList<CalendarEventParticipant> participants,
        IReadOnlyList<CalendarEventGuest> guests, CancellationToken ct)
    {
        if (participants.Count == 0 && guests.Count == 0)
            return;

        var callerEmployee = await employees.GetDefaultForUserAsync(tenantId, currentUser.UserId, ct);
        var organizerName = callerEmployee is null ? "Someone" : $"{callerEmployee.FirstName} {callerEmployee.LastName}";
        await notifications.NotifyEventUpdatedAsync(
            tenantId, changed.Title, changed.StartDate, changed.Location, changed.MeetingLink,
            participants.Select(p => new CalendarParticipantRecipient(p.EmployeeId, CalendarEmailKeys.Update(changed.Id, changed.Revision, p.Id))).ToList(),
            guests.Select(g => new CalendarGuestRecipient(g.Email, CalendarEmailKeys.Update(changed.Id, changed.Revision, g.Id))).ToList(),
            organizerName, ct);
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
