using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
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
            tasks.Setup(x => x.GetByIdForTenantAsync(TenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
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

        var handler = new DeleteTaskCommandHandler(
            currentUser.Object, identity.Object, tasks.Object, objectives.Object, unitOfWork.Object, membership.Object);
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
    public async Task Handle_CallerNotObjectiveOwner_ReturnsForbidden()
    {
        var (handler, tasks, _) = Build(SeedTask(), callerEmployeeId: Guid.NewGuid());

        var result = await handler.Handle(new DeleteTaskCommand(TaskId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        Assert.Equal("Only this milestone's owner can delete tasks.", result.Error);
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
    public async Task Handle_PlainMemberOfGrandparentObjective_RemovesTask()
    {
        // Caller is not this objective's own OwnerId, but IsEffectiveManagerAsync reports them as
        // an effective manager via an ancestor (grandparent) membership - the coordinator's own
        // ancestor-walk logic is unit-tested separately in MilestoneMembershipCoordinatorTests, so
        // this only proves the handler defers to its answer instead of the direct OwnerId check.
        var grandparentMemberId = Guid.NewGuid();
        var (handler, tasks, _) = Build(SeedTask(), callerEmployeeId: grandparentMemberId, callerIsEffectiveManager: true);

        var result = await handler.Handle(new DeleteTaskCommand(TaskId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        tasks.Verify(x => x.Remove(It.Is<WorkTask>(t => t.Id == TaskId)), Times.Once);
    }
}
