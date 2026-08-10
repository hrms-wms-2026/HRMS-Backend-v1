using Moq;
using ONEVO.Application.Features.Auth.Permission.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.ProjectMembers.Entities;
using ONEVO.Domain.Lookups;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Application.Common.RepositoryInterfaces;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public class MilestoneMembershipCoordinatorTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();

    private static Employee ActiveEmployee() => new() { Id = EmployeeId, TenantId = TenantId, UserId = UserId, EmploymentStatusId = EmploymentStatusIds.Active };
    private static Employee InactiveEmployee() => new() { Id = EmployeeId, TenantId = TenantId, UserId = UserId, EmploymentStatusId = 4 };

    private (MilestoneMembershipCoordinator Coordinator, Mock<IProjectMemberRepository> Members) BuildCoordinator(Employee? employee)
    {
        var employees = new Mock<IEmployeeRepository>();
        employees.Setup(x => x.GetByUserIdAsync(TenantId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync(employee);

        var members = new Mock<IProjectMemberRepository>();

        var coordinator = new MilestoneMembershipCoordinator(employees.Object, members.Object);
        return (coordinator, members);
    }

    [Fact]
    public async Task GetActiveAssigneeAsync_ActiveEmployee_ReturnsIt()
    {
        var (coordinator, _) = BuildCoordinator(ActiveEmployee());

        var result = await coordinator.GetActiveAssigneeAsync(TenantId, UserId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(EmployeeId, result!.Id);
    }

    [Fact]
    public async Task GetActiveAssigneeAsync_NoEmployeeRecord_ReturnsNull()
    {
        var (coordinator, _) = BuildCoordinator(null);

        var result = await coordinator.GetActiveAssigneeAsync(TenantId, UserId, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetActiveAssigneeAsync_InactiveEmployee_ReturnsNull()
    {
        var (coordinator, _) = BuildCoordinator(InactiveEmployee());

        var result = await coordinator.GetActiveAssigneeAsync(TenantId, UserId, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task UpsertMembershipAsync_NoExistingRow_AddsNew()
    {
        var (coordinator, members) = BuildCoordinator(ActiveEmployee());
        members.Setup(x => x.GetTrackedForObjectiveAsync(TenantId, ProjectId, ObjectiveId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProjectMember?)null);

        await coordinator.UpsertMembershipAsync(TenantId, ProjectId, ObjectiveId, UserId, EmployeeId, CancellationToken.None);

        members.Verify(x => x.AddAsync(It.Is<ProjectMember>(m =>
            m.TenantId == TenantId && m.ProjectId == ProjectId && m.ObjectiveId == ObjectiveId &&
            m.UserId == UserId && m.EmployeeId == EmployeeId && m.IsActive &&
            m.MembershipSource == ProjectMembershipSources.ObjectiveInvitation), It.IsAny<CancellationToken>()), Times.Once);
        members.Verify(x => x.Update(It.IsAny<ProjectMember>()), Times.Never);
    }

    [Fact]
    public async Task UpsertMembershipAsync_ExistingInactiveRow_Reactivates()
    {
        var existing = new ProjectMember { Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, ObjectiveId = ObjectiveId, UserId = UserId, EmployeeId = EmployeeId, IsActive = false, RemovedAt = DateTimeOffset.UtcNow };
        var (coordinator, members) = BuildCoordinator(ActiveEmployee());
        members.Setup(x => x.GetTrackedForObjectiveAsync(TenantId, ProjectId, ObjectiveId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        await coordinator.UpsertMembershipAsync(TenantId, ProjectId, ObjectiveId, UserId, EmployeeId, CancellationToken.None);

        Assert.True(existing.IsActive);
        Assert.Null(existing.RemovedAt);
        members.Verify(x => x.Update(existing), Times.Once);
        members.Verify(x => x.AddAsync(It.IsAny<ProjectMember>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpsertMembershipAsync_ExistingActiveRow_NoOp()
    {
        var existing = new ProjectMember { Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, ObjectiveId = ObjectiveId, UserId = UserId, EmployeeId = EmployeeId, IsActive = true };
        var (coordinator, members) = BuildCoordinator(ActiveEmployee());
        members.Setup(x => x.GetTrackedForObjectiveAsync(TenantId, ProjectId, ObjectiveId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        await coordinator.UpsertMembershipAsync(TenantId, ProjectId, ObjectiveId, UserId, EmployeeId, CancellationToken.None);

        members.Verify(x => x.Update(It.IsAny<ProjectMember>()), Times.Never);
        members.Verify(x => x.AddAsync(It.IsAny<ProjectMember>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeactivateMembershipAsync_ExistingActiveRow_Deactivates()
    {
        var existing = new ProjectMember { Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, ObjectiveId = ObjectiveId, UserId = UserId, EmployeeId = EmployeeId, IsActive = true };
        var (coordinator, members) = BuildCoordinator(ActiveEmployee());
        members.Setup(x => x.GetTrackedForObjectiveAsync(TenantId, ProjectId, ObjectiveId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        await coordinator.DeactivateMembershipAsync(TenantId, ProjectId, ObjectiveId, UserId, CancellationToken.None);

        Assert.False(existing.IsActive);
        Assert.NotNull(existing.RemovedAt);
        members.Verify(x => x.Update(existing), Times.Once);
    }

    [Fact]
    public async Task DeactivateMembershipAsync_NoExistingRow_NoOp()
    {
        var (coordinator, members) = BuildCoordinator(ActiveEmployee());
        members.Setup(x => x.GetTrackedForObjectiveAsync(TenantId, ProjectId, ObjectiveId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProjectMember?)null);

        await coordinator.DeactivateMembershipAsync(TenantId, ProjectId, ObjectiveId, UserId, CancellationToken.None);

        members.Verify(x => x.Update(It.IsAny<ProjectMember>()), Times.Never);
    }

    [Fact]
    public async Task HasOtherActiveAccessAsync_DelegatesToRepository()
    {
        var (coordinator, members) = BuildCoordinator(ActiveEmployee());
        members.Setup(x => x.HasActiveMembershipExcludingObjectiveAsync(TenantId, ProjectId, UserId, ObjectiveId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await coordinator.HasOtherActiveAccessAsync(TenantId, ProjectId, UserId, ObjectiveId, CancellationToken.None);

        Assert.True(result);
    }
}
