using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Queries.GetMyProjectMilestones;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.ProjectMembers.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public class GetMyProjectMilestonesQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid DefaultObjectiveId = Guid.NewGuid();
    private static readonly Guid MilestoneId = Guid.NewGuid();
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly Guid ReportingManagerId = Guid.NewGuid();

    private static ProjectMember Membership(Guid objectiveId, bool isActive = true, DateTimeOffset? removedAt = null) => new()
    {
        Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, ObjectiveId = objectiveId, UserId = UserId,
        EmployeeId = Guid.NewGuid(), IsActive = isActive, RemovedAt = removedAt, JoinedAt = DateTimeOffset.UtcNow
    };

    private static Objective DefaultObjective() => new()
    {
        Id = DefaultObjectiveId, TenantId = TenantId, ProjectId = ProjectId, IsDefault = true, IsActive = true,
        Title = "Default", OwnerId = OwnerId, ReportingManagerId = null,
        StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 6, 1), AllocatedHours = 40m, CompletedHours = 0m
    };

    private static Objective Milestone(bool isActive = true, bool isAchieved = false) => new()
    {
        Id = MilestoneId, TenantId = TenantId, ProjectId = ProjectId, ParentObjectiveId = DefaultObjectiveId, IsDefault = false, IsActive = isActive,
        Title = "Design Phase", OwnerId = OwnerId, ReportingManagerId = ReportingManagerId, IsAchieved = isAchieved,
        StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 3, 1), AllocatedHours = 20m, CompletedHours = 5m
    };

    private static Employee Owner() => new() { Id = Guid.NewGuid(), TenantId = TenantId, UserId = OwnerId, FirstName = "Alice", LastName = "Owner" };
    private static Employee ReportingManager() => new() { Id = Guid.NewGuid(), TenantId = TenantId, UserId = ReportingManagerId, FirstName = "Bob", LastName = "Manager" };

    private (GetMyProjectMilestonesQueryHandler Handler, Mock<IEmployeeRepository> Employees) BuildHandler(
        List<ProjectMember> memberships, List<Objective> objectives, List<Employee>? employees = null, Guid? callerId = null)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(callerId ?? UserId);

        var members = new Mock<IProjectMemberRepository>();
        members.Setup(x => x.ListForUserInProjectAsync(TenantId, ProjectId, callerId ?? UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(memberships);

        var objectivesRepo = new Mock<IObjectiveRepository>();
        objectivesRepo.Setup(x => x.GetAllByProjectIdAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(objectives);

        var employeesRepo = new Mock<IEmployeeRepository>();
        employeesRepo.Setup(x => x.GetByUserIdsAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(employees ?? new List<Employee>());

        var handler = new GetMyProjectMilestonesQueryHandler(currentUser.Object, members.Object, objectivesRepo.Object, employeesRepo.Object);
        return (handler, employeesRepo);
    }

    [Fact]
    public async Task Handle_NoMemberships_ReturnsEmptyList()
    {
        var (handler, _) = BuildHandler(new List<ProjectMember>(), new List<Objective>());

        var result = await handler.Handle(new GetMyProjectMilestonesQuery(ProjectId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!);
    }

    [Fact]
    public async Task Handle_ActiveMembership_ReturnsMilestoneWithResolvedNames()
    {
        var (handler, _) = BuildHandler(
            new List<ProjectMember> { Membership(MilestoneId) },
            new List<Objective> { Milestone() },
            new List<Employee> { Owner(), ReportingManager() });

        var result = await handler.Handle(new GetMyProjectMilestonesQuery(ProjectId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!);
        Assert.Equal(MilestoneId, item.ObjectiveId);
        Assert.Equal("Alice Owner", item.OwnerName);
        Assert.Equal("Bob Manager", item.ReportingManagerName);
        Assert.True(item.MembershipIsActive);
        Assert.Null(item.MembershipRemovedAt);
    }

    [Fact]
    public async Task Handle_RemovedMembership_StillIncludedWithMembershipIsActiveFalse()
    {
        var removedAt = DateTimeOffset.UtcNow;
        var (handler, _) = BuildHandler(
            new List<ProjectMember> { Membership(MilestoneId, isActive: false, removedAt: removedAt) },
            new List<Objective> { Milestone() },
            new List<Employee> { Owner(), ReportingManager() });

        var result = await handler.Handle(new GetMyProjectMilestonesQuery(ProjectId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!);
        Assert.False(item.MembershipIsActive);
        Assert.Equal(removedAt, item.MembershipRemovedAt);
    }

    [Fact]
    public async Task Handle_MilestoneSoftDeletedAndAchieved_StillIncludedWithOwnStatus()
    {
        var (handler, _) = BuildHandler(
            new List<ProjectMember> { Membership(MilestoneId) },
            new List<Objective> { Milestone(isActive: false, isAchieved: true) },
            new List<Employee> { Owner(), ReportingManager() });

        var result = await handler.Handle(new GetMyProjectMilestonesQuery(ProjectId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!);
        Assert.False(item.ObjectiveIsActive);
        Assert.True(item.IsAchieved);
        Assert.True(item.MembershipIsActive);
    }

    [Fact]
    public async Task Handle_DefaultObjectiveMembership_ReportingManagerFieldsNull()
    {
        var (handler, _) = BuildHandler(
            new List<ProjectMember> { Membership(DefaultObjectiveId) },
            new List<Objective> { DefaultObjective() },
            new List<Employee> { Owner() });

        var result = await handler.Handle(new GetMyProjectMilestonesQuery(ProjectId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!);
        Assert.True(item.IsDefault);
        Assert.Null(item.ReportingManagerId);
        Assert.Null(item.ReportingManagerName);
        Assert.Equal("Alice Owner", item.OwnerName);
    }

    [Fact]
    public async Task Handle_UnresolvableEmployee_NameFieldIsNull()
    {
        var (handler, _) = BuildHandler(
            new List<ProjectMember> { Membership(MilestoneId) },
            new List<Objective> { Milestone() },
            new List<Employee>());

        var result = await handler.Handle(new GetMyProjectMilestonesQuery(ProjectId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!);
        Assert.Null(item.OwnerName);
        Assert.Null(item.ReportingManagerName);
    }

    [Fact]
    public async Task Handle_TwoMilestonesSameOwner_BatchLookupCalledOnceWithDedupedIds()
    {
        var secondMilestoneId = Guid.NewGuid();
        var secondMilestone = new Objective
        {
            Id = secondMilestoneId, TenantId = TenantId, ProjectId = ProjectId, ParentObjectiveId = DefaultObjectiveId, IsActive = true,
            Title = "Second Phase", OwnerId = OwnerId, ReportingManagerId = ReportingManagerId,
            StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 3, 1), AllocatedHours = 10m, CompletedHours = 0m
        };

        var (handler, employees) = BuildHandler(
            new List<ProjectMember> { Membership(MilestoneId), Membership(secondMilestoneId) },
            new List<Objective> { Milestone(), secondMilestone },
            new List<Employee> { Owner(), ReportingManager() });

        var result = await handler.Handle(new GetMyProjectMilestonesQuery(ProjectId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Count);
        employees.Verify(x => x.GetByUserIdsAsync(TenantId,
            It.Is<IReadOnlyList<Guid>>(ids => ids.Distinct().Count() == ids.Count && ids.Contains(OwnerId) && ids.Contains(ReportingManagerId)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_CallerIsOwner_IsOwnerTrue()
    {
        var (handler, _) = BuildHandler(
            new List<ProjectMember> { Membership(MilestoneId) },
            new List<Objective> { Milestone() },
            new List<Employee> { Owner(), ReportingManager() },
            callerId: OwnerId);

        var result = await handler.Handle(new GetMyProjectMilestonesQuery(ProjectId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(Assert.Single(result.Value!).IsOwner);
    }

    [Fact]
    public async Task Handle_CallerIsNotOwner_IsOwnerFalse()
    {
        var (handler, _) = BuildHandler(
            new List<ProjectMember> { Membership(MilestoneId) },
            new List<Objective> { Milestone() },
            new List<Employee> { Owner(), ReportingManager() });

        var result = await handler.Handle(new GetMyProjectMilestonesQuery(ProjectId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(Assert.Single(result.Value!).IsOwner);
    }

    [Fact]
    public async Task Handle_NotAuthenticated_ReturnsForbidden()
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(false);

        var members = new Mock<IProjectMemberRepository>();
        var objectivesRepo = new Mock<IObjectiveRepository>();
        var employeesRepo = new Mock<IEmployeeRepository>();

        var handler = new GetMyProjectMilestonesQueryHandler(currentUser.Object, members.Object, objectivesRepo.Object, employeesRepo.Object);

        var result = await handler.Handle(new GetMyProjectMilestonesQuery(ProjectId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }
}
