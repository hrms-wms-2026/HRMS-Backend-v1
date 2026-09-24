using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetSprintTasks;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class GetSprintTasksQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid CallerEmployeeId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();
    private static readonly Guid SprintId = Guid.NewGuid();
    private static readonly Guid OtherSprintId = Guid.NewGuid();

    private static Sprint SprintOf(Guid projectId) => new()
    {
        Id = SprintId,
        TenantId = TenantId,
        ProjectId = projectId,
        Name = "Sprint 1",
        StartDate = new DateOnly(2026, 8, 1),
        EndDate = new DateOnly(2026, 8, 14),
        Status = SprintStatuses.Active,
        CreatedAt = DateTimeOffset.UtcNow
    };

    private static WorkTask TaskOn(Guid sprintId, Guid id, string title) => new()
    {
        Id = id,
        TenantId = TenantId,
        ProjectId = ProjectId,
        ObjectiveId = ObjectiveId,
        SprintId = sprintId,
        Title = title,
        ShortId = title,
        CreatedAt = DateTimeOffset.UtcNow
    };

    private (GetSprintTasksQueryHandler Handler, Mock<IWorkTaskRepository> Tasks, Mock<IProjectMemberRepository> Members) BuildHandler(
        Sprint? sprint,
        bool hasReadPermission = false,
        bool hasActiveMembership = false,
        IReadOnlyList<WorkTask>? sprintTasks = null,
        IReadOnlyList<TaskAssignment>? assignments = null)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CallerEmployeeId);

        var sprints = new Mock<ISprintRepository>();
        sprints.Setup(x => x.GetByIdForTenantAsync(TenantId, SprintId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sprint);

        var members = new Mock<IProjectMemberRepository>();
        if (sprint is not null)
            members.Setup(x => x.HasActiveMembershipAsync(TenantId, sprint.ProjectId, CallerEmployeeId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(hasActiveMembership);

        var permissionResolver = new Mock<IPermissionResolver>();
        permissionResolver.Setup(x => x.ResolveAsync(UserId, TenantId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(hasReadPermission ? new List<string> { "projects:read" } : new List<string>());

        var thisSprintTask = TaskOn(SprintId, Guid.NewGuid(), "This sprint");
        var tasks = new Mock<IWorkTaskRepository>();
        tasks.Setup(x => x.GetBySprintIdAsync(TenantId, SprintId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sprintTasks ?? new List<WorkTask> { thisSprintTask });

        var assignmentRepo = new Mock<ITaskAssignmentRepository>();
        assignmentRepo.Setup(x => x.GetByTaskIdsAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(assignments ?? new List<TaskAssignment>());

        var handler = new GetSprintTasksQueryHandler(
            currentUser.Object, identity.Object, sprints.Object, members.Object,
            permissionResolver.Object, tasks.Object, assignmentRepo.Object);

        return (handler, tasks, members);
    }

    [Fact]
    public async Task Handle_ProjectMember_ReturnsOnlyThatSprintsTasks()
    {
        var (handler, tasks, _) = BuildHandler(
            SprintOf(ProjectId),
            hasActiveMembership: true);

        var result = await handler.Handle(new GetSprintTasksQuery(SprintId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var returned = Assert.Single(result.Value!);
        Assert.Equal(SprintId, returned.SprintId);
        tasks.Verify(x => x.GetBySprintIdAsync(TenantId, SprintId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ReadPermissionNonMember_ReturnsTasks()
    {
        var (handler, _, members) = BuildHandler(
            SprintOf(ProjectId),
            hasReadPermission: true,
            hasActiveMembership: false);

        var result = await handler.Handle(new GetSprintTasksQuery(SprintId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!);
        members.Verify(x => x.HasActiveMembershipAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_NoMembershipAndNoPermission_ReturnsForbidden()
    {
        var (handler, tasks, _) = BuildHandler(
            SprintOf(ProjectId),
            hasActiveMembership: false);

        var result = await handler.Handle(new GetSprintTasksQuery(SprintId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        tasks.Verify(x => x.GetBySprintIdAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_UnknownSprint_ReturnsNotFound()
    {
        var (handler, _, _) = BuildHandler(sprint: null);

        var result = await handler.Handle(new GetSprintTasksQuery(SprintId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_PopulatesAssigneeEmployeeIds_FromBulkAssignmentLookup()
    {
        var taskWithAssignee = Guid.NewGuid();
        var taskWithoutAssignee = Guid.NewGuid();
        var assigneeEmployeeId = Guid.NewGuid();

        var (handler, _, _) = BuildHandler(
            SprintOf(ProjectId),
            hasActiveMembership: true,
            sprintTasks: new List<WorkTask>
            {
                TaskOn(SprintId, taskWithAssignee, "A"),
                TaskOn(SprintId, taskWithoutAssignee, "B")
            },
            assignments: new List<TaskAssignment>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    TaskId = taskWithAssignee,
                    EmployeeId = assigneeEmployeeId,
                    UserId = Guid.NewGuid(),
                    AssignedById = Guid.NewGuid(),
                    AssignedAt = DateTimeOffset.UtcNow
                }
            });

        var result = await handler.Handle(new GetSprintTasksQuery(SprintId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var assigned = result.Value!.Single(t => t.Id == taskWithAssignee);
        var unassigned = result.Value!.Single(t => t.Id == taskWithoutAssignee);
        Assert.Equal([assigneeEmployeeId], assigned.AssigneeEmployeeIds);
        Assert.Empty(unassigned.AssigneeEmployeeIds!);
    }
}
