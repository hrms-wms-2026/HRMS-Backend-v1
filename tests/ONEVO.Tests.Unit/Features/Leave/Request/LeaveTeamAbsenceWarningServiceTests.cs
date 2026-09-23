using FluentAssertions;
using Moq;
using ONEVO.Application.Features.CoreHr.EmployeeHierarchyClosure.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Request.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Request.Services;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Request;

public class LeaveTeamAbsenceWarningServiceTests
{
    [Fact]
    public async Task BuildWarningAsync_UsesManagerTeamNotEmployeeAsAncestor()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var teammateId = Guid.NewGuid();
        var hierarchy = new Mock<IEmployeeHierarchyClosureRepository>();
        var requests = new Mock<ILeaveRequestRepository>();
        hierarchy.Setup(x => x.GetDirectManagerEmployeeIdAsync(tenantId, employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(managerId);
        hierarchy.Setup(x => x.GetDescendantEmployeeIdsAsync(tenantId, managerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([employeeId, teammateId]);
        requests.Setup(x => x.CountDistinctEmployeesPendingOrApprovedInRangeAsync(
                tenantId, It.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(teammateId) && !ids.Contains(employeeId)),
                It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var service = new LeaveTeamAbsenceWarningService(hierarchy.Object, requests.Object);
        var warning = await service.BuildWarningAsync(
            tenantId, employeeId, new DateOnly(2026, 8, 18), new DateOnly(2026, 8, 20), 20m, CancellationToken.None);

        warning.Should().NotBeNull();
        warning!.AbsentCount.Should().Be(1);
        hierarchy.Verify(x => x.GetDescendantEmployeeIdsAsync(tenantId, managerId, It.IsAny<CancellationToken>()), Times.Once);
        hierarchy.Verify(x => x.GetDescendantEmployeeIdsAsync(tenantId, employeeId, It.IsAny<CancellationToken>()), Times.Never);
    }
}
