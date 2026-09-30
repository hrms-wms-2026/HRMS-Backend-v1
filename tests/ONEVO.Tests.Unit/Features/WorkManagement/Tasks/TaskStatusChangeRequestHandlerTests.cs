using System.Text.Json;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Approvals.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Hierarchy;
using ONEVO.Application.Features.WorkManagement.Notifications.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Appliers;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.CreateTaskStatusChangeRequest;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.Notifications.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using TaskStatusEntity = ONEVO.Domain.Features.WorkManagement.Tasks.Entities.TaskStatus;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

/// <summary>Project task-status template change requests, now a project.status_template_change on the approval engine.</summary>
public class TaskStatusChangeRequestHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid RootObjectiveId = Guid.NewGuid();
    private static readonly Guid RootOwnerId = Guid.NewGuid();
    private static readonly Guid RootMemberId = Guid.NewGuid();
    private static readonly Guid RequesterId = Guid.NewGuid();
    private static readonly Guid ToDo = Guid.NewGuid();
    private static readonly Guid Active = Guid.NewGuid();
    private static readonly Guid Done = Guid.NewGuid();

    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<ICallerIdentityResolver> _identity = new();
    private readonly Mock<IProjectRepository> _projects = new();
    private readonly Mock<ITaskStatusRepository> _statuses = new();
    private readonly Mock<IWorkTaskRepository> _tasks = new();
    private readonly Mock<ITaskStatusChangeAccessService> _access = new();
    private readonly Mock<ITaskStatusChangeRequestConflictSweeper> _sweeper = new();
    private readonly Mock<IWorkApprovalEngine> _approvals = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Objective _root = new()
    {
        Id = RootObjectiveId, TenantId = TenantId, ProjectId = ProjectId, OwnerId = RootOwnerId,
        IsDefault = true, IsActive = true, Title = "Portal", CreatedAt = DateTimeOffset.UtcNow
    };
    private readonly List<TaskStatusEntity> _live;

    public TaskStatusChangeRequestHandlerTests()
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        _currentUser.SetupGet(x => x.UserId).Returns(UserId);
        _projects.Setup(x => x.GetByIdForTenantAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Project { Id = ProjectId, TenantId = TenantId, IsActive = true, Name = "Portal", Identifier = "PRT", CreatedAt = DateTimeOffset.UtcNow });
        _identity.Setup(x => x.ResolveDisplayNamesByEmployeeIdAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, string> { [RequesterId] = "Req Uester" });

        _live =
        [
            new() { Id = ToDo, TenantId = TenantId, ProjectId = ProjectId, Name = "To Do", DisplayOrder = 0, Category = TaskStatusCategories.NotStarted, Color = "#94A3B8", Visibility = "public" },
            new() { Id = Active, TenantId = TenantId, ProjectId = ProjectId, Name = "In Process", DisplayOrder = 1, Category = TaskStatusCategories.Active, Color = "#2563EB", Visibility = "public" },
            new() { Id = Done, TenantId = TenantId, ProjectId = ProjectId, Name = "Complete", DisplayOrder = 2, Category = TaskStatusCategories.Done, Color = "#16A34A", Visibility = "public", MarksTaskComplete = true }
        ];
        _statuses.Setup(x => x.GetProjectTemplateAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(() => _live);

        _unitOfWork.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result<TaskStatusChangeRequestResponse>>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result<TaskStatusChangeRequestResponse>>> op, CancellationToken ct) => op(ct));
        _unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
    }

    private void CallerIs(Guid employeeId, bool canEditDirectly, bool canRequest)
    {
        _identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync(employeeId);
        _access.Setup(x => x.ResolveAsync(TenantId, ProjectId, employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TaskStatusChangeAccess(_root, canEditDirectly, canRequest));
    }

    private static TaskStatusChangeSet RenameActive(string from = "In Process", string to = "Doing") => new(
        [],
        [new TaskStatusUpdateChange(Active, new TaskStatusSnapshot(from, TaskStatusCategories.Active, "#2563EB", "public"),
            new TaskStatusSnapshot(to, TaskStatusCategories.Active, "#2563EB", "public"))],
        [],
        [ToDo, Active, Done],
        [ToDo.ToString(), Active.ToString(), Done.ToString()]);

    private CreateTaskStatusChangeRequestCommandHandler CreateHandler() => new(
        _currentUser.Object, _identity.Object, _projects.Object, _statuses.Object, _access.Object,
        _approvals.Object, _unitOfWork.Object);

    private TaskStatusTemplateChangeApplier Applier() => new(_currentUser.Object, _statuses.Object, _tasks.Object, _sweeper.Object);

    private static WorkApprovalRequest TemplateRequest(TaskStatusChangeSet changes, Guid? requestedBy = null) => new()
    {
        Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId,
        ActionType = WorkActionTypes.ProjectStatusTemplateChange, TargetType = WorkTargetTypes.Project,
        TargetTitle = "Portal", PositionObjectiveId = RootObjectiveId, ApproverSource = WorkApprovalSources.Hierarchy,
        ApproverEmployeeId = RootOwnerId, RequestedByEmployeeId = requestedBy ?? RequesterId,
        PayloadJson = JsonSerializer.Serialize(new TaskStatusTemplateChangePayload(changes, "please")),
        Status = WorkApprovalRequestStatuses.Pending
    };

    private Task<ApplyOutcome> Apply(WorkApprovalRequest request)
        => Applier().ApplyAsync(new ApprovalApplyContext(request, request.PayloadJson, RootOwnerId), CancellationToken.None);

    // ---- Create ----

    [Fact]
    public async Task Create_by_submodule_member_submits_template_change_to_engine_with_root_position()
    {
        CallerIs(RequesterId, canEditDirectly: false, canRequest: true);
        var requestId = Guid.NewGuid();
        _approvals.Setup(x => x.SubmitAsync(It.IsAny<WorkAction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ApprovalDecision>.Success(ApprovalDecision.Pending(requestId, RootOwnerId)));

        var result = await CreateHandler().Handle(new CreateTaskStatusChangeRequestCommand(ProjectId, " please ", RenameActive()), default);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(requestId, result.Value!.Id);
        Assert.Equal("pending", result.Value.Status);
        Assert.Equal("please", result.Value.Note);
        Assert.Equal("In Process", _live.Single(s => s.Id == Active).Name);
        _approvals.Verify(x => x.SubmitAsync(It.Is<WorkAction>(a =>
            a.ActionType == "project.status_template_change" && a.TargetType == "project" && a.TargetId == null &&
            a.TargetModuleId == RootObjectiveId && a.PositionModuleId == RootObjectiveId && a.ActorEmployeeId == RequesterId &&
            a.TargetTitle == "Portal" && a.PayloadJson.Contains("\"Changes\"")), It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Create_by_root_owner_is_refused_because_they_edit_directly()
    {
        CallerIs(RootOwnerId, canEditDirectly: true, canRequest: false);

        var result = await CreateHandler().Handle(new CreateTaskStatusChangeRequestCommand(ProjectId, null, RenameActive()), default);

        Assert.Equal(400, result.StatusCode);
        _approvals.Verify(x => x.SubmitAsync(It.IsAny<WorkAction>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_by_root_member_who_is_not_owner_now_goes_to_the_engine()
    {
        // Behaviour change (2026-09-28): root members used to edit directly; now they request.
        CallerIs(RootMemberId, canEditDirectly: false, canRequest: true);
        _approvals.Setup(x => x.SubmitAsync(It.IsAny<WorkAction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ApprovalDecision>.Success(ApprovalDecision.Pending(Guid.NewGuid(), RootOwnerId)));

        var result = await CreateHandler().Handle(new CreateTaskStatusChangeRequestCommand(ProjectId, null, RenameActive()), default);

        Assert.True(result.IsSuccess, result.Error);
        _approvals.Verify(x => x.SubmitAsync(It.Is<WorkAction>(a => a.ActorEmployeeId == RootMemberId), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Create_by_non_member_is_forbidden()
    {
        CallerIs(Guid.NewGuid(), canEditDirectly: false, canRequest: false);

        var result = await CreateHandler().Handle(new CreateTaskStatusChangeRequestCommand(ProjectId, null, RenameActive()), default);

        Assert.Equal(403, result.StatusCode);
        _approvals.Verify(x => x.SubmitAsync(It.IsAny<WorkAction>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_against_already_stale_snapshot_is_a_conflict()
    {
        CallerIs(RequesterId, canEditDirectly: false, canRequest: true);

        var result = await CreateHandler().Handle(
            new CreateTaskStatusChangeRequestCommand(ProjectId, null, RenameActive(from: "Old Name")), default);

        Assert.Equal(409, result.StatusCode);
        _approvals.Verify(x => x.SubmitAsync(It.IsAny<WorkAction>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_engine_failure_passes_through()
    {
        CallerIs(RequesterId, canEditDirectly: false, canRequest: true);
        _approvals.Setup(x => x.SubmitAsync(It.IsAny<WorkAction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ApprovalDecision>.UnprocessableEntity("No active approver."));

        var result = await CreateHandler().Handle(new CreateTaskStatusChangeRequestCommand(ProjectId, null, RenameActive()), default);

        Assert.Equal(422, result.StatusCode);
        Assert.Equal("No active approver.", result.Error);
        _unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---- Applier ----

    [Fact]
    public async Task Applier_applies_changes_and_sweeps_conflicts_excluding_itself()
    {
        var request = TemplateRequest(RenameActive());

        var outcome = await Apply(request);

        Assert.Equal(ApplyOutcomeKind.Applied, outcome.Kind);
        _statuses.Verify(x => x.Update(It.Is<TaskStatusEntity>(s => s.Id == Active && s.Name == "Doing")), Times.Once);
        _sweeper.Verify(x => x.MarkConflictingStaleAsync(TenantId, ProjectId, RootOwnerId,
            It.Is<TaskStatusChangeFootprint>(f => f.TouchedStatusIds.Contains(Active)), request.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Applier_stale_snapshot_returns_Stale_without_touching_statuses()
    {
        var request = TemplateRequest(RenameActive());
        _live.Single(s => s.Id == Active).Name = "Renamed By Someone Else";

        var outcome = await Apply(request);

        Assert.Equal(ApplyOutcomeKind.Stale, outcome.Kind);
        _statuses.Verify(x => x.Update(It.IsAny<TaskStatusEntity>()), Times.Never);
        _sweeper.Verify(x => x.MarkConflictingStaleAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(),
            It.IsAny<TaskStatusChangeFootprint>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Applier_delete_with_tasks_still_in_the_status_returns_Invalid_with_message()
    {
        var extra = Guid.NewGuid();
        _live.Add(new TaskStatusEntity { Id = extra, TenantId = TenantId, ProjectId = ProjectId, Name = "Blocked", DisplayOrder = 3, Category = TaskStatusCategories.Active, Color = "#DC2626", Visibility = "public" });
        _tasks.Setup(x => x.AnyActiveByStatusIdAsync(TenantId, extra, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var request = TemplateRequest(new TaskStatusChangeSet([], [], [new TaskStatusDeleteChange(extra, "Blocked")],
            [ToDo, Active, Done, extra], [ToDo.ToString(), Active.ToString(), Done.ToString()]));

        var outcome = await Apply(request);

        Assert.Equal(ApplyOutcomeKind.Invalid, outcome.Kind);
        Assert.Equal("Move all tasks out of \"Blocked\" before approving its deletion.", outcome.Error);
        _statuses.Verify(x => x.Remove(It.IsAny<TaskStatusEntity>()), Times.Never);
    }

    [Fact]
    public async Task Applier_reads_migrated_PascalCase_payload()
    {
        // Shape the data migration writes: jsonb_build_object('Changes', changes_json, 'Note', note).
        var request = TemplateRequest(RenameActive());
        request.PayloadJson = $"{{\"Changes\":{JsonSerializer.Serialize(RenameActive())},\"Note\":\"x\"}}";

        var outcome = await Apply(request);

        Assert.Equal(ApplyOutcomeKind.Applied, outcome.Kind);
        _statuses.Verify(x => x.Update(It.Is<TaskStatusEntity>(s => s.Id == Active && s.Name == "Doing")), Times.Once);
    }

    // ---- Sweeper ----

    [Fact]
    public async Task Sweeper_marks_only_conflicting_pending_requests_stale_and_notifies_their_requesters()
    {
        var conflicting = TemplateRequest(RenameActive());
        var unrelated = TemplateRequest(new TaskStatusChangeSet([],
            [new TaskStatusUpdateChange(ToDo, new TaskStatusSnapshot("To Do", "not_started", "#94A3B8", "public"),
                new TaskStatusSnapshot("Backlog", "not_started", "#94A3B8", "public"))],
            [], [ToDo, Active, Done], [ToDo.ToString(), Active.ToString(), Done.ToString()]));
        var requests = new Mock<IWorkApprovalRequestRepository>();
        requests.Setup(x => x.ListTrackedPendingByActionAsync(TenantId, ProjectId, WorkActionTypes.ProjectStatusTemplateChange, It.IsAny<CancellationToken>()))
            .ReturnsAsync([conflicting, unrelated]);
        var notifications = new Mock<IWorkNotificationEngine>();
        var sweeper = new TaskStatusChangeRequestConflictSweeper(requests.Object, notifications.Object);

        var count = await sweeper.MarkConflictingStaleAsync(
            TenantId, ProjectId, RootOwnerId, new TaskStatusChangeFootprint(new HashSet<Guid> { Active }, false), null);

        Assert.Equal(1, count);
        Assert.Equal(WorkApprovalRequestStatuses.Stale, conflicting.Status);
        Assert.Equal(TaskStatusChangeRequestConflictSweeper.OutdatedComment, conflicting.DecisionComment);
        Assert.Equal(WorkApprovalRequestStatuses.Pending, unrelated.Status);
        requests.Verify(x => x.Update(conflicting), Times.Once);
        notifications.Verify(x => x.NotifyAsync(It.Is<WorkNotificationEvent>(e =>
            e.Kind == WorkNotificationKinds.Stale && e.ActionType == WorkActionTypes.ProjectStatusTemplateChange &&
            e.ApprovalRequestId == conflicting.Id && e.RecipientEmployeeIds.SequenceEqual(new[] { RequesterId })),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // ---- Access + decision rule ----

    [Fact]
    public async Task Access_direct_edit_is_root_owner_only_and_root_member_can_request()
    {
        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetDefaultByProjectIdAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(_root);
        var subOwner = Guid.NewGuid();
        objectives.Setup(x => x.GetAllByProjectIdAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            _root,
            new Objective { Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, ParentObjectiveId = RootObjectiveId, OwnerId = subOwner, IsActive = true, Title = "Sub" }
        ]);
        var members = new Mock<IProjectMemberRepository>();
        members.Setup(x => x.HasActiveMembershipAsync(TenantId, ProjectId, RootMemberId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var service = new TaskStatusChangeAccessService(objectives.Object, members.Object);

        var rootOwner = await service.ResolveAsync(TenantId, ProjectId, RootOwnerId);
        var rootMember = await service.ResolveAsync(TenantId, ProjectId, RootMemberId);
        var subModuleOwner = await service.ResolveAsync(TenantId, ProjectId, subOwner);
        var outsider = await service.ResolveAsync(TenantId, ProjectId, Guid.NewGuid());

        Assert.True(rootOwner!.CanEditDirectly);
        Assert.False(rootMember!.CanEditDirectly);
        Assert.True(rootMember.CanRequest);
        Assert.False(subModuleOwner!.CanEditDirectly);
        Assert.True(subModuleOwner.CanRequest);
        Assert.False(outsider!.CanRequest);
    }

    [Fact]
    public void Root_member_cannot_decide_a_template_request()
    {
        var tree = new ProjectModuleTree([_root]);
        var request = TemplateRequest(RenameActive());

        Assert.False(WorkApprovalDecisionRules.CanDecide(tree, request, RootMemberId));
        Assert.True(WorkApprovalDecisionRules.CanDecide(tree, request, RootOwnerId));
    }
}
