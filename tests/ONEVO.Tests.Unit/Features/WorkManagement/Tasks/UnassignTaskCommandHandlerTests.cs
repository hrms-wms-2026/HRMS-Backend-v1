using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.UnassignTask;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class UnassignTaskCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();
    private static readonly Guid OwnerEmployeeId = Guid.NewGuid();
    private static readonly Guid TaskId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();

    private (UnassignTaskCommandHandler Handler, Mock<ITaskAssignmentRepository> Assignments) Build(
        WorkTask? task, TaskAssignment? assignment, Guid callerEmployeeId, bool? callerIsEffectiveManager = null)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(callerEmployeeId);

        var tasks = new Mock<IWorkTaskRepository>();
        tasks.Setup(x => x.GetByIdForTenantAsync(TenantId, TaskId, It.IsAny<CancellationToken>())).ReturnsAsync(task);

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

        var assignments = new Mock<ITaskAssignmentRepository>();
        assignments.Setup(x => x.GetByTaskAndEmployeeAsync(TaskId, EmployeeId, It.IsAny<CancellationToken>())).ReturnsAsync(assignment);

        var membership = new Mock<IMilestoneMembershipCoordinator>();
        // Mirrors direct-owner-only behavior by default so pre-existing tests keep passing
        // unmodified; callerIsEffectiveManager lets a test override this to simulate an
        // ancestor-cascade grant.
        membership.Setup(x => x.IsEffectiveManagerAsync(TenantId, ObjectiveId, callerEmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(callerIsEffectiveManager ?? (objective.OwnerId == callerEmployeeId));

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result>> op, CancellationToken ct) => op(ct));
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new UnassignTaskCommandHandler(
            currentUser.Object, identity.Object, tasks.Object, objectives.Object, assignments.Object, unitOfWork.Object, membership.Object);
        return (handler, assignments);
    }

    [Fact]
    public async Task Handle_HappyPath_RemovesAssignment()
    {
        var task = new WorkTask { Id = TaskId, TenantId = TenantId, ObjectiveId = ObjectiveId, Title = "A", ShortId = "T-1", CreatedAt = DateTimeOffset.UtcNow };
        var assignment = new TaskAssignment { Id = Guid.NewGuid(), TaskId = TaskId, EmployeeId = EmployeeId };
        var (handler, assignments) = Build(task, assignment, OwnerEmployeeId);

        var result = await handler.Handle(new UnassignTaskCommand(TaskId, EmployeeId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        assignments.Verify(x => x.Remove(assignment), Times.Once);
    }

    [Fact]
    public async Task Handle_TaskNotFound_ReturnsNotFound()
    {
        var (handler, assignments) = Build(task: null, assignment: null, callerEmployeeId: OwnerEmployeeId);
        var result = await handler.Handle(new UnassignTaskCommand(TaskId, EmployeeId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
        assignments.Verify(x => x.Remove(It.IsAny<TaskAssignment>()), Times.Never);
    }

    [Fact]
    public async Task Handle_CallerNotObjectiveOwner_ReturnsForbidden()
    {
        var task = new WorkTask { Id = TaskId, TenantId = TenantId, ObjectiveId = ObjectiveId, Title = "A", ShortId = "T-1", CreatedAt = DateTimeOffset.UtcNow };
        var assignment = new TaskAssignment { Id = Guid.NewGuid(), TaskId = TaskId, EmployeeId = EmployeeId };
        var (handler, assignments) = Build(task, assignment, callerEmployeeId: Guid.NewGuid());

        var result = await handler.Handle(new UnassignTaskCommand(TaskId, EmployeeId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        assignments.Verify(x => x.Remove(It.IsAny<TaskAssignment>()), Times.Never);
    }

    [Fact]
    public async Task Handle_CallerIsEffectiveManagerNotOwner_RemovesAssignment()
    {
        // Caller is not this objective's own OwnerId, but IsEffectiveManagerAsync reports them as
        // an effective manager via an ancestor (grandparent) membership - the coordinator's own
        // ancestor-walk logic is unit-tested separately, so this only proves the handler defers to
        // its answer instead of the direct OwnerId check.
        var task = new WorkTask { Id = TaskId, TenantId = TenantId, ObjectiveId = ObjectiveId, Title = "A", ShortId = "T-1", CreatedAt = DateTimeOffset.UtcNow };
        var assignment = new TaskAssignment { Id = Guid.NewGuid(), TaskId = TaskId, EmployeeId = EmployeeId };
        var (handler, assignments) = Build(task, assignment, callerEmployeeId: Guid.NewGuid(), callerIsEffectiveManager: true);

        var result = await handler.Handle(new UnassignTaskCommand(TaskId, EmployeeId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        assignments.Verify(x => x.Remove(assignment), Times.Once);
    }
}
