using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Hierarchy;
using ONEVO.Application.Features.WorkManagement.Notifications.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.CreateTask;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.DeleteTask;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.EditTask;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.Notifications.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

/// <summary>Create/edit/delete ask IWorkApprovalEngine whether to apply now or file a request.</summary>
public class TaskActionsThroughEngineTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid CategoryId = Guid.NewGuid();
    private static readonly Guid RootOwner = Guid.NewGuid();
    private static readonly Guid MidOwner = Guid.NewGuid();
    private static readonly Guid LeafOwner = Guid.NewGuid();
    private static readonly Guid Member = Guid.NewGuid();
    private static readonly Guid Assignee = Guid.NewGuid();

    private readonly Objective _root = new() { Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, OwnerId = RootOwner, IsDefault = true, IsActive = true };
    private readonly Objective _mid;
    private readonly Objective _leaf;
    private readonly WorkTask _task;

    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<ICallerIdentityResolver> _identity = new();
    private readonly Mock<IObjectiveRepository> _objectives = new();
    private readonly Mock<IWorkTaskRepository> _tasks = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IMilestoneMembershipCoordinator> _membership = new();
    private readonly Mock<ITaskAssignmentRepository> _assignments = new();
    private readonly Mock<ITaskAssetLinker> _assetLinker = new();
    private readonly Mock<ITaskWriteService> _writes = new();
    private readonly Mock<IWorkHierarchyService> _hierarchy = new();
    private readonly Mock<IWorkApprovalEngine> _approvals = new();
    private readonly Mock<IWorkNotificationEngine> _notifications = new();
    private Guid _caller = Member;

    public TaskActionsThroughEngineTests()
    {
        _mid = new() { Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, OwnerId = MidOwner, ParentObjectiveId = _root.Id, IsActive = true };
        _leaf = new() { Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, OwnerId = LeafOwner, ParentObjectiveId = _mid.Id, IsActive = true };
        _task = new()
        {
            Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, ObjectiveId = _leaf.Id, Title = "Fix login",
            ShortId = "WEB-1", CreatorPositionObjectiveId = _mid.Id, CreatedAt = DateTimeOffset.UtcNow.AddDays(-2),
            UpdatedAt = DateTimeOffset.UtcNow.AddDays(-1)
        };

        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        _currentUser.SetupGet(x => x.UserId).Returns(UserId);
        _identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _caller);
        _objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, _leaf.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_leaf);
        _tasks.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, _task.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_task);
        _membership.Setup(x => x.IsEffectiveManagerAsync(TenantId, _leaf.Id, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _assignments.Setup(x => x.GetByTaskIdAsync(_task.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new TaskAssignment { TaskId = _task.Id, EmployeeId = Assignee } });
        _hierarchy.Setup(x => x.LoadTreeAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProjectModuleTree(new[] { _root, _mid, _leaf }));
        _unitOfWork.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result<TaskWriteOutcome>>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result<TaskWriteOutcome>>> op, CancellationToken ct) => op(ct));

        _writes.Setup(x => x.ValidateCreateAsync(TenantId, It.IsAny<TaskCreateInput>(), It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success());
        _writes.Setup(x => x.ValidateEditAsync(TenantId, _task, _leaf, It.IsAny<TaskEditInput>(), It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success());
        _writes.Setup(x => x.ApplyEditAsync(TenantId, It.IsAny<Guid>(), _task, _leaf, It.IsAny<TaskEditInput>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        _writes.Setup(x => x.CreateAsync(TenantId, UserId, It.IsAny<Guid>(), It.IsAny<TaskCreateInput>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, Guid _, Guid _, TaskCreateInput input, Guid position, CancellationToken _) =>
                Result<WorkTask>.Success(new WorkTask
                {
                    Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, ObjectiveId = input.ObjectiveId,
                    Title = input.Title, ShortId = "WEB-9", CreatorPositionObjectiveId = position
                }));
    }

    private void EngineReturns(Result<ApprovalDecision> decision)
        => _approvals.Setup(x => x.SubmitAsync(It.IsAny<WorkAction>(), It.IsAny<CancellationToken>())).ReturnsAsync(decision);

    private CreateTaskCommandHandler CreateHandler() => new(
        _currentUser.Object, _identity.Object, _objectives.Object, _unitOfWork.Object, _membership.Object,
        _assetLinker.Object, _writes.Object, _hierarchy.Object, _approvals.Object, _notifications.Object);

    private EditTaskCommandHandler EditHandler() => new(
        _currentUser.Object, _tasks.Object, _objectives.Object, _unitOfWork.Object, _identity.Object, _membership.Object,
        _assignments.Object, _assetLinker.Object, _writes.Object, _hierarchy.Object, _approvals.Object, _notifications.Object);

    private DeleteTaskCommandHandler DeleteHandler() => new(
        _currentUser.Object, _identity.Object, _tasks.Object, _objectives.Object, _unitOfWork.Object, _membership.Object,
        _assignments.Object, _writes.Object, _hierarchy.Object, _approvals.Object, _notifications.Object);

    private CreateTaskCommand CreateCommand() => new(_leaf.Id, " New task ", null, CategoryId, WorkTaskPriorities.Medium, null, null, null, null);
    private EditTaskCommand EditCommand() => new(_task.Id, "Renamed", null, WorkTaskPriorities.High, null, null, null, null, null);

    [Fact]
    public async Task Create_EnginePending_Returns202Outcome_NoTaskInserted_Saved()
    {
        var requestId = Guid.NewGuid();
        EngineReturns(Result<ApprovalDecision>.Success(ApprovalDecision.Pending(requestId, LeafOwner)));

        var result = await CreateHandler().Handle(CreateCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.Task);
        Assert.Equal(requestId, result.Value.ApprovalRequestId);
        _writes.Verify(x => x.CreateAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<TaskCreateInput>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _approvals.Verify(x => x.SubmitAsync(It.Is<WorkAction>(a =>
            a.ActionType == WorkActionTypes.TaskCreate && a.TargetType == WorkTargetTypes.Task && a.TargetId == null &&
            a.TargetTitle == "New task" && a.TargetModuleId == _leaf.Id && a.PositionModuleId == _leaf.Id &&
            a.ActorEmployeeId == Member && a.PayloadJson.Contains("\"Title\":\"New task\"")), It.IsAny<CancellationToken>()), Times.Once);
        _assetLinker.Verify(x => x.SyncAttachmentsAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_EngineDirect_CreatesWithHighestOwnedModuleAsPosition_NotifiesModuleOwner()
    {
        // Caller owns both the leaf's parent (mid) and the leaf itself: the position is the one nearer the root.
        _caller = MidOwner;
        _leaf.OwnerId = MidOwner;
        EngineReturns(Result<ApprovalDecision>.Success(ApprovalDecision.Direct));

        var result = await CreateHandler().Handle(CreateCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value!.Task);
        Assert.Null(result.Value.ApprovalRequestId);
        _writes.Verify(x => x.CreateAsync(TenantId, UserId, MidOwner, It.IsAny<TaskCreateInput>(), _mid.Id, It.IsAny<CancellationToken>()), Times.Once);
        _notifications.Verify(x => x.NotifyAsync(It.Is<WorkNotificationEvent>(e =>
            e.Kind == WorkNotificationKinds.Direct && e.ActionType == WorkActionTypes.TaskCreate &&
            e.RecipientEmployeeIds.SequenceEqual(new[] { MidOwner })), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Create_EngineDirect_ActorOwnsNothingInChain_PositionIsTargetModule()
    {
        // e.g. the engine's HR fallback let a non-owner apply directly.
        EngineReturns(Result<ApprovalDecision>.Success(ApprovalDecision.Direct));

        var result = await CreateHandler().Handle(CreateCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _writes.Verify(x => x.CreateAsync(TenantId, UserId, Member, It.IsAny<TaskCreateInput>(), _leaf.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Create_NotModuleMember_Forbidden_EngineNeverCalled()
    {
        _membership.Setup(x => x.IsEffectiveManagerAsync(TenantId, _leaf.Id, Member, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await CreateHandler().Handle(CreateCommand(), CancellationToken.None);

        Assert.Equal(403, result.StatusCode);
        _approvals.Verify(x => x.SubmitAsync(It.IsAny<WorkAction>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_ValidationFails_ReturnsItsStatus_EngineNeverCalled()
    {
        _writes.Setup(x => x.ValidateCreateAsync(TenantId, It.IsAny<TaskCreateInput>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure("No task statuses configured for this milestone yet.", 422));

        var result = await CreateHandler().Handle(CreateCommand(), CancellationToken.None);

        Assert.Equal(422, result.StatusCode);
        Assert.Equal("No task statuses configured for this milestone yet.", result.Error);
        _approvals.Verify(x => x.SubmitAsync(It.IsAny<WorkAction>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Edit_PassesCreatorPositionAndUpdatedAtToEngine()
    {
        EngineReturns(Result<ApprovalDecision>.Success(ApprovalDecision.Pending(Guid.NewGuid(), MidOwner)));

        var result = await EditHandler().Handle(EditCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.Task);
        _approvals.Verify(x => x.SubmitAsync(It.Is<WorkAction>(a =>
            a.ActionType == WorkActionTypes.TaskEdit && a.TargetId == _task.Id && a.TargetTitle == "Fix login" &&
            a.TargetModuleId == _leaf.Id && a.PositionModuleId == _mid.Id && a.TargetUpdatedAt == _task.UpdatedAt),
            It.IsAny<CancellationToken>()), Times.Once);
        _writes.Verify(x => x.ApplyEditAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<WorkTask>(), It.IsAny<Objective>(),
            It.IsAny<TaskEditInput>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Edit_EngineDirect_AppliesWithDirectSource_NotifiesPositionHolderAssigneesAndOwner()
    {
        _caller = RootOwner;
        EngineReturns(Result<ApprovalDecision>.Success(ApprovalDecision.Direct));

        var result = await EditHandler().Handle(EditCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value!.Task);
        _writes.Verify(x => x.ApplyEditAsync(TenantId, RootOwner, _task, _leaf, It.Is<TaskEditInput>(i => i.Title == "Renamed"),
            TaskEditLogSources.Direct, null, It.IsAny<CancellationToken>()), Times.Once);
        _notifications.Verify(x => x.NotifyAsync(It.Is<WorkNotificationEvent>(e =>
            e.ActionType == WorkActionTypes.TaskEdit && e.Kind == WorkNotificationKinds.Direct && e.TargetId == _task.Id &&
            e.RecipientEmployeeIds.OrderBy(g => g).SequenceEqual(new[] { MidOwner, Assignee, LeafOwner }.OrderBy(g => g))),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Edit_EngineConflict409_PassesThrough()
    {
        EngineReturns(Result<ApprovalDecision>.Conflict("A change to this task is already waiting for approval."));

        var result = await EditHandler().Handle(EditCommand(), CancellationToken.None);

        Assert.Equal(409, result.StatusCode);
        Assert.Equal("A change to this task is already waiting for approval.", result.Error);
        _unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Delete_EnginePending_TaskNotRemoved()
    {
        var requestId = Guid.NewGuid();
        EngineReturns(Result<ApprovalDecision>.Success(ApprovalDecision.Pending(requestId, MidOwner)));

        var result = await DeleteHandler().Handle(new DeleteTaskCommand(_task.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(requestId, result.Value!.ApprovalRequestId);
        _writes.Verify(x => x.Delete(It.IsAny<WorkTask>()), Times.Never);
        _approvals.Verify(x => x.SubmitAsync(It.Is<WorkAction>(a =>
            a.ActionType == WorkActionTypes.TaskDelete && a.TargetId == _task.Id && a.PositionModuleId == _mid.Id &&
            a.PayloadJson == "{}"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Delete_EngineDirect_RemovesAndNotifies()
    {
        _caller = MidOwner;
        EngineReturns(Result<ApprovalDecision>.Success(ApprovalDecision.Direct));

        var result = await DeleteHandler().Handle(new DeleteTaskCommand(_task.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.ApprovalRequestId);
        Assert.Null(result.Value.Task);
        _writes.Verify(x => x.Delete(_task), Times.Once);
        _notifications.Verify(x => x.NotifyAsync(It.Is<WorkNotificationEvent>(e =>
            e.ActionType == WorkActionTypes.TaskDelete && e.TargetId == _task.Id &&
            e.RecipientEmployeeIds.Contains(Assignee) && e.RecipientEmployeeIds.Contains(LeafOwner) && e.RecipientEmployeeIds.Contains(MidOwner)),
            It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
