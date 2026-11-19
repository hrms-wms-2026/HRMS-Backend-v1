using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ONEVO.Application.Features.Monitoring.TrayActivation.ServiceInterfaces;
using ONEVO.Application.Features.SharedPlatform.Notifications.Delivery;
using ONEVO.Application.Features.SharedPlatform.Notifications.Tray;
using Xunit;

namespace ONEVO.Tests.Unit.Features.SharedPlatform.Notifications;

public class NotificationTrayPushOutboxHandlerTests
{
    private static string Payload(Guid tenant, Guid user) => JsonSerializer.Serialize(new NotificationTrayPushPayload(tenant, user));

    [Fact]
    public async Task SendsTheSameNotificationsPendingSignalAsLegacyTrayAlerts()
    {
        var push = new Mock<ITrayPushNotifier>();
        var tenant = Guid.NewGuid();
        var user = Guid.NewGuid();

        await new NotificationTrayPushOutboxHandler(NullLogger<NotificationTrayPushOutboxHandler>.Instance, push.Object)
            .HandleAsync(Payload(tenant, user), default);

        push.Verify(p => p.NotifyUserAsync(tenant, user, TrayPushMessages.NotificationsPending, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PushFailure_DoesNotFailTheMessage_TheFallbackPollStillDelivers()
    {
        var push = new Mock<ITrayPushNotifier>();
        push.Setup(p => p.NotifyUserAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("hub down"));

        var act = () => new NotificationTrayPushOutboxHandler(NullLogger<NotificationTrayPushOutboxHandler>.Instance, push.Object)
            .HandleAsync(Payload(Guid.NewGuid(), Guid.NewGuid()), default);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task NoPushChannelRegistered_IsANoOp()
    {
        var act = () => new NotificationTrayPushOutboxHandler(NullLogger<NotificationTrayPushOutboxHandler>.Instance)
            .HandleAsync(Payload(Guid.NewGuid(), Guid.NewGuid()), default);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public void TrayDeliveryDto_UsesTheCamelCaseNamesTheTrayClientReads()
    {
        var dto = new TrayDeliveryDto(Guid.NewGuid(), "work_approval_requested", "t", "m", "open_related", "work_approval_request", Guid.NewGuid(), DateTimeOffset.UtcNow);

        // MVC default (JsonSerializerDefaults.Web); the tray mirrors these names in PendingTrayDeliveryPayload.
        var json = JsonSerializer.Serialize(new { deliveries = new[] { dto } }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var item = JsonDocument.Parse(json).RootElement.GetProperty("deliveries")[0];

        item.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(
            "deliveryId", "eventKey", "title", "message", "actionKey", "relatedEntityType", "relatedEntityId", "expiresAt");
    }
}
