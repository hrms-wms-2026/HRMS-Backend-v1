using FluentAssertions;
using ONEVO.Application.Features.Inbox.Contracts;
using ONEVO.Application.Features.WorkManagement.Approvals.DTOs;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Inbox;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Inbox;

/// <summary>Plan FW: a Work approval's undo carries the standard revert window, so the Inbox shows its undo banner.</summary>
public sealed class WorkInboxItemMapperTests
{
    private static readonly Guid Me = Guid.NewGuid();

    private static ApprovalFeedEntry Decided(DateTimeOffset decidedAt, Guid decidedBy)
    {
        var request = new WorkApprovalRequest
        {
            Id = Guid.NewGuid(), Status = WorkApprovalRequestStatuses.Approved, DecidedAt = decidedAt, DecidedByEmployeeId = decidedBy,
            ProjectId = Guid.NewGuid(), RequestedByEmployeeId = Guid.NewGuid(), ApproverEmployeeId = decidedBy,
            ActionType = "module.edit", TargetType = "module", TargetTitle = "Payments", PayloadJson = "{}", CreatedAt = decidedAt.AddHours(-1),
        };
        var item = new ApprovalFeedItemResponse(request.Id, ApprovalFeedSources.Engine, "module.edit", "module", Guid.NewGuid(), "Payments",
            null, null, "Apollo", "approved", "received", request.RequestedByEmployeeId, "Bala", decidedBy, "Anu", decidedBy, "Anu", null,
            request.CreatedAt, decidedAt, CanDecide: false, CanCancel: false, Summary: null);
        return new ApprovalFeedEntry(request.ProjectId, item, request);
    }

    [Fact]
    public void The_decider_gets_an_undo_with_the_revert_deadline_inside_the_window()
    {
        var decidedAt = DateTimeOffset.UtcNow.AddMinutes(-5);

        var revert = WorkInboxItemMapper.FromFeed(Decided(decidedAt, Me), Me).AvailableActions.Single(a => a.Key == WorkInboxActions.Revert);

        revert.Meta.Should().ContainKey(InboxStandardActions.MetaRevertableUntil);
        DateTimeOffset.Parse(revert.Meta![InboxStandardActions.MetaRevertableUntil])
            .Should().Be(decidedAt.AddMinutes(ApprovalRevertWindow.Minutes));
    }

    [Fact]
    public void No_undo_after_the_window_or_for_someone_else()
    {
        WorkInboxItemMapper.FromFeed(Decided(DateTimeOffset.UtcNow.AddMinutes(-(ApprovalRevertWindow.Minutes + 1)), Me), Me)
            .AvailableActions.Should().NotContain(a => a.Key == WorkInboxActions.Revert);
        WorkInboxItemMapper.FromFeed(Decided(DateTimeOffset.UtcNow.AddMinutes(-5), Guid.NewGuid()), Me)
            .AvailableActions.Should().NotContain(a => a.Key == WorkInboxActions.Revert);
    }
}
