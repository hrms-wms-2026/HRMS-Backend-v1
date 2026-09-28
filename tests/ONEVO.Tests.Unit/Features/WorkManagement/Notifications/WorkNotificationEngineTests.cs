using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
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
    private readonly List<WorkNotificationLog> _written = new();
    private readonly List<WorkNotificationPayload> _enqueued = new();

    public WorkNotificationEngineTests()
    {
        _logs.Setup(x => x.AddAsync(It.IsAny<WorkNotificationLog>(), It.IsAny<CancellationToken>()))
            .Callback<WorkNotificationLog, CancellationToken>((l, _) => _written.Add(l)).Returns(Task.CompletedTask);
        _outbox.Setup(x => x.EnqueueAsync(It.IsAny<string>(), It.IsAny<WorkNotificationPayload>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback<string, WorkNotificationPayload, Guid?, CancellationToken>((_, p, _, _) => _enqueued.Add(p)).Returns(Task.CompletedTask);
        _identity.Setup(x => x.ResolveDisplayNamesByEmployeeIdAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, string> { [Actor] = "Bala" });
        _membership.Setup(x => x.GetActiveAssigneeAsync(TenantId, Owner, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = Owner, UserId = OwnerUser });
    }

    private WorkNotificationEngine Build() => new(_logs.Object, _outbox.Object, _identity.Object, _membership.Object);

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

        _enqueued[0].TemplateCode.Should().Be("work_approval_requested");
        _enqueued[0].RelatedEntityType.Should().Be("work_approval_request");
        _enqueued[0].RelatedEntityId.Should().Be(e.ApprovalRequestId);
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
    public async Task NoRecipientsLeft_DoesNothing()
    {
        await Build().NotifyAsync(Event(WorkNotificationKinds.Direct, Actor));

        _written.Should().BeEmpty();
        _identity.Verify(x => x.ResolveDisplayNamesByEmployeeIdAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
