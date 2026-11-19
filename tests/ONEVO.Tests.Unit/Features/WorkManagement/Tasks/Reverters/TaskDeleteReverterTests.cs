using FluentAssertions;
using Moq;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Reverters;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks.Reverters;

public class TaskDeleteReverterTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private readonly Mock<ITaskWriteService> _writes = new();
    private readonly Mock<IWorkTaskRepository> _tasks = new();

    private TaskDeleteReverter Build() => new(_writes.Object, _tasks.Object);

    [Fact]
    public async Task RevertAsync_RestoresTheDeletedTask()
    {
        var taskId = Guid.NewGuid();
        var task = new WorkTask { Id = taskId, TenantId = TenantId, IsDeleted = true, DeletedAt = DateTimeOffset.UtcNow };
        var request = new WorkApprovalRequest { Id = Guid.NewGuid(), TenantId = TenantId, TargetId = taskId, ActionType = WorkActionTypes.TaskDelete };
        _tasks.Setup(x => x.GetTrackedByIdForTenantIncludingDeletedAsync(TenantId, taskId, It.IsAny<CancellationToken>())).ReturnsAsync(task);

        var outcome = await Build().RevertAsync(new ApprovalRevertContext(request, Guid.NewGuid()), CancellationToken.None);

        outcome.Kind.Should().Be(RevertOutcomeKind.Reverted);
        _writes.Verify(x => x.Restore(task), Times.Once);
    }

    [Fact]
    public async Task RevertAsync_TaskNotFound_ReturnsStale()
    {
        var request = new WorkApprovalRequest { Id = Guid.NewGuid(), TenantId = TenantId, TargetId = Guid.NewGuid(), ActionType = WorkActionTypes.TaskDelete };
        _tasks.Setup(x => x.GetTrackedByIdForTenantIncludingDeletedAsync(TenantId, request.TargetId!.Value, It.IsAny<CancellationToken>())).ReturnsAsync((WorkTask?)null);

        var outcome = await Build().RevertAsync(new ApprovalRevertContext(request, Guid.NewGuid()), CancellationToken.None);

        outcome.Kind.Should().Be(RevertOutcomeKind.Stale);
    }
}
