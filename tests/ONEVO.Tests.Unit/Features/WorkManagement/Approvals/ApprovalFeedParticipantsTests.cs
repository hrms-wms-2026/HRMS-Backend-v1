using FluentAssertions;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Hierarchy;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Approvals;

public class ApprovalFeedParticipantsTests
{
    private static readonly Guid Requester = Guid.NewGuid(), Approver = Guid.NewGuid(), Stranger = Guid.NewGuid();

    private static WorkApprovalRequest Req(string status = WorkApprovalRequestStatuses.Pending, Guid? decidedBy = null) => new()
    {
        Id = Guid.NewGuid(), RequestedByEmployeeId = Requester, ApproverEmployeeId = Approver,
        ApproverSource = WorkApprovalSources.Hr, Status = status, DecidedByEmployeeId = decidedBy
    };

    private static ProjectModuleTree Empty() => new([]);

    [Fact]
    public void Requester_is_sender_not_receiver()
    {
        var r = Req();
        ApprovalFeedParticipants.IsSender(r, Requester).Should().BeTrue();
        ApprovalFeedParticipants.IsReceiver(Empty(), r, Requester).Should().BeFalse();
    }

    [Fact]
    public void Resolved_approver_is_receiver_even_after_decision()
        => ApprovalFeedParticipants.IsReceiver(Empty(), Req(WorkApprovalRequestStatuses.Approved, Approver), Approver).Should().BeTrue();

    [Fact]
    public void Decider_is_receiver()
    {
        var decider = Guid.NewGuid();
        ApprovalFeedParticipants.IsReceiver(Empty(), Req(WorkApprovalRequestStatuses.Rejected, decider), decider).Should().BeTrue();
    }

    [Fact]
    public void Stranger_is_neither()
    {
        var r = Req();
        ApprovalFeedParticipants.IsSender(r, Stranger).Should().BeFalse();
        ApprovalFeedParticipants.IsReceiver(Empty(), r, Stranger).Should().BeFalse();
        ApprovalFeedParticipants.CanSee(Empty(), r, Stranger).Should().BeFalse();
    }

    [Fact]
    public void Ancestor_module_owner_is_receiver_of_hierarchy_request()
    {
        var lead = Guid.NewGuid();
        var root = Guid.NewGuid();
        var p = Guid.NewGuid();
        var tree = new ProjectModuleTree(new[]
        {
            new Objective { Id = root, OwnerId = lead, IsDefault = true },
            new Objective { Id = p, ParentObjectiveId = root, OwnerId = Approver },
        });
        var r = new WorkApprovalRequest
        {
            PositionObjectiveId = p, ApproverSource = WorkApprovalSources.Hierarchy,
            ApproverEmployeeId = Approver, RequestedByEmployeeId = Requester, Status = WorkApprovalRequestStatuses.Pending
        };

        ApprovalFeedParticipants.IsReceiver(tree, r, lead).Should().BeTrue();
    }

    [Fact]
    public void Notify_targets_are_distinct_participants()
    {
        ApprovalFeedParticipants.NotifyTargets(Req()).Should().BeEquivalentTo(new[] { Requester, Approver });
        ApprovalFeedParticipants.NotifyTargets(Req(WorkApprovalRequestStatuses.Approved, Approver)).Should().HaveCount(2);
    }
}
