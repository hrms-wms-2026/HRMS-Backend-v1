using FluentAssertions;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Approval.Queries;
using ONEVO.Application.Features.Leave.Approval.Services;
using ONEVO.Application.Features.Leave.Approval.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Request.Services;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.Leave.Common;
using ONEVO.Domain.Features.Leave.Request.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Approval;

public class LeaveApprovalQueryHandlerTests
{
    [Fact]
    public async Task ListPending_SkipsLaterInOrderApprover()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var current = Employee(tenantId, userId, "Mgr");
        var firstApproverId = Guid.NewGuid();
        var request = Request(tenantId, LeaveRequestStatuses.Pending);
        var pendingRow = new LeavePendingApprovalListRow(request, "Priya Nair", "Annual Leave", "AL");
        var state = new LeaveApprovalState(
            request,
            null,
            Employee(tenantId, Guid.NewGuid(), "Priya"),
            "Annual Leave",
            "AL",
            LeaveApprovalModes.InOrder,
            [
                new LeaveRequestApprover
                {
                    ApproverEmployeeId = firstApproverId,
                    SequenceOrder = 1,
                    Status = LeaveRequestApproverStatuses.Pending
                },
                new LeaveRequestApprover
                {
                    ApproverEmployeeId = current.Id,
                    SequenceOrder = 2,
                    Status = LeaveRequestApproverStatuses.Pending
                }
            ],
            []);

        var (currentUser, employees, repo) = Mocks(tenantId, userId, current);
        repo.Setup(x => x.ListPendingForApproverAsync(tenantId, current.Id, It.IsAny<LeaveApprovalListFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([pendingRow]);
        repo.Setup(x => x.GetStateAsync(tenantId, request.Id, It.IsAny<CancellationToken>())).ReturnsAsync(state);

        var handler = new ListPendingLeaveApprovalsQueryHandler(currentUser.Object, employees.Object, repo.Object);
        var result = await handler.Handle(new ListPendingLeaveApprovalsQuery(null, null, null, null, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task ListAll_MapsRepositoryRows()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var request = Request(tenantId, LeaveRequestStatuses.Pending);
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(tenantId);
        currentUser.SetupGet(x => x.UserId).Returns(userId);
        var repo = new Mock<ILeaveApprovalRepository>();
        repo.Setup(x => x.ListAllAsync(tenantId, It.IsAny<LeaveRequestAllListFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new LeaveRequestAllListRow(request, "Priya Nair", null, null, "Annual Leave", ["Mathu Kumar"])]);

        var handler = new ListAllLeaveRequestsQueryHandler(currentUser.Object, repo.Object);
        var result = await handler.Handle(new ListAllLeaveRequestsQuery(null, null, null, null, null, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle(x => x.RequestId == request.Id && x.EmployeeName == "Priya Nair" && x.ApproverNames.SequenceEqual(new[] { "Mathu Kumar" }));
    }

    [Fact]
    public async Task GetDetail_WhenCallerIsNotAssignedApprover_ReturnsForbidden()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var current = Employee(tenantId, userId, "Mgr");
        var request = Request(tenantId, LeaveRequestStatuses.Pending);
        var state = new LeaveApprovalState(
            request,
            null,
            Employee(tenantId, Guid.NewGuid(), "Priya"),
            "Annual Leave",
            "AL",
            LeaveApprovalModes.AnyOne,
            [
                new LeaveRequestApprover
                {
                    ApproverEmployeeId = Guid.NewGuid(),
                    SequenceOrder = 1,
                    Status = LeaveRequestApproverStatuses.Pending
                }
            ],
            []);

        var (currentUser, employees, repo) = Mocks(tenantId, userId, current);
        repo.Setup(x => x.GetStateAsync(tenantId, request.Id, It.IsAny<CancellationToken>())).ReturnsAsync(state);
        var conflicts = new Mock<ILeaveRequestConflictProvider>();

        var handler = new GetLeaveApprovalDetailQueryHandler(currentUser.Object, employees.Object, repo.Object, conflicts.Object, new LeaveForwardTargetResolver(Mock.Of<ILeaveApproverResolver>()), Clock());
        var result = await handler.Handle(new GetLeaveApprovalDetailQuery(request.Id), CancellationToken.None);

        result.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task GetDetail_OrgWideRead_WhenCallerIsNotAssignedApprover_ReturnsReadOnlyDetail()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var current = Employee(tenantId, userId, "Hr");
        var request = Request(tenantId, LeaveRequestStatuses.Pending);
        var approverId = Guid.NewGuid();
        var state = new LeaveApprovalState(
            request,
            null,
            Employee(tenantId, Guid.NewGuid(), "Priya"),
            "Annual Leave",
            "AL",
            LeaveApprovalModes.AnyOne,
            [
                new LeaveRequestApprover
                {
                    ApproverEmployeeId = approverId,
                    SequenceOrder = 1,
                    Status = LeaveRequestApproverStatuses.Pending
                }
            ],
            []);

        var (currentUser, employees, repo) = Mocks(tenantId, userId, current);
        currentUser.Setup(x => x.HasPermission(It.IsAny<string>())).Returns(true);
        employees.Setup(x => x.ListByIdsAsync(tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, Employee>());
        repo.Setup(x => x.GetStateAsync(tenantId, request.Id, It.IsAny<CancellationToken>())).ReturnsAsync(state);
        var conflicts = new Mock<ILeaveRequestConflictProvider>();
        conflicts.Setup(x => x.ListConflictsAsync(tenantId, request.EmployeeId, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var handler = new GetLeaveApprovalDetailQueryHandler(currentUser.Object, employees.Object, repo.Object, conflicts.Object, new LeaveForwardTargetResolver(Mock.Of<ILeaveApproverResolver>()), Clock());
        var result = await handler.Handle(new GetLeaveApprovalDetailQuery(request.Id, OrgWideRead: true), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.CanDecide.Should().BeFalse();
        result.Value.Approvers.Should().ContainSingle(a => a.ApproverEmployeeId == approverId);
    }

    [Fact]
    public async Task GetDetail_WhenCallerCanDecide_NamesTheForwardTarget()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var current = Employee(tenantId, userId, "Mathu");
        var request = Request(tenantId, LeaveRequestStatuses.Pending);
        var state = new LeaveApprovalState(
            request, null, Employee(tenantId, Guid.NewGuid(), "Tharmi"), "Annual Leave", "AL", LeaveApprovalModes.AnyOne,
            [new LeaveRequestApprover { ApproverEmployeeId = current.Id, SequenceOrder = 1, Status = LeaveRequestApproverStatuses.Pending }],
            []);
        var manager = Employee(tenantId, Guid.NewGuid(), "Nadesh");

        var (currentUser, employees, repo) = Mocks(tenantId, userId, current);
        currentUser.Setup(x => x.HasPermission("leave:approve")).Returns(true);
        employees.Setup(x => x.ListByIdsAsync(tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, Employee> { [current.Id] = current, [manager.Id] = manager });
        repo.Setup(x => x.GetStateAsync(tenantId, request.Id, It.IsAny<CancellationToken>())).ReturnsAsync(state);
        var conflicts = new Mock<ILeaveRequestConflictProvider>();
        conflicts.Setup(x => x.ListConflictsAsync(tenantId, request.EmployeeId, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var approverResolver = new Mock<ILeaveApproverResolver>();
        approverResolver.Setup(x => x.ResolveAsync(tenantId, current.Id, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LeaveApproverResolution([new LeaveApproverResolutionRow(manager.Id, 1, null)]));

        var handler = new GetLeaveApprovalDetailQueryHandler(
            currentUser.Object, employees.Object, repo.Object, conflicts.Object, new LeaveForwardTargetResolver(approverResolver.Object), Clock());
        var result = await handler.Handle(new GetLeaveApprovalDetailQuery(request.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.CanDecide.Should().BeTrue();
        result.Value.ForwardTo.Should().NotBeNull();
        result.Value.ForwardTo!.EmployeeId.Should().Be(manager.Id);
        result.Value.ForwardTo.Name.Should().StartWith("Nadesh");
    }

    [Fact]
    public async Task ListHistory_MapsTheCallersPastActions()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var current = Employee(tenantId, userId, "Mathu");
        var request = Request(tenantId, LeaveRequestStatuses.Pending);
        var actedAt = new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

        var (currentUser, employees, repo) = Mocks(tenantId, userId, current);
        repo.Setup(x => x.ListApprovalHistoryAsync(tenantId, current.Id, null, null, ListLeaveApprovalHistoryQueryHandler.Limit, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new LeaveApprovalHistoryRow(request, "Tharmi Rajendran", "Annual Leave",
                LeaveRequestApproverStatuses.Forwarded, actedAt, "Please decide")]);

        var handler = new ListLeaveApprovalHistoryQueryHandler(currentUser.Object, employees.Object, repo.Object);
        var result = await handler.Handle(new ListLeaveApprovalHistoryQuery(null, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var item = result.Value.Should().ContainSingle().Subject;
        item.RequestId.Should().Be(request.Id);
        item.MyAction.Should().Be("forwarded");
        item.ActedAt.Should().Be(actedAt);
        item.MyComment.Should().Be("Please decide");
        item.RequestStatus.Should().Be(LeaveRequestStatuses.Pending);
    }

    [Fact]
    public async Task GetDetail_CanChangeDecision_OnlyForTheDecidingApproverBeforeTheLeaveStarts()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var current = Employee(tenantId, userId, "Mathu");
        var request = Request(tenantId, LeaveRequestStatuses.Approved);
        request.ApprovedBy = current.Id;
        var state = new LeaveApprovalState(
            request, null, Employee(tenantId, Guid.NewGuid(), "Tharmi"), "Annual Leave", "AL", LeaveApprovalModes.AnyOne,
            [new LeaveRequestApprover { ApproverEmployeeId = current.Id, SequenceOrder = 1, Status = LeaveRequestApproverStatuses.Approved }],
            []);

        var (currentUser, employees, repo) = Mocks(tenantId, userId, current);
        currentUser.Setup(x => x.HasPermission("leave:approve")).Returns(true);
        employees.Setup(x => x.ListByIdsAsync(tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, Employee> { [current.Id] = current });
        repo.Setup(x => x.GetStateAsync(tenantId, request.Id, It.IsAny<CancellationToken>())).ReturnsAsync(state);
        var conflicts = new Mock<ILeaveRequestConflictProvider>();
        conflicts.Setup(x => x.ListConflictsAsync(tenantId, request.EmployeeId, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var before = new GetLeaveApprovalDetailQueryHandler(
            currentUser.Object, employees.Object, repo.Object, conflicts.Object,
            new LeaveForwardTargetResolver(Mock.Of<ILeaveApproverResolver>()), Clock(request.StartAt.AddDays(-1)));
        (await before.Handle(new GetLeaveApprovalDetailQuery(request.Id), CancellationToken.None)).Value!.CanChangeDecision.Should().BeTrue();

        var after = new GetLeaveApprovalDetailQueryHandler(
            currentUser.Object, employees.Object, repo.Object, conflicts.Object,
            new LeaveForwardTargetResolver(Mock.Of<ILeaveApproverResolver>()), Clock(request.StartAt.AddHours(1)));
        (await after.Handle(new GetLeaveApprovalDetailQuery(request.Id), CancellationToken.None)).Value!.CanChangeDecision.Should().BeFalse();
    }

    private static IDateTimeProvider Clock(DateTimeOffset? now = null)
    {
        var clock = new Mock<IDateTimeProvider>();
        clock.SetupGet(x => x.UtcNow).Returns(now ?? new DateTimeOffset(2026, 8, 22, 10, 0, 0, TimeSpan.Zero));
        return clock.Object;
    }

    private static (Mock<ICurrentUser> CurrentUser, Mock<IEmployeeRepository> Employees, Mock<ILeaveApprovalRepository> Repo)
        Mocks(Guid tenantId, Guid userId, Employee current)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(tenantId);
        currentUser.SetupGet(x => x.UserId).Returns(userId);
        var employees = new Mock<IEmployeeRepository>();
        employees.Setup(x => x.GetByUserIdAsync(tenantId, userId, It.IsAny<CancellationToken>())).ReturnsAsync(current);
        return (currentUser, employees, new Mock<ILeaveApprovalRepository>());
    }

    private static Employee Employee(Guid tenantId, Guid userId, string first) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        UserId = userId,
        FirstName = first,
        LastName = "One",
        EmployeeNumber = "E1",
        HireDate = new DateOnly(2024, 1, 1)
    };

    private static LeaveRequest Request(Guid tenantId, string status) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        EmployeeId = Guid.NewGuid(),
        LeaveTypeId = Guid.NewGuid(),
        StartAt = new DateTimeOffset(2026, 9, 14, 9, 0, 0, TimeSpan.Zero),
        EndAt = new DateTimeOffset(2026, 9, 14, 18, 0, 0, TimeSpan.Zero),
        TotalHours = 1m,
        PaidHours = 1m,
        Status = status,
        CreatedAt = DateTimeOffset.UtcNow
    };
}
