using Moq;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.ProjectMembers.Entities;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Sprints;

public class SprintAccessServiceTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid CreatorUserId = Guid.NewGuid();
    private static readonly Guid CallerUserId = Guid.NewGuid();
    private static readonly Guid CallerEmployeeId = Guid.NewGuid();
    private static readonly Guid ModuleA = Guid.NewGuid();
    private static readonly Guid ModuleB = Guid.NewGuid();

    private readonly Mock<IWorkTaskRepository> _tasks = new();
    private readonly Mock<IMilestoneMembershipCoordinator> _membership = new();
    private readonly Mock<IProjectMemberRepository> _members = new();

    private SprintAccessService Build() => new(_tasks.Object, _membership.Object, _members.Object);

    private static Sprint NewSprint() => new()
    {
        Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, Name = "S", CreatedById = CreatorUserId
    };

    private static WorkTask NewTask(Guid sprintId, Guid objectiveId) => new()
    {
        Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, ObjectiveId = objectiveId, SprintId = sprintId
    };

    [Fact]
    public async Task CanManage_Creator_True_WithoutTaskLookup()
    {
        var sprint = NewSprint();
        var result = await Build().CanManageAsync(TenantId, sprint, CreatorUserId, CallerEmployeeId);
        Assert.True(result);
        _tasks.Verify(x => x.GetBySprintIdAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CanManage_OwnerOfAnyTaskModule_True()
    {
        var sprint = NewSprint();
        _tasks.Setup(x => x.GetBySprintIdAsync(TenantId, sprint.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WorkTask> { NewTask(sprint.Id, ModuleA), NewTask(sprint.Id, ModuleB) });
        _membership.Setup(x => x.IsEffectiveOwnerAsync(TenantId, ModuleA, CallerEmployeeId, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _membership.Setup(x => x.IsEffectiveOwnerAsync(TenantId, ModuleB, CallerEmployeeId, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        Assert.True(await Build().CanManageAsync(TenantId, sprint, CallerUserId, CallerEmployeeId));
    }

    [Fact]
    public async Task CanManage_NotCreatorAndOwnsNoTaskModule_False()
    {
        var sprint = NewSprint();
        _tasks.Setup(x => x.GetBySprintIdAsync(TenantId, sprint.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WorkTask> { NewTask(sprint.Id, ModuleA) });
        _membership.Setup(x => x.IsEffectiveOwnerAsync(TenantId, ModuleA, CallerEmployeeId, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        Assert.False(await Build().CanManageAsync(TenantId, sprint, CallerUserId, CallerEmployeeId));
    }

    [Fact]
    public async Task CanManage_EmptySprint_NotCreator_False()
    {
        var sprint = NewSprint();
        _tasks.Setup(x => x.GetBySprintIdAsync(TenantId, sprint.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new List<WorkTask>());
        Assert.False(await Build().CanManageAsync(TenantId, sprint, CallerUserId, CallerEmployeeId));
    }

    [Fact]
    public async Task GetManageableSprintIds_BatchesOwnershipPerModule()
    {
        var mine = NewSprint();
        var owned = NewSprint();
        owned.CreatedById = Guid.NewGuid();
        var foreign = NewSprint();
        foreign.CreatedById = Guid.NewGuid();
        mine.CreatedById = CallerUserId;

        _tasks.Setup(x => x.GetByProjectAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WorkTask>
            {
                NewTask(owned.Id, ModuleA), NewTask(owned.Id, ModuleA), NewTask(foreign.Id, ModuleB)
            });
        _membership.Setup(x => x.IsEffectiveOwnerAsync(TenantId, ModuleA, CallerEmployeeId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _membership.Setup(x => x.IsEffectiveOwnerAsync(TenantId, ModuleB, CallerEmployeeId, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var ids = await Build().GetManageableSprintIdsAsync(TenantId, ProjectId, new List<Sprint> { mine, owned, foreign }, CallerUserId, CallerEmployeeId);

        Assert.Contains(mine.Id, ids);
        Assert.Contains(owned.Id, ids);
        Assert.DoesNotContain(foreign.Id, ids);
        _membership.Verify(x => x.IsEffectiveOwnerAsync(TenantId, ModuleA, CallerEmployeeId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetAudience_DistinctActiveMembersAcrossTaskModules()
    {
        var sprint = NewSprint();
        var shared = Guid.NewGuid();
        var onlyB = Guid.NewGuid();
        _tasks.Setup(x => x.GetBySprintIdAsync(TenantId, sprint.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WorkTask> { NewTask(sprint.Id, ModuleA), NewTask(sprint.Id, ModuleB) });
        _members.Setup(x => x.ListActiveForObjectiveAsync(TenantId, ModuleA, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProjectMember> { new() { EmployeeId = shared } });
        _members.Setup(x => x.ListActiveForObjectiveAsync(TenantId, ModuleB, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProjectMember> { new() { EmployeeId = shared }, new() { EmployeeId = onlyB } });

        var audience = await Build().GetAudienceEmployeeIdsAsync(TenantId, sprint.Id);

        Assert.Equal(2, audience.Count);
        Assert.Contains(shared, audience);
        Assert.Contains(onlyB, audience);
    }
}
