using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Inbox.OutboxHandlers;
using ONEVO.Application.Features.SharedPlatform.Notifications.Delivery;
using ONEVO.Application.Features.WorkManagement.Common.OutboxHandlers;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Notifications.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Notifications.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Domain.Features.CoreHr.Entities; // Employee lives here despite its CoreHr/Employee/Entities folder
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.Notifications.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Notifications;

public class WorkNotificationEngineTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly Guid OwnerUser = Guid.NewGuid();
    private static readonly Guid Inactive = Guid.NewGuid();

    private readonly Mock<IWorkNotificationLogRepository> _logs = new();
    private readonly Mock<IOutboxWriter> _outbox = new();
    private readonly Mock<ICallerIdentityResolver> _identity = new();
    private readonly Mock<IMilestoneMembershipCoordinator> _membership = new();
    private readonly Mock<INotificationOrchestrator> _orchestrator = new();
    private readonly List<NotificationDispatchRequest> _dispatched = new();
    private readonly List<WorkNotificationLog> _written = new();
    private readonly List<WorkNotificationPayload> _legacyEnqueued = new();
    /// <summary>What reached the orchestrator, in the legacy payload's shape (all engine events are cut over).</summary>
    private List<WorkNotificationPayload> _enqueued => _dispatched
        .Select(r => new WorkNotificationPayload(r.TenantId, r.RecipientUserId, r.EventKey, new Dictionary<string, string>(r.Variables), r.RelatedEntityType, r.RelatedEntityId, r.EventId))
        .ToList();
    private readonly List<InboxChangedPayload> _inboxChanges = new();

    public WorkNotificationEngineTests()
    {
        _logs.Setup(x => x.AddAsync(It.IsAny<WorkNotificationLog>(), It.IsAny<CancellationToken>()))
            .Callback<WorkNotificationLog, CancellationToken>((l, _) => _written.Add(l)).Returns(Task.CompletedTask);
        _outbox.Setup(x => x.EnqueueAsync(It.IsAny<string>(), It.IsAny<WorkNotificationPayload>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback<string, WorkNotificationPayload, Guid?, CancellationToken>((_, p, _, _) => _legacyEnqueued.Add(p)).Returns(Task.CompletedTask);
        _outbox.Setup(x => x.EnqueueAsync(OutboxMessageTypes.InboxChanged, It.IsAny<InboxChangedPayload>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback<string, InboxChangedPayload, Guid?, CancellationToken>((_, p, _, _) => _inboxChanges.Add(p)).Returns(Task.CompletedTask);
        _identity.Setup(x => x.ResolveDisplayNamesByEmployeeIdAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, string> { [Actor] = "Bala" });
        _membership.Setup(x => x.GetActiveAssigneeAsync(TenantId, Owner, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = Owner, UserId = OwnerUser });
        _orchestrator.Setup(x => x.EnqueueAsync(It.IsAny<NotificationDispatchRequest>(), It.IsAny<CancellationToken>()))
            .Callback<NotificationDispatchRequest, CancellationToken>((r, _) => _dispatched.Add(r)).Returns(Task.CompletedTask);
    }

    private WorkNotificationEngine Build() => new(_logs.Object, _outbox.Object, _identity.Object, _membership.Object, _orchestrator.Object);

    private static WorkNotificationEvent Event(string kind, params Guid[] recipients) => new(
        TenantId, ProjectId, Actor, kind, WorkActionTypes.TaskEdit, WorkTargetTypes.Task,
        Guid.NewGuid(), "Audit events", kind == WorkNotificationKinds.Direct ? null : Guid.NewGuid(), recipients);

    [Fact]
    public async Task Direct_WritesLogAndEnqueuesActivityTemplate_PerActiveRecipient()
    {
        await Build().NotifyAsync(Event(WorkNotificationKinds.Direct, Owner));

        _written.Should().ContainSingle(l => l.RecipientEmployeeId == Owner && l.Kind == WorkNotificationKinds.Direct && l.ProjectId == ProjectId);
        _enqueued.Should().ContainSingle();
        _enqueued[0].RecipientUserId.Should().Be(OwnerUser);
        _enqueued[0].TemplateCode.Should().Be("work_activity_recorded");
        _enqueued[0].Placeholders["actorName"].Should().Be("Bala");
        _enqueued[0].Placeholders["actionLabel"].Should().Be("edited the task");
        _enqueued[0].RelatedEntityType.Should().Be(WorkTargetTypes.Task);
    }

    [Fact]
    public async Task Alert_FromTheSystem_UsesTheMonitorTemplate_AndLinksToTheTarget()
    {
        var targetId = Guid.NewGuid();
        await Build().NotifyAsync(new WorkNotificationEvent(TenantId, ProjectId, Guid.Empty, WorkNotificationKinds.Alert,
            "monitor.module_over_capacity", "module", targetId, "Payments", null, [Owner]));

        _enqueued.Should().ContainSingle();
        _enqueued[0].TemplateCode.Should().Be("work_monitor_alert");
        _enqueued[0].Placeholders["actionLabel"].Should().Be("Module over capacity");
        _enqueued[0].RelatedEntityType.Should().Be("module");
        _enqueued[0].RelatedEntityId.Should().Be(targetId);
        _identity.Verify(x => x.ResolveDisplayNamesByEmployeeIdAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ActorAndDuplicatesAndInactiveRecipients_AreSkipped()
    {
        await Build().NotifyAsync(Event(WorkNotificationKinds.Direct, Actor, Owner, Owner, Inactive));

        _written.Select(l => l.RecipientEmployeeId).Should().Equal(Owner);
        _enqueued.Should().HaveCount(1);
    }

    [Fact]
    public async Task Requested_UsesRequestTemplateAndLinksTheApproval()
    {
        var e = Event(WorkNotificationKinds.Requested, Owner);
        await Build().NotifyAsync(e);

        // Orchestrator-owned: routed per tenant settings, no legacy in-app payload.
        _legacyEnqueued.Should().BeEmpty();
        var request = _dispatched.Should().ContainSingle().Subject;
        request.EventKey.Should().Be("work_approval_requested");
        request.TenantId.Should().Be(TenantId);
        request.RecipientUserId.Should().Be(OwnerUser);
        request.EventId.Should().NotBeEmpty();
        request.Variables["actorName"].Should().Be("Bala");
        request.RelatedEntityType.Should().Be("work_approval_request");
        request.RelatedEntityId.Should().Be(e.ApprovalRequestId);
        _written[0].ApprovalRequestId.Should().Be(e.ApprovalRequestId);
    }

    [Theory]
    [InlineData(WorkNotificationKinds.Approved, "approved")]
    [InlineData(WorkNotificationKinds.Rejected, "rejected")]
    [InlineData(WorkNotificationKinds.Cancelled, "cancelled")]
    [InlineData(WorkNotificationKinds.Stale, "closed as outdated")]
    public async Task Decisions_UseDecidedTemplateWithDecisionWord(string kind, string decision)
    {
        await Build().NotifyAsync(Event(kind, Owner));

        _enqueued[0].TemplateCode.Should().Be("work_approval_decided");
        _enqueued[0].Placeholders["decision"].Should().Be(decision);
    }

    [Fact]
    public async Task Requested_ToSeveralRecipients_SharesOneStableEventId()
    {
        var second = Guid.NewGuid();
        _membership.Setup(x => x.GetActiveAssigneeAsync(TenantId, second, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = second, UserId = Guid.NewGuid() });

        await Build().NotifyAsync(Event(WorkNotificationKinds.Requested, Owner, second));

        _dispatched.Should().HaveCount(2);
        _dispatched.Select(d => d.EventId).Distinct().Should().ContainSingle();
    }

    [Fact]
    public async Task NoRecipientsLeft_DoesNothing()
    {
        await Build().NotifyAsync(Event(WorkNotificationKinds.Direct, Actor));

        _written.Should().BeEmpty();
        _identity.Verify(x => x.ResolveDisplayNamesByEmployeeIdAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("sprint.start", "started the sprint")]
    [InlineData("sprint.complete", "completed the sprint")]
    [InlineData("sprint.achieve", "achieved the sprint")]
    public void Labels_cover_sprint_lifecycle(string actionType, string label)
        => WorkActionLabels.For(actionType).Should().Be(label);

    [Fact]
    public async Task An_approval_event_tells_the_actor_and_recipients_inbox_to_refresh_the_request()
    {
        var e = Event(WorkNotificationKinds.Requested, Owner, Inactive);

        await Build().NotifyAsync(e);

        var change = _inboxChanges.Should().ContainSingle().Subject;
        change.Should().BeEquivalentTo(new InboxChangedPayload(TenantId, [Actor, Owner, Inactive], "work", "approval", e.ApprovalRequestId!.Value));
    }

    [Fact]
    public async Task A_monitor_alert_notification_links_to_the_alert_inbox_item()
    {
        var alertId = Guid.NewGuid();
        await Build().NotifyAsync(new WorkNotificationEvent(TenantId, ProjectId, Guid.Empty, WorkNotificationKinds.Alert,
            "monitor.task_overdue", "task", Guid.NewGuid(), "Audit events", null, [Owner], MonitorAlertId: alertId));

        _enqueued.Should().ContainSingle();
        _enqueued[0].RelatedEntityType.Should().Be("monitor_alert");
        _enqueued[0].RelatedEntityId.Should().Be(alertId);
    }

    [Fact]
    public async Task A_monitor_alert_event_refreshes_the_alert_item()
    {
        var alertId = Guid.NewGuid();
        await Build().NotifyAsync(new WorkNotificationEvent(TenantId, ProjectId, Guid.Empty, WorkNotificationKinds.Alert,
            "monitor.task_overdue", "task", Guid.NewGuid(), "Audit events", null, [Owner], MonitorAlertId: alertId));

        _inboxChanges.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new InboxChangedPayload(TenantId, [Owner], "work", "monitor_alert", alertId));
    }

    [Fact]
    public async Task A_direct_activity_event_does_not_touch_the_inbox()
    {
        await Build().NotifyAsync(Event(WorkNotificationKinds.Direct, Owner));

        _inboxChanges.Should().BeEmpty();
    }

    [Fact]
    public async Task InboxChanged_enqueues_one_outbox_message_for_the_distinct_people()
    {
        var id = Guid.NewGuid();
        await Build().InboxChangedAsync(TenantId, [Owner, Owner, Guid.Empty, Actor], "monitor_alert", id);

        _inboxChanges.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new InboxChangedPayload(TenantId, [Owner, Actor], "work", "monitor_alert", id));
    }

    [Fact]
    public async Task InboxChanged_with_nobody_to_tell_enqueues_nothing()
    {
        await Build().InboxChangedAsync(TenantId, [Guid.Empty], "approval", Guid.NewGuid());

        _inboxChanges.Should().BeEmpty();
    }
}
