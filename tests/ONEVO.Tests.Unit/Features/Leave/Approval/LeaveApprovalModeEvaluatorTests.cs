using FluentAssertions;
using ONEVO.Application.Features.Leave.Approval.Helpers;
using ONEVO.Domain.Features.Leave.Common;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Approval;

public class LeaveApprovalModeEvaluatorTests
{
    [Fact]
    public void ApplyApproval_AnyOne_CompletesRequestAndSkipsOtherPendingApprovers()
    {
        var currentApproverId = Guid.NewGuid();
        var otherApproverId = Guid.NewGuid();

        var result = LeaveApprovalModeEvaluator.ApplyApproval(
            LeaveApprovalModes.AnyOne,
            [
                new ApprovalModeRow(currentApproverId, 1, LeaveRequestApproverStatuses.Approved),
                new ApprovalModeRow(otherApproverId, 1, LeaveRequestApproverStatuses.Pending)
            ],
            currentApproverId);

        result.RequestCompleted.Should().BeTrue();
        result.ApproversToSkip.Should().ContainSingle().Which.Should().Be(otherApproverId);
    }

    [Fact]
    public void ApplyApproval_AllMustApprove_WaitsForRemainingPendingApprover()
    {
        var currentApproverId = Guid.NewGuid();
        var otherApproverId = Guid.NewGuid();

        var result = LeaveApprovalModeEvaluator.ApplyApproval(
            LeaveApprovalModes.AllMustApprove,
            [
                new ApprovalModeRow(currentApproverId, 1, LeaveRequestApproverStatuses.Approved),
                new ApprovalModeRow(otherApproverId, 1, LeaveRequestApproverStatuses.Pending)
            ],
            currentApproverId);

        result.RequestCompleted.Should().BeFalse();
        result.NextApproverIds.Should().ContainSingle().Which.Should().Be(otherApproverId);
    }

    [Fact]
    public void ApplyApproval_InOrder_OnlyAdvancesToNextSequence()
    {
        var firstApproverId = Guid.NewGuid();
        var secondApproverId = Guid.NewGuid();

        var result = LeaveApprovalModeEvaluator.ApplyApproval(
            LeaveApprovalModes.InOrder,
            [
                new ApprovalModeRow(firstApproverId, 1, LeaveRequestApproverStatuses.Approved),
                new ApprovalModeRow(secondApproverId, 2, LeaveRequestApproverStatuses.Pending)
            ],
            firstApproverId);

        result.RequestCompleted.Should().BeFalse();
        result.NextApproverIds.Should().ContainSingle().Which.Should().Be(secondApproverId);
    }

    [Fact]
    public void IsActionable_InOrder_ReturnsFalseForLaterSequence()
    {
        var firstApproverId = Guid.NewGuid();
        var secondApproverId = Guid.NewGuid();

        var actionable = LeaveApprovalModeEvaluator.IsActionable(
            LeaveApprovalModes.InOrder,
            [
                new ApprovalModeRow(firstApproverId, 1, LeaveRequestApproverStatuses.Pending),
                new ApprovalModeRow(secondApproverId, 2, LeaveRequestApproverStatuses.Pending)
            ],
            secondApproverId);

        actionable.Should().BeFalse();
    }
}
