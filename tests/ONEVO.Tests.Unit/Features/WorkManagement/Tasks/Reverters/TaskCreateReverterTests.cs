using FluentAssertions;
using Moq;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Planner.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Reverters;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks.Reverters;

public class TaskCreateReverterTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private readonly Mock<ITaskWriteService> _writes = new();
    private readonly Mock<IWorkTaskRepository> _tasks = new();

    private readonly Mock<IPlannerBlockWriter> _planner = new();

    private TaskCreateReverter Build() => new(_writes.Object, _tasks.Object, _planner.Object);

    // Follow-up 2: an approved request may have planned the task; the revert takes those blocks off the Planner too.
    [Fact]
    public async Task RevertAsync_RemovesThePlannerBlocksOfTheCreatedTask()
    {
        var taskId = Guid.NewGuid();
        var request = new WorkApprovalRequest { Id = Guid.NewGuid(), TenantId = TenantId, TargetId = taskId, ActionType = WorkActionTypes.TaskCreate };
        _tasks.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, taskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkTask { Id = taskId, TenantId = TenantId });

        await Build().RevertAsync(new ApprovalRevertContext(request, Guid.NewGuid()), CancellationToken.None);

        _planner.Verify(x => x.RemoveBlocksOfTaskAsync(TenantId, taskId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RevertAsync_SoftDeletesTheCreatedTaskAndNullsTargetId()
    {
        var taskId = Guid.NewGuid();
        var task = new WorkTask { Id = taskId, TenantId = TenantId };
        var request = new WorkApprovalRequest { Id = Guid.NewGuid(), TenantId = TenantId, TargetId = taskId, ActionType = WorkActionTypes.TaskCreate };
        _tasks.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, taskId, It.IsAny<CancellationToken>())).ReturnsAsync(task);

        var outcome = await Build().RevertAsync(new ApprovalRevertContext(request, Guid.NewGuid()), CancellationToken.None);

        outcome.Kind.Should().Be(RevertOutcomeKind.Reverted);
        _writes.Verify(x => x.Restore(It.IsAny<WorkTask>()), Times.Never);
        _writes.Verify(x => x.Delete(task), Times.Once);
        request.TargetId.Should().BeNull();
    }

    [Fact]
    public async Task RevertAsync_TaskAlreadyGone_ReturnsStale()
    {
        var request = new WorkApprovalRequest { Id = Guid.NewGuid(), TenantId = TenantId, TargetId = Guid.NewGuid(), ActionType = WorkActionTypes.TaskCreate };
        _tasks.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, request.TargetId!.Value, It.IsAny<CancellationToken>())).ReturnsAsync((WorkTask?)null);

        var outcome = await Build().RevertAsync(new ApprovalRevertContext(request, Guid.NewGuid()), CancellationToken.None);

        outcome.Kind.Should().Be(RevertOutcomeKind.Stale);
    }
}
