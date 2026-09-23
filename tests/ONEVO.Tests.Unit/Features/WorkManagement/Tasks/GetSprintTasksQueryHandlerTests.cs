using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetSprintTasks;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
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
    private static readonly Guid ParentId = Guid.NewGuid();
    private static readonly Guid SprintId = Guid.NewGuid();
    private static readonly Guid OtherSprintId = Guid.NewGuid();

    private static Objective Objective(Guid id, Guid? parentId) => new()
    {
        Id = id,
        TenantId = TenantId,
        ProjectId = ProjectId,
        ParentObjectiveId = parentId,
        Title = "Obj",
        OwnerId = Guid.NewGuid(),
        IsActive = true,
        CreatedAt = DateTimeOffset.UtcNow
    };

    private static Sprint SprintOn(Guid objectiveId) => new()
    {
        Id = SprintId,
        TenantId = TenantId,
        ProjectId = ProjectId,
        ObjectiveId = objectiveId,
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

    private (GetSprintTasksQueryHandler Handler, Mock<IWorkTaskRepository> Tasks) BuildHandler(
        Sprint? sprint,
        Objective? objective,
        Objective? parent = null,
        bool hasReadPermission = false,
        Func<IReadOnlyList<Guid>, bool>? membershipForIds = null,
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

        var objectives = new Mock<IObjectiveRepository>();
        if (objective is not null)
            objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, objective.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(objective);
        if (parent is not null)
            objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, parent.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(parent);

        var members = new Mock<IProjectMemberRepository>();
        members.Setup(x => x.HasActiveMembershipForAnyObjectiveAsync(
                TenantId, ProjectId, CallerEmployeeId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, Guid _, Guid _, IReadOnlyList<Guid> ids, CancellationToken _) =>
                membershipForIds?.Invoke(ids) ?? false);

        var permissionResolver = new Mock<IPermissionResolver>();
        permissionResolver.Setup(x => x.ResolveAsync(UserId, TenantId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(hasReadPermission ? new List<string> { "projects:read" } : new List<string>());

        var thisSprintTask = TaskOn(SprintId, Guid.NewGuid(), "This sprint");
        var otherSprintTask = TaskOn(OtherSprintId, Guid.NewGuid(), "Other sprint");
        var tasks = new Mock<IWorkTaskRepository>();
        tasks.Setup(x => x.GetBySprintIdAsync(TenantId, SprintId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sprintTasks ?? new List<WorkTask> { thisSprintTask });
        tasks.Setup(x => x.GetByObjectiveIdAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WorkTask> { thisSprintTask, otherSprintTask });

        var assignmentRepo = new Mock<ITaskAssignmentRepository>();
        assignmentRepo.Setup(x => x.GetByTaskIdsAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(assignments ?? new List<TaskAssignment>());

        var handler = new GetSprintTasksQueryHandler(
            currentUser.Object, identity.Object, sprints.Object, objectives.Object, members.Object,
            permissionResolver.Object, tasks.Object, assignmentRepo.Object);

        return (handler, tasks);
    }

    [Fact]
    public async Task Handle_MemberOfSprintObjective_ReturnsOnlyThatSprintsTasks()
    {
        var (handler, tasks) = BuildHandler(
            SprintOn(ObjectiveId),
            Objective(ObjectiveId, parentId: null),
            membershipForIds: ids => ids.Contains(ObjectiveId));

        var result = await handler.Handle(new GetSprintTasksQuery(SprintId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var returned = Assert.Single(result.Value!);
        Assert.Equal(SprintId, returned.SprintId);
        tasks.Verify(x => x.GetBySprintIdAsync(TenantId, SprintId, It.IsAny<CancellationToken>()), Times.Once);
        tasks.Verify(x => x.GetByObjectiveIdAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ActiveMembershipOnlyOnAncestor_ReturnsTasks()
    {
        var parent = Objective(ParentId, parentId: null);
        IReadOnlyList<Guid>? walkedIds = null;
        var (handler, _) = BuildHandler(
            SprintOn(ObjectiveId),
            Objective(ObjectiveId, parentId: ParentId),
            parent,
            membershipForIds: ids =>
            {
                walkedIds = ids;
                return ids.Contains(ParentId);
            });

        var result = await handler.Handle(new GetSprintTasksQuery(SprintId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!);
        Assert.NotNull(walkedIds);
        Assert.Contains(ObjectiveId, walkedIds);
        Assert.Contains(ParentId, walkedIds);
    }

    [Fact]
    public async Task Handle_NoMembershipAndNoPermission_ReturnsForbidden()
    {
        var (handler, tasks) = BuildHandler(
            SprintOn(ObjectiveId),
            Objective(ObjectiveId, parentId: null),
            membershipForIds: _ => false);

        var result = await handler.Handle(new GetSprintTasksQuery(SprintId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        tasks.Verify(x => x.GetBySprintIdAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_UnknownSprint_ReturnsNotFound()
    {
        var (handler, _) = BuildHandler(sprint: null, objective: null);

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

        var (handler, _) = BuildHandler(
            SprintOn(ObjectiveId),
            Objective(ObjectiveId, parentId: null),
            membershipForIds: ids => ids.Contains(ObjectiveId),
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
