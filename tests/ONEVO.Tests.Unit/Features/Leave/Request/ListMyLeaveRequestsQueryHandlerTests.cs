using FluentAssertions;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Request.Queries.ListMyLeaveRequests;
using ONEVO.Application.Features.Leave.Request.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.Leave.Common;
using ONEVO.Domain.Features.Leave.Request.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Request;

public class ListMyLeaveRequestsQueryHandlerTests
{
    [Fact]
    public async Task Handle_ReturnsOnlyCallerRequests()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var employee = new Employee { Id = Guid.NewGuid(), TenantId = tenantId, UserId = userId, FirstName = "Priya", LastName = "Nair", EmployeeNumber = "E1", HireDate = new DateOnly(2024, 1, 1) };
        var request = new LeaveRequest
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EmployeeId = employee.Id,
            LeaveTypeId = Guid.NewGuid(),
            StartDate = new DateOnly(2026, 8, 18),
            EndDate = new DateOnly(2026, 8, 18),
            TotalDays = 1m,
            Status = LeaveRequestStatuses.Pending,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var currentUser = new Mock<ICurrentUser>();
        var employees = new Mock<IEmployeeRepository>();
        var requests = new Mock<ILeaveRequestRepository>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(tenantId);
        currentUser.SetupGet(x => x.UserId).Returns(userId);
        employees.Setup(x => x.GetByUserIdAsync(tenantId, userId, It.IsAny<CancellationToken>())).ReturnsAsync(employee);
        requests.Setup(x => x.ListOwnAsync(tenantId, employee.Id, It.IsAny<LeaveRequestListFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new LeaveRequestListRow(request, "Annual Leave", "AL")]);

        var handler = new ListMyLeaveRequestsQueryHandler(currentUser.Object, employees.Object, requests.Object);
        var result = await handler.Handle(new ListMyLeaveRequestsQuery(null, null, null, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle(x => x.Id == request.Id && x.EmployeeId == employee.Id);
    }
}
