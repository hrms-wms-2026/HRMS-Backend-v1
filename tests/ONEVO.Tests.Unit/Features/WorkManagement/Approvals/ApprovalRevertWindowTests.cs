using FluentAssertions;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Approvals;

public class ApprovalRevertWindowTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Decider = Guid.NewGuid();
    private static readonly Guid Requester = Guid.NewGuid();

    private static WorkApprovalRequest Request(string status, Guid? decidedBy, DateTimeOffset? decidedAt) => new()
    {
        Id = Guid.NewGuid(), TenantId = TenantId, ActionType = WorkActionTypes.ModuleEdit, TargetType = WorkTargetTypes.Module,
        RequestedByEmployeeId = Requester, Status = status, DecidedByEmployeeId = decidedBy, DecidedAt = decidedAt,
    };

    [Theory]
    [InlineData(WorkApprovalRequestStatuses.Approved, true, -5, true)]   // decider, in window -> true
    [InlineData(WorkApprovalRequestStatuses.Approved, false, -5, false)] // requester (not decider) -> false
    [InlineData(WorkApprovalRequestStatuses.Approved, true, -31, false)] // decided 31 minutes ago -> false
    [InlineData(WorkApprovalRequestStatuses.Pending, true, -5, false)]   // still pending -> false
    [InlineData(WorkApprovalRequestStatuses.Rejected, true, -5, true)]   // rejected, in window -> true
    public void ComputeCanRevert_MatchesExpectedTableRow(string status, bool viewerIsDecider, int decidedMinutesAgo, bool expectedCanRevert)
    {
        var decidedAt = status == WorkApprovalRequestStatuses.Pending ? (DateTimeOffset?)null : DateTimeOffset.UtcNow.AddMinutes(decidedMinutesAgo);
        var decidedBy = status == WorkApprovalRequestStatuses.Pending ? (Guid?)null : Decider;
        var request = Request(status, decidedBy, decidedAt);
        var viewer = viewerIsDecider ? Decider : Requester;

        var (canRevert, _) = ApprovalRevertWindow.ComputeCanRevert(request, viewer);

        canRevert.Should().Be(expectedCanRevert);
    }

    [Fact]
    public void ComputeCanRevert_RevertableUntilIsThirtyMinutesAfterDecidedAt()
    {
        var decidedAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        var request = Request(WorkApprovalRequestStatuses.Approved, Decider, decidedAt);

        var (_, revertableUntil) = ApprovalRevertWindow.ComputeCanRevert(request, Decider);

        revertableUntil.Should().Be(decidedAt.AddMinutes(ApprovalRevertWindow.Minutes));
    }

    [Fact]
    public void ComputeCanRevert_PendingRequest_RevertableUntilIsNull()
    {
        var request = Request(WorkApprovalRequestStatuses.Pending, null, null);

        var (canRevert, revertableUntil) = ApprovalRevertWindow.ComputeCanRevert(request, Decider);

        canRevert.Should().BeFalse();
        revertableUntil.Should().BeNull();
    }
}
