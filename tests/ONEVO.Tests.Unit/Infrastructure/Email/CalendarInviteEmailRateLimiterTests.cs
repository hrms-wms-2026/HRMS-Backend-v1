using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.OutboxHandlers;
using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Infrastructure.ExternalServices.Email;
using ONEVO.Tests.Unit.Fakes;
using Xunit;

namespace ONEVO.Tests.Unit.Infrastructure.Email;

/// <summary>
/// The calendar invite outbox handler paces its sends through <see cref="IEmailRateLimiter"/>. Without the
/// pacing, one bulk invite of 300 people hits the email provider in one burst.
/// </summary>
public sealed class CalendarInviteEmailRateLimiterTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 21, 9, 0, 0, TimeSpan.Zero);

    private static string Payload() => JsonSerializer.Serialize(new CalendarEventInviteEmailPayload(
        Guid.NewGuid(), "bob@acme.com", "Bob", "Planning", Start, null, "Ada"));

    [Fact]
    public async Task The_handler_waits_for_the_limiter_before_it_sends()
    {
        var email = new Mock<IEmailService>();
        email.Setup(e => e.SendCalendarEventEmailAsync(It.IsAny<CalendarEventEmailMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var limiter = new Mock<IEmailRateLimiter>();
        limiter.Setup(l => l.WaitAsync(It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);

        var handler = new CalendarEventInviteEmailOutboxHandler(email.Object, limiter.Object);

        await handler.HandleAsync(Payload(), CancellationToken.None);

        limiter.Verify(l => l.WaitAsync(It.IsAny<CancellationToken>()), Times.Once);
        email.Verify(e => e.SendCalendarEventEmailAsync(It.IsAny<CalendarEventEmailMessage>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task The_send_happens_after_the_wait_not_before()
    {
        var order = new List<string>();
        var email = new Mock<IEmailService>();
        email.Setup(e => e.SendCalendarEventEmailAsync(It.IsAny<CalendarEventEmailMessage>(), It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("send"))
            .Returns(Task.CompletedTask);

        var limiter = new Mock<IEmailRateLimiter>();
        limiter.Setup(l => l.WaitAsync(It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("wait"))
            .Returns(ValueTask.CompletedTask);

        var handler = new CalendarEventInviteEmailOutboxHandler(email.Object, limiter.Object);

        await handler.HandleAsync(Payload(), CancellationToken.None);

        order.Should().Equal("wait", "send");
    }

    [Fact]
    public async Task A_cancelled_wait_never_sends_the_email()
    {
        var email = new Mock<IEmailService>();
        email.Setup(e => e.SendCalendarEventEmailAsync(It.IsAny<CalendarEventEmailMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var limiter = new Mock<IEmailRateLimiter>();
        limiter.Setup(l => l.WaitAsync(It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromCanceled(cts.Token));

        var handler = new CalendarEventInviteEmailOutboxHandler(email.Object, limiter.Object);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => handler.HandleAsync(Payload(), cts.Token));

        email.Verify(e => e.SendCalendarEventEmailAsync(It.IsAny<CalendarEventEmailMessage>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
