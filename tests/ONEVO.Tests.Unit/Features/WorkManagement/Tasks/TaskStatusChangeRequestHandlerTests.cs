using System.Text.Json;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.ApproveTaskStatusChangeRequest;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.CancelTaskStatusChangeRequest;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.CreateTaskStatusChangeRequest;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.RejectTaskStatusChangeRequest;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Lookups;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.ProjectMembers.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using TaskStatusEntity = ONEVO.Domain.Features.WorkManagement.Tasks.Entities.TaskStatus;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class TaskStatusChangeRequestHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid RootObjectiveId = Guid.NewGuid();
    private static readonly Guid RootOwnerId = Guid.NewGuid();
    private static readonly Guid RootMemberId = Guid.NewGuid();
    private static readonly Guid RequesterId = Guid.NewGuid();
    private static readonly Guid RequesterUserId = Guid.NewGuid();
    private static readonly Guid ToDo = Guid.NewGuid();
    private static readonly Guid Active = Guid.NewGuid();
    private static readonly Guid Done = Guid.NewGuid();

    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<ICallerIdentityResolver> _identity = new();
    private readonly Mock<IProjectRepository> _projects = new();
    private readonly Mock<ITaskStatusRepository> _statuses = new();
    private readonly Mock<IWorkTaskRepository> _tasks = new();
    private readonly Mock<ITaskStatusChangeRequestRepository> _requests = new();
    private readonly Mock<ITaskStatusChangeAccessService> _access = new();
    private readonly Mock<ITaskStatusChangeRequestConflictSweeper> _sweeper = new();
    private readonly Mock<IMilestoneMembershipCoordinator> _membership = new();
    private readonly Mock<INotificationDispatcher> _notifications = new();
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
        _membership.Setup(x => x.GetActiveAssigneeAsync(TenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, Guid employeeId, CancellationToken _) => new Employee
            {
                Id = employeeId, TenantId = TenantId, UserId = employeeId == RequesterId ? RequesterUserId : Guid.NewGuid(),
                EmploymentStatusId = EmploymentStatusIds.Active
            });
        _access.Setup(x => x.ListApproverEmployeeIdsAsync(TenantId, _root, It.IsAny<CancellationToken>()))
            .ReturnsAsync([RootOwnerId, RootMemberId]);

        _live =
        [
            new() { Id = ToDo, TenantId = TenantId, ProjectId = ProjectId, Name = "To Do", DisplayOrder = 0, Category = TaskStatusCategories.NotStarted, Color = "#94A3B8", Visibility = "public" },
            new() { Id = Active, TenantId = TenantId, ProjectId = ProjectId, Name = "In Process", DisplayOrder = 1, Category = TaskStatusCategories.Active, Color = "#2563EB", Visibility = "public" },
            new() { Id = Done, TenantId = TenantId, ProjectId = ProjectId, Name = "Complete", DisplayOrder = 2, Category = TaskStatusCategories.Done, Color = "#16A34A", Visibility = "public", MarksTaskComplete = true }
        ];
        _statuses.Setup(x => x.GetProjectTemplateAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(() => _live);

        _unitOfWork.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result>> op, CancellationToken ct) => op(ct));
        _unitOfWork.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result<TaskStatusChangeRequestResponse>>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result<TaskStatusChangeRequestResponse>>> op, CancellationToken ct) => op(ct));
        _unitOfWork.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result<IReadOnlyList<TaskStatusResponse>>>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result<IReadOnlyList<TaskStatusResponse>>>> op, CancellationToken ct) => op(ct));
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
        _currentUser.Object, _identity.Object, _projects.Object, _statuses.Object, _requests.Object,
        _access.Object, _membership.Object, _notifications.Object, _unitOfWork.Object);

    private ApproveTaskStatusChangeRequestCommandHandler ApproveHandler() => new(
        _currentUser.Object, _identity.Object, _projects.Object, _statuses.Object, _tasks.Object, _requests.Object,
        _access.Object, _sweeper.Object, _membership.Object, _notifications.Object, _unitOfWork.Object);

    private TaskStatusChangeRequest PendingRequest(TaskStatusChangeSet changes)
    {
        var entity = new TaskStatusChangeRequest
        {
            Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, RequestedByEmployeeId = RequesterId,
            ChangesJson = JsonSerializer.Serialize(changes), Status = TaskStatusChangeRequestStatuses.Pending,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _requests.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, entity.Id, It.IsAny<CancellationToken>())).ReturnsAsync(entity);
        return entity;
    }

    [Fact]
    public async Task Create_by_submodule_member_queues_request_and_notifies_every_root_approver()
    {
        CallerIs(RequesterId, canEditDirectly: false, canRequest: true);
        TaskStatusChangeRequest? saved = null;
        _requests.Setup(x => x.AddAsync(It.IsAny<TaskStatusChangeRequest>(), It.IsAny<CancellationToken>()))
            .Callback<TaskStatusChangeRequest, CancellationToken>((r, _) => saved = r);

        var result = await CreateHandler().Handle(new CreateTaskStatusChangeRequestCommand(ProjectId, "please", RenameActive()), default);

        Assert.True(result.IsSuccess, result.Error);
        Assert.NotNull(saved);
        Assert.Equal(TaskStatusChangeRequestStatuses.Pending, saved!.Status);
        Assert.Equal("In Process", _live.Single(s => s.Id == Active).Name);
        _notifications.Verify(x => x.SendTemplatedAsync(TenantId, It.IsAny<Guid>(), "work_task_status_change_request_created",
            It.IsAny<IReadOnlyDictionary<string, string>>(), "task_status_change_request", saved.Id, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Create_by_root_approver_is_refused_because_they_edit_directly()
    {
        CallerIs(RootMemberId, canEditDirectly: true, canRequest: false);

        var result = await CreateHandler().Handle(new CreateTaskStatusChangeRequestCommand(ProjectId, null, RenameActive()), default);

        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task Create_by_non_member_is_forbidden()
    {
        CallerIs(Guid.NewGuid(), canEditDirectly: false, canRequest: false);

        var result = await CreateHandler().Handle(new CreateTaskStatusChangeRequestCommand(ProjectId, null, RenameActive()), default);

        Assert.Equal(403, result.StatusCode);
        _requests.Verify(x => x.AddAsync(It.IsAny<TaskStatusChangeRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_against_already_stale_snapshot_is_a_conflict()
    {
        CallerIs(RequesterId, canEditDirectly: false, canRequest: true);

        var result = await CreateHandler().Handle(
            new CreateTaskStatusChangeRequestCommand(ProjectId, null, RenameActive(from: "Old Name")), default);

        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task Approve_by_root_member_applies_changes_marks_approved_and_sweeps_conflicts()
    {
        CallerIs(RootMemberId, canEditDirectly: true, canRequest: false);
        var pending = PendingRequest(RenameActive());

        var result = await ApproveHandler().Handle(new ApproveTaskStatusChangeRequestCommand(pending.Id), default);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("Doing", result.Value!.Single(s => s.Id == Active).Name);
        Assert.Equal(TaskStatusChangeRequestStatuses.Approved, pending.Status);
        Assert.Equal(RootMemberId, pending.DecidedByEmployeeId);
        _statuses.Verify(x => x.Update(It.Is<TaskStatusEntity>(s => s.Id == Active && s.Name == "Doing")), Times.Once);
        _sweeper.Verify(x => x.MarkConflictingOutdatedAsync(TenantId, ProjectId, "Portal",
            It.Is<TaskStatusChangeFootprint>(f => f.TouchedStatusIds.Contains(Active)), pending.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Approve_by_submodule_member_is_forbidden()
    {
        CallerIs(RequesterId, canEditDirectly: false, canRequest: true);
        var pending = PendingRequest(RenameActive());

        var result = await ApproveHandler().Handle(new ApproveTaskStatusChangeRequestCommand(pending.Id), default);

        Assert.Equal(403, result.StatusCode);
        Assert.Equal(TaskStatusChangeRequestStatuses.Pending, pending.Status);
    }

    [Fact]
    public async Task Approve_of_stale_request_closes_it_as_outdated_without_touching_statuses()
    {
        CallerIs(RootOwnerId, canEditDirectly: true, canRequest: false);
        var pending = PendingRequest(RenameActive());
        _live.Single(s => s.Id == Active).Name = "Renamed By Someone Else";

        var result = await ApproveHandler().Handle(new ApproveTaskStatusChangeRequestCommand(pending.Id), default);

        Assert.Equal(409, result.StatusCode);
        Assert.Equal(TaskStatusChangeRequestStatuses.Outdated, pending.Status);
        _statuses.Verify(x => x.Update(It.IsAny<TaskStatusEntity>()), Times.Never);
    }

    [Fact]
    public async Task Approve_of_delete_with_tasks_still_in_the_status_stays_pending()
    {
        CallerIs(RootOwnerId, canEditDirectly: true, canRequest: false);
        var extra = Guid.NewGuid();
        _live.Add(new TaskStatusEntity { Id = extra, TenantId = TenantId, ProjectId = ProjectId, Name = "Blocked", DisplayOrder = 3, Category = TaskStatusCategories.Active, Color = "#DC2626", Visibility = "public" });
        _tasks.Setup(x => x.AnyActiveByStatusIdAsync(TenantId, extra, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var pending = PendingRequest(new TaskStatusChangeSet([], [], [new TaskStatusDeleteChange(extra, "Blocked")],
            [ToDo, Active, Done, extra], [ToDo.ToString(), Active.ToString(), Done.ToString()]));

        var result = await ApproveHandler().Handle(new ApproveTaskStatusChangeRequestCommand(pending.Id), default);

        Assert.Equal(409, result.StatusCode);
        Assert.Equal(TaskStatusChangeRequestStatuses.Pending, pending.Status);
        _statuses.Verify(x => x.Remove(It.IsAny<TaskStatusEntity>()), Times.Never);
    }

    [Fact]
    public async Task Reject_by_root_approver_records_decision()
    {
        CallerIs(RootOwnerId, canEditDirectly: true, canRequest: false);
        var pending = PendingRequest(RenameActive());
        var handler = new RejectTaskStatusChangeRequestCommandHandler(
            _currentUser.Object, _identity.Object, _projects.Object, _requests.Object, _access.Object,
            _membership.Object, _notifications.Object, _unitOfWork.Object);

        var result = await handler.Handle(new RejectTaskStatusChangeRequestCommand(pending.Id, "not now"), default);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(TaskStatusChangeRequestStatuses.Rejected, pending.Status);
        Assert.Equal("not now", pending.DecisionComment);
    }

    [Fact]
    public async Task Cancel_is_only_allowed_for_the_requester()
    {
        var pending = PendingRequest(RenameActive());
        var handler = new CancelTaskStatusChangeRequestCommandHandler(
            _currentUser.Object, _identity.Object, _requests.Object, _unitOfWork.Object);

        CallerIs(RootOwnerId, canEditDirectly: true, canRequest: false);
        Assert.Equal(403, (await handler.Handle(new CancelTaskStatusChangeRequestCommand(pending.Id), default)).StatusCode);

        CallerIs(RequesterId, canEditDirectly: false, canRequest: true);
        Assert.True((await handler.Handle(new CancelTaskStatusChangeRequestCommand(pending.Id), default)).IsSuccess);
        Assert.Equal(TaskStatusChangeRequestStatuses.Cancelled, pending.Status);
    }

    [Fact]
    public async Task Sweeper_outdates_only_conflicting_pending_requests_and_notifies_their_requesters()
    {
        var conflicting = new TaskStatusChangeRequest
        {
            Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, RequestedByEmployeeId = RequesterId,
            ChangesJson = JsonSerializer.Serialize(RenameActive()), Status = TaskStatusChangeRequestStatuses.Pending
        };
        var unrelated = new TaskStatusChangeRequest
        {
            Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, RequestedByEmployeeId = RequesterId,
            ChangesJson = JsonSerializer.Serialize(new TaskStatusChangeSet([],
                [new TaskStatusUpdateChange(ToDo, new TaskStatusSnapshot("To Do", "not_started", "#94A3B8", "public"),
                    new TaskStatusSnapshot("Backlog", "not_started", "#94A3B8", "public"))],
                [], [ToDo, Active, Done], [ToDo.ToString(), Active.ToString(), Done.ToString()])),
            Status = TaskStatusChangeRequestStatuses.Pending
        };
        _requests.Setup(x => x.ListTrackedPendingForProjectAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([conflicting, unrelated]);
        var sweeper = new TaskStatusChangeRequestConflictSweeper(_requests.Object, _membership.Object, _notifications.Object);

        var count = await sweeper.MarkConflictingOutdatedAsync(
            TenantId, ProjectId, "Portal", new TaskStatusChangeFootprint(new HashSet<Guid> { Active }, false), null);

        Assert.Equal(1, count);
        Assert.Equal(TaskStatusChangeRequestStatuses.Outdated, conflicting.Status);
        Assert.Equal(TaskStatusChangeRequestStatuses.Pending, unrelated.Status);
        _notifications.Verify(x => x.SendTemplatedAsync(TenantId, RequesterUserId, "work_task_status_change_request_decided",
            It.Is<IReadOnlyDictionary<string, string>>(p => p["decision"] == "outdated"),
            "task_status_change_request", conflicting.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Access_service_grants_direct_edit_to_root_managers_and_requests_to_submodule_owners()
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
        members.Setup(x => x.ListActiveForObjectiveAsync(TenantId, RootObjectiveId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ProjectMember { EmployeeId = RootMemberId }]);
        _membership.Setup(x => x.IsEffectiveManagerAsync(TenantId, RootObjectiveId, RootMemberId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var service = new TaskStatusChangeAccessService(objectives.Object, members.Object, _membership.Object);

        var rootMember = await service.ResolveAsync(TenantId, ProjectId, RootMemberId);
        var owner = await service.ResolveAsync(TenantId, ProjectId, subOwner);
        var outsider = await service.ResolveAsync(TenantId, ProjectId, Guid.NewGuid());
        var approvers = await service.ListApproverEmployeeIdsAsync(TenantId, _root);

        Assert.True(rootMember!.CanEditDirectly);
        Assert.False(owner!.CanEditDirectly);
        Assert.True(owner.CanRequest);
        Assert.False(outsider!.CanRequest);
        Assert.Equal([RootOwnerId, RootMemberId], approvers);
    }
}
