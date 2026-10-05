using System.Text.Json;
using FluentAssertions;
using Moq;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Reverters;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;
using TaskStatusEntity = ONEVO.Domain.Features.WorkManagement.Tasks.Entities.TaskStatus;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks.Reverters;

public class ProjectStatusTemplateChangeReverterTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private readonly Mock<ITaskStatusRepository> _statuses = new();
    private readonly Mock<IWorkTaskRepository> _tasks = new();

    private ProjectStatusTemplateChangeReverter Build() => new(_statuses.Object, _tasks.Object);

    private static WorkApprovalRequest Request(ProjectStatusTemplateUndoSnapshot snapshot) => new()
    {
        Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, ActionType = WorkActionTypes.ProjectStatusTemplateChange,
        UndoStateJson = JsonSerializer.Serialize(snapshot)
    };

    [Fact]
    public async Task RevertAsync_RestoresAModifiedStatussFields()
    {
        var statusId = Guid.NewGuid();
        var live = new TaskStatusEntity { Id = statusId, TenantId = TenantId, Name = "Renamed", DisplayOrder = 0, Category = TaskStatusCategories.Active, Color = "#000000", Visibility = TaskStatusVisibilities.Public };
        var snapshot = new ProjectStatusTemplateUndoSnapshot([new ProjectStatusTemplateSnapshotEntry(statusId, "Original", 0, TaskStatusCategories.Active, "#FFFFFF", TaskStatusVisibilities.Public, false)]);
        _statuses.Setup(x => x.GetProjectTemplateAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<TaskStatusEntity> { live });

        var outcome = await Build().RevertAsync(new ApprovalRevertContext(Request(snapshot), Guid.NewGuid()), CancellationToken.None);

        outcome.Kind.Should().Be(RevertOutcomeKind.Reverted);
        live.Name.Should().Be("Original");
        live.Color.Should().Be("#FFFFFF");
    }

    [Fact]
    public async Task RevertAsync_StatusAddedByTheOriginalApply_IsDeletedOnRevert()
    {
        var addedId = Guid.NewGuid();
        var added = new TaskStatusEntity { Id = addedId, TenantId = TenantId, Name = "New status" };
        var snapshot = new ProjectStatusTemplateUndoSnapshot([]); // added status wasn't in the pre-apply template
        _statuses.Setup(x => x.GetProjectTemplateAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<TaskStatusEntity> { added });
        _tasks.Setup(x => x.AnyActiveByStatusIdAsync(TenantId, addedId, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var outcome = await Build().RevertAsync(new ApprovalRevertContext(Request(snapshot), Guid.NewGuid()), CancellationToken.None);

        outcome.Kind.Should().Be(RevertOutcomeKind.Reverted);
        _statuses.Verify(x => x.Remove(added), Times.Once);
    }

    [Fact]
    public async Task RevertAsync_StatusAddedByApplyNowHasActiveTasks_ReturnsConflict()
    {
        var addedId = Guid.NewGuid();
        var added = new TaskStatusEntity { Id = addedId, TenantId = TenantId, Name = "New status" };
        var snapshot = new ProjectStatusTemplateUndoSnapshot([]);
        _statuses.Setup(x => x.GetProjectTemplateAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<TaskStatusEntity> { added });
        _tasks.Setup(x => x.AnyActiveByStatusIdAsync(TenantId, addedId, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var outcome = await Build().RevertAsync(new ApprovalRevertContext(Request(snapshot), Guid.NewGuid()), CancellationToken.None);

        outcome.Kind.Should().Be(RevertOutcomeKind.Conflict);
        _statuses.Verify(x => x.Remove(It.IsAny<TaskStatusEntity>()), Times.Never);
    }

    [Fact]
    public async Task RevertAsync_StatusDeletedByTheOriginalApply_IsReCreatedOnRevert()
    {
        var deletedId = Guid.NewGuid();
        var snapshot = new ProjectStatusTemplateUndoSnapshot([new ProjectStatusTemplateSnapshotEntry(deletedId, "Blocked", 2, TaskStatusCategories.Active, "#111111", TaskStatusVisibilities.Public, false)]);
        _statuses.Setup(x => x.GetProjectTemplateAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<TaskStatusEntity>());

        var outcome = await Build().RevertAsync(new ApprovalRevertContext(Request(snapshot), Guid.NewGuid()), CancellationToken.None);

        outcome.Kind.Should().Be(RevertOutcomeKind.Reverted);
        _statuses.Verify(x => x.AddAsync(It.Is<TaskStatusEntity>(s => s.Id == deletedId && s.Name == "Blocked"), It.IsAny<CancellationToken>()), Times.Once);
    }
}
