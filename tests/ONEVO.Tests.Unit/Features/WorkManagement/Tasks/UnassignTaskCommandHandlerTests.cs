using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.UnassignTask;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class UnassignTaskCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid TaskId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();

    private (UnassignTaskCommandHandler Handler, Mock<ITaskAssignmentRepository> Assignments) Build(WorkTask? task, TaskAssignment? assignment)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);

        var tasks = new Mock<IWorkTaskRepository>();
        tasks.Setup(x => x.GetByIdForTenantAsync(TenantId, TaskId, It.IsAny<CancellationToken>())).ReturnsAsync(task);

        var assignments = new Mock<ITaskAssignmentRepository>();
        assignments.Setup(x => x.GetByTaskAndEmployeeAsync(TaskId, EmployeeId, It.IsAny<CancellationToken>())).ReturnsAsync(assignment);

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result>> op, CancellationToken ct) => op(ct));
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new UnassignTaskCommandHandler(currentUser.Object, tasks.Object, assignments.Object, unitOfWork.Object);
        return (handler, assignments);
    }

    [Fact]
    public async Task Handle_HappyPath_RemovesAssignment()
    {
        var task = new WorkTask { Id = TaskId, TenantId = TenantId, Title = "A", ShortId = "T-1", CreatedAt = DateTimeOffset.UtcNow };
        var assignment = new TaskAssignment { Id = Guid.NewGuid(), TaskId = TaskId, EmployeeId = EmployeeId };
        var (handler, assignments) = Build(task, assignment);

        var result = await handler.Handle(new UnassignTaskCommand(TaskId, EmployeeId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        assignments.Verify(x => x.Remove(assignment), Times.Once);
    }

    [Fact]
    public async Task Handle_TaskNotFound_ReturnsNotFound()
    {
        var (handler, assignments) = Build(task: null, assignment: null);
        var result = await handler.Handle(new UnassignTaskCommand(TaskId, EmployeeId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
        assignments.Verify(x => x.Remove(It.IsAny<TaskAssignment>()), Times.Never);
    }
}
