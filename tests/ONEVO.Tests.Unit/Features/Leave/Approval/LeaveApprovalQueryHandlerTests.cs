using FluentAssertions;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Approval.Queries;
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
            .ReturnsAsync([new LeaveRequestAllListRow(request, "Priya Nair", null, null, "Annual Leave")]);

        var handler = new ListAllLeaveRequestsQueryHandler(currentUser.Object, repo.Object);
        var result = await handler.Handle(new ListAllLeaveRequestsQuery(null, null, null, null, null, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle(x => x.RequestId == request.Id && x.EmployeeName == "Priya Nair");
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

        var handler = new GetLeaveApprovalDetailQueryHandler(currentUser.Object, employees.Object, repo.Object, conflicts.Object);
        var result = await handler.Handle(new GetLeaveApprovalDetailQuery(request.Id), CancellationToken.None);

        result.StatusCode.Should().Be(403);
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
        StartDate = new DateOnly(2026, 9, 14),
        EndDate = new DateOnly(2026, 9, 14),
        TotalDays = 1m,
        PaidDays = 1m,
        Status = status,
        CreatedAt = DateTimeOffset.UtcNow
    };
}
