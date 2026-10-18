using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.OutboxHandlers;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.OutboxHandlers;

namespace ONEVO.Application.Features.Calendar.Services;

public sealed class CalendarNotificationSender(IOutboxWriter outboxWriter, IEmployeeRepository employees) : ICalendarNotificationSender
{
    public async Task NotifyParticipantsAddedAsync(
        Guid tenantId, string eventTitle, DateTimeOffset startDate, string? location,
        IReadOnlyList<Guid> employeeIds, string organizerName, CancellationToken ct = default,
        string? meetingLink = null, bool sendInviteEmail = true, CalendarEventIcs? ics = null)
    {
        foreach (var employeeId in employeeIds)
        {
            var employee = await employees.GetByIdAsync(tenantId, employeeId, ct);
            if (employee is null) continue;

            await outboxWriter.EnqueueAsync(
                OutboxMessageTypes.WorkNotification,
                new WorkNotificationPayload(
                    tenantId, employee.UserId, "calendar_event_participant_added",
                    new Dictionary<string, string> { ["organizerName"] = organizerName, ["eventTitle"] = eventTitle, ["eventDate"] = startDate.ToString("u") },
                    "calendar_event", null, EventId: Guid.NewGuid()),
                tenantId, ct);

            if (!sendInviteEmail) continue;

            await EmailAsync(tenantId, employee.Email, $"{employee.FirstName} {employee.LastName}", CalendarEventEmailKinds.Invite,
                eventTitle, startDate, location, organizerName, meetingLink, ics, ct);
        }
    }

    public async Task NotifyMeetingLinkAddedAsync(
        Guid tenantId, string eventTitle, DateTimeOffset startDate, string? location,
        IReadOnlyList<Guid> employeeIds, string organizerName, string meetingLink, CancellationToken ct = default, CalendarEventIcs? ics = null)
    {
        foreach (var employeeId in employeeIds)
        {
            var employee = await employees.GetByIdAsync(tenantId, employeeId, ct);
            if (employee is null) continue;

            await EmailAsync(tenantId, employee.Email, $"{employee.FirstName} {employee.LastName}", CalendarEventEmailKinds.Invite,
                eventTitle, startDate, location, organizerName, meetingLink, ics, ct);
        }
    }

    public async Task NotifyGuestsAsync(
        Guid tenantId, string eventTitle, DateTimeOffset startDate, string? location,
        IReadOnlyList<string> guestEmails, string organizerName, string? meetingLink, CancellationToken ct = default, CalendarEventIcs? ics = null)
    {
        foreach (var email in guestEmails)
            await EmailAsync(tenantId, email, email.Split('@')[0], CalendarEventEmailKinds.Invite, eventTitle, startDate, location, organizerName, meetingLink, ics, ct);
    }

    public async Task EmailEventChangedAsync(
        Guid tenantId, string kind, CalendarEventIcs ics, string eventTitle, DateTimeOffset startDate, string? location, string? meetingLink,
        IReadOnlyList<Guid> employeeIds, IReadOnlyList<string> guestEmails, string organizerName, CancellationToken ct = default)
    {
        foreach (var employeeId in employeeIds)
        {
            var employee = await employees.GetByIdAsync(tenantId, employeeId, ct);
            if (employee is null || string.IsNullOrWhiteSpace(employee.Email)) continue;
            await EmailAsync(tenantId, employee.Email, $"{employee.FirstName} {employee.LastName}", kind, eventTitle, startDate, location,
                organizerName, meetingLink, ics, ct);
        }
        foreach (var email in guestEmails)
            await EmailAsync(tenantId, email, email.Split('@')[0], kind, eventTitle, startDate, location, organizerName, meetingLink, ics, ct);
    }

    public async Task NotifyEventUpdatedAsync(
        Guid tenantId, string eventTitle, IReadOnlyList<Guid> employeeIds, string organizerName, CancellationToken ct = default)
        => await NotifyInAppOnlyAsync(tenantId, "calendar_event_updated", eventTitle, employeeIds, organizerName, ct);

    public async Task NotifyEventCancelledAsync(
        Guid tenantId, string eventTitle, IReadOnlyList<Guid> employeeIds, string organizerName, CancellationToken ct = default)
        => await NotifyInAppOnlyAsync(tenantId, "calendar_event_cancelled", eventTitle, employeeIds, organizerName, ct);

    public async Task NotifyResolutionRequestedAsync(
        Guid tenantId, Guid organizerUserId, string eventTitle, string responderName, string reason, CancellationToken ct = default)
        => await InAppAsync(tenantId, organizerUserId, "calendar_event_resolution_requested",
            new Dictionary<string, string> { ["responderName"] = responderName, ["eventTitle"] = eventTitle, ["reason"] = reason }, ct);

    public async Task NotifyReplacementNominatedAsync(
        Guid tenantId, Guid organizerUserId, string eventTitle, string responderName, string nomineeName, CancellationToken ct = default)
        => await InAppAsync(tenantId, organizerUserId, "calendar_event_replacement_nominated",
            new Dictionary<string, string> { ["responderName"] = responderName, ["eventTitle"] = eventTitle, ["nomineeName"] = nomineeName }, ct);

    public async Task NotifyRsvpAnsweredAsync(
        Guid tenantId, Guid organizerUserId, string eventTitle, string responderName, bool accepted, CancellationToken ct = default)
        => await InAppAsync(tenantId, organizerUserId, accepted ? "calendar_event_rsvp_accepted" : "calendar_event_rsvp_rejected",
            new Dictionary<string, string> { ["responderName"] = responderName, ["eventTitle"] = eventTitle }, ct);

    public async Task NotifyNomineeAsync(Guid tenantId, Guid nomineeUserId, string eventTitle, string responderName, CancellationToken ct = default)
        => await InAppAsync(tenantId, nomineeUserId, "calendar_event_replacement_nominee",
            new Dictionary<string, string> { ["responderName"] = responderName, ["eventTitle"] = eventTitle }, ct);

    public async Task NotifyParticipantRemovedAsync(Guid tenantId, Guid employeeId, string eventTitle, string organizerName, CancellationToken ct = default)
        => await NotifyInAppOnlyAsync(tenantId, "calendar_event_participant_removed", eventTitle, [employeeId], organizerName, ct);

    public async Task NotifyMeetingLinkRemovedAsync(
        Guid tenantId, string eventTitle, IReadOnlyList<Guid> employeeIds, string organizerName, CancellationToken ct = default)
        => await NotifyInAppOnlyAsync(tenantId, "calendar_event_meeting_link_removed", eventTitle, employeeIds, organizerName, ct);

    public async Task NotifyMeetingLinkFailedAsync(
        Guid tenantId, Guid organizerUserId, string eventTitle, string providerLabel, CancellationToken ct = default)
        => await InAppAsync(tenantId, organizerUserId, "calendar_event_meeting_link_failed",
            new Dictionary<string, string> { ["eventTitle"] = eventTitle, ["provider"] = providerLabel }, ct);

    public async Task NotifyConnectionProblemAsync(Guid tenantId, Guid userId, string providerLabel, string problem, CancellationToken ct = default)
        => await InAppAsync(tenantId, userId, problem switch
        {
            "reauth_required" => "calendar_connection_reauth_required",
            "sync_conflict" => "calendar_connection_sync_conflict",
            _ => "calendar_connection_failed",
        }, new Dictionary<string, string> { ["provider"] = providerLabel }, ct);

    public async Task NotifyEventReminderAsync(Guid tenantId, Guid userId, string eventTitle, DateTimeOffset startDate, CancellationToken ct = default)
        => await InAppAsync(tenantId, userId, "calendar_event_reminder",
            new Dictionary<string, string> { ["eventTitle"] = eventTitle, ["eventTime"] = startDate.ToString("u") }, ct);

    public async Task QueueIncidentEvaluationAsync(Guid tenantId, IReadOnlyCollection<Guid> employeeIds, CancellationToken ct = default)
    {
        if (employeeIds.Count == 0)
            return;
        await outboxWriter.EnqueueAsync(OutboxMessageTypes.CalendarIncidentEvaluate,
            new CalendarIncidentEvaluatePayload(tenantId, employeeIds.Distinct().ToList()), tenantId, ct);
    }

    private async Task EmailAsync(
        Guid tenantId, string email, string recipientName, string kind, string eventTitle, DateTimeOffset startDate, string? location,
        string organizerName, string? meetingLink, CalendarEventIcs? ics, CancellationToken ct)
        => await outboxWriter.EnqueueAsync(
            OutboxMessageTypes.CalendarEventInviteEmail,
            new CalendarEventInviteEmailPayload(tenantId, email, recipientName, eventTitle, startDate, location, organizerName, meetingLink,
                kind, ics?.EventId, ics?.EndUtc, ics?.Sequence ?? 0, ics?.RecurrenceRule, ics?.RecurrenceId),
            tenantId, ct);

    private async Task InAppAsync(Guid tenantId, Guid userId, string templateCode, Dictionary<string, string> placeholders, CancellationToken ct)
        => await outboxWriter.EnqueueAsync(
            OutboxMessageTypes.WorkNotification,
            new WorkNotificationPayload(tenantId, userId, templateCode, placeholders, "calendar_event", null, EventId: Guid.NewGuid()),
            tenantId, ct);

    private async Task NotifyInAppOnlyAsync(
        Guid tenantId, string templateCode, string eventTitle, IReadOnlyList<Guid> employeeIds, string organizerName, CancellationToken ct)
    {
        foreach (var employeeId in employeeIds)
        {
            var employee = await employees.GetByIdAsync(tenantId, employeeId, ct);
            if (employee is null) continue;

            await InAppAsync(tenantId, employee.UserId, templateCode,
                new Dictionary<string, string> { ["organizerName"] = organizerName, ["eventTitle"] = eventTitle }, ct);
        }
    }
}
