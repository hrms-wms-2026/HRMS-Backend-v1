using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Dashboard.Team.DTOs;
using ONEVO.Application.Features.Dashboard.Team.Sources;
using ONEVO.Application.Features.Leave.Approval.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.Leave.Common;
using ONEVO.Domain.Features.Leave.Request.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Dashboard.Team.Sources;

public sealed class LeaveApprovalTeamActionSourceTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid UserId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid ApproverEmployeeId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid LegalEntityId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

    private static LeaveRequest Request(Guid employeeId, DateTimeOffset createdAt) => new()
    {
        Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = employeeId, LeaveTypeId = Guid.NewGuid(),
        StartAt = createdAt, EndAt = createdAt.AddDays(1), Status = LeaveRequestStatuses.Pending, CreatedAt = createdAt,
    };

    private static LeavePendingApprovalListRow Row(LeaveRequest request, string name) => new(request, name, "Annual", "AN");

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task IsGatedAsync_reflects_leave_approve_permission(bool hasPermission)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.Setup(x => x.HasPermission("leave:approve")).Returns(hasPermission);
        var source = new LeaveApprovalTeamActionSource(
            currentUser.Object, Mock.Of<IEmployeeRepository>(), Mock.Of<ILeaveApprovalRepository>());

        Assert.Equal(hasPermission, await source.IsGatedAsync(CancellationToken.None));
    }

    [Fact]
    public async Task No_employee_record_returns_empty_ok_summary()
    {
        var currentUser = CurrentUser();
        var employees = new Mock<IEmployeeRepository>();
        employees.Setup(x => x.GetByUserIdAsync(TenantId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync((Employee?)null);
        var source = new LeaveApprovalTeamActionSource(currentUser, employees.Object, Mock.Of<ILeaveApprovalRepository>());

        var summary = await source.GetSummaryAsync(LegalEntityId, 5, CancellationToken.None);

        Assert.Equal(ActionSourceSummary.StatusOk, summary.Status);
        Assert.Equal(0, summary.PendingCount);
        Assert.Empty(summary.TopItems);
    }

    [Fact]
    public async Task Only_rows_the_evaluator_says_are_actionable_are_counted()
    {
        var currentUser = CurrentUser();
        var employees = EmployeesWithApprover();
        var repository = new Mock<ILeaveApprovalRepository>();

        var actionableRequest = Request(Guid.NewGuid(), DateTimeOffset.Parse("2026-09-01T00:00:00Z"));
        var actionableRow = Row(actionableRequest, "Priya K");
        var notMyTurnRequest = Request(Guid.NewGuid(), DateTimeOffset.Parse("2026-09-02T00:00:00Z"));
        var notMyTurnRow = Row(notMyTurnRequest, "Arjun M");

        repository.Setup(x => x.ListPendingForApproverAsync(TenantId, ApproverEmployeeId, It.IsAny<LeaveApprovalListFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([actionableRow, notMyTurnRow]);

        // any_one mode: pending row for this approver -> actionable regardless of others.
        repository.Setup(x => x.GetStateAsync(TenantId, actionableRequest.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LeaveApprovalState(
                actionableRequest, null, new Employee { Id = actionableRequest.EmployeeId }, "Annual", "AN",
                LeaveApprovalModes.AnyOne,
                [new LeaveRequestApprover { ApproverEmployeeId = ApproverEmployeeId, SequenceOrder = 1, Status = LeaveRequestApproverStatuses.Pending }],
                []));

        // in_order mode: this approver is sequence 2, but sequence 1 is still pending -> not actionable yet.
        repository.Setup(x => x.GetStateAsync(TenantId, notMyTurnRequest.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LeaveApprovalState(
                notMyTurnRequest, null, new Employee { Id = notMyTurnRequest.EmployeeId }, "Annual", "AN",
                LeaveApprovalModes.InOrder,
                [
                    new LeaveRequestApprover { ApproverEmployeeId = Guid.NewGuid(), SequenceOrder = 1, Status = LeaveRequestApproverStatuses.Pending },
                    new LeaveRequestApprover { ApproverEmployeeId = ApproverEmployeeId, SequenceOrder = 2, Status = LeaveRequestApproverStatuses.Pending },
                ],
                []));

        var source = new LeaveApprovalTeamActionSource(currentUser, employees, repository.Object);

        var summary = await source.GetSummaryAsync(LegalEntityId, 5, CancellationToken.None);

        Assert.Equal(1, summary.PendingCount);
        var item = Assert.Single(summary.TopItems);
        Assert.Equal(actionableRequest.Id, item.EntityId);
        Assert.Equal("leave.approval", item.SourceKey);
        Assert.Equal(ActionItemLink.KindLeaveApproval, item.Link.Kind);
        Assert.Equal(actionableRequest.Id.ToString(), item.Link.Params["requestId"]);
    }

    [Fact]
    public async Task Ordered_oldest_first_and_oldestPendingAt_is_the_earliest_createdAt()
    {
        var currentUser = CurrentUser();
        var employees = EmployeesWithApprover();
        var repository = new Mock<ILeaveApprovalRepository>();

        var older = Request(Guid.NewGuid(), DateTimeOffset.Parse("2026-08-01T00:00:00Z"));
        var newer = Request(Guid.NewGuid(), DateTimeOffset.Parse("2026-09-01T00:00:00Z"));
        repository.Setup(x => x.ListPendingForApproverAsync(TenantId, ApproverEmployeeId, It.IsAny<LeaveApprovalListFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Row(newer, "Newer"), Row(older, "Older")]);

        foreach (var request in new[] { older, newer })
        {
            repository.Setup(x => x.GetStateAsync(TenantId, request.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new LeaveApprovalState(
                    request, null, new Employee { Id = request.EmployeeId }, "Annual", "AN", LeaveApprovalModes.AnyOne,
                    [new LeaveRequestApprover { ApproverEmployeeId = ApproverEmployeeId, SequenceOrder = 1, Status = LeaveRequestApproverStatuses.Pending }],
                    []));
        }

        var source = new LeaveApprovalTeamActionSource(currentUser, employees, repository.Object);
        var summary = await source.GetSummaryAsync(LegalEntityId, 5, CancellationToken.None);

        Assert.Equal(older.CreatedAt, summary.OldestPendingAt);
        Assert.Equal("Older", summary.TopItems[0].SubjectName);
        Assert.Equal("Newer", summary.TopItems[1].SubjectName);
    }

    [Fact]
    public async Task TopItems_capped_but_pendingCount_reflects_the_whole_actionable_set()
    {
        var currentUser = CurrentUser();
        var employees = EmployeesWithApprover();
        var repository = new Mock<ILeaveApprovalRepository>();

        var requests = Enumerable.Range(0, 4)
            .Select(i => Request(Guid.NewGuid(), DateTimeOffset.Parse("2026-09-01T00:00:00Z").AddDays(i)))
            .ToList();
        repository.Setup(x => x.ListPendingForApproverAsync(TenantId, ApproverEmployeeId, It.IsAny<LeaveApprovalListFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(requests.Select(r => Row(r, "Someone")).ToList());
        foreach (var request in requests)
        {
            repository.Setup(x => x.GetStateAsync(TenantId, request.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new LeaveApprovalState(
                    request, null, new Employee { Id = request.EmployeeId }, "Annual", "AN", LeaveApprovalModes.AnyOne,
                    [new LeaveRequestApprover { ApproverEmployeeId = ApproverEmployeeId, SequenceOrder = 1, Status = LeaveRequestApproverStatuses.Pending }],
                    []));
        }

        var source = new LeaveApprovalTeamActionSource(currentUser, employees, repository.Object);
        var summary = await source.GetSummaryAsync(LegalEntityId, top: 2, CancellationToken.None);

        Assert.Equal(4, summary.PendingCount);
        Assert.Equal(2, summary.TopItems.Count);
    }

    [Fact]
    public async Task Passes_the_active_legal_entity_through_the_filter()
    {
        var currentUser = CurrentUser();
        var employees = EmployeesWithApprover();
        var repository = new Mock<ILeaveApprovalRepository>();
        repository.Setup(x => x.ListPendingForApproverAsync(TenantId, ApproverEmployeeId, It.IsAny<LeaveApprovalListFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var source = new LeaveApprovalTeamActionSource(currentUser, employees, repository.Object);
        await source.GetSummaryAsync(LegalEntityId, 5, CancellationToken.None);

        repository.Verify(x => x.ListPendingForApproverAsync(
            TenantId, ApproverEmployeeId, It.Is<LeaveApprovalListFilter>(f => f.LegalEntityId == LegalEntityId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private static ICurrentUser CurrentUser()
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);
        currentUser.Setup(x => x.HasPermission("leave:approve")).Returns(true);
        return currentUser.Object;
    }

    private static IEmployeeRepository EmployeesWithApprover()
    {
        var employees = new Mock<IEmployeeRepository>();
        employees.Setup(x => x.GetByUserIdAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = ApproverEmployeeId, UserId = UserId, TenantId = TenantId });
        return employees.Object;
    }
}
