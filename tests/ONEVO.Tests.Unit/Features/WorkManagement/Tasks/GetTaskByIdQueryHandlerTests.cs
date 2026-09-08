using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetTaskById;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public sealed class GetTaskByIdQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();
    private static readonly Guid TaskId = Guid.NewGuid();

    private static Project ActiveProject() => new()
    {
        Id = ProjectId, TenantId = TenantId, IsActive = true,
        Name = "Project", Identifier = "P1", CreatedAt = DateTimeOffset.UtcNow
    };

    private static WorkTask Task() => new()
    {
        Id = TaskId, TenantId = TenantId, ObjectiveId = ObjectiveId, ProjectId = ProjectId,
        ShortId = "P-1", Title = "A task", CategoryId = Guid.NewGuid(), StatusId = Guid.NewGuid(),
        CreatedAt = DateTimeOffset.UtcNow
    };

    private static GetTaskByIdQueryHandler BuildHandler(
        WorkTask? task, Project? project, bool hasReadPermission,
        IReadOnlyList<Guid>? accessibleObjectiveIds = null, bool authenticated = true, bool employeeExists = true)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(authenticated);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(employeeExists ? EmployeeId : (Guid?)null);

        var tasks = new Mock<IWorkTaskRepository>();
        tasks.Setup(x => x.GetByIdForTenantAsync(TenantId, TaskId, It.IsAny<CancellationToken>())).ReturnsAsync(task);

        var projects = new Mock<IProjectRepository>();
        projects.Setup(x => x.GetByIdForTenantAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(project);

        var members = new Mock<IProjectMemberRepository>();
        members.Setup(x => x.GetActiveObjectiveIdsForEmployeeInProjectAsync(TenantId, ProjectId, EmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(accessibleObjectiveIds ?? Array.Empty<Guid>());

        var permissions = new Mock<IPermissionResolver>();
        permissions.Setup(x => x.ResolveAsync(UserId, TenantId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(hasReadPermission ? new List<string> { "projects:read" } : new List<string>());

        var assignments = new Mock<ITaskAssignmentRepository>();
        assignments.Setup(x => x.GetByTaskIdsAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<TaskAssignment>());

        var sessions = new Mock<ITaskClockingSessionRepository>();
        sessions.Setup(x => x.GetOpenSessionsForTasksAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, OpenTaskClockingSessionSummary>());
        sessions.Setup(x => x.GetTotalClosedSessionMinutesForTasksAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, int>());

        return new GetTaskByIdQueryHandler(
            currentUser.Object, identity.Object, tasks.Object, projects.Object,
            members.Object, permissions.Object, assignments.Object, sessions.Object);
    }

    [Fact]
    public async Task Handle_ReadPermission_ReturnsTask()
    {
        var handler = BuildHandler(Task(), ActiveProject(), hasReadPermission: true);
        var result = await handler.Handle(new GetTaskByIdQuery(TaskId), CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.Equal(TaskId, result.Value!.Id);
        Assert.Equal(ObjectiveId, result.Value!.ObjectiveId);
    }

    [Fact]
    public async Task Handle_MemberOfTasksObjective_ReturnsTask()
    {
        var handler = BuildHandler(Task(), ActiveProject(), hasReadPermission: false, accessibleObjectiveIds: new[] { ObjectiveId });
        var result = await handler.Handle(new GetTaskByIdQuery(TaskId), CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.Equal(TaskId, result.Value!.Id);
    }

    [Fact]
    public async Task Handle_NotAMemberAndNoPermission_ReturnsNotFound()
    {
        var handler = BuildHandler(Task(), ActiveProject(), hasReadPermission: false, accessibleObjectiveIds: Array.Empty<Guid>());
        var result = await handler.Handle(new GetTaskByIdQuery(TaskId), CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_TaskDoesNotExist_ReturnsNotFound()
    {
        var handler = BuildHandler(task: null, ActiveProject(), hasReadPermission: true);
        var result = await handler.Handle(new GetTaskByIdQuery(TaskId), CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_NotAuthenticated_ReturnsForbidden()
    {
        var handler = BuildHandler(Task(), ActiveProject(), hasReadPermission: true, authenticated: false);
        var result = await handler.Handle(new GetTaskByIdQuery(TaskId), CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }
}
