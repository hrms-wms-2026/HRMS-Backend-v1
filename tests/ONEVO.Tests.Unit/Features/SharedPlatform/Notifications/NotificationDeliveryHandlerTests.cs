using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.SharedPlatform.Notifications.Delivery;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.SharedPlatform.Notifications.Entities;
using ONEVO.Infrastructure.Services.SharedPlatform.Notifications;
using Xunit;

namespace ONEVO.Tests.Unit.Features.SharedPlatform.Notifications;

public class NotificationDeliveryHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

    private readonly NotificationTestFakes.FakeDeliveryRepository _deliveries = new();
    private readonly Mock<INotificationRepository> _inbox = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<ITransactionalEmailSender> _email = new();
    private readonly Mock<IDateTimeProvider> _clock = new();
    private readonly List<Notification> _inboxRows = [];
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _recipient = Guid.NewGuid();

    public NotificationDeliveryHandlerTests()
    {
        _clock.SetupGet(c => c.UtcNow).Returns(Now);
        _inbox.Setup(i => i.AddAsync(It.IsAny<Notification>(), It.IsAny<CancellationToken>()))
            .Callback<Notification, CancellationToken>((n, _) => _inboxRows.Add(n)).Returns(Task.CompletedTask);
    }

    private NotificationDeliveryOutboxHandler Build() => new(
        _deliveries,
        [
            new NotificationInAppDeliveryHandler(_inbox.Object, _clock.Object),
            new NotificationEmailDeliveryHandler(_users.Object, _email.Object, _clock.Object)
        ],
        NullLogger<NotificationDeliveryOutboxHandler>.Instance);

    private NotificationDelivery Delivery(NotificationChannels channel, Guid? eventId = null)
    {
        var d = new NotificationDelivery
        {
            Id = Guid.NewGuid(), TenantId = _tenant, EventId = eventId ?? Guid.NewGuid(), EventKey = "work_approval_requested",
            RecipientUserId = _recipient, Channel = channel, Title = "Approval needed", Body = "Bala asked",
            EmailSubject = "Approval needed", EmailHtml = "<p>Bala asked</p>", EmailText = "Bala asked",
            RelatedEntityType = "work_approval_request", RelatedEntityId = Guid.NewGuid(), CreatedAt = Now
        };
        _deliveries.Deliveries.Add(d);
        return d;
    }

    private string Payload(NotificationDelivery d) => JsonSerializer.Serialize(new NotificationDeliveryPayload(d.Id, d.TenantId));

    private void UserHasEmail(string? email)
        => _users.Setup(u => u.GetByIdAsync(_recipient, It.IsAny<CancellationToken>()))
            .ReturnsAsync(email is null ? null : new User { Id = _recipient, Email = email });

    [Fact]
    public async Task InApp_CreatesTheInboxRow_AndMarksItAvailable()
    {
        var d = Delivery(NotificationChannels.InApp);

        await Build().HandleAsync(Payload(d), default);

        var row = _inboxRows.Should().ContainSingle().Subject;
        row.Title.Should().Be("Approval needed");
        row.TemplateCode.Should().Be("work_approval_requested");
        row.RelatedEntityId.Should().Be(d.RelatedEntityId);
        d.NotificationId.Should().Be(row.Id);
        d.Status.Should().Be(NotificationDeliveryStatus.AvailableInInbox);
    }

    [Fact]
    public async Task InApp_Redelivered_DoesNotCreateASecondInboxRow()
    {
        var d = Delivery(NotificationChannels.InApp);

        await Build().HandleAsync(Payload(d), default);
        await Build().HandleAsync(Payload(d), default);

        _inboxRows.Should().ContainSingle();
    }

    [Fact]
    public async Task Email_CarriesAnIdempotencyKey_FromTheDelivery_SoARetryAfterAcceptanceIsNotSentTwice()
    {
        // A crash after the provider accepted the email retries the delivery; Resend drops the repeat when the key matches.
        UserHasEmail("priya@tenant.test");
        _email.Setup(e => e.SendAsync(It.IsAny<TransactionalEmailRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TransactionalEmailResult.Sent("resend", "m-1", Now));
        var d = Delivery(NotificationChannels.Email);

        await Build().HandleAsync(Payload(d), default);

        _email.Verify(e => e.SendAsync(It.Is<TransactionalEmailRequest>(r =>
            r.IdempotencyKey == $"notification-delivery/{d.Id}"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Email_Accepted_IsProviderAccepted_NotRead()
    {
        UserHasEmail("priya@tenant.test");
        _email.Setup(e => e.SendAsync(It.IsAny<TransactionalEmailRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TransactionalEmailResult.Sent("resend", "m-1", Now));
        var d = Delivery(NotificationChannels.Email);

        await Build().HandleAsync(Payload(d), default);

        d.Status.Should().Be(NotificationDeliveryStatus.ProviderAccepted);
        _email.Verify(e => e.SendAsync(It.Is<TransactionalEmailRequest>(r =>
            r.RecipientEmail == "priya@tenant.test" && r.TenantId == _tenant && r.HtmlBody == "<p>Bala asked</p>"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Email_RecipientWithoutAddress_IsSkippedWithAReason()
    {
        UserHasEmail(null);
        var d = Delivery(NotificationChannels.Email);

        await Build().HandleAsync(Payload(d), default);

        d.Status.Should().Be(NotificationDeliveryStatus.Skipped);
        d.StatusReason.Should().Be("no_email_address");
        _email.Verify(e => e.SendAsync(It.IsAny<TransactionalEmailRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Email_Failure_RetriesThatChannelOnly()
    {
        UserHasEmail("priya@tenant.test");
        _email.Setup(e => e.SendAsync(It.IsAny<TransactionalEmailRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TransactionalEmailResult.Failed("resend", "provider unavailable"));
        var eventId = Guid.NewGuid();
        var inApp = Delivery(NotificationChannels.InApp, eventId);
        var email = Delivery(NotificationChannels.Email, eventId);
        await Build().HandleAsync(Payload(inApp), default);

        var act = () => Build().HandleAsync(Payload(email), default);

        await act.Should().ThrowAsync<NotificationDeliveryRetryException>();
        email.Status.Should().Be(NotificationDeliveryStatus.Pending);
        email.AttemptCount.Should().Be(1);
        email.StatusReason.Should().Be("provider unavailable");
        inApp.Status.Should().Be(NotificationDeliveryStatus.AvailableInInbox);
        _inboxRows.Should().ContainSingle("the successful in-app delivery is not replayed");
    }

    [Fact]
    public async Task Email_LastAttemptFailure_IsMarkedFailed_InsteadOfRetryingForever()
    {
        UserHasEmail("priya@tenant.test");
        _email.Setup(e => e.SendAsync(It.IsAny<TransactionalEmailRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TransactionalEmailResult.Failed("resend", "rejected"));
        var d = Delivery(NotificationChannels.Email);
        d.AttemptCount = NotificationEmailDeliveryHandler.MaxAttempts - 1;

        await Build().HandleAsync(Payload(d), default);

        d.Status.Should().Be(NotificationDeliveryStatus.Failed);
    }

    [Fact]
    public async Task CompletedDelivery_IsANoOp()
    {
        var d = Delivery(NotificationChannels.Email);
        d.Status = NotificationDeliveryStatus.ProviderAccepted;

        await Build().HandleAsync(Payload(d), default);

        _email.Verify(e => e.SendAsync(It.IsAny<TransactionalEmailRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PayloadTenantMismatch_DoesNothing()
    {
        var d = Delivery(NotificationChannels.InApp);

        await Build().HandleAsync(JsonSerializer.Serialize(new NotificationDeliveryPayload(d.Id, Guid.NewGuid())), default);

        _inboxRows.Should().BeEmpty();
        d.Status.Should().Be(NotificationDeliveryStatus.Pending);
    }
}
