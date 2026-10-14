using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.OutboxHandlers;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.OutboxHandlers;
using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Application.Features.Calendar.Services;

public sealed class CalendarNotificationSender(IOutboxWriter outboxWriter, IEmployeeRepository employees) : ICalendarNotificationSender
{
    public async Task NotifyParticipantsAddedAsync(
        Guid tenantId, string eventTitle, DateTimeOffset startDate, string? location,
        IReadOnlyList<CalendarParticipantRecipient> participants, string organizerName, CancellationToken ct = default,
        string? meetingLink = null, bool sendInviteEmail = true)
    {
        foreach (var recipient in participants)
        {
            var employee = await employees.GetByIdAsync(tenantId, recipient.EmployeeId, ct);
            if (employee is null) continue;

            await EnqueueInAppAsync(tenantId, employee.UserId, "calendar_event_participant_added",
                new Dictionary<string, string> { ["organizerName"] = organizerName, ["eventTitle"] = eventTitle, ["eventDate"] = startDate.ToString("u") }, ct);

            if (!sendInviteEmail) continue;

            await EnqueueEmailAsync(tenantId,
                new CalendarEventInviteEmailPayload(tenantId, employee.Email, FullName(employee), eventTitle, startDate, location, organizerName, meetingLink),
                recipient.EmailKey, ct);
        }
    }

    public async Task NotifyMeetingLinkAddedAsync(
        Guid tenantId, string eventTitle, DateTimeOffset startDate, string? location,
        IReadOnlyList<CalendarParticipantRecipient> participants, string organizerName, string? meetingLink, CancellationToken ct = default)
    {
        foreach (var recipient in participants)
        {
            var employee = await employees.GetByIdAsync(tenantId, recipient.EmployeeId, ct);
            if (employee is null) continue;

            await EnqueueEmailAsync(tenantId,
                new CalendarEventInviteEmailPayload(tenantId, employee.Email, FullName(employee), eventTitle, startDate, location, organizerName, meetingLink),
                recipient.EmailKey, ct);
        }
    }

    public Task NotifyMeetingLinkRemovedAsync(
        Guid tenantId, string eventTitle, DateTimeOffset startDate, string? location,
        IReadOnlyList<CalendarParticipantRecipient> participants, IReadOnlyList<CalendarGuestRecipient> guests,
        string organizerName, CancellationToken ct = default)
        => NotifyChangeAsync(tenantId, "calendar_event_meeting_link_removed", CalendarEmailKinds.LinkRemoved, eventTitle, startDate,
            location, meetingLink: null, participants, guests, organizerName, ct);

    public Task NotifyOrganizerAboutMeetingAsync(
        Guid tenantId, Guid organizerUserId, string templateCode, string eventTitle, string providerName, CancellationToken ct = default)
        => EnqueueInAppAsync(tenantId, organizerUserId, templateCode,
            new Dictionary<string, string> { ["eventTitle"] = eventTitle, ["provider"] = providerName }, ct);

    public async Task NotifyGuestsAsync(
        Guid tenantId, string eventTitle, DateTimeOffset startDate, string? location,
        IReadOnlyList<CalendarGuestRecipient> guests, string organizerName, string? meetingLink, CancellationToken ct = default)
    {
        foreach (var guest in guests)
        {
            await EnqueueEmailAsync(tenantId,
                new CalendarEventInviteEmailPayload(tenantId, guest.Email, GuestName(guest.Email), eventTitle, startDate, location, organizerName, meetingLink),
                guest.EmailKey, ct);
        }
    }

    public Task NotifyEventUpdatedAsync(
        Guid tenantId, string eventTitle, DateTimeOffset startDate, string? location, string? meetingLink,
        IReadOnlyList<CalendarParticipantRecipient> participants, IReadOnlyList<CalendarGuestRecipient> guests,
        string organizerName, CancellationToken ct = default)
        => NotifyChangeAsync(tenantId, "calendar_event_updated", CalendarEmailKinds.Updated, eventTitle, startDate, location,
            meetingLink, participants, guests, organizerName, ct);

    public Task NotifyEventCancelledAsync(
        Guid tenantId, string eventTitle, DateTimeOffset startDate, string? location,
        IReadOnlyList<CalendarParticipantRecipient> participants, IReadOnlyList<CalendarGuestRecipient> guests,
        string organizerName, CancellationToken ct = default)
        => NotifyChangeAsync(tenantId, "calendar_event_cancelled", CalendarEmailKinds.Cancelled, eventTitle, startDate, location,
            meetingLink: null, participants, guests, organizerName, ct);

    public Task NotifyResolutionRequestedAsync(
        Guid tenantId, Guid organizerUserId, string eventTitle, string responderName, string reason, CancellationToken ct = default)
        => EnqueueInAppAsync(tenantId, organizerUserId, "calendar_event_resolution_requested",
            new Dictionary<string, string> { ["responderName"] = responderName, ["eventTitle"] = eventTitle, ["reason"] = reason }, ct);

    public Task NotifyReplacementNominatedAsync(
        Guid tenantId, Guid organizerUserId, string eventTitle, string responderName, string nomineeName, CancellationToken ct = default)
        => EnqueueInAppAsync(tenantId, organizerUserId, "calendar_event_replacement_nominated",
            new Dictionary<string, string> { ["responderName"] = responderName, ["eventTitle"] = eventTitle, ["nomineeName"] = nomineeName }, ct);

    public Task NotifyRsvpAsync(
        Guid tenantId, Guid organizerUserId, string eventTitle, string responderName, bool accepted, CancellationToken ct = default)
        => EnqueueInAppAsync(tenantId, organizerUserId, accepted ? "calendar_event_rsvp_accepted" : "calendar_event_rsvp_rejected",
            new Dictionary<string, string> { ["responderName"] = responderName, ["eventTitle"] = eventTitle }, ct);

    public async Task NotifyNomineeAsync(
        Guid tenantId, Guid nomineeEmployeeId, string eventTitle, string responderName, CancellationToken ct = default)
    {
        var nominee = await employees.GetByIdAsync(tenantId, nomineeEmployeeId, ct);
        if (nominee is null) return;

        await EnqueueInAppAsync(tenantId, nominee.UserId, "calendar_event_replacement_nominee",
            new Dictionary<string, string> { ["responderName"] = responderName, ["eventTitle"] = eventTitle }, ct);
    }

    public async Task NotifyParticipantRemovedAsync(
        Guid tenantId, string eventTitle, DateTimeOffset startDate, string? location, Guid employeeId,
        string organizerName, CancellationToken ct = default)
    {
        var employee = await employees.GetByIdAsync(tenantId, employeeId, ct);
        if (employee is null) return;

        await EnqueueInAppAsync(tenantId, employee.UserId, "calendar_event_participant_removed",
            new Dictionary<string, string> { ["organizerName"] = organizerName, ["eventTitle"] = eventTitle }, ct);
        await EnqueueEmailAsync(tenantId,
            new CalendarEventInviteEmailPayload(tenantId, employee.Email, FullName(employee), eventTitle, startDate, location,
                organizerName, null, CalendarEmailKinds.Removed),
            emailKey: null, ct);
    }

    public Task NotifyGuestRemovedAsync(
        Guid tenantId, string eventTitle, DateTimeOffset startDate, string? location, string guestEmail,
        string organizerName, CancellationToken ct = default)
        => EnqueueEmailAsync(tenantId,
            new CalendarEventInviteEmailPayload(tenantId, guestEmail, GuestName(guestEmail), eventTitle, startDate, location,
                organizerName, null, CalendarEmailKinds.Removed),
            emailKey: null, ct);

    private async Task NotifyChangeAsync(
        Guid tenantId, string templateCode, string emailKind, string eventTitle, DateTimeOffset startDate, string? location,
        string? meetingLink, IReadOnlyList<CalendarParticipantRecipient> participants, IReadOnlyList<CalendarGuestRecipient> guests,
        string organizerName, CancellationToken ct)
    {
        foreach (var recipient in participants)
        {
            var employee = await employees.GetByIdAsync(tenantId, recipient.EmployeeId, ct);
            if (employee is null) continue;

            await EnqueueInAppAsync(tenantId, employee.UserId, templateCode,
                new Dictionary<string, string> { ["organizerName"] = organizerName, ["eventTitle"] = eventTitle }, ct);
            await EnqueueEmailAsync(tenantId,
                new CalendarEventInviteEmailPayload(tenantId, employee.Email, FullName(employee), eventTitle, startDate, location,
                    organizerName, meetingLink, emailKind),
                recipient.EmailKey, ct);
        }

        foreach (var guest in guests)
        {
            await EnqueueEmailAsync(tenantId,
                new CalendarEventInviteEmailPayload(tenantId, guest.Email, GuestName(guest.Email), eventTitle, startDate, location,
                    organizerName, meetingLink, emailKind),
                guest.EmailKey, ct);
        }
    }

    private Task EnqueueInAppAsync(Guid tenantId, Guid userId, string templateCode, Dictionary<string, string> placeholders, CancellationToken ct)
        => outboxWriter.EnqueueAsync(
            OutboxMessageTypes.WorkNotification,
            new WorkNotificationPayload(tenantId, userId, templateCode, placeholders, "calendar_event", null),
            tenantId, ct);

    private async Task EnqueueEmailAsync(Guid tenantId, CalendarEventInviteEmailPayload payload, string? emailKey, CancellationToken ct)
    {
        if (emailKey is null)
            await outboxWriter.EnqueueAsync(OutboxMessageTypes.CalendarEventInviteEmail, payload, tenantId, ct);
        else
            await outboxWriter.EnqueueKeyedAsync(OutboxMessageTypes.CalendarEventInviteEmail, payload, emailKey, tenantId, ct);
    }

    private static string FullName(Employee employee) => $"{employee.FirstName} {employee.LastName}";

    private static string GuestName(string email) => email.Split('@')[0];
}
