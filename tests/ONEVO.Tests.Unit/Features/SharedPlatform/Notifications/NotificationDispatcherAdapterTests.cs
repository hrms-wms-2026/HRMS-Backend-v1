using System.Text.Json;
using FluentAssertions;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.SharedPlatform.Notifications.Catalog;
using ONEVO.Application.Features.SharedPlatform.Notifications.Delivery;
using ONEVO.Application.Features.WorkManagement.Common.OutboxHandlers;
using ONEVO.Domain.Features.SharedPlatform.Notifications.Entities;
using ONEVO.Infrastructure.Services.SharedPlatform.Notifications;
using Xunit;

namespace ONEVO.Tests.Unit.Features.SharedPlatform.Notifications;

/// <summary>The legacy dispatcher is the single cut-over point for direct producers: owned events go to the
/// orchestrator (never also to the legacy insert), everything else keeps the old behaviour.</summary>
public class NotificationDispatcherAdapterTests
{
    private readonly Mock<INotificationRepository> _repo = new();
    private readonly Mock<INotificationOrchestrator> _orchestrator = new();
    private readonly List<NotificationDispatchRequest> _dispatched = [];
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _user = Guid.NewGuid();

    public NotificationDispatcherAdapterTests()
    {
        _orchestrator.Setup(o => o.EnqueueAsync(It.IsAny<NotificationDispatchRequest>(), It.IsAny<CancellationToken>()))
            .Callback<NotificationDispatchRequest, CancellationToken>((r, _) => _dispatched.Add(r)).Returns(Task.CompletedTask);
        _repo.Setup(r => r.GetTemplateByCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string code, CancellationToken _) => new NotificationTemplate { Code = code, InAppTitleTemplate = "t", InAppBodyTemplate = "b" });
    }

    private NotificationDispatcher Build() => new(_repo.Object, _orchestrator.Object);

    private static readonly string OwnedEvent = NotificationEventCatalog.FirstSliceEventKey;
    private const string ProducerlessEvent = "work_task_creation_request_created";

    [Fact]
    public async Task OwnedEvent_GoesToTheOrchestratorOnly_PreservingRecipientAndNavigation()
    {
        var related = Guid.NewGuid();
        var eventId = Guid.NewGuid();

        await Build().SendTemplatedEventAsync(eventId, _tenant, _user, OwnedEvent, new Dictionary<string, string> { ["actorName"] = "Bala" },
            "work_approval_request", related);

        var request = _dispatched.Should().ContainSingle().Subject;
        request.Should().BeEquivalentTo(new
        {
            EventId = eventId, EventKey = OwnedEvent, TenantId = _tenant, RecipientUserId = _user,
            RelatedEntityType = "work_approval_request", RelatedEntityId = (Guid?)related
        });
        request.Variables["actorName"].Should().Be("Bala");
        _repo.Verify(r => r.AddAsync(It.IsAny<Notification>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task OwnedEvent_WithoutAnEventId_GetsANewOneInsideTheCallersTransaction()
    {
        await Build().SendTemplatedAsync(_tenant, _user, OwnedEvent, new Dictionary<string, string>());

        _dispatched.Single().EventId.Should().NotBeEmpty();
    }

    [Fact]
    public async Task NotOwnedEvent_KeepsTheLegacyInsert()
    {
        await Build().SendTemplatedAsync(_tenant, _user, ProducerlessEvent, new Dictionary<string, string>());

        _dispatched.Should().BeEmpty();
        _repo.Verify(r => r.AddAsync(It.Is<Notification>(n => n.TemplateCode == ProducerlessEvent), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task WorkNotificationPayload_EventIdSurvivesRetries()
    {
        var eventId = Guid.NewGuid();
        var json = JsonSerializer.Serialize(new WorkNotificationPayload(_tenant, _user, OwnedEvent, new Dictionary<string, string>(), null, null, eventId));
        var handler = new WorkNotificationOutboxHandler(Build());

        await handler.HandleAsync(json, default);
        await handler.HandleAsync(json, default);

        _dispatched.Select(d => d.EventId).Should().Equal(eventId, eventId);
    }

    [Fact]
    public async Task InFlightPayloadWithoutEventId_GetsADeterministicIdSoARetryCannotDuplicate()
    {
        var json = JsonSerializer.Serialize(new { TenantId = _tenant, RecipientUserId = _user, TemplateCode = OwnedEvent,
            Placeholders = new Dictionary<string, string> { ["actorName"] = "Bala" }, RelatedEntityType = (string?)null, RelatedEntityId = (Guid?)null });
        var handler = new WorkNotificationOutboxHandler(Build());

        await handler.HandleAsync(json, default);
        await handler.HandleAsync(json, default);

        _dispatched.Should().HaveCount(2);
        _dispatched[0].EventId.Should().NotBeEmpty().And.Be(_dispatched[1].EventId);
    }
}
