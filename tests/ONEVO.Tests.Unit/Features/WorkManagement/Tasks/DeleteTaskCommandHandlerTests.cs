using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Hierarchy;
using ONEVO.Application.Features.WorkManagement.Notifications.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.DeleteTask;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class DeleteTaskCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid OwnerEmployeeId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();
    private static readonly Guid TaskId = Guid.NewGuid();
    private static readonly Guid SprintId = Guid.NewGuid();
    private static readonly Guid PendingRequestId = Guid.NewGuid();

    /// <summary>
    /// In-memory stand-in for the EF global query filter: Remove hides the row from GetBy*
    /// the same way IsDeleted does after SoftDeleteInterceptor + SaveChanges.
    /// </summary>
    private sealed class FilterAwareTaskStore
    {
        private readonly List<WorkTask> _visible = new();

        public void Seed(WorkTask task) => _visible.Add(task);

        public void Bind(Mock<IWorkTaskRepository> tasks)
        {
            tasks.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid _, Guid id, CancellationToken _) => _visible.FirstOrDefault(t => t.Id == id));
            tasks.Setup(x => x.GetByObjectiveIdAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => _visible.Where(t => t.ObjectiveId == ObjectiveId).ToList());
            tasks.Setup(x => x.GetBySprintIdAsync(TenantId, SprintId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => _visible.Where(t => t.SprintId == SprintId).ToList());
            tasks.Setup(x => x.Remove(It.IsAny<WorkTask>()))
                .Callback<WorkTask>(t => _visible.RemoveAll(x => x.Id == t.Id));
        }
    }

    private (DeleteTaskCommandHandler Handler, Mock<IWorkTaskRepository> Tasks, FilterAwareTaskStore Store) Build(
        WorkTask? task, Guid callerEmployeeId, bool? callerIsEffectiveManager = null)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(callerEmployeeId);

        var store = new FilterAwareTaskStore();
        if (task is not null)
            store.Seed(task);

        var tasks = new Mock<IWorkTaskRepository>();
        store.Bind(tasks);

        var objective = new Objective
        {
            Id = ObjectiveId,
            TenantId = TenantId,
            OwnerId = OwnerEmployeeId,
            IsActive = true,
            Title = "Obj",
            CreatedAt = DateTimeOffset.UtcNow
        };
        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(objective);

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var membership = new Mock<IMilestoneMembershipCoordinator>();
        // Mirrors direct-owner-only behavior by default so pre-existing tests keep passing
        // unmodified; callerIsEffectiveManager lets a test override this to simulate an
        // ancestor-cascade grant (the coordinator's own ancestor-walk logic is unit-tested
        // separately in MilestoneMembershipCoordinatorTests).
        membership.Setup(x => x.IsEffectiveManagerAsync(TenantId, ObjectiveId, callerEmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(callerIsEffectiveManager ?? (callerEmployeeId == OwnerEmployeeId));

        unitOfWork.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result<TaskWriteOutcome>>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result<TaskWriteOutcome>>> op, CancellationToken ct) => op(ct));

        var assignments = new Mock<ITaskAssignmentRepository>();
        assignments.Setup(x => x.GetByTaskIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<TaskAssignment>());

        var hierarchy = new Mock<IWorkHierarchyService>();
        hierarchy.Setup(x => x.LoadTreeAsync(TenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProjectModuleTree(new[] { objective }));

        // Stand-in for the engine: the Module owner deletes now, anyone else files a request.
        var approvals = new Mock<IWorkApprovalEngine>();
        approvals.Setup(x => x.SubmitAsync(It.IsAny<WorkAction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkAction a, CancellationToken _) => a.ActorEmployeeId == OwnerEmployeeId
                ? Result<ApprovalDecision>.Success(ApprovalDecision.Direct)
                : Result<ApprovalDecision>.Success(ApprovalDecision.Pending(PendingRequestId, OwnerEmployeeId)));

        var writes = new Mock<ITaskWriteService>();
        writes.Setup(x => x.Delete(It.IsAny<WorkTask>())).Callback<WorkTask>(t => tasks.Object.Remove(t));

        var handler = new DeleteTaskCommandHandler(
            currentUser.Object, identity.Object, tasks.Object, objectives.Object, unitOfWork.Object, membership.Object,
            assignments.Object, writes.Object, hierarchy.Object, approvals.Object, new Mock<IWorkNotificationEngine>().Object);
        return (handler, tasks, store);
    }

    private static WorkTask SeedTask() => new()
    {
        Id = TaskId,
        TenantId = TenantId,
        ObjectiveId = ObjectiveId,
        SprintId = SprintId,
        Title = "A",
        ShortId = "T-1",
        CreatedAt = DateTimeOffset.UtcNow
    };

    [Fact]
    public async Task Handle_ObjectiveOwner_RemovesTaskAndItDisappearsFromReads()
    {
        var (handler, tasks, _) = Build(SeedTask(), OwnerEmployeeId);

        var result = await handler.Handle(new DeleteTaskCommand(TaskId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        tasks.Verify(x => x.Remove(It.Is<WorkTask>(t => t.Id == TaskId)), Times.Once);
        Assert.Empty(await tasks.Object.GetByObjectiveIdAsync(TenantId, ObjectiveId));
        Assert.Empty(await tasks.Object.GetBySprintIdAsync(TenantId, SprintId));
    }

    [Fact]
    public async Task Handle_CallerNotModuleMember_ReturnsForbidden()
    {
        var (handler, tasks, _) = Build(SeedTask(), callerEmployeeId: Guid.NewGuid());

        var result = await handler.Handle(new DeleteTaskCommand(TaskId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        Assert.Equal("Only members of this module can change its tasks.", result.Error);
        tasks.Verify(x => x.Remove(It.IsAny<WorkTask>()), Times.Never);
    }

    [Fact]
    public async Task Handle_TaskNotFound_ReturnsNotFound()
    {
        var (handler, tasks, _) = Build(task: null, callerEmployeeId: OwnerEmployeeId);

        var result = await handler.Handle(new DeleteTaskCommand(TaskId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
        tasks.Verify(x => x.Remove(It.IsAny<WorkTask>()), Times.Never);
    }

    [Fact]
    public async Task Handle_DeletingTwice_SecondCallReturnsNotFound()
    {
        var (handler, _, _) = Build(SeedTask(), OwnerEmployeeId);

        var first = await handler.Handle(new DeleteTaskCommand(TaskId), CancellationToken.None);
        var second = await handler.Handle(new DeleteTaskCommand(TaskId), CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.False(second.IsSuccess);
        Assert.Equal(404, second.StatusCode);
    }

    [Fact]
    public async Task Handle_PlainMemberOfGrandparentObjective_SentForApproval()
    {
        // IsEffectiveManagerAsync lets any member through to the engine; the engine (not the
        // handler) decides that a plain member needs approval, so nothing is removed yet.
        var grandparentMemberId = Guid.NewGuid();
        var (handler, tasks, _) = Build(SeedTask(), callerEmployeeId: grandparentMemberId, callerIsEffectiveManager: true);

        var result = await handler.Handle(new DeleteTaskCommand(TaskId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(PendingRequestId, result.Value!.ApprovalRequestId);
        tasks.Verify(x => x.Remove(It.IsAny<WorkTask>()), Times.Never);
    }
}
