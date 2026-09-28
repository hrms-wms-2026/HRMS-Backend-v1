using System.Text.Json;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.Appliers;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class TaskAppliersTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();
    private static readonly Guid CategoryId = Guid.NewGuid();
    private static readonly Guid RequesterEmployeeId = Guid.NewGuid();
    private static readonly Guid RequesterUserId = Guid.NewGuid();
    private static readonly Guid DeciderEmployeeId = Guid.NewGuid();

    private readonly Mock<ITaskWriteService> _writes = new();
    private readonly Mock<IObjectiveRepository> _objectives = new();
    private readonly Mock<IMilestoneMembershipCoordinator> _membership = new();
    private readonly Mock<IWorkTaskRepository> _tasks = new();
    private readonly Objective _objective = new() { Id = ObjectiveId, TenantId = TenantId, ProjectId = ProjectId, IsActive = true };
    private readonly WorkTask _task = new()
    {
        Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, ObjectiveId = ObjectiveId, Title = "Old",
        ShortId = "WEB-1", CreatedAt = DateTimeOffset.UtcNow.AddDays(-3), UpdatedAt = DateTimeOffset.UtcNow.AddDays(-2)
    };

    public TaskAppliersTests()
    {
        _objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>())).ReturnsAsync(_objective);
        _membership.Setup(x => x.GetActiveAssigneeAsync(TenantId, RequesterEmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = RequesterEmployeeId, TenantId = TenantId, UserId = RequesterUserId });
        _tasks.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, _task.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_task);
    }

    private static WorkApprovalRequest Request(string actionType, Guid? targetId, string payload, DateTimeOffset? snapshot = null) => new()
    {
        Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, ActionType = actionType, TargetType = WorkTargetTypes.Task,
        TargetId = targetId, RequestedByEmployeeId = RequesterEmployeeId, PayloadJson = payload, TargetUpdatedAtSnapshot = snapshot
    };

    private static string CreatePayload(string title = "New task")
        => JsonSerializer.Serialize(new TaskCreateInput(ObjectiveId, title, null, CategoryId, WorkTaskPriorities.Medium, null, null, null, null));

    private static string EditPayload()
        => JsonSerializer.Serialize(new TaskEditInput("Renamed", null, WorkTaskPriorities.High, null, null, null, null, "why", null));

    private TaskCreateApplier CreateApplier() => new(_writes.Object, _objectives.Object, _membership.Object);
    private TaskEditApplier EditApplier() => new(_writes.Object, _objectives.Object, _tasks.Object);
    private TaskDeleteApplier DeleteApplier() => new(_writes.Object, _tasks.Object);

    private static Task<ApplyOutcome> Apply(IApprovalActionApplier applier, WorkApprovalRequest request)
        => applier.ApplyAsync(new ApprovalApplyContext(request, request.PayloadJson, DeciderEmployeeId), CancellationToken.None);

    [Fact]
    public async Task Create_AppliesAsRequester_PositionIsTargetModule_StoresCreatedTaskIdAsTarget()
    {
        var created = new WorkTask { Id = Guid.NewGuid() };
        _writes.Setup(x => x.CreateAsync(TenantId, RequesterUserId, RequesterEmployeeId, It.Is<TaskCreateInput>(i => i.Title == "New task"),
                ObjectiveId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<WorkTask>.Success(created));
        var request = Request(WorkActionTypes.TaskCreate, null, CreatePayload());

        var outcome = await Apply(CreateApplier(), request);

        Assert.Equal(ApplyOutcomeKind.Applied, outcome.Kind);
        Assert.Equal(created.Id, request.TargetId);
    }

    [Fact]
    public async Task Create_ObjectiveGone_Stale()
    {
        _objective.IsActive = false;

        var outcome = await Apply(CreateApplier(), Request(WorkActionTypes.TaskCreate, null, CreatePayload()));

        Assert.Equal(ApplyOutcomeKind.Stale, outcome.Kind);
        _writes.Verify(x => x.CreateAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<TaskCreateInput>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_ValidationFails_Invalid_WithMessage()
    {
        _writes.Setup(x => x.CreateAsync(TenantId, RequesterUserId, RequesterEmployeeId, It.IsAny<TaskCreateInput>(), ObjectiveId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<WorkTask>.NotFound("Category not found."));

        var outcome = await Apply(CreateApplier(), Request(WorkActionTypes.TaskCreate, null, CreatePayload()));

        Assert.Equal(ApplyOutcomeKind.Invalid, outcome.Kind);
        Assert.Equal("Category not found.", outcome.Error);
    }

    [Fact]
    public async Task Create_RequesterInactive_Stale()
    {
        _membership.Setup(x => x.GetActiveAssigneeAsync(TenantId, RequesterEmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Employee?)null);

        var outcome = await Apply(CreateApplier(), Request(WorkActionTypes.TaskCreate, null, CreatePayload()));

        Assert.Equal(ApplyOutcomeKind.Stale, outcome.Kind);
    }

    [Fact]
    public async Task Create_ReadsLegacyPascalCasePayload()
    {
        // Shape the data migration writes: the old task-creation payload plus ObjectiveId, PascalCase.
        var legacy = $"{{\"ObjectiveId\":\"{ObjectiveId}\",\"Title\":\"Legacy\",\"Description\":null,\"CategoryId\":\"{CategoryId}\"," +
                     "\"Priority\":\"high\",\"DueDate\":null,\"EstimatedHours\":4.5,\"StoryPoints\":3,\"SprintId\":null}";
        _writes.Setup(x => x.CreateAsync(TenantId, RequesterUserId, RequesterEmployeeId, It.IsAny<TaskCreateInput>(), ObjectiveId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<WorkTask>.Success(new WorkTask { Id = Guid.NewGuid() }));

        var outcome = await Apply(CreateApplier(), Request(WorkActionTypes.TaskCreate, null, legacy));

        Assert.Equal(ApplyOutcomeKind.Applied, outcome.Kind);
        _writes.Verify(x => x.CreateAsync(TenantId, RequesterUserId, RequesterEmployeeId, It.Is<TaskCreateInput>(i =>
            i.ObjectiveId == ObjectiveId && i.Title == "Legacy" && i.CategoryId == CategoryId && i.Priority == "high" &&
            i.EstimatedHours == 4.5m && i.StoryPoints == 3), ObjectiveId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Edit_TaskChangedAfterRequest_Stale()
    {
        var request = Request(WorkActionTypes.TaskEdit, _task.Id, EditPayload(), snapshot: _task.UpdatedAt!.Value.AddHours(-1));

        var outcome = await Apply(EditApplier(), request);

        Assert.Equal(ApplyOutcomeKind.Stale, outcome.Kind);
        _writes.Verify(x => x.ApplyEditAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<WorkTask>(), It.IsAny<Objective>(),
            It.IsAny<TaskEditInput>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Edit_NoSnapshot_AppliesWithApprovedRequestSourceAndRequestId()
    {
        _writes.Setup(x => x.ApplyEditAsync(TenantId, RequesterEmployeeId, _task, _objective, It.IsAny<TaskEditInput>(),
                TaskEditLogSources.ApprovedRequest, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        var request = Request(WorkActionTypes.TaskEdit, _task.Id, EditPayload());

        var outcome = await Apply(EditApplier(), request);

        Assert.Equal(ApplyOutcomeKind.Applied, outcome.Kind);
        _writes.Verify(x => x.ApplyEditAsync(TenantId, RequesterEmployeeId, _task, _objective,
            It.Is<TaskEditInput>(i => i.Title == "Renamed" && i.Reason == "why"),
            TaskEditLogSources.ApprovedRequest, request.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Edit_TaskGone_Stale()
    {
        var outcome = await Apply(EditApplier(), Request(WorkActionTypes.TaskEdit, Guid.NewGuid(), EditPayload()));

        Assert.Equal(ApplyOutcomeKind.Stale, outcome.Kind);
    }

    [Fact]
    public async Task Delete_TaskGone_Stale()
    {
        var outcome = await Apply(DeleteApplier(), Request(WorkActionTypes.TaskDelete, Guid.NewGuid(), "{}"));

        Assert.Equal(ApplyOutcomeKind.Stale, outcome.Kind);
        _writes.Verify(x => x.Delete(It.IsAny<WorkTask>()), Times.Never);
    }

    [Fact]
    public async Task Delete_RemovesTask()
    {
        var outcome = await Apply(DeleteApplier(), Request(WorkActionTypes.TaskDelete, _task.Id, "{}"));

        Assert.Equal(ApplyOutcomeKind.Applied, outcome.Kind);
        _writes.Verify(x => x.Delete(_task), Times.Once);
    }
}
