using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class TaskAccessResolverTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly Guid TaskId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();

    private (TaskAccessResolver Resolver, Mock<IWorkTaskRepository> Tasks, Mock<IProjectRepository> Projects,
        Mock<IProjectMemberRepository> Members, Mock<IPermissionResolver> Permissions) Build(
        params ONEVO.Domain.Features.WorkManagement.Objectives.Entities.Objective[] modules)
    {
        var tasks = new Mock<IWorkTaskRepository>();
        var projects = new Mock<IProjectRepository>();
        var members = new Mock<IProjectMemberRepository>();
        var permissions = new Mock<IPermissionResolver>();
        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmployeeId);

        var resolver = new TaskAccessResolver(identity.Object, tasks.Object, projects.Object, members.Object, permissions.Object,
            WorkHierarchyServiceMocks.WithModules(modules).Object);
        return (resolver, tasks, projects, members, permissions);
    }

    private static WorkTask Task_(Guid objectiveId) => new() { Id = TaskId, TenantId = TenantId, ProjectId = ProjectId, ObjectiveId = objectiveId };
    private static Project ActiveProject() => new() { Id = ProjectId, TenantId = TenantId, IsActive = true };

    [Fact]
    public async Task ResolveViewableTaskAsync_TaskNotFound_ReturnsNotFound()
    {
        var (resolver, tasks, _, _, _) = Build();
        tasks.Setup(x => x.GetByIdForTenantAsync(TenantId, TaskId, It.IsAny<CancellationToken>())).ReturnsAsync((WorkTask?)null);

        var result = await resolver.ResolveViewableTaskAsync(TenantId, UserId, TaskId, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task ResolveViewableTaskAsync_HasProjectsReadPermission_Succeeds()
    {
        var (resolver, tasks, projects, _, permissions) = Build();
        tasks.Setup(x => x.GetByIdForTenantAsync(TenantId, TaskId, It.IsAny<CancellationToken>())).ReturnsAsync(Task_(ObjectiveId));
        projects.Setup(x => x.GetByIdForTenantAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(ActiveProject());
        permissions.Setup(x => x.ResolveAsync(UserId, TenantId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "*" });

        var result = await resolver.ResolveViewableTaskAsync(TenantId, UserId, TaskId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(EmployeeId, result.Value!.CallerEmployeeId);
    }

    [Fact]
    public async Task ResolveViewableTaskAsync_NoPermissionAndNotAnObjectiveMember_ReturnsNotFound()
    {
        var (resolver, tasks, projects, members, permissions) = Build();
        tasks.Setup(x => x.GetByIdForTenantAsync(TenantId, TaskId, It.IsAny<CancellationToken>())).ReturnsAsync(Task_(ObjectiveId));
        projects.Setup(x => x.GetByIdForTenantAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(ActiveProject());
        permissions.Setup(x => x.ResolveAsync(UserId, TenantId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());
        members.Setup(x => x.GetActiveObjectiveIdsForEmployeeInProjectAsync(TenantId, ProjectId, EmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid>());

        var result = await resolver.ResolveViewableTaskAsync(TenantId, UserId, TaskId, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task ResolveViewableTaskAsync_NoPermissionButIsObjectiveMember_Succeeds()
    {
        var (resolver, tasks, projects, members, permissions) = Build();
        tasks.Setup(x => x.GetByIdForTenantAsync(TenantId, TaskId, It.IsAny<CancellationToken>())).ReturnsAsync(Task_(ObjectiveId));
        projects.Setup(x => x.GetByIdForTenantAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(ActiveProject());
        permissions.Setup(x => x.ResolveAsync(UserId, TenantId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());
        members.Setup(x => x.GetActiveObjectiveIdsForEmployeeInProjectAsync(TenantId, ProjectId, EmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { ObjectiveId });

        var result = await resolver.ResolveViewableTaskAsync(TenantId, UserId, TaskId, CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task ResolveViewableTaskAsync_MemberOfAncestorModule_Succeeds()
    {
        // Comments, reactions and attachments go through this resolver: a parent-module member
        // (e.g. the tenant owner on the root module) must reach tasks in child modules.
        var root = Guid.NewGuid();
        var (resolver, tasks, projects, members, permissions) = Build(
            WorkHierarchyServiceMocks.Module(root, null), WorkHierarchyServiceMocks.Module(ObjectiveId, root));
        tasks.Setup(x => x.GetByIdForTenantAsync(TenantId, TaskId, It.IsAny<CancellationToken>())).ReturnsAsync(Task_(ObjectiveId));
        projects.Setup(x => x.GetByIdForTenantAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(ActiveProject());
        permissions.Setup(x => x.ResolveAsync(UserId, TenantId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());
        members.Setup(x => x.GetActiveObjectiveIdsForEmployeeInProjectAsync(TenantId, ProjectId, EmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { root });

        var result = await resolver.ResolveViewableTaskAsync(TenantId, UserId, TaskId, CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task ResolveViewableTaskAsync_AssigneePrivateTask_AssigneeSucceeds()
    {
        var (resolver, tasks, projects, _, permissions) = Build();
        var task = Task_(ObjectiveId);
        task.TaskKind = WorkTaskKinds.EmployeeChecklist;
        task.VisibilityScope = WorkTaskVisibilityScopes.Assignees;
        tasks.Setup(x => x.GetByIdForTenantAsync(TenantId, TaskId, It.IsAny<CancellationToken>())).ReturnsAsync(task);
        tasks.Setup(x => x.IsAssignedToEmployeeAsync(TaskId, EmployeeId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        projects.Setup(x => x.GetByIdForTenantAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(ActiveProject());
        permissions.Setup(x => x.ResolveAsync(UserId, TenantId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        var result = await resolver.ResolveViewableTaskAsync(TenantId, UserId, TaskId, CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task ResolveViewableTaskAsync_AssigneePrivateTask_UnrelatedMemberGetsNotFound()
    {
        var (resolver, tasks, projects, members, permissions) = Build();
        var task = Task_(ObjectiveId);
        task.TaskKind = WorkTaskKinds.EmployeeChecklist;
        task.VisibilityScope = WorkTaskVisibilityScopes.Assignees;
        tasks.Setup(x => x.GetByIdForTenantAsync(TenantId, TaskId, It.IsAny<CancellationToken>())).ReturnsAsync(task);
        tasks.Setup(x => x.IsAssignedToEmployeeAsync(TaskId, EmployeeId, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        projects.Setup(x => x.GetByIdForTenantAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(ActiveProject());
        permissions.Setup(x => x.ResolveAsync(UserId, TenantId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());
        members.Setup(x => x.GetActiveObjectiveIdsForEmployeeInProjectAsync(
            TenantId, ProjectId, EmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { ObjectiveId });

        var result = await resolver.ResolveViewableTaskAsync(TenantId, UserId, TaskId, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task ResolveViewableTasksAsync_ReturnsOnlyTheVisibleTasks_InOneRoundTrip()
    {
        var visibleObjective = Guid.NewGuid();
        var hiddenObjective = Guid.NewGuid();
        var (resolver, tasks, projects, members, permissions) = Build();
        var visible = new WorkTask { Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, ObjectiveId = visibleObjective };
        var hidden = new WorkTask { Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, ObjectiveId = hiddenObjective };
        ProjectsAre(projects, ActiveProject());
        permissions.Setup(x => x.ResolveAsync(UserId, TenantId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());
        MembershipsAre(members, (ProjectId, visibleObjective));

        var result = await resolver.ResolveViewableTasksAsync(TenantId, UserId, new[] { visible, hidden }, CancellationToken.None);

        Assert.Equal(new[] { visible.Id }, result.Select(t => t.Id));
        // One project lookup and one membership lookup for the whole set - not one per task.
        projects.Verify(x => x.ListByIdsAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()), Times.Once);
        members.Verify(x => x.GetActiveObjectiveIdsForEmployeeInProjectsAsync(
            TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), EmployeeId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ResolveViewableTasksAsync_AnAssigneeScopeTaskTheCallerIsNotOnIsDropped()
    {
        var (resolver, tasks, projects, members, permissions) = Build();
        var task = Task_(ObjectiveId);
        task.VisibilityScope = WorkTaskVisibilityScopes.Assignees;
        ProjectsAre(projects, ActiveProject());
        permissions.Setup(x => x.ResolveAsync(UserId, TenantId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());
        MembershipsAre(members, (ProjectId, ObjectiveId));
        tasks.Setup(x => x.GetAssignedEmployeeIdsByTaskIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<Guid>>());

        var result = await resolver.ResolveViewableTasksAsync(TenantId, UserId, new[] { task }, CancellationToken.None);

        Assert.Empty(result);
    }

    /// <summary>Improvements task 14: tasks spread over many projects still cost one project lookup, one
    /// membership lookup and one tree load - and an inactive project's tasks are still dropped.</summary>
    [Fact]
    public async Task ResolveViewableTasksAsync_UsesOneBatchedCallPerLookup_HoweverManyProjects()
    {
        var (resolver, _, projects, members, permissions) = Build();
        var projectRows = Enumerable.Range(0, 5).Select(i => new Project { Id = Guid.NewGuid(), TenantId = TenantId, IsActive = i != 4 }).ToList();
        var objectives = projectRows.ToDictionary(p => p.Id, _ => Guid.NewGuid());
        var taskList = projectRows.Select(p => new WorkTask { Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = p.Id, ObjectiveId = objectives[p.Id] }).ToList();
        ProjectsAre(projects, projectRows.ToArray());
        permissions.Setup(x => x.ResolveAsync(UserId, TenantId, null, It.IsAny<CancellationToken>())).ReturnsAsync(new List<string>());
        MembershipsAre(members, projectRows.Select(p => (p.Id, objectives[p.Id])).ToArray());

        var result = await resolver.ResolveViewableTasksAsync(TenantId, UserId, taskList, CancellationToken.None);

        Assert.Equal(taskList.Take(4).Select(t => t.Id), result.Select(t => t.Id));
        projects.Verify(x => x.ListByIdsAsync(TenantId, It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 5), It.IsAny<CancellationToken>()), Times.Once);
        projects.Verify(x => x.GetByIdForTenantAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        members.Verify(x => x.GetActiveObjectiveIdsForEmployeeInProjectsAsync(
            TenantId, It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 4), EmployeeId, It.IsAny<CancellationToken>()), Times.Once);
        members.Verify(x => x.GetActiveObjectiveIdsForEmployeeInProjectAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task FilterViewableTasksAsync_LooksUpEveryPrivateTasksProjectInOneCall()
    {
        var (resolver, tasks, projects, _, permissions) = Build();
        var leadProject = new Project { Id = Guid.NewGuid(), TenantId = TenantId, LeadId = EmployeeId, IsActive = true };
        var otherProjects = Enumerable.Range(0, 3).Select(_ => new Project { Id = Guid.NewGuid(), TenantId = TenantId, LeadId = Guid.NewGuid(), IsActive = true }).ToList();
        WorkTask Private(Project p) => new()
        {
            Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = p.Id, ObjectiveId = ObjectiveId,
            VisibilityScope = WorkTaskVisibilityScopes.Assignees, CreatedById = Guid.NewGuid()
        };
        var led = Private(leadProject);
        var hiddenTasks = otherProjects.Select(Private).ToList();
        ProjectsAre(projects, otherProjects.Append(leadProject).ToArray());
        permissions.Setup(x => x.ResolveAsync(UserId, TenantId, null, It.IsAny<CancellationToken>())).ReturnsAsync(new List<string>());
        tasks.Setup(x => x.GetAssignedEmployeeIdsByTaskIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<Guid>>());

        var result = await resolver.FilterViewableTasksAsync(TenantId, UserId, EmployeeId, hiddenTasks.Append(led).ToList(), CancellationToken.None);

        Assert.Equal(new[] { led.Id }, result.Select(t => t.Id));
        projects.Verify(x => x.ListByIdsAsync(TenantId, It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 4), It.IsAny<CancellationToken>()), Times.Once);
        projects.Verify(x => x.GetByIdForTenantAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static void ProjectsAre(Mock<IProjectRepository> projects, params Project[] rows)
        => projects.Setup(x => x.ListByIdsAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, IReadOnlyCollection<Guid> ids, CancellationToken _) => rows.Where(p => ids.Contains(p.Id)).ToList());

    private static void MembershipsAre(Mock<IProjectMemberRepository> members, params (Guid ProjectId, Guid ObjectiveId)[] rows)
        => members.Setup(x => x.GetActiveObjectiveIdsForEmployeeInProjectsAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), EmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, IReadOnlyCollection<Guid> ids, Guid _, CancellationToken _) => rows
                .Where(r => ids.Contains(r.ProjectId))
                .GroupBy(r => r.ProjectId)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<Guid>)g.Select(r => r.ObjectiveId).ToList()));
}
