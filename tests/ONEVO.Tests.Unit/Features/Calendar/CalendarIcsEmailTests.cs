using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.OutboxHandlers;
using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Infrastructure.ExternalServices.Email;
using ONEVO.Tests.Unit.Fakes;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

/// <summary>Calendar P2-1 / P1-3 (plan C1): calendar emails carry an .ics with a stable UID and a growing SEQUENCE.</summary>
public sealed class CalendarIcsEmailTests
{
    private static readonly Guid EventId = Guid.NewGuid();
    private static readonly DateTimeOffset Start = new(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void An_invite_is_a_REQUEST_and_a_cancellation_a_CANCEL_for_the_same_UID()
    {
        var ics = new CalendarEventIcs(EventId, Start.AddHours(1), 42);

        var invite = CalendarIcs.Build(CalendarEventEmailKinds.Invite, ics, "Planning; Q4", Start, "Room 4", "Ada", "bob@acme.com", "https://zoom.us/j/1", Start);
        var cancel = CalendarIcs.Build(CalendarEventEmailKinds.Cancelled, ics with { Sequence = 43 }, "Planning; Q4", Start, null, "Ada", "bob@acme.com", null, Start);

        invite.Should().Contain("METHOD:REQUEST").And.Contain("STATUS:CONFIRMED").And.Contain("SEQUENCE:42")
            .And.Contain("DTSTART:20261020T090000Z").And.Contain("DTEND:20261020T100000Z").And.Contain("SUMMARY:Planning\\; Q4")
            .And.Contain("ATTENDEE;ROLE=REQ-PARTICIPANT;RSVP=TRUE:mailto:bob@acme.com").And.Contain("URL:https://zoom.us/j/1");
        cancel.Should().Contain("METHOD:CANCEL").And.Contain("STATUS:CANCELLED").And.Contain("SEQUENCE:43");
        Uid(invite).Should().Be(Uid(cancel)).And.Be(CalendarIcs.Uid(EventId));
        invite.Split("\r\n").Should().OnlyContain(line => Encoding.UTF8.GetByteCount(line) <= 75);
    }

    [Fact]
    public void The_sequence_only_grows_with_time()
        => CalendarIcs.Sequence(Start.AddMinutes(1)).Should().BeGreaterThan(CalendarIcs.Sequence(Start));

    [Fact]
    public async Task A_retried_invite_is_byte_identical_so_the_provider_idempotency_key_still_matches()
    {
        // The outbox sends a retry under the same idempotency key, and Resend refuses a different body under a key it
        // has seen (409). The .ics must therefore not depend on when the attempt ran.
        var email = new Mock<IEmailService>();
        var sent = new List<CalendarEventEmailMessage>();
        email.Setup(e => e.SendCalendarEventEmailAsync(It.IsAny<CalendarEventEmailMessage>(), It.IsAny<CancellationToken>()))
            .Callback((CalendarEventEmailMessage m, CancellationToken _) => sent.Add(m)).Returns(Task.CompletedTask);
        var handler = new CalendarEventInviteEmailOutboxHandler(email.Object);
        var payload = JsonSerializer.Serialize(new CalendarEventInviteEmailPayload(
            Guid.NewGuid(), "bob@acme.com", "Bob", "Planning", Start, null, "Ada", null, CalendarEventEmailKinds.Invite, EventId,
            Start.AddHours(1), CalendarIcs.Sequence(Start.AddDays(-1))));

        await handler.HandleAsync(payload, default);
        await Task.Delay(1100);
        await handler.HandleAsync(payload, default);

        sent[1].IcsContent.Should().Be(sent[0].IcsContent);
        // DTSTAMP is the moment the event version was written - the time SEQUENCE is counted from.
        sent[0].IcsContent.Should().Contain("DTSTAMP:20261019T090000Z");
    }

    [Fact]
    public async Task The_outbox_handler_attaches_the_ics_when_the_event_is_known_and_none_for_old_rows()
    {
        var email = new Mock<IEmailService>();
        var sent = new List<CalendarEventEmailMessage>();
        email.Setup(e => e.SendCalendarEventEmailAsync(It.IsAny<CalendarEventEmailMessage>(), It.IsAny<CancellationToken>()))
            .Callback((CalendarEventEmailMessage m, CancellationToken _) => sent.Add(m)).Returns(Task.CompletedTask);
        var handler = new CalendarEventInviteEmailOutboxHandler(email.Object);

        await handler.HandleAsync(JsonSerializer.Serialize(new CalendarEventInviteEmailPayload(
            Guid.NewGuid(), "bob@acme.com", "Bob", "Planning", Start, null, "Ada", null, CalendarEventEmailKinds.Updated, EventId, Start.AddHours(1), 7)), default);
        // A row queued before kinds and the .ics existed.
        await handler.HandleAsync("""{"TenantId":"00000000-0000-0000-0000-000000000001","ToEmail":"c@acme.com","RecipientName":"C","EventTitle":"Old","StartDateUtc":"2026-10-20T09:00:00+00:00","Location":null,"OrganizerName":"Ada"}""", default);

        sent[0].Kind.Should().Be(CalendarEventEmailKinds.Updated);
        sent[0].IcsMethod.Should().Be("REQUEST");
        sent[0].IcsContent.Should().Contain($"UID:{CalendarIcs.Uid(EventId)}").And.Contain("SEQUENCE:7");
        (sent[1].Kind, sent[1].IcsContent).Should().Be((CalendarEventEmailKinds.Invite, (string?)null));
    }

    [Fact]
    public async Task The_email_service_sends_the_ics_as_a_text_calendar_attachment()
    {
        TransactionalEmailRequest? request = null;
        var sender = new Mock<ITransactionalEmailSender>();
        sender.Setup(s => s.SendAsync(It.IsAny<TransactionalEmailRequest>(), It.IsAny<CancellationToken>()))
            .Callback((TransactionalEmailRequest r, CancellationToken _) => request = r)
            .ReturnsAsync(new TransactionalEmailResult(true, "resend", "msg-1", null, Start));
        var service = new TransactionalEmailService(sender.Object, new EmailTemplateRenderer(Options.Create(new EmailOptions())),
            NullLogger<TransactionalEmailService>.Instance);

        await service.SendCalendarEventEmailAsync(new CalendarEventEmailMessage(
            "bob@acme.com", "Bob", CalendarEventEmailKinds.Cancelled, "Planning", Start, null, "Ada", null, "BEGIN:VCALENDAR\r\nEND:VCALENDAR\r\n", "CANCEL"));

        request!.Subject.Should().Be("Cancelled: Planning");
        var file = request.Attachments.Should().ContainSingle().Subject;
        (file.FileName, file.ContentType).Should().Be(("cancel.ics", "text/calendar; method=CANCEL; charset=UTF-8"));
        Encoding.UTF8.GetString(file.Content).Should().StartWith("BEGIN:VCALENDAR");
    }

    [Fact]
    public void Untrusted_values_can_never_start_a_new_property_line()
    {
        var ics = new CalendarEventIcs(EventId, Start.AddHours(1), 1, "FREQ=WEEKLY\r\nATTENDEE:mailto:evil@x.com");

        var text = CalendarIcs.Build(CalendarEventEmailKinds.Invite, ics, "Title\rORGANIZER:mailto:evil@x.com", Start, "Room\n4", "Ada\r\nX-EVIL:1",
            "bob@acme.com\r\nATTENDEE:mailto:evil@x.com", "https://zoom.us/j/1\r\nATTENDEE:mailto:evil@x.com", Start);

        // Physical lines are either a property or a fold continuation (leading space); no injected property appears.
        var lines = text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        lines.Should().NotContain(l => l.StartsWith("ATTENDEE:mailto:evil") || l.StartsWith("ORGANIZER:mailto:evil") || l.StartsWith("X-EVIL"));
        lines.Count(l => l.StartsWith("ATTENDEE")).Should().Be(1);
        text.Replace("\r\n", "").Should().NotContain("\r").And.NotContain("\n");
    }

    private static string Uid(string ics) => ics.Split("\r\n").Single(l => l.StartsWith("UID:"))["UID:".Length..];
}
