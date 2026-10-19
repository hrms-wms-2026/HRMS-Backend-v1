using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.EmployeeWorkGraph.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectWorkGraph.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.ProjectWorkGraph.Queries.GetProjectWorkGraph;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using TaskStatusEntity = ONEVO.Domain.Features.WorkManagement.Tasks.Entities.TaskStatus;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public sealed class GetProjectWorkGraphQueryHandlerTests
{
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<ICallerIdentityResolver> _identity = new();
    private readonly Mock<IProjectRepository> _projects = new();
    private readonly Mock<IProjectMemberRepository> _members = new();
    private readonly Mock<IObjectiveRepository> _objectives = new();
    private readonly Mock<IWorkTaskRepository> _tasks = new();
    private readonly Mock<IDateTimeProvider> _clock = new();
    private readonly Mock<ITaskAccessResolver> _access = new();
    private readonly Mock<ITaskStatusRepository> _statuses = new();

    private static readonly DateOnly Today = new(2026, 10, 19);
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _callerEmployeeId = Guid.NewGuid();
    private readonly Guid _projectId = Guid.NewGuid();
    private readonly Guid _rootId = Guid.NewGuid();
    private readonly Guid _childId = Guid.NewGuid();
    private readonly Guid _openStatusId = Guid.NewGuid();
    private readonly Guid _activeStatusId = Guid.NewGuid();
    private readonly Guid _doneStatusId = Guid.NewGuid();

    public GetProjectWorkGraphQueryHandlerTests()
    {
        _currentUser.SetupGet(u => u.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(u => u.TenantId).Returns(_tenantId);
        _currentUser.SetupGet(u => u.UserId).Returns(_userId);
        _identity.Setup(i => i.ResolveCallerEmployeeIdAsync(_tenantId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_callerEmployeeId);
        _identity.Setup(i => i.ResolveIdentitiesByEmployeeIdAsync(_tenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, EmployeeIdentityDto>());
        _projects.Setup(p => p.GetByIdForTenantAsync(_tenantId, _projectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Proj());
        _members.Setup(m => m.HasActiveMembershipAsync(_tenantId, _projectId, _callerEmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _members.Setup(m => m.ListActiveMemberEmployeeIdsByObjectivesAsync(_tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<Guid>>());
        _objectives.Setup(o => o.GetTreeByProjectIdAsync(_tenantId, _projectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Objective>());
        _tasks.Setup(t => t.CountByObjectivesAsync(_tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), Today, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, WorkTaskCounts>());
        _clock.SetupGet(c => c.Today).Returns(Today);
        _tasks.Setup(t => t.GetByProjectAsync(_tenantId, _projectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<WorkTask>());
        // The resolver's own rules are tested elsewhere; here it lets everything through unless a test says not.
        _access.Setup(a => a.FilterViewableTasksAsync(_tenantId, _userId, _callerEmployeeId, It.IsAny<IReadOnlyList<WorkTask>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, Guid _, Guid _, IReadOnlyList<WorkTask> tasks, CancellationToken _) => tasks);
        _statuses.Setup(r => r.GetByIdsForTenantAsync(_tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new TaskStatusEntity { Id = _openStatusId, TenantId = _tenantId, ProjectId = _projectId, Name = "To Do", Category = TaskStatusCategories.NotStarted },
                new TaskStatusEntity { Id = _activeStatusId, TenantId = _tenantId, ProjectId = _projectId, Name = "In Progress", Category = TaskStatusCategories.Active },
                new TaskStatusEntity { Id = _doneStatusId, TenantId = _tenantId, ProjectId = _projectId, Name = "Done", Category = TaskStatusCategories.Done }
            });
    }

    private GetProjectWorkGraphQueryHandler CreateHandler() =>
        new(_currentUser.Object, _identity.Object, _projects.Object, _members.Object,
            _objectives.Object, _tasks.Object, _clock.Object, _access.Object, _statuses.Object);

    private Project Proj() =>
        new() { Id = _projectId, TenantId = _tenantId, Name = "Hardware integration portal", Identifier = "HIP", IsActive = true };

    private Objective Obj(Guid id, string title, Guid? parentId, bool isDefault = false,
        decimal allocated = 0m, decimal completed = 0m) =>
        new()
        {
            Id = id,
            TenantId = _tenantId,
            ProjectId = _projectId,
            ParentObjectiveId = parentId,
            Title = title,
            OwnerId = _callerEmployeeId,
            IsDefault = isDefault,
            IsActive = true,
            StartDate = new DateOnly(2026, 5, 1),
            EndDate = new DateOnly(2026, 12, 15),
            AllocatedHours = allocated,
            CompletedHours = completed
        };

    private WorkTask Task(string title, Guid objectiveId, Guid statusId, Guid? parentTaskId = null,
        DateOnly? due = null, string visibility = WorkTaskVisibilityScopes.Module) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            ProjectId = _projectId,
            ObjectiveId = objectiveId,
            ParentTaskId = parentTaskId,
            StatusId = statusId,
            ShortId = "HIP-" + title.Length,
            Title = title,
            Priority = "high",
            DueDate = due,
            VisibilityScope = visibility
        };

    private void ArrangeTasks(params WorkTask[] tasks) =>
        _tasks.Setup(t => t.GetByProjectAsync(_tenantId, _projectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tasks);

    private void ArrangeTree(params Objective[] objectives) =>
        _objectives.Setup(o => o.GetTreeByProjectIdAsync(_tenantId, _projectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(objectives);

    private Task<Result<ProjectWorkGraphResponse>> Run() =>
        CreateHandler().Handle(new GetProjectWorkGraphQuery(_projectId), CancellationToken.None);

    [Fact]
    public async Task Handle_ReturnsRootModuleAndDescendants_AsModuleNodes()
    {
        ArrangeTree(Obj(_rootId, "Hardware integration portal", null, isDefault: true),
            Obj(_childId, "Device Connectivity Framework", _rootId));

        var result = await Run();

        Assert.True(result.IsSuccess);
        var modules = result.Value!.Nodes.Where(n => n.Kind == WorkGraphNodeKinds.Module).ToList();
        Assert.Equal(2, modules.Count);
        Assert.Contains(modules, n => n.Id == $"module:{_rootId}" && n.Label == "Hardware integration portal");
        Assert.Contains(modules, n => n.Id == $"module:{_childId}" && n.Label == "Device Connectivity Framework");
        Assert.All(modules, n => Assert.Null(n.Role));
    }

    [Fact]
    public async Task Handle_LinksEachChildToItsParent_WithContainsKind()
    {
        ArrangeTree(Obj(_rootId, "Root", null, isDefault: true), Obj(_childId, "Child", _rootId));

        var result = await Run();

        var link = Assert.Single(result.Value!.Links);
        Assert.Equal($"module:{_rootId}", link.Source);
        Assert.Equal($"module:{_childId}", link.Target);
        Assert.Equal(WorkGraphLinkKinds.Contains, link.Kind);
    }

    [Fact]
    public async Task Handle_SetsStatsFromTaskCounts_IncludingAllocatedAndCompletedHours()
    {
        ArrangeTree(Obj(_rootId, "Root", null, isDefault: true, allocated: 2600m, completed: 390m));
        _tasks.Setup(t => t.CountByObjectivesAsync(_tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), Today, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, WorkTaskCounts> { [_rootId] = new(5, 2, 1, 2, 1) });

        var result = await Run();

        var stats = Assert.Single(result.Value!.Nodes).Stats;
        Assert.NotNull(stats);
        Assert.Equal(5, stats!.TotalTasks);
        Assert.Equal(2600m, stats.AllocatedHours);
        Assert.Equal(390m, stats.CompletedHours);
    }

    [Fact]
    public async Task Handle_ReturnsForbidden_WhenCallerIsNotAnActiveProjectMember()
    {
        _members.Setup(m => m.HasActiveMembershipAsync(_tenantId, _projectId, _callerEmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await Run();

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        _objectives.Verify(o => o.GetTreeByProjectIdAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _tasks.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_ReturnsNotFound_WhenProjectIsMissingOrInactive()
    {
        _projects.Setup(p => p.GetByIdForTenantAsync(_tenantId, _projectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Project?)null);

        var result = await Run();

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ReturnsSingleNodeAndNoLinks_WhenRootHasNoChildren()
    {
        ArrangeTree(Obj(_rootId, "Root", null, isDefault: true));

        var result = await Run();

        Assert.Single(result.Value!.Nodes);
        Assert.Empty(result.Value!.Links);
        Assert.Equal(0, result.Value!.HiddenTaskCount);
    }

    [Fact]
    public async Task Handle_AddsOpenTasksOfEachModule_AsTaskNodes()
    {
        ArrangeTree(Obj(_rootId, "Root", null, isDefault: true));
        ArrangeTasks(Task("Fix timeout", _rootId, _activeStatusId), Task("Write docs", _rootId, _openStatusId),
            Task("Shipped", _rootId, _doneStatusId));

        var result = await Run();

        var tasks = result.Value!.Nodes.Where(n => n.Kind == WorkGraphNodeKinds.Task).ToList();
        Assert.Equal(2, tasks.Count);
        Assert.Contains(tasks, n => n.Label == "Fix timeout" && n.Status == TaskStatusCategories.Active && n.Task!.StatusName == "In Progress");
        Assert.Contains(tasks, n => n.Label == "Write docs" && n.Status == TaskStatusCategories.NotStarted);
        Assert.DoesNotContain(tasks, n => n.Label == "Shipped");
    }

    [Fact]
    public async Task Handle_ExcludesSubtasks_WhenTaskHasAParentTask()
    {
        ArrangeTree(Obj(_rootId, "Root", null, isDefault: true));
        var parent = Task("Parent", _rootId, _activeStatusId);
        ArrangeTasks(parent, Task("Child step", _rootId, _activeStatusId, parentTaskId: parent.Id));

        var result = await Run();

        var task = Assert.Single(result.Value!.Nodes, n => n.Kind == WorkGraphNodeKinds.Task);
        Assert.Equal("Parent", task.Label);
    }

    [Fact]
    public async Task Handle_ExcludesPrivateTasks_TheCallerIsNotAssignedTo()
    {
        ArrangeTree(Obj(_rootId, "Root", null, isDefault: true));
        var visible = Task("Visible", _rootId, _activeStatusId);
        var hidden = Task("Secret", _rootId, _activeStatusId, visibility: WorkTaskVisibilityScopes.Assignees);
        ArrangeTasks(visible, hidden);
        _access.Setup(a => a.FilterViewableTasksAsync(_tenantId, _userId, _callerEmployeeId, It.IsAny<IReadOnlyList<WorkTask>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, Guid _, Guid _, IReadOnlyList<WorkTask> tasks, CancellationToken _) =>
                tasks.Where(t => t.Id != hidden.Id).ToList());

        var result = await Run();

        var task = Assert.Single(result.Value!.Nodes, n => n.Kind == WorkGraphNodeKinds.Task);
        Assert.Equal("Visible", task.Label);
    }

    [Fact]
    public async Task Handle_DoesNotCountExcludedPrivateTasks_TowardHiddenTaskCount()
    {
        ArrangeTree(Obj(_rootId, "Root", null, isDefault: true));
        var open = Enumerable.Range(0, GetProjectWorkGraphQueryHandler.MaxTaskNodes)
            .Select(i => Task($"Task {i}", _rootId, _activeStatusId)).ToList();
        var secret = Task("Secret", _rootId, _activeStatusId, visibility: WorkTaskVisibilityScopes.Assignees);
        ArrangeTasks(open.Append(secret).ToArray());
        _access.Setup(a => a.FilterViewableTasksAsync(_tenantId, _userId, _callerEmployeeId, It.IsAny<IReadOnlyList<WorkTask>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, Guid _, Guid _, IReadOnlyList<WorkTask> tasks, CancellationToken _) =>
                tasks.Where(t => t.Id != secret.Id).ToList());

        var result = await Run();

        Assert.Equal(GetProjectWorkGraphQueryHandler.MaxTaskNodes, result.Value!.Nodes.Count(n => n.Kind == WorkGraphNodeKinds.Task));
        Assert.Equal(0, result.Value!.HiddenTaskCount);
    }

    [Fact]
    public async Task Handle_CapsTaskNodesAtMaxTaskNodes_AndReportsTheRestAsHidden()
    {
        ArrangeTree(Obj(_rootId, "Root", null, isDefault: true));
        ArrangeTasks(Enumerable.Range(0, GetProjectWorkGraphQueryHandler.MaxTaskNodes + 3)
            .Select(i => Task($"Task {i}", _rootId, _activeStatusId)).ToArray());

        var result = await Run();

        Assert.Equal(GetProjectWorkGraphQueryHandler.MaxTaskNodes, result.Value!.Nodes.Count(n => n.Kind == WorkGraphNodeKinds.Task));
        Assert.Equal(3, result.Value!.HiddenTaskCount);
    }

    [Fact]
    public async Task Handle_LinksEachTaskToItsModule_WithHasTaskKind()
    {
        ArrangeTree(Obj(_rootId, "Root", null, isDefault: true), Obj(_childId, "Child", _rootId));
        var task = Task("Pair over BLE", _childId, _openStatusId);
        ArrangeTasks(task);

        var result = await Run();

        Assert.Contains(result.Value!.Links, l =>
            l.Source == $"module:{_childId}" && l.Target == $"task:{task.Id}" && l.Kind == WorkGraphLinkKinds.HasTask);
    }

    [Fact]
    public async Task Handle_SetsIsOverdue_WhenDueDateIsBeforeToday()
    {
        ArrangeTree(Obj(_rootId, "Root", null, isDefault: true));
        ArrangeTasks(Task("Late", _rootId, _activeStatusId, due: Today.AddDays(-1)),
            Task("Due today", _rootId, _activeStatusId, due: Today));

        var result = await Run();

        var tasks = result.Value!.Nodes.Where(n => n.Kind == WorkGraphNodeKinds.Task).ToDictionary(n => n.Label);
        Assert.True(tasks["Late"].Task!.IsOverdue);
        Assert.False(tasks["Due today"].Task!.IsOverdue);
    }
}
