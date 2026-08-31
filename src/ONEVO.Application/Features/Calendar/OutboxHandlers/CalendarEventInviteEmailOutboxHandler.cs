using System.Text.Json;
using ONEVO.Application.Common.ServiceInterfaces;

namespace ONEVO.Application.Features.Calendar.OutboxHandlers;

public sealed record CalendarEventInviteEmailPayload(
    Guid TenantId,
    string ToEmail,
    string RecipientName,
    string EventTitle,
    DateTimeOffset StartDateUtc,
    string? Location,
    string OrganizerName);

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

        await _emailService.SendCalendarEventInviteAsync(
            payload.ToEmail, payload.RecipientName, payload.EventTitle, payload.StartDateUtc, payload.Location, payload.OrganizerName, ct);
    }
}
