using System.Text.Json;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Services;

namespace ONEVO.Application.Features.Calendar.OutboxHandlers;

/// <summary>One calendar email. <see cref="Kind"/> (invite / updated / cancelled / removed, plan C1 P1-3) defaults to
/// invite so rows queued before it existed still send. With <see cref="EventId"/> the email carries an .ics (P2-1):
/// a stable UID per event and <see cref="Sequence"/>.</summary>
public sealed record CalendarEventInviteEmailPayload(
    Guid TenantId,
    string ToEmail,
    string RecipientName,
    string EventTitle,
    DateTimeOffset StartDateUtc,
    string? Location,
    string OrganizerName,
    string? MeetingLink = null,
    string Kind = CalendarEventEmailKinds.Invite,
    Guid? EventId = null,
    DateTimeOffset? EndDateUtc = null,
    int Sequence = 0,
    string? RecurrenceRule = null,
    DateTimeOffset? RecurrenceId = null);

/// <summary>Sends a calendar email from the outbox. Safe to retry: resending the same invite, update or cancellation
/// (same UID and SEQUENCE) is harmless, and the email is byte-identical on every attempt - DTSTAMP is the event
/// version's time, not the attempt's - so the provider's idempotency key matches it.</summary>
public sealed class CalendarEventInviteEmailOutboxHandler : IOutboxMessageHandler
{
    private readonly IEmailService _emailService;

    public CalendarEventInviteEmailOutboxHandler(IEmailService emailService)
    {
        _emailService = emailService;
    }

    public string Type => OutboxMessageTypes.CalendarEventInviteEmail;

    public async Task HandleAsync(string payloadJson, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<CalendarEventInviteEmailPayload>(payloadJson)
            ?? throw new InvalidOperationException("calendar_event_invite_email payload is empty.");

        string? ics = null;
        string? method = null;
        if (payload.EventId is Guid eventId)
        {
            ics = CalendarIcs.Build(payload.Kind, new CalendarEventIcs(eventId, payload.EndDateUtc ?? payload.StartDateUtc, payload.Sequence,
                    payload.RecurrenceRule, payload.RecurrenceId),
                payload.EventTitle, payload.StartDateUtc, payload.Location, payload.OrganizerName, payload.ToEmail, payload.MeetingLink,
                CalendarIcs.VersionTime(payload.Sequence));
            method = CalendarEventEmailKinds.IsCancel(payload.Kind) ? "CANCEL" : "REQUEST";
        }

        await _emailService.SendCalendarEventEmailAsync(new CalendarEventEmailMessage(
            payload.ToEmail, payload.RecipientName, payload.Kind, payload.EventTitle, payload.StartDateUtc, payload.Location,
            payload.OrganizerName, payload.MeetingLink, ics, method), ct);
    }
}
