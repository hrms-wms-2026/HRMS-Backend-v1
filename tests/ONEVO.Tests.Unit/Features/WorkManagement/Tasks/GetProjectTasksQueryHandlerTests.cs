using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetProjectTasks;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public sealed class GetProjectTasksQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid ObjectiveA = Guid.NewGuid();
    private static readonly Guid ObjectiveB = Guid.NewGuid();

    private static Project ActiveProject() => new()
    {
        Id = ProjectId,
        TenantId = TenantId,
        IsActive = true,
        Name = "Project",
        Identifier = "P1",
        CreatedAt = DateTimeOffset.UtcNow
    };

    private static WorkTask Task(Guid objectiveId, string title, Guid? sprintId = null) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = TenantId,
        ObjectiveId = objectiveId,
        ShortId = $"P-{Random.Shared.Next(1, 9999)}",
        Title = title,
        CategoryId = Guid.NewGuid(),
        StatusId = Guid.NewGuid(),
        SprintId = sprintId,
        CreatedAt = DateTimeOffset.UtcNow
    };

    private static GetProjectTasksQueryHandler BuildHandler(
        Project? project,
        IReadOnlyList<Guid> accessibleObjectiveIds,
        bool hasReadPermission,
        IReadOnlyList<WorkTask>? tasks = null,
        IReadOnlyList<TaskAssignment>? assignments = null,
        bool authenticated = true)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(authenticated);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmployeeId);

        var projects = new Mock<IProjectRepository>();
        projects.Setup(x => x.GetByIdForTenantAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(project);

        var members = new Mock<IProjectMemberRepository>();
        members.Setup(x => x.GetActiveObjectiveIdsForEmployeeInProjectAsync(
                TenantId, ProjectId, EmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(accessibleObjectiveIds);

        var permissions = new Mock<IPermissionResolver>();
        permissions.Setup(x => x.ResolveAsync(UserId, TenantId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(hasReadPermission ? new List<string> { "projects:read" } : new List<string>());

        var taskRepository = new Mock<IWorkTaskRepository>();
        taskRepository.Setup(x => x.GetByProjectAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tasks ?? Array.Empty<WorkTask>());

        var assignmentRepository = new Mock<ITaskAssignmentRepository>();
        assignmentRepository.Setup(x => x.GetByTaskIdsAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(assignments ?? Array.Empty<TaskAssignment>());

        var sessionRepository = new Mock<ITaskClockingSessionRepository>();
        sessionRepository.Setup(x => x.GetOpenSessionsForTasksAsync(
                TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, OpenTaskClockingSessionSummary>());
        sessionRepository.Setup(x => x.GetTotalClosedSessionMinutesForTasksAsync(
                TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, int>());

        return new GetProjectTasksQueryHandler(
            currentUser.Object, identity.Object, projects.Object, members.Object,
            permissions.Object, taskRepository.Object, assignmentRepository.Object, sessionRepository.Object);
    }

    [Fact]
    public async Task Handle_ReadPermission_ReturnsTasksFromMultipleObjectives()
    {
        var tasks = new[] { Task(ObjectiveA, "A task"), Task(ObjectiveB, "B task") };
        var handler = BuildHandler(ActiveProject(), Array.Empty<Guid>(), hasReadPermission: true, tasks);

        var result = await handler.Handle(new GetProjectTasksQuery(ProjectId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Count);
        Assert.Contains(result.Value!, task => task.ObjectiveId == ObjectiveA);
        Assert.Contains(result.Value!, task => task.ObjectiveId == ObjectiveB);
    }

    [Fact]
    public async Task Handle_NonPrivilegedMember_ReturnsOnlyAccessibleObjectives()
    {
        var tasks = new[] { Task(ObjectiveA, "Visible"), Task(ObjectiveB, "Hidden") };
        var handler = BuildHandler(ActiveProject(), new[] { ObjectiveA }, hasReadPermission: false, tasks);

        var result = await handler.Handle(new GetProjectTasksQuery(ProjectId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var task = Assert.Single(result.Value!);
        Assert.Equal(ObjectiveA, task.ObjectiveId);
    }

    [Fact]
    public async Task Handle_IncludesSprintlessTasks()
    {
        var sprintId = Guid.NewGuid();
        var sprintTask = Task(ObjectiveA, "Sprint task", sprintId);
        var unsortedTask = Task(ObjectiveA, "Unsorted task");
        var handler = BuildHandler(ActiveProject(), Array.Empty<Guid>(), hasReadPermission: true, new[] { sprintTask, unsortedTask });

        var result = await handler.Handle(new GetProjectTasksQuery(ProjectId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains(result.Value!, task => task.Id == sprintTask.Id && task.SprintId == sprintId);
        Assert.Contains(result.Value!, task => task.Id == unsortedTask.Id && task.SprintId is null);
    }

    [Fact]
    public async Task Handle_PopulatesAssigneeEmployeeIds()
    {
        var task = Task(ObjectiveA, "Assigned");
        var assignment = new TaskAssignment
        {
            Id = Guid.NewGuid(),
            TaskId = task.Id,
            EmployeeId = EmployeeId,
            UserId = UserId,
            AssignedById = UserId,
            AssignedAt = DateTimeOffset.UtcNow
        };
        var handler = BuildHandler(ActiveProject(), Array.Empty<Guid>(), hasReadPermission: true, new[] { task }, new[] { assignment });

        var result = await handler.Handle(new GetProjectTasksQuery(ProjectId), CancellationToken.None);

        Assert.Equal(new[] { EmployeeId }, Assert.Single(result.Value!).AssigneeEmployeeIds);
    }

    [Fact]
    public async Task Handle_MissingProject_ReturnsNotFound()
    {
        var handler = BuildHandler(null, Array.Empty<Guid>(), hasReadPermission: true);

        var result = await handler.Handle(new GetProjectTasksQuery(ProjectId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_UnauthenticatedCaller_ReturnsForbidden()
    {
        var handler = BuildHandler(ActiveProject(), Array.Empty<Guid>(), hasReadPermission: true, authenticated: false);

        var result = await handler.Handle(new GetProjectTasksQuery(ProjectId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }
}
