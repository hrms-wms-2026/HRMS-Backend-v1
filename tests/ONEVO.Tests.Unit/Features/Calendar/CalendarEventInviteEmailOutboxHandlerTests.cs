using System.Text.Json;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.OutboxHandlers;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class CalendarEventInviteEmailOutboxHandlerTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_PassesTheKindThrough()
    {
        var email = new Mock<IEmailService>();
        var payload = new CalendarEventInviteEmailPayload(
            Guid.NewGuid(), "ada@example.com", "Ada", "Standup", Start, null, "Owner", null, CalendarEmailKinds.Cancelled);

        await new CalendarEventInviteEmailOutboxHandler(email.Object).HandleAsync(JsonSerializer.Serialize(payload), CancellationToken.None);

        email.Verify(e => e.SendCalendarEventInviteAsync(
            "ada@example.com", "Ada", "Standup", Start, null, "Owner", null, CalendarEmailKinds.Cancelled, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_LegacyPayloadWithoutKind_SendsTheInvite()
    {
        var email = new Mock<IEmailService>();
        const string legacyJson = """{"TenantId":"3f1d1c56-35c0-4a52-9a52-4f7c0b0d8e11","ToEmail":"ada@example.com","RecipientName":"Ada","EventTitle":"Standup","StartDateUtc":"2026-10-20T09:00:00+00:00","Location":null,"OrganizerName":"Owner","MeetingLink":null}""";

        await new CalendarEventInviteEmailOutboxHandler(email.Object).HandleAsync(legacyJson, CancellationToken.None);

        email.Verify(e => e.SendCalendarEventInviteAsync(
            "ada@example.com", "Ada", "Standup", Start, null, "Owner", null, CalendarEmailKinds.Invite, It.IsAny<CancellationToken>()), Times.Once);
    }
}
