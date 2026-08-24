using Moq;
using ONEVO.Application.Features.Auth.Permission.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
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
        var (coordinator, members, _) = BuildCoordinator(employee, new Mock<IObjectiveRepository>());
        return (coordinator, members);
    }

    private (MilestoneMembershipCoordinator Coordinator, Mock<IProjectMemberRepository> Members, Mock<IObjectiveRepository> Objectives) BuildCoordinator(Employee? employee, Mock<IObjectiveRepository> objectives)
    {
        var employees = new Mock<IEmployeeRepository>();
        employees.Setup(x => x.GetByIdAsync(TenantId, EmployeeId, It.IsAny<CancellationToken>())).ReturnsAsync(employee);

        var members = new Mock<IProjectMemberRepository>();

        var coordinator = new MilestoneMembershipCoordinator(employees.Object, members.Object, objectives.Object);
        return (coordinator, members, objectives);
    }

    [Fact]
    public async Task GetActiveAssigneeAsync_ActiveEmployee_ReturnsIt()
    {
        var (coordinator, _) = BuildCoordinator(ActiveEmployee());

        var result = await coordinator.GetActiveAssigneeAsync(TenantId, EmployeeId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(EmployeeId, result!.Id);
    }

    [Fact]
    public async Task GetActiveAssigneeAsync_NoEmployeeRecord_ReturnsNull()
    {
        var (coordinator, _) = BuildCoordinator(null);

        var result = await coordinator.GetActiveAssigneeAsync(TenantId, EmployeeId, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetActiveAssigneeAsync_InactiveEmployee_ReturnsNull()
    {
        var (coordinator, _) = BuildCoordinator(InactiveEmployee());

        var result = await coordinator.GetActiveAssigneeAsync(TenantId, EmployeeId, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task UpsertMembershipAsync_NoExistingRow_AddsNew()
    {
        var (coordinator, members) = BuildCoordinator(ActiveEmployee());
        members.Setup(x => x.GetTrackedForObjectiveAsync(TenantId, ProjectId, ObjectiveId, EmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProjectMember?)null);

        await coordinator.UpsertMembershipAsync(TenantId, ProjectId, ObjectiveId, EmployeeId, CancellationToken.None);

        members.Verify(x => x.AddAsync(It.Is<ProjectMember>(m =>
            m.TenantId == TenantId && m.ProjectId == ProjectId && m.ObjectiveId == ObjectiveId &&
            m.EmployeeId == EmployeeId && m.IsActive &&
            m.MembershipSource == ProjectMembershipSources.ObjectiveInvitation), It.IsAny<CancellationToken>()), Times.Once);
        members.Verify(x => x.Update(It.IsAny<ProjectMember>()), Times.Never);
    }

    [Fact]
    public async Task UpsertMembershipAsync_ExistingInactiveRow_Reactivates()
    {
        var existing = new ProjectMember { Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, ObjectiveId = ObjectiveId, EmployeeId = EmployeeId, IsActive = false, RemovedAt = DateTimeOffset.UtcNow };
        var (coordinator, members) = BuildCoordinator(ActiveEmployee());
        members.Setup(x => x.GetTrackedForObjectiveAsync(TenantId, ProjectId, ObjectiveId, EmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        await coordinator.UpsertMembershipAsync(TenantId, ProjectId, ObjectiveId, EmployeeId, CancellationToken.None);

        Assert.True(existing.IsActive);
        Assert.Null(existing.RemovedAt);
        members.Verify(x => x.Update(existing), Times.Once);
        members.Verify(x => x.AddAsync(It.IsAny<ProjectMember>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpsertMembershipAsync_ExistingActiveRow_NoOp()
    {
        var existing = new ProjectMember { Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, ObjectiveId = ObjectiveId, EmployeeId = EmployeeId, IsActive = true };
        var (coordinator, members) = BuildCoordinator(ActiveEmployee());
        members.Setup(x => x.GetTrackedForObjectiveAsync(TenantId, ProjectId, ObjectiveId, EmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        await coordinator.UpsertMembershipAsync(TenantId, ProjectId, ObjectiveId, EmployeeId, CancellationToken.None);

        members.Verify(x => x.Update(It.IsAny<ProjectMember>()), Times.Never);
        members.Verify(x => x.AddAsync(It.IsAny<ProjectMember>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeactivateMembershipAsync_ExistingActiveRow_Deactivates()
    {
        var existing = new ProjectMember { Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, ObjectiveId = ObjectiveId, EmployeeId = EmployeeId, IsActive = true };
        var (coordinator, members) = BuildCoordinator(ActiveEmployee());
        members.Setup(x => x.GetTrackedForObjectiveAsync(TenantId, ProjectId, ObjectiveId, EmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        await coordinator.DeactivateMembershipAsync(TenantId, ProjectId, ObjectiveId, EmployeeId, CancellationToken.None);

        Assert.False(existing.IsActive);
        Assert.NotNull(existing.RemovedAt);
        members.Verify(x => x.Update(existing), Times.Once);
    }

    [Fact]
    public async Task DeactivateMembershipAsync_NoExistingRow_NoOp()
    {
        var (coordinator, members) = BuildCoordinator(ActiveEmployee());
        members.Setup(x => x.GetTrackedForObjectiveAsync(TenantId, ProjectId, ObjectiveId, EmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProjectMember?)null);

        await coordinator.DeactivateMembershipAsync(TenantId, ProjectId, ObjectiveId, EmployeeId, CancellationToken.None);

        members.Verify(x => x.Update(It.IsAny<ProjectMember>()), Times.Never);
    }

    [Fact]
    public async Task HasOtherActiveAccessAsync_DelegatesToRepository()
    {
        var (coordinator, members) = BuildCoordinator(ActiveEmployee());
        members.Setup(x => x.HasActiveMembershipExcludingObjectiveAsync(TenantId, ProjectId, EmployeeId, ObjectiveId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await coordinator.HasOtherActiveAccessAsync(TenantId, ProjectId, EmployeeId, ObjectiveId, CancellationToken.None);

        Assert.True(result);
    }

    // --- IsEffectiveManagerAsync: Root (no parent) -> Child (parent = Root) -> Grandchild (parent = Child),
    // plus an unrelated Sibling (no parent, not an ancestor of any of the three). ---

    private static readonly Guid RootId = Guid.NewGuid();
    private static readonly Guid ChildId = Guid.NewGuid();
    private static readonly Guid GrandchildId = Guid.NewGuid();
    private static readonly Guid SiblingId = Guid.NewGuid();
    private static readonly Guid OtherEmployeeId = Guid.NewGuid();

    private static Objective MakeObjective(Guid id, Guid? parentId, Guid ownerId) => new()
    {
        Id = id,
        TenantId = TenantId,
        ProjectId = ProjectId,
        ParentObjectiveId = parentId,
        OwnerId = ownerId,
    };

    private (MilestoneMembershipCoordinator Coordinator, Mock<IProjectMemberRepository> Members) BuildTreeCoordinator(
        Objective root, Objective child, Objective grandchild, Objective sibling)
    {
        var (coordinator, members, objectives) = BuildCoordinator(ActiveEmployee(), new Mock<IObjectiveRepository>());

        objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, RootId, It.IsAny<CancellationToken>())).ReturnsAsync(root);
        objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ChildId, It.IsAny<CancellationToken>())).ReturnsAsync(child);
        objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, GrandchildId, It.IsAny<CancellationToken>())).ReturnsAsync(grandchild);
        objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, SiblingId, It.IsAny<CancellationToken>())).ReturnsAsync(sibling);

        foreach (var id in new[] { RootId, ChildId, GrandchildId, SiblingId })
            members.Setup(x => x.ListActiveForObjectiveAsync(TenantId, id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<ProjectMember>());

        return (coordinator, members);
    }

    [Fact]
    public async Task IsEffectiveManagerAsync_SelfOwner_ReturnsTrue()
    {
        var root = MakeObjective(RootId, null, OtherEmployeeId);
        var child = MakeObjective(ChildId, RootId, OtherEmployeeId);
        var grandchild = MakeObjective(GrandchildId, ChildId, EmployeeId);
        var sibling = MakeObjective(SiblingId, null, OtherEmployeeId);
        var (coordinator, _) = BuildTreeCoordinator(root, child, grandchild, sibling);

        var result = await coordinator.IsEffectiveManagerAsync(TenantId, GrandchildId, EmployeeId, CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task IsEffectiveManagerAsync_SelfActiveMember_ReturnsTrue()
    {
        var root = MakeObjective(RootId, null, OtherEmployeeId);
        var child = MakeObjective(ChildId, RootId, OtherEmployeeId);
        var grandchild = MakeObjective(GrandchildId, ChildId, OtherEmployeeId);
        var sibling = MakeObjective(SiblingId, null, OtherEmployeeId);
        var (coordinator, members) = BuildTreeCoordinator(root, child, grandchild, sibling);
        members.Setup(x => x.ListActiveForObjectiveAsync(TenantId, GrandchildId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new ProjectMember { Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, ObjectiveId = GrandchildId, EmployeeId = EmployeeId, IsActive = true } });

        var result = await coordinator.IsEffectiveManagerAsync(TenantId, GrandchildId, EmployeeId, CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task IsEffectiveManagerAsync_ParentOwner_ReturnsTrue()
    {
        var root = MakeObjective(RootId, null, OtherEmployeeId);
        var child = MakeObjective(ChildId, RootId, EmployeeId);
        var grandchild = MakeObjective(GrandchildId, ChildId, OtherEmployeeId);
        var sibling = MakeObjective(SiblingId, null, OtherEmployeeId);
        var (coordinator, _) = BuildTreeCoordinator(root, child, grandchild, sibling);

        var result = await coordinator.IsEffectiveManagerAsync(TenantId, GrandchildId, EmployeeId, CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task IsEffectiveManagerAsync_GrandparentActiveMember_ReturnsTrue()
    {
        var root = MakeObjective(RootId, null, OtherEmployeeId);
        var child = MakeObjective(ChildId, RootId, OtherEmployeeId);
        var grandchild = MakeObjective(GrandchildId, ChildId, OtherEmployeeId);
        var sibling = MakeObjective(SiblingId, null, OtherEmployeeId);
        var (coordinator, members) = BuildTreeCoordinator(root, child, grandchild, sibling);
        members.Setup(x => x.ListActiveForObjectiveAsync(TenantId, RootId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new ProjectMember { Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, ObjectiveId = RootId, EmployeeId = EmployeeId, IsActive = true } });

        var result = await coordinator.IsEffectiveManagerAsync(TenantId, GrandchildId, EmployeeId, CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task IsEffectiveManagerAsync_SiblingOwner_ReturnsFalse()
    {
        var root = MakeObjective(RootId, null, OtherEmployeeId);
        var child = MakeObjective(ChildId, RootId, OtherEmployeeId);
        var grandchild = MakeObjective(GrandchildId, ChildId, OtherEmployeeId);
        var sibling = MakeObjective(SiblingId, null, EmployeeId);
        var (coordinator, _) = BuildTreeCoordinator(root, child, grandchild, sibling);

        var result = await coordinator.IsEffectiveManagerAsync(TenantId, GrandchildId, EmployeeId, CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task IsEffectiveManagerAsync_NoRelationship_ReturnsFalse()
    {
        var root = MakeObjective(RootId, null, OtherEmployeeId);
        var child = MakeObjective(ChildId, RootId, OtherEmployeeId);
        var grandchild = MakeObjective(GrandchildId, ChildId, OtherEmployeeId);
        var sibling = MakeObjective(SiblingId, null, OtherEmployeeId);
        var (coordinator, _) = BuildTreeCoordinator(root, child, grandchild, sibling);

        var result = await coordinator.IsEffectiveManagerAsync(TenantId, GrandchildId, EmployeeId, CancellationToken.None);

        Assert.False(result);
    }
}
