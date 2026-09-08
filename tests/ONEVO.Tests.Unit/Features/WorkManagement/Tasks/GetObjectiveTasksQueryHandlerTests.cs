using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetObjectiveTasks;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class GetObjectiveTasksQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid CallerEmployeeId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();
    private static readonly Guid ParentId = Guid.NewGuid();

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

    /// <summary>Mocks representing an authenticated caller with active membership on ObjectiveId itself.</summary>
    private static (Mock<ICallerIdentityResolver> Identity, Mock<IObjectiveRepository> Objectives, Mock<IProjectMemberRepository> Members, Mock<IPermissionResolver> Permissions) MembershipOnObjectiveItself()
    {
        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CallerEmployeeId);

        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Objective(ObjectiveId, parentId: null));

        var members = new Mock<IProjectMemberRepository>();
        members.Setup(x => x.HasActiveMembershipForAnyObjectiveAsync(
                TenantId, ProjectId, CallerEmployeeId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, Guid _, Guid _, IReadOnlyList<Guid> ids, CancellationToken _) => ids.Contains(ObjectiveId));

        var permissions = new Mock<IPermissionResolver>();
        permissions.Setup(x => x.ResolveAsync(UserId, TenantId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        return (identity, objectives, members, permissions);
    }

    [Fact]
    public async Task Handle_ReturnsAllTasksForObjective()
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var tasks = new Mock<IWorkTaskRepository>();
        tasks.Setup(x => x.GetByObjectiveIdAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WorkTask>
            {
                new() { Id = Guid.NewGuid(), TenantId = TenantId, ObjectiveId = ObjectiveId, Title = "A", ShortId = "T-1", CreatedAt = DateTimeOffset.UtcNow },
                new() { Id = Guid.NewGuid(), TenantId = TenantId, ObjectiveId = ObjectiveId, Title = "B", ShortId = "T-2", CreatedAt = DateTimeOffset.UtcNow }
            });

                var assignments = new Mock<ITaskAssignmentRepository>();
        assignments.Setup(x => x.GetByTaskIdsAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TaskAssignment>());
        var sessions = new Mock<ITaskClockingSessionRepository>();
        sessions.Setup(x => x.GetOpenSessionsForTasksAsync(
                TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, OpenTaskClockingSessionSummary>());
        sessions.Setup(x => x.GetTotalClosedSessionMinutesForTasksAsync(
                TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, int>());

        var (identity, objectives, members, permissions) = MembershipOnObjectiveItself();
        var handler = new GetObjectiveTasksQueryHandler(
            currentUser.Object, identity.Object, objectives.Object, members.Object, permissions.Object,
            tasks.Object, assignments.Object, sessions.Object);

        var result = await handler.Handle(new GetObjectiveTasksQuery(ObjectiveId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Count);
    }

    [Fact]
    public async Task Handle_PopulatesAssigneeEmployeeIds_FromBulkAssignmentLookup()
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var taskWithAssignee = Guid.NewGuid();
        var taskWithoutAssignee = Guid.NewGuid();
        var assigneeEmployeeId = Guid.NewGuid();

        var tasks = new Mock<IWorkTaskRepository>();
        tasks.Setup(x => x.GetByObjectiveIdAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WorkTask>
            {
                new() { Id = taskWithAssignee, TenantId = TenantId, ObjectiveId = ObjectiveId, Title = "A", ShortId = "T-1", CreatedAt = DateTimeOffset.UtcNow },
                new() { Id = taskWithoutAssignee, TenantId = TenantId, ObjectiveId = ObjectiveId, Title = "B", ShortId = "T-2", CreatedAt = DateTimeOffset.UtcNow }
            });

        var assignments = new Mock<ITaskAssignmentRepository>();
        assignments.Setup(x => x.GetByTaskIdsAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TaskAssignment>
            {
                new() { Id = Guid.NewGuid(), TaskId = taskWithAssignee, EmployeeId = assigneeEmployeeId, UserId = Guid.NewGuid(), AssignedById = Guid.NewGuid(), AssignedAt = DateTimeOffset.UtcNow }
            });

                var sessions = new Mock<ITaskClockingSessionRepository>();
        sessions.Setup(x => x.GetOpenSessionsForTasksAsync(
                TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, OpenTaskClockingSessionSummary>());
        sessions.Setup(x => x.GetTotalClosedSessionMinutesForTasksAsync(
                TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, int>());

        var (identity, objectives, members, permissions) = MembershipOnObjectiveItself();
        var handler = new GetObjectiveTasksQueryHandler(
            currentUser.Object, identity.Object, objectives.Object, members.Object, permissions.Object,
            tasks.Object, assignments.Object, sessions.Object);

        var result = await handler.Handle(new GetObjectiveTasksQuery(ObjectiveId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var assigned = result.Value!.Single(t => t.Id == taskWithAssignee);
        var unassigned = result.Value!.Single(t => t.Id == taskWithoutAssignee);
        Assert.Equal([assigneeEmployeeId], assigned.AssigneeEmployeeIds);
        Assert.Empty(unassigned.AssigneeEmployeeIds!);
    }

    private GetObjectiveTasksQueryHandler BuildHandler(
        Objective? objective,
        Objective? parent = null,
        bool hasReadPermission = false,
        Func<IReadOnlyList<Guid>, bool>? membershipForIds = null)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CallerEmployeeId);

        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>()))
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

        var tasks = new Mock<IWorkTaskRepository>();
        tasks.Setup(x => x.GetByObjectiveIdAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WorkTask>
            {
                new() { Id = Guid.NewGuid(), TenantId = TenantId, ObjectiveId = ObjectiveId, Title = "A", ShortId = "T-1", CreatedAt = DateTimeOffset.UtcNow }
            });

                var assignments = new Mock<ITaskAssignmentRepository>();
        assignments.Setup(x => x.GetByTaskIdsAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TaskAssignment>());
        var sessions = new Mock<ITaskClockingSessionRepository>();
        sessions.Setup(x => x.GetOpenSessionsForTasksAsync(
                TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, OpenTaskClockingSessionSummary>());
        sessions.Setup(x => x.GetTotalClosedSessionMinutesForTasksAsync(
                TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, int>());

        return new GetObjectiveTasksQueryHandler(
            currentUser.Object, identity.Object, objectives.Object, members.Object, permissionResolver.Object,
            tasks.Object, assignments.Object, sessions.Object);

    }

    [Fact]
    public async Task Handle_ActiveMembershipOnObjective_ReturnsTasks()
    {
        var handler = BuildHandler(
            Objective(ObjectiveId, parentId: null),
            membershipForIds: ids => ids.Contains(ObjectiveId));

        var result = await handler.Handle(new GetObjectiveTasksQuery(ObjectiveId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!);
    }

    [Fact]
    public async Task Handle_ActiveMembershipOnlyOnAncestor_ReturnsTasks()
    {
        var parent = Objective(ParentId, parentId: null);
        IReadOnlyList<Guid>? walkedIds = null;
        var handler = BuildHandler(
            Objective(ObjectiveId, parentId: ParentId),
            parent,
            membershipForIds: ids =>
            {
                walkedIds = ids;
                return ids.Contains(ParentId);
            });

        var result = await handler.Handle(new GetObjectiveTasksQuery(ObjectiveId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!);
        Assert.NotNull(walkedIds);
        Assert.Contains(ObjectiveId, walkedIds);
        Assert.Contains(ParentId, walkedIds);
    }

    [Fact]
    public async Task Handle_ProjectsReadPermissionWithoutMembership_ReturnsTasks()
    {
        var handler = BuildHandler(
            Objective(ObjectiveId, parentId: null),
            hasReadPermission: true,
            membershipForIds: _ => false);

        var result = await handler.Handle(new GetObjectiveTasksQuery(ObjectiveId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!);
    }

    [Fact]
    public async Task Handle_NoMembershipAndNoPermission_ReturnsForbidden()
    {
        var handler = BuildHandler(
            Objective(ObjectiveId, parentId: null),
            membershipForIds: _ => false);

        var result = await handler.Handle(new GetObjectiveTasksQuery(ObjectiveId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_UnknownObjective_ReturnsNotFound()
    {
        var handler = BuildHandler(objective: null);

        var result = await handler.Handle(new GetObjectiveTasksQuery(ObjectiveId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }
}
