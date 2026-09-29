using Moq;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Sprints;

public class SprintTaskAssignmentServiceTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid Caller = Guid.NewGuid();
    private static readonly Guid OwnedModule = Guid.NewGuid();
    private static readonly Guid ForeignModule = Guid.NewGuid();

    private readonly Mock<IWorkTaskRepository> _tasks = new();
    private readonly Mock<ISprintRepository> _sprints = new();
    private readonly Mock<IMilestoneMembershipCoordinator> _membership = new();

    public SprintTaskAssignmentServiceTests()
    {
        _membership.Setup(x => x.IsEffectiveOwnerAsync(TenantId, OwnedModule, Caller, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _membership.Setup(x => x.IsEffectiveOwnerAsync(TenantId, ForeignModule, Caller, It.IsAny<CancellationToken>())).ReturnsAsync(false);
    }

    private SprintTaskAssignmentService Build() => new(_tasks.Object, _sprints.Object, _membership.Object);

    private static Sprint NewSprint(string status = SprintStatuses.Draft) =>
        new() { Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, Name = "S", Status = status };

    private WorkTask Seed(Guid objectiveId, Guid? sprintId = null, Guid? projectId = null)
    {
        var task = new WorkTask { Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = projectId ?? ProjectId, ObjectiveId = objectiveId, SprintId = sprintId, ShortId = "T-1" };
        _tasks.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, task.Id, It.IsAny<CancellationToken>())).ReturnsAsync(task);
        return task;
    }

    [Fact]
    public async Task Add_OwnedBacklogTask_IsInChangeSet_AndApplySetsSprint()
    {
        var sprint = NewSprint();
        var task = Seed(OwnedModule);

        var result = await Build().PrepareAsync(TenantId, sprint, new[] { task.Id }, Array.Empty<Guid>(), Caller);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.ToAdd);
        Build().Apply(result.Value!, sprint.Id);
        Assert.Equal(sprint.Id, task.SprintId);
    }

    [Fact]
    public async Task Add_TaskFromOtherSprint_MovesIt()
    {
        var sprint = NewSprint();
        var other = NewSprint(SprintStatuses.Active);
        _sprints.Setup(x => x.GetByIdForTenantAsync(TenantId, other.Id, It.IsAny<CancellationToken>())).ReturnsAsync(other);
        var task = Seed(OwnedModule, other.Id);

        var result = await Build().PrepareAsync(TenantId, sprint, new[] { task.Id }, Array.Empty<Guid>(), Caller);
        Build().Apply(result.Value!, sprint.Id);

        Assert.Equal(sprint.Id, task.SprintId);
    }

    [Fact]
    public async Task Add_TaskInAchievedSprint_Forbidden()
    {
        var sprint = NewSprint();
        var achieved = NewSprint(SprintStatuses.Achieved);
        _sprints.Setup(x => x.GetByIdForTenantAsync(TenantId, achieved.Id, It.IsAny<CancellationToken>())).ReturnsAsync(achieved);
        var task = Seed(OwnedModule, achieved.Id);

        var result = await Build().PrepareAsync(TenantId, sprint, new[] { task.Id }, Array.Empty<Guid>(), Caller);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Add_TaskFromModuleNotOwned_Forbidden()
    {
        var task = Seed(ForeignModule);
        var result = await Build().PrepareAsync(TenantId, NewSprint(), new[] { task.Id }, Array.Empty<Guid>(), Caller);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Add_TaskFromOtherProject_NotFound()
    {
        var task = Seed(OwnedModule, projectId: Guid.NewGuid());
        var result = await Build().PrepareAsync(TenantId, NewSprint(), new[] { task.Id }, Array.Empty<Guid>(), Caller);
        Assert.Equal(404, result.StatusCode);
    }

    [Theory]
    [InlineData(SprintStatuses.Complete)]
    [InlineData(SprintStatuses.Achieved)]
    public async Task EndedSprint_Conflict(string status)
    {
        var task = Seed(OwnedModule);
        var result = await Build().PrepareAsync(TenantId, NewSprint(status), new[] { task.Id }, Array.Empty<Guid>(), Caller);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task Remove_OwnedTaskInSprint_ClearsSprint()
    {
        var sprint = NewSprint();
        var task = Seed(OwnedModule, sprint.Id);

        var result = await Build().PrepareAsync(TenantId, sprint, Array.Empty<Guid>(), new[] { task.Id }, Caller);
        Build().Apply(result.Value!, sprint.Id);

        Assert.Null(task.SprintId);
    }

    [Fact]
    public async Task Remove_TaskFromModuleNotOwned_Forbidden()
    {
        var sprint = NewSprint();
        var task = Seed(ForeignModule, sprint.Id);
        var result = await Build().PrepareAsync(TenantId, sprint, Array.Empty<Guid>(), new[] { task.Id }, Caller);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Remove_TaskNotInThisSprint_IsIgnored()
    {
        var sprint = NewSprint();
        var task = Seed(OwnedModule, Guid.NewGuid());
        var result = await Build().PrepareAsync(TenantId, sprint, Array.Empty<Guid>(), new[] { task.Id }, Caller);
        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.IsEmpty);
    }
}
