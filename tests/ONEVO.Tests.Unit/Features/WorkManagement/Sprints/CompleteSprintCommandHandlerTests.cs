using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.Commands.CompleteSprint;
using ONEVO.Application.Features.WorkManagement.Sprints.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.ProjectMembers.Entities;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using TaskStatusEntity = ONEVO.Domain.Features.WorkManagement.Tasks.Entities.TaskStatus;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Sprints;

public class CompleteSprintCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid OwnerEmployeeId = Guid.NewGuid();
    private static readonly Guid OtherEmployeeId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();
    private static readonly Guid SprintId = Guid.NewGuid();
    private static readonly Guid DoneStatusId = Guid.NewGuid();
    private static readonly Guid InProcessStatusId = Guid.NewGuid();
    private static readonly Guid MemberEmployeeId = Guid.NewGuid();
    private static readonly Guid MemberUserId = Guid.NewGuid();

    private (CompleteSprintCommandHandler Handler, Sprint Sprint, Mock<INotificationDispatcher> Notifications) Build(
        IReadOnlyList<WorkTask> tasksInSprint, Guid? callerEmployeeId = null, bool? callerIsEffectiveManager = null)
    {
        var resolvedCallerEmployeeId = callerEmployeeId ?? OwnerEmployeeId;

        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync(resolvedCallerEmployeeId);

        var sprint = new Sprint { Id = SprintId, TenantId = TenantId, ObjectiveId = ObjectiveId, Name = "S1", StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2026, 9, 14), Status = SprintStatuses.Active, CreatedAt = DateTimeOffset.UtcNow };
        var sprints = new Mock<ISprintRepository>();
        sprints.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, SprintId, It.IsAny<CancellationToken>())).ReturnsAsync(sprint);

        var objective = new Objective { Id = ObjectiveId, TenantId = TenantId, OwnerId = OwnerEmployeeId, IsActive = true, Title = "Obj", CreatedAt = DateTimeOffset.UtcNow };
        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>())).ReturnsAsync(objective);

        var tasks = new Mock<IWorkTaskRepository>();
        tasks.Setup(x => x.GetBySprintIdAsync(TenantId, SprintId, It.IsAny<CancellationToken>())).ReturnsAsync(tasksInSprint);

        var doneStatus = new TaskStatusEntity { Id = DoneStatusId, TenantId = TenantId, Name = "Done", MarksTaskComplete = true, CreatedAt = DateTimeOffset.UtcNow };
        var inProcessStatus = new TaskStatusEntity { Id = InProcessStatusId, TenantId = TenantId, Name = "In Process", MarksTaskComplete = false, CreatedAt = DateTimeOffset.UtcNow };
        var statuses = new Mock<ITaskStatusRepository>();
        statuses.Setup(x => x.GetByIdForTenantAsync(TenantId, DoneStatusId, It.IsAny<CancellationToken>())).ReturnsAsync(doneStatus);
        statuses.Setup(x => x.GetByIdForTenantAsync(TenantId, InProcessStatusId, It.IsAny<CancellationToken>())).ReturnsAsync(inProcessStatus);

        var members = new Mock<IProjectMemberRepository>();
        members.Setup(x => x.ListActiveForObjectiveAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProjectMember>
            {
                new() { EmployeeId = MemberEmployeeId, ObjectiveId = ObjectiveId, IsActive = true }
            });

        var membership = new Mock<IMilestoneMembershipCoordinator>();
        membership.Setup(x => x.GetActiveAssigneeAsync(TenantId, MemberEmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = MemberEmployeeId, TenantId = TenantId, UserId = MemberUserId });
        // Mirrors direct-owner-only behavior by default so pre-existing tests keep passing
        // unmodified; callerIsEffectiveManager lets a test override this to simulate an
        // ancestor-cascade grant (the coordinator's own ancestor-walk logic is unit-tested
        // separately in MilestoneMembershipCoordinatorTests).
        membership.Setup(x => x.IsEffectiveManagerAsync(TenantId, ObjectiveId, resolvedCallerEmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(callerIsEffectiveManager ?? (objective.OwnerId == resolvedCallerEmployeeId));

        var notifications = new Mock<INotificationDispatcher>();

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result<SprintResponse>>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result<SprintResponse>>> op, CancellationToken ct) => op(ct));
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new CompleteSprintCommandHandler(
            currentUser.Object, identity.Object, objectives.Object, sprints.Object, tasks.Object, statuses.Object,
            members.Object, membership.Object, notifications.Object, unitOfWork.Object);
        return (handler, sprint, notifications);
    }

    [Fact]
    public async Task Handle_AllTasksComplete_MarksSprintComplete()
    {
        var tasksInSprint = new List<WorkTask> { new() { Id = Guid.NewGuid(), TenantId = TenantId, StatusId = DoneStatusId, Title = "A", ShortId = "T-1", CreatedAt = DateTimeOffset.UtcNow } };
        var (handler, sprint, _) = Build(tasksInSprint);

        var result = await handler.Handle(new CompleteSprintCommand(SprintId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(SprintStatuses.Complete, sprint.Status);
        Assert.NotNull(sprint.CompletedAt);
    }

    [Fact]
    public async Task Handle_SomeTaskNotComplete_ReturnsFailure()
    {
        var tasksInSprint = new List<WorkTask>
        {
            new() { Id = Guid.NewGuid(), TenantId = TenantId, StatusId = DoneStatusId, Title = "A", ShortId = "T-1", CreatedAt = DateTimeOffset.UtcNow },
            new() { Id = Guid.NewGuid(), TenantId = TenantId, StatusId = InProcessStatusId, Title = "B", ShortId = "T-2", CreatedAt = DateTimeOffset.UtcNow }
        };
        var (handler, sprint, notifications) = Build(tasksInSprint);

        var result = await handler.Handle(new CompleteSprintCommand(SprintId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(422, result.StatusCode);
        Assert.Equal(SprintStatuses.Active, sprint.Status);
        notifications.Verify(
            x => x.SendTemplatedAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_Complete_NotifiesObjectiveMembers()
    {
        var tasksInSprint = new List<WorkTask> { new() { Id = Guid.NewGuid(), TenantId = TenantId, StatusId = DoneStatusId, Title = "A", ShortId = "T-1", CreatedAt = DateTimeOffset.UtcNow } };
        var (handler, _, notifications) = Build(tasksInSprint);

        var result = await handler.Handle(new CompleteSprintCommand(SprintId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        notifications.Verify(
            x => x.SendTemplatedAsync(
                TenantId, MemberUserId, "work_sprint_completed",
                It.Is<IReadOnlyDictionary<string, string>>(p => p["sprintName"] == "S1" && p["objectiveName"] == "Obj"),
                "sprint", SprintId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_NotOwner_ReturnsForbidden()
    {
        var tasksInSprint = new List<WorkTask> { new() { Id = Guid.NewGuid(), TenantId = TenantId, StatusId = DoneStatusId, Title = "A", ShortId = "T-1", CreatedAt = DateTimeOffset.UtcNow } };
        var (handler, sprint, _) = Build(tasksInSprint, callerEmployeeId: OtherEmployeeId);

        var result = await handler.Handle(new CompleteSprintCommand(SprintId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        Assert.Equal(SprintStatuses.Active, sprint.Status);
    }

    [Fact]
    public async Task Handle_CallerIsEffectiveManagerViaCascade_CompletesSprint()
    {
        // Caller is not this objective's own OwnerId, but IsEffectiveManagerAsync reports them as
        // an effective manager via an ancestor membership - the coordinator's own ancestor-walk
        // logic is unit-tested separately in MilestoneMembershipCoordinatorTests, so this only
        // proves the handler defers to its answer instead of the direct OwnerId check.
        var tasksInSprint = new List<WorkTask> { new() { Id = Guid.NewGuid(), TenantId = TenantId, StatusId = DoneStatusId, Title = "A", ShortId = "T-1", CreatedAt = DateTimeOffset.UtcNow } };
        var (handler, sprint, _) = Build(tasksInSprint, callerEmployeeId: OtherEmployeeId, callerIsEffectiveManager: true);

        var result = await handler.Handle(new CompleteSprintCommand(SprintId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(SprintStatuses.Complete, sprint.Status);
    }
}
