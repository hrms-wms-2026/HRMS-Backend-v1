using FluentAssertions;
using Moq;
using ONEVO.Application.Features.WorkManagement.Monitoring.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Monitoring.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Monitoring;

/// <summary>LoadTargetsAsync must see the same Modules, sprints and tasks as the full snapshot, or an alert's
/// creator position (and so who it goes to) would differ between the Inbox and the monitor.</summary>
public class ProjectMonitorTargetsLoaderTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();

    private readonly Mock<IObjectiveRepository> _objectives = new();
    private readonly Mock<ISprintRepository> _sprints = new();
    private readonly Mock<IWorkTaskRepository> _tasks = new();

    private ProjectMonitorSnapshotLoader Build() => new(
        _objectives.Object, Mock.Of<IProjectMemberRepository>(), _sprints.Object, _tasks.Object, Mock.Of<ITaskStatusRepository>(),
        Mock.Of<ITaskAssignmentRepository>(), Mock.Of<ITaskClockingSessionRepository>(), Mock.Of<IWorkCalendarResolver>(),
        Mock.Of<IConversionRateReader>(), Mock.Of<ITaskRateSettingRepository>());

    [Fact]
    public async Task Leaves_out_what_the_snapshot_leaves_out()
    {
        var root = new Objective { Id = Guid.NewGuid(), IsActive = true };
        var module = new Objective { Id = Guid.NewGuid(), ParentObjectiveId = root.Id, CreatorPositionObjectiveId = root.Id, IsActive = true };
        var inactive = new Objective { Id = Guid.NewGuid(), ParentObjectiveId = root.Id, IsActive = false };
        var deletedModule = new Objective { Id = Guid.NewGuid(), ParentObjectiveId = root.Id, IsActive = true, IsDeleted = true };
        _objectives.Setup(r => r.GetAllByProjectIdAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([root, module, inactive, deletedModule]);

        var sprint = new Sprint { Id = Guid.NewGuid(), CreatorPositionObjectiveId = module.Id };
        var deletedSprint = new Sprint { Id = Guid.NewGuid(), IsDeleted = true };
        _sprints.Setup(r => r.GetByProjectAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync([sprint, deletedSprint]);

        var task = new WorkTask { Id = Guid.NewGuid(), ObjectiveId = module.Id, CreatorPositionObjectiveId = root.Id };
        var deletedTask = new WorkTask { Id = Guid.NewGuid(), ObjectiveId = module.Id, IsDeleted = true };
        var taskInInactiveModule = new WorkTask { Id = Guid.NewGuid(), ObjectiveId = inactive.Id };
        _tasks.Setup(r => r.GetByProjectAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([task, deletedTask, taskInInactiveModule]);

        var targets = await Build().LoadTargetsAsync(TenantId, ProjectId);

        targets.Modules.Should().BeEquivalentTo(new[]
        {
            new MonitorTargetModule(root.Id, null, null), new MonitorTargetModule(module.Id, root.Id, root.Id),
        });
        targets.Sprints.Should().BeEquivalentTo(new[] { new MonitorTargetSprint(sprint.Id, module.Id) });
        targets.Tasks.Should().BeEquivalentTo(new[] { new MonitorTargetTask(task.Id, module.Id, root.Id) });
    }
}
