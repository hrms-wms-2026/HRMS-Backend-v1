using System.Text.Json;
using ONEVO.Application.Common.ServiceInterfaces;

namespace ONEVO.Application.Features.Calendar.OutboxHandlers;

public static class CalendarEmailKinds
{
    public const string Invite = "invite";
    public const string Updated = "updated";
    public const string Cancelled = "cancelled";
    public const string Removed = "removed";
}

public sealed record CalendarEventInviteEmailPayload(
    Guid TenantId,
    string ToEmail,
    string RecipientName,
    string EventTitle,
    DateTimeOffset StartDateUtc,
    string? Location,
    string OrganizerName,
    string? MeetingLink = null,
    string Kind = CalendarEmailKinds.Invite);

/// <summary>Sends the calendar-event-invite email from the outbox. Safe to retry: resending an
/// invite email for the same event is harmless.</summary>
public sealed class CalendarEventInviteEmailOutboxHandler : IOutboxMessageHandler
{
    private readonly IEmailService _emailService;

    public CalendarEventInviteEmailOutboxHandler(IEmailService emailService) => _emailService = emailService;

    public string Type => OutboxMessageTypes.CalendarEventInviteEmail;

    public async Task HandleAsync(string payloadJson, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<CalendarEventInviteEmailPayload>(payloadJson)
            ?? throw new InvalidOperationException("calendar_event_invite_email payload is empty.");

        // Payloads queued before Kind existed deserialize with Kind = null: those were invites.
        var kind = string.IsNullOrEmpty(payload.Kind) ? CalendarEmailKinds.Invite : payload.Kind;
        await _emailService.SendCalendarEventInviteAsync(
            payload.ToEmail, payload.RecipientName, payload.EventTitle, payload.StartDateUtc, payload.Location,
            payload.OrganizerName, payload.MeetingLink, kind, ct);
    }
}
