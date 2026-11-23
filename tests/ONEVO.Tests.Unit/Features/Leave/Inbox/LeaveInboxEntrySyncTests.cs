using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Inbox.Contracts;
using ONEVO.Application.Features.Leave.Approval.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Inbox;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.Inbox.Entities;
using ONEVO.Domain.Features.Leave.Common;
using ONEVO.Domain.Features.Leave.Request.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Inbox;

/// <summary>Task 37: the inbox_items header a leave request maps to, and the leave badge counts read from it.</summary>
public sealed class LeaveInboxEntrySyncTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid First = Guid.NewGuid();
    private static readonly Guid Second = Guid.NewGuid();
    private static readonly DateTimeOffset Created = new(2026, 11, 20, 9, 0, 0, TimeSpan.Zero);

    private static LeaveApprovalState State(string mode, string status, params (Guid Approver, int Sequence, string Status)[] approvers)
    {
        var employee = new Employee { Id = Guid.NewGuid(), TenantId = Tenant, FirstName = "Ada", LastName = "Lovelace" };
        var request = new LeaveRequest
        {
            Id = Guid.NewGuid(), TenantId = Tenant, EmployeeId = employee.Id, LeaveTypeId = Guid.NewGuid(), Status = status,
            StartAt = Created.AddDays(3), EndAt = Created.AddDays(4), CreatedAt = Created,
        };
        var rows = approvers.Select(a => new LeaveRequestApprover
        {
            Id = Guid.NewGuid(), TenantId = Tenant, LeaveRequestId = request.Id, ApproverEmployeeId = a.Approver,
            SequenceOrder = a.Sequence, Status = a.Status,
            DecidedAt = a.Status == LeaveRequestApproverStatuses.Pending ? null : Created.AddHours(a.Sequence),
        }).ToList();
        return new LeaveApprovalState(request, null, employee, "Annual leave", "AL", mode, rows);
    }

    private static IEnumerable<(Guid, string)> Chain(InboxEntryState entry) => entry.Assignees.Select(a => (a.EmployeeId, a.Status));

    [Fact]
    public void InOrder_OnlyTheFirstPendingStepMustAct_TheNextWaits()
    {
        var entry = LeaveInboxEntrySync.Build(State(LeaveApprovalModes.InOrder, LeaveRequestStatuses.Pending,
            (First, 1, LeaveRequestApproverStatuses.Pending), (Second, 2, LeaveRequestApproverStatuses.Pending)));

        entry.Status.Should().Be(InboxEntryStatuses.Pending);
        entry.Source.Should().Be("leave");
        entry.Kind.Should().Be("request");
        entry.Title.Should().Be("Annual leave");
        entry.Summary.Should().Contain("Ada Lovelace");
        Chain(entry).Should().Equal((First, InboxEntryAssigneeStatuses.Pending), (Second, InboxEntryAssigneeStatuses.Waiting));
    }

    [Fact]
    public void InOrder_AfterTheFirstApproves_TheSecondMustAct()
    {
        var entry = LeaveInboxEntrySync.Build(State(LeaveApprovalModes.InOrder, LeaveRequestStatuses.Pending,
            (First, 1, LeaveRequestApproverStatuses.Approved), (Second, 2, LeaveRequestApproverStatuses.Pending)));

        Chain(entry).Should().Equal((First, InboxEntryAssigneeStatuses.Approved), (Second, InboxEntryAssigneeStatuses.Pending));
    }

    [Fact]
    public void AnyOne_EveryPendingApproverMustAct()
    {
        var entry = LeaveInboxEntrySync.Build(State(LeaveApprovalModes.AnyOne, LeaveRequestStatuses.Pending,
            (First, 1, LeaveRequestApproverStatuses.Pending), (Second, 2, LeaveRequestApproverStatuses.Pending)));

        entry.Assignees.Should().OnlyContain(a => a.Status == InboxEntryAssigneeStatuses.Pending);
    }

    [Fact]
    public void Approved_NobodyMustAct_AndTheDecisionIsRecorded()
    {
        var entry = LeaveInboxEntrySync.Build(State(LeaveApprovalModes.AnyOne, LeaveRequestStatuses.Approved,
            (First, 1, LeaveRequestApproverStatuses.Approved), (Second, 2, LeaveRequestApproverStatuses.Skipped)));

        entry.Status.Should().Be(InboxEntryStatuses.Approved);
        entry.DecidedByEmployeeId.Should().Be(First);
        entry.DecidedAt.Should().NotBeNull();
        Chain(entry).Should().Equal((First, InboxEntryAssigneeStatuses.Approved), (Second, InboxEntryAssigneeStatuses.Skipped));
    }

    [Fact]
    public void Cancelled_AStillPendingRowCountsForNobody()
    {
        var entry = LeaveInboxEntrySync.Build(State(LeaveApprovalModes.AnyOne, LeaveRequestStatuses.Cancelled,
            (First, 1, LeaveRequestApproverStatuses.Pending)));

        entry.Status.Should().Be(InboxEntryStatuses.Cancelled);
        Chain(entry).Should().Equal((First, InboxEntryAssigneeStatuses.Cancelled));
    }

    [Fact]
    public void NoApprovalMode_NobodyCanAct()
    {
        var entry = LeaveInboxEntrySync.Build(State(null!, LeaveRequestStatuses.Pending, (First, 1, LeaveRequestApproverStatuses.Pending)));

        Chain(entry).Should().Equal((First, InboxEntryAssigneeStatuses.Waiting));
    }

    [Fact]
    public async Task StageAsync_ByState_StagesTheBuiltHeader()
    {
        var store = new Mock<IInboxEntryStore>();
        var state = State(LeaveApprovalModes.AnyOne, LeaveRequestStatuses.Pending, (First, 1, LeaveRequestApproverStatuses.Pending));

        await new LeaveInboxEntrySync(store.Object, Mock.Of<ILeaveApprovalRepository>()).StageAsync(state, default);

        store.Verify(s => s.StageAsync(It.Is<InboxEntryState>(e => e.SubjectId == state.Request.Id
            && e.Assignees.Single().Status == InboxEntryAssigneeStatuses.Pending), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CountSource_WithoutApprovePermission_CountsNothing_AndDoesNotQuery()
    {
        var user = new Mock<ICurrentUser>();
        user.Setup(u => u.HasPermission(LeaveInboxSourceProvider.LeaveApprovePermission)).Returns(false);
        var store = new Mock<IInboxEntryStore>(MockBehavior.Strict);

        var counts = await new LeaveInboxCountSource(user.Object, store.Object).CountAsync(Tenant, First, default);

        counts.PendingReceived.Should().Be(0);
    }

    [Fact]
    public async Task CountSource_WithPermission_ReturnsTheHeaderCountsByKind()
    {
        var user = new Mock<ICurrentUser>();
        user.Setup(u => u.HasPermission(LeaveInboxSourceProvider.LeaveApprovePermission)).Returns(true);
        var store = new Mock<IInboxEntryStore>();
        store.Setup(s => s.CountPendingForAsync(Tenant, First, It.Is<IReadOnlyCollection<string>>(c => c.Single() == "leave"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<(string, string), int> { [("leave", "request")] = 2 });

        var counts = await new LeaveInboxCountSource(user.Object, store.Object).CountAsync(Tenant, First, default);

        counts.PendingReceived.Should().Be(2);
        counts.ByKind.Should().BeEquivalentTo(new Dictionary<string, int> { ["request"] = 2 });
    }
}
