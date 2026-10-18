using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.SharedPlatform.Notifications.Catalog;
using ONEVO.Application.Features.SharedPlatform.Notifications.Delivery;
using ONEVO.Domain.Features.SharedPlatform.Notifications.Entities;
using ONEVO.Infrastructure.Services.SharedPlatform.Notifications;
using Xunit;

namespace ONEVO.Tests.Unit.Features.SharedPlatform.Notifications;

public class NotificationOrchestratorTests
{
    private const string Event = NotificationEventCatalog.FirstSliceEventKey;
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 11, 0, 0, TimeSpan.Zero);

    private readonly NotificationTestFakes.FakeTemplateStore _store = new();
    private readonly NotificationTestFakes.FakeRuleRepository _rules = new();
    private readonly NotificationTestFakes.FakeDeliveryRepository _deliveries = new();
    private readonly Mock<IOutboxWriter> _outbox = new();
    private readonly Mock<IDateTimeProvider> _clock = new();
    private readonly List<NotificationDeliveryPayload> _enqueued = [];
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _recipient = Guid.NewGuid();
    private readonly NotificationTemplate _template;

    public NotificationOrchestratorTests()
    {
        _clock.SetupGet(c => c.UtcNow).Returns(Now);
        _outbox.Setup(o => o.EnqueueAsync(OutboxMessageTypes.NotificationDelivery, It.IsAny<NotificationDeliveryPayload>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback<string, NotificationDeliveryPayload, Guid?, CancellationToken>((_, p, _, _) => _enqueued.Add(p))
            .Returns(Task.CompletedTask);
        _template = Publish(new NotificationTemplate { Id = Guid.NewGuid(), Code = Event, EventKey = Event, Name = "Default" },
            "Approval needed", "{{actorName}} asked about \"{{targetTitle}}\"");
    }

    private NotificationTemplate Publish(NotificationTemplate template, string title, string body)
    {
        var version = NotificationTemplateVersion.CreateDraft(template.Id, _store.Versions.Count(v => v.TemplateId == template.Id) + 1,
            new NotificationTemplateContent(title, body, null, null, null, null, null, null, NotificationActionKeys.OpenRelated), null, Now);
        version.Publish(null, Now);
        template.Activate(version, Now);
        if (!_store.Templates.Contains(template))
            _store.Templates.Add(template);
        _store.Versions.Add(version);
        return template;
    }

    private NotificationOrchestrator Build() => new(
        _rules, _store, _deliveries, new NotificationTemplateRenderer(), _outbox.Object, _clock.Object,
        NullLogger<NotificationOrchestrator>.Instance);

    private NotificationDispatchRequest Request(Guid? eventId = null) => new(
        eventId ?? Guid.NewGuid(), Event, _tenant, _recipient,
        new Dictionary<string, string> { ["actorName"] = "Bala", ["actionLabel"] = "edited", ["targetTitle"] = "Q4 plan", ["decision"] = "" },
        "work_approval_request", Guid.NewGuid());

    private void Route(NotificationChannels channels, Guid? templateId = null)
        => _rules.Rules.Add(new TenantNotificationRule { Id = Guid.NewGuid(), TenantId = _tenant, EventKey = Event, EnabledChannels = channels, TemplateId = templateId });

    [Fact]
    public async Task Defaults_CreateOnlyAnInAppDelivery()
    {
        await Build().EnqueueAsync(Request());

        _deliveries.Deliveries.Should().ContainSingle(d => d.Channel == NotificationChannels.InApp && d.Status == NotificationDeliveryStatus.Pending);
        _enqueued.Should().ContainSingle(p => p.DeliveryId == _deliveries.Deliveries[0].Id && p.TenantId == _tenant);
    }

    [Fact]
    public async Task InAppOff_EmailOn_StillQueuesEmail()
    {
        Route(NotificationChannels.Email);

        await Build().EnqueueAsync(Request());

        _deliveries.Deliveries.Should().ContainSingle();
        var email = _deliveries.Deliveries[0];
        email.Channel.Should().Be(NotificationChannels.Email);
        email.EmailSubject.Should().Be("Approval needed");
        email.EmailHtml.Should().Contain("Bala asked about");
        _enqueued.Should().ContainSingle(p => p.DeliveryId == email.Id);
    }

    [Fact]
    public async Task Tray_IsHeldForThePollingClient_WithAnExpiry()
    {
        Route(NotificationChannels.Tray);

        await Build().EnqueueAsync(Request());

        var tray = _deliveries.Deliveries.Should().ContainSingle().Subject;
        tray.Channel.Should().Be(NotificationChannels.Tray);
        tray.ExpiresAt.Should().Be(Now.Add(NotificationOrchestrator.TrayExpiry));
        _enqueued.Should().BeEmpty("the tray collects its own deliveries");
    }

    [Fact]
    public async Task RepeatedSameEvent_YieldsOneRecordPerChannel()
    {
        Route(NotificationChannels.InApp | NotificationChannels.Email | NotificationChannels.Tray);
        var eventId = Guid.NewGuid();

        await Build().EnqueueAsync(Request(eventId));
        await Build().EnqueueAsync(Request(eventId));

        _deliveries.Deliveries.Should().HaveCount(3);
        _deliveries.Deliveries.Select(d => d.Channel).Should().OnlyHaveUniqueItems();
        _enqueued.Should().HaveCount(2);
    }

    [Fact]
    public async Task RenderedContent_IsCapturedAtEnqueue_SoLaterPublishesDoNotChangeIt()
    {
        await Build().EnqueueAsync(Request());
        var versionAtEnqueue = _template.ActiveVersionId;

        Publish(_template, "Changed wording", "Totally different");

        var delivery = _deliveries.Deliveries.Single();
        delivery.Title.Should().Be("Approval needed");
        delivery.Body.Should().Be("Bala asked about \"Q4 plan\"");
        delivery.TemplateVersionId.Should().Be(versionAtEnqueue);
    }

    [Fact]
    public async Task TenantSelectedTemplate_IsUsed()
    {
        var friendly = Publish(new NotificationTemplate { Id = Guid.NewGuid(), Code = Event + ".friendly", EventKey = Event, Name = "Friendly" },
            "Hey, a quick approval", "{{actorName}} needs you");
        Route(NotificationChannels.InApp, friendly.Id);

        await Build().EnqueueAsync(Request());

        _deliveries.Deliveries.Single().Title.Should().Be("Hey, a quick approval");
    }

    [Fact]
    public async Task RenderFailure_RecordsFailedDeliveries_WithoutThrowingIntoTheBusinessTransaction()
    {
        Route(NotificationChannels.InApp | NotificationChannels.Email);
        var request = Request() with { Variables = new Dictionary<string, string>() }; // required actorName missing

        await Build().EnqueueAsync(request);

        _deliveries.Deliveries.Should().HaveCount(2).And.OnlyContain(d =>
            d.Status == NotificationDeliveryStatus.Failed && d.StatusReason == "render_failed");
        _enqueued.Should().BeEmpty();
    }

    [Fact]
    public async Task UnregisteredEvent_Throws()
    {
        var act = () => Build().EnqueueAsync(Request() with { EventKey = "not_registered" });

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
