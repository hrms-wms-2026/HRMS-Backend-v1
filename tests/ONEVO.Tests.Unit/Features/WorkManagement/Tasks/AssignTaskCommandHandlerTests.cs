using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.AssignTask;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class AssignTaskCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();
    private static readonly Guid OwnerEmployeeId = Guid.NewGuid();
    private static readonly Guid TaskId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly Guid AssigneeUserId = Guid.NewGuid();

    private (AssignTaskCommandHandler Handler, Mock<ITaskAssignmentRepository> Assignments) Build(
        WorkTask? task, Employee? assignee, Guid callerEmployeeId)
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

        var membership = new Mock<IMilestoneMembershipCoordinator>();
        membership.Setup(x => x.GetActiveAssigneeAsync(TenantId, EmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(assignee);

        var assignments = new Mock<ITaskAssignmentRepository>();
        assignments.Setup(x => x.GetByTaskAndEmployeeAsync(TaskId, EmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TaskAssignment?)null);

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result>> op, CancellationToken ct) => op(ct));
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new AssignTaskCommandHandler(
            currentUser.Object, identity.Object, tasks.Object, objectives.Object, assignments.Object, membership.Object, unitOfWork.Object);
        return (handler, assignments);
    }

    [Fact]
    public async Task Handle_HappyPath_AddsAssignment()
    {
        var task = new WorkTask { Id = TaskId, TenantId = TenantId, ObjectiveId = ObjectiveId, Title = "A", ShortId = "T-1", CreatedAt = DateTimeOffset.UtcNow };
        var assignee = new Employee { Id = EmployeeId, TenantId = TenantId, UserId = AssigneeUserId, EmployeeNumber = "E1", HireDate = new DateOnly(2020, 1, 1) };
        var (handler, assignments) = Build(task, assignee, OwnerEmployeeId);

        var result = await handler.Handle(new AssignTaskCommand(TaskId, EmployeeId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        assignments.Verify(x => x.AddAsync(It.Is<TaskAssignment>(a => a.EmployeeId == EmployeeId && a.UserId == AssigneeUserId), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_TaskNotFound_ReturnsNotFound()
    {
        var (handler, assignments) = Build(
            task: null,
            assignee: new Employee { Id = EmployeeId, UserId = AssigneeUserId },
            callerEmployeeId: OwnerEmployeeId);
        var result = await handler.Handle(new AssignTaskCommand(TaskId, EmployeeId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
        assignments.Verify(x => x.AddAsync(It.IsAny<TaskAssignment>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_EmployeeNotActive_ReturnsFailure()
    {
        var task = new WorkTask { Id = TaskId, TenantId = TenantId, ObjectiveId = ObjectiveId, Title = "A", ShortId = "T-1", CreatedAt = DateTimeOffset.UtcNow };
        var (handler, assignments) = Build(task, assignee: null, callerEmployeeId: OwnerEmployeeId);

        var result = await handler.Handle(new AssignTaskCommand(TaskId, EmployeeId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        assignments.Verify(x => x.AddAsync(It.IsAny<TaskAssignment>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_CallerNotObjectiveOwner_ReturnsForbidden()
    {
        var task = new WorkTask { Id = TaskId, TenantId = TenantId, ObjectiveId = ObjectiveId, Title = "A", ShortId = "T-1", CreatedAt = DateTimeOffset.UtcNow };
        var assignee = new Employee { Id = EmployeeId, TenantId = TenantId, UserId = AssigneeUserId, EmployeeNumber = "E1", HireDate = new DateOnly(2020, 1, 1) };
        var (handler, assignments) = Build(task, assignee, callerEmployeeId: Guid.NewGuid());

        var result = await handler.Handle(new AssignTaskCommand(TaskId, EmployeeId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        assignments.Verify(x => x.AddAsync(It.IsAny<TaskAssignment>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
