using System.Text.Json;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.OutboxHandlers;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public class WorkNotificationOutboxHandlerTests
{
    [Fact]
    public async Task HandleAsync_ValidPayload_CallsSendTemplatedAsyncOnceWithDeserializedValues()
    {
        var notifications = new Mock<INotificationDispatcher>();
        var handler = new WorkNotificationOutboxHandler(notifications.Object);
        var tenantId = Guid.NewGuid();
        var recipientUserId = Guid.NewGuid();
        var relatedId = Guid.NewGuid();
        var placeholders = new Dictionary<string, string> { ["inviterName"] = "Ada", ["projectName"] = "Website" };
        var payload = new WorkNotificationPayload(
            tenantId, recipientUserId, "work_project_member_invited", placeholders, "project_member_invitation", relatedId);
        var json = JsonSerializer.Serialize(payload);

        await handler.HandleAsync(json, CancellationToken.None);

        Assert.Equal(OutboxMessageTypes.WorkNotification, handler.Type);
        notifications.Verify(x => x.SendTemplatedAsync(
            tenantId, recipientUserId, "work_project_member_invited",
            It.Is<IReadOnlyDictionary<string, string>>(d =>
                d["inviterName"] == "Ada" && d["projectName"] == "Website"),
            "project_member_invitation", relatedId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_NullPayload_ThrowsInvalidOperationException()
    {
        var handler = new WorkNotificationOutboxHandler(new Mock<INotificationDispatcher>().Object);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync("null", CancellationToken.None));

        Assert.Equal("work_notification payload is empty.", ex.Message);
    }

    [Fact]
    public async Task HandleAsync_MalformedPayload_Throws()
    {
        var handler = new WorkNotificationOutboxHandler(new Mock<INotificationDispatcher>().Object);

        await Assert.ThrowsAsync<JsonException>(() => handler.HandleAsync("{not-json", CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_CalledTwice_DispatchesTwiceWithoutThrowing()
    {
        var notifications = new Mock<INotificationDispatcher>();
        var handler = new WorkNotificationOutboxHandler(notifications.Object);
        var json = JsonSerializer.Serialize(new WorkNotificationPayload(
            Guid.NewGuid(), Guid.NewGuid(), "work_project_member_invited",
            new Dictionary<string, string>(), null, null));

        await handler.HandleAsync(json, CancellationToken.None);
        await handler.HandleAsync(json, CancellationToken.None);

        notifications.Verify(x => x.SendTemplatedAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(),
            It.IsAny<IReadOnlyDictionary<string, string>>(),
            It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }
}
