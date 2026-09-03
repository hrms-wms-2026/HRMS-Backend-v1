using System.Text.Json;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.ApproveTaskEditRequest;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class ApproveTaskEditRequestCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid OwnerEmployeeId = Guid.NewGuid();
    private static readonly Guid OtherEmployeeId = Guid.NewGuid();
    private static readonly Guid RequesterEmployeeId = Guid.NewGuid();
    private static readonly Guid RequestId = Guid.NewGuid();
    private static readonly Guid TaskId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid SprintId = Guid.NewGuid();
    private static readonly Guid StatusId = Guid.NewGuid();

    private (
        ApproveTaskEditRequestCommandHandler Handler,
                WorkTask Task,
        Mock<IWorkTaskRepository> Tasks,
        Mock<ITaskEditRequestRepository> Requests,
        List<TaskEditLog> EditLogs,
        List<TaskPercentageLog> PercentageLogs) Build(
        Guid? callerEmployeeId = null,
        string requestStatus = TaskEditRequestStatuses.Pending,
        string sprintStatus = SprintStatuses.Active,
        decimal allocatedHours = 100m,
        decimal existingTaskSum = 20m,
        bool? callerIsEffectiveManager = null,
        string requestedTitle = "Updated title",
        int? payloadProgressPercent = null,
        int currentTaskPercent = 0,
        string requestedDescription = "Updated description",
        string requestedPriority = WorkTaskPriorities.High,
        DateOnly? requestedDueDate = null,
        decimal? requestedEstimatedHours = 40m,
        int? requestedStoryPoints = 8,
        bool authenticated = true,
        bool employeeExists = true,
        bool taskExists = true,
        bool objectiveExists = true,
        Mock<ONEVO.Application.Features.WorkManagement.CalendarEvents.RepositoryInterfaces.ICalendarEventRepository>? calendarEvents = null)

    {
        var resolvedCallerEmployeeId = callerEmployeeId ?? OwnerEmployeeId;
        var currentUser = new Mock<ICurrentUser>();
                currentUser.SetupGet(x => x.IsAuthenticated).Returns(authenticated);

        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(employeeExists ? resolvedCallerEmployeeId : null);

        var payload = new TaskEditRequestPayload(
            requestedTitle, requestedDescription, requestedPriority,
            requestedDueDate ?? new DateOnly(2026, 10, 1), requestedEstimatedHours, requestedStoryPoints, payloadProgressPercent);

        var pending = new TaskEditRequest
        {
            Id = RequestId,
            TenantId = TenantId,
            TaskId = TaskId,
            RequestedByEmployeeId = RequesterEmployeeId,
            PayloadJson = JsonSerializer.Serialize(payload),
            Status = requestStatus,
            CreatedById = UserId,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var requests = new Mock<ITaskEditRequestRepository>();
        requests.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, RequestId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pending);

        var task = new WorkTask
        {
            Id = TaskId,
            TenantId = TenantId,
            ProjectId = ProjectId,
            ObjectiveId = ObjectiveId,
            SprintId = SprintId,
            ShortId = "WEB-7",
            Title = "Original title",
            Description = "Original description",
            CategoryId = Guid.NewGuid(),
            StatusId = StatusId,
            Priority = WorkTaskPriorities.Medium,
            StoryPoints = 3,
            DueDate = new DateOnly(2026, 9, 1),
                        EstimatedHours = 20m,
            ProgressPercent = currentTaskPercent,
            CreatedById = UserId,

            CreatedAt = DateTimeOffset.UtcNow
        };
        var tasks = new Mock<IWorkTaskRepository>();
                tasks.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, TaskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(taskExists ? task : null);

        tasks.Setup(x => x.GetActiveAllocationSumByObjectiveIdAsync(
                TenantId, ObjectiveId, TaskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingTaskSum);

        var objective = new Objective
        {
            Id = ObjectiveId,
            TenantId = TenantId,
            ProjectId = ProjectId,
            OwnerId = OwnerEmployeeId,
            Title = "Objective",
            AllocatedHours = allocatedHours,
            IsActive = true,
            CreatedById = UserId,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var objectives = new Mock<IObjectiveRepository>();
                objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(objectiveExists ? objective : null);

        objectives.Setup(x => x.GetTrackedActiveDirectChildrenAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Objective>());

        var membership = new Mock<IMilestoneMembershipCoordinator>();
        // Mirrors direct-owner-only behavior by default so pre-existing tests keep passing
        // unmodified; callerIsEffectiveManager lets a test override this to simulate an
        // ancestor-cascade grant.
        membership.Setup(x => x.IsEffectiveManagerAsync(TenantId, ObjectiveId, resolvedCallerEmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(callerIsEffectiveManager ?? (objective.OwnerId == resolvedCallerEmployeeId));

        var sprints = new Mock<ISprintRepository>();
        sprints.Setup(x => x.GetByIdForTenantAsync(TenantId, SprintId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sprint
            {
                Id = SprintId,
                TenantId = TenantId,
                ProjectId = ProjectId,
                ObjectiveId = ObjectiveId,
                Name = "Sprint 1",
                Status = sprintStatus,
                StartDate = new DateOnly(2026, 9, 1),
                EndDate = new DateOnly(2026, 9, 14),
                CreatedById = UserId,
                CreatedAt = DateTimeOffset.UtcNow
            });

        var editLogs = new List<TaskEditLog>();
        var editLogRepository = new Mock<ITaskEditLogRepository>();
        editLogRepository.Setup(x => x.AddAsync(It.IsAny<TaskEditLog>(), It.IsAny<CancellationToken>()))
            .Callback<TaskEditLog, CancellationToken>((log, _) => editLogs.Add(log))
            .Returns(Task.CompletedTask);

        var percentageLogs = new List<TaskPercentageLog>();
        var percentageLogRepository = new Mock<ITaskPercentageLogRepository>();
        percentageLogRepository.Setup(x => x.AddAsync(It.IsAny<TaskPercentageLog>(), It.IsAny<CancellationToken>()))
            .Callback<TaskPercentageLog, CancellationToken>((log, _) => percentageLogs.Add(log))
            .Returns(Task.CompletedTask);

        var unitOfWork = new Mock<IUnitOfWork>();

        unitOfWork.Setup(x => x.ExecuteInTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<Result<WorkTaskResponse>>>>(),
                It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result<WorkTaskResponse>>> op, CancellationToken ct) => op(ct));
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new ApproveTaskEditRequestCommandHandler(
            currentUser.Object,
            identity.Object,
            requests.Object,
            tasks.Object,
            objectives.Object,
            sprints.Object,
            new ObjectiveAllocationSlackCalculator(objectives.Object, tasks.Object),
            membership.Object,
                        new Mock<INotificationDispatcher>().Object,
            unitOfWork.Object,
            editLogRepository.Object,
            percentageLogRepository.Object,
            (calendarEvents ?? CalendarEventRepositoryMocks.Empty()).Object);

        return (handler, task, tasks, requests, editLogs, percentageLogs);

    }

        [Fact]
    public async Task Handle_OnApproval_WritesTaskEditLogAttributedToRequester_NotApprover()
    {
        var (handler, _, _, _, editLogs, _) = Build(requestedTitle: "New Title");

        var result = await handler.Handle(new ApproveTaskEditRequestCommand(RequestId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var logged = Assert.Single(editLogs);
        Assert.Equal(TaskEditLogSources.ApprovedRequest, logged.Source);
        Assert.Equal(RequestId, logged.EditRequestId);
        Assert.Equal(RequesterEmployeeId, logged.EmployeeId);
        Assert.NotEqual(OwnerEmployeeId, logged.EmployeeId);
    }

    [Fact]
    public async Task Handle_WhenPayloadProgressPercentDiffers_WritesManualEditPercentageLog()
    {
        var (handler, task, _, _, _, percentageLogs) = Build(
            requestedTitle: "Original title", payloadProgressPercent: 75, currentTaskPercent: 30);

        var result = await handler.Handle(new ApproveTaskEditRequestCommand(RequestId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var logged = Assert.Single(percentageLogs);
        Assert.Equal(TaskPercentageLogSources.ManualEdit, logged.Source);
        Assert.Null(logged.ClockingSessionId);
        Assert.Equal(RequesterEmployeeId, logged.EmployeeId);
        Assert.Equal(30, logged.PreviousPercent);
        Assert.Equal(75, logged.NewPercent);
        Assert.Equal(75, task.ProgressPercent);
    }

    [Fact]
    public async Task Handle_WhenPayloadHasNoProgressPercent_WritesNoPercentageLog()
    {
        var (handler, task, _, _, _, percentageLogs) = Build(currentTaskPercent: 30);

        var result = await handler.Handle(new ApproveTaskEditRequestCommand(RequestId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(percentageLogs);
        Assert.Equal(30, task.ProgressPercent);
    }

    [Fact]
    public async Task Handle_OwnerApproves_UpdatesFieldsAndRechecksSlackExcludingCurrentTask()

    {
        var (handler, task, tasks, requests, _, _) = Build();

        var result = await handler.Handle(new ApproveTaskEditRequestCommand(RequestId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Updated title", task.Title);
        Assert.Equal("Updated description", task.Description);
        Assert.Equal(WorkTaskPriorities.High, task.Priority);
        Assert.Equal(new DateOnly(2026, 10, 1), task.DueDate);
        Assert.Equal(40m, task.EstimatedHours);
        Assert.Equal(8, task.StoryPoints);
        Assert.Equal("Updated title", result.Value!.Title);
        tasks.Verify(x => x.GetActiveAllocationSumByObjectiveIdAsync(
            TenantId, ObjectiveId, TaskId, It.IsAny<CancellationToken>()), Times.Once);
        requests.Verify(x => x.Update(It.Is<TaskEditRequest>(r =>
            r.Status == TaskEditRequestStatuses.Approved
            && r.DecidedByEmployeeId == OwnerEmployeeId
            && r.DecidedAt.HasValue)), Times.Once);
    }

    [Fact]
    public async Task Handle_NotOwner_ReturnsForbiddenWithoutUpdatingTaskOrRequest()
    {
        var (handler, task, tasks, requests, _, _) = Build(callerEmployeeId: OtherEmployeeId);

        var result = await handler.Handle(new ApproveTaskEditRequestCommand(RequestId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        Assert.Equal("Original title", task.Title);
        tasks.Verify(x => x.GetActiveAllocationSumByObjectiveIdAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
        requests.Verify(x => x.Update(It.IsAny<TaskEditRequest>()), Times.Never);
    }

    [Fact]
    public async Task Handle_CallerIsEffectiveManagerNotOwner_ApprovesRequest()
    {
        // Caller is not this objective's own OwnerId, but IsEffectiveManagerAsync reports them as
        // an effective manager via an ancestor (grandparent) membership - the coordinator's own
        // ancestor-walk logic is unit-tested separately, so this only proves the handler defers to
        // its answer instead of the direct OwnerId check.
        var (handler, task, tasks, requests, _, _) = Build(callerEmployeeId: OtherEmployeeId, callerIsEffectiveManager: true);

        var result = await handler.Handle(new ApproveTaskEditRequestCommand(RequestId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Updated title", task.Title);
        requests.Verify(x => x.Update(It.Is<TaskEditRequest>(r =>
            r.Status == TaskEditRequestStatuses.Approved && r.DecidedByEmployeeId == OtherEmployeeId)), Times.Once);
    }

    [Fact]
    public async Task Handle_AlreadyDecided_ReturnsConflictWithoutUpdatingTaskOrRequest()
    {
        var (handler, task, tasks, requests, _, _) = Build(requestStatus: TaskEditRequestStatuses.Rejected);

        var result = await handler.Handle(new ApproveTaskEditRequestCommand(RequestId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        Assert.Equal("Original title", task.Title);
        tasks.Verify(x => x.GetTrackedByIdForTenantAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        requests.Verify(x => x.Update(It.IsAny<TaskEditRequest>()), Times.Never);
    }

        [Fact]
    public async Task Handle_NotAuthenticated_ReturnsForbidden()
    {
        var (handler, task, tasks, requests, _, _) = Build(authenticated: false);

        var result = await handler.Handle(new ApproveTaskEditRequestCommand(RequestId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        Assert.Equal("Original title", task.Title);
        requests.Verify(x => x.Update(It.IsAny<TaskEditRequest>()), Times.Never);
    }

    [Fact]
    public async Task Handle_NoEmployeeRecord_ReturnsForbidden()
    {
        var (handler, task, tasks, requests, _, _) = Build(employeeExists: false);

        var result = await handler.Handle(new ApproveTaskEditRequestCommand(RequestId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        Assert.Equal("Original title", task.Title);
        requests.Verify(x => x.Update(It.IsAny<TaskEditRequest>()), Times.Never);
    }

    [Fact]
    public async Task Handle_TaskNotFound_ReturnsNotFound()
    {
        var (handler, _, tasks, requests, _, _) = Build(taskExists: false);

        var result = await handler.Handle(new ApproveTaskEditRequestCommand(RequestId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
        requests.Verify(x => x.Update(It.IsAny<TaskEditRequest>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ObjectiveNotFound_ReturnsNotFound()
    {
        var (handler, _, tasks, requests, _, _) = Build(objectiveExists: false);

        var result = await handler.Handle(new ApproveTaskEditRequestCommand(RequestId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
        requests.Verify(x => x.Update(It.IsAny<TaskEditRequest>()), Times.Never);
    }

    [Fact]
    public async Task Handle_AlreadyApproved_ReturnsConflictWithoutMutatingTask()
    {
        var (handler, task, tasks, requests, editLogs, percentageLogs) = Build(requestStatus: TaskEditRequestStatuses.Approved);

        var result = await handler.Handle(new ApproveTaskEditRequestCommand(RequestId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        Assert.Equal("Original title", task.Title);
        Assert.Empty(editLogs);
        Assert.Empty(percentageLogs);
        requests.Verify(x => x.Update(It.IsAny<TaskEditRequest>()), Times.Never);
    }

    [Fact]
    public async Task Handle_PayloadProgressEqualsCurrent_WritesNoPercentageLog()
    {
        var (handler, task, _, _, _, percentageLogs) = Build(
            requestedTitle: "Original title", payloadProgressPercent: 30, currentTaskPercent: 30);

        var result = await handler.Handle(new ApproveTaskEditRequestCommand(RequestId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(30, task.ProgressPercent);
        Assert.Empty(percentageLogs);
    }

    [Fact]
    public async Task Handle_MultiplePayloadFieldsChanged_WritesOnlyThoseKeysToEditLog()
    {
        var (handler, _, _, _, editLogs, _) = Build(
            requestedTitle: "New title", requestedDescription: "New description",
            requestedPriority: WorkTaskPriorities.High, requestedDueDate: new DateOnly(2026, 9, 2),
            requestedEstimatedHours: 20m, requestedStoryPoints: 3);

        var result = await handler.Handle(new ApproveTaskEditRequestCommand(RequestId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var log = Assert.Single(editLogs);
        using var values = JsonDocument.Parse(log.NewValuesJson);
        var keys = values.RootElement.EnumerateObject().Select(property => property.Name).ToHashSet();
        Assert.Equal(4, keys.Count);
        Assert.Contains("title", keys);
        Assert.Contains("description", keys);
        Assert.Contains("priority", keys);
        Assert.Contains("dueDate", keys);
        Assert.DoesNotContain("estimatedHours", keys);
        Assert.DoesNotContain("storyPoints", keys);
    }

    [Fact]
    public async Task Handle_ApprovingTwice_ReturnsConflictAndDoesNotWriteDuplicateLogs()
    {
        var (handler, task, _, requests, editLogs, percentageLogs) = Build(
            requestedTitle: "New title", payloadProgressPercent: 75, currentTaskPercent: 30);

        var first = await handler.Handle(new ApproveTaskEditRequestCommand(RequestId), CancellationToken.None);
        var firstEditCount = editLogs.Count;
        var firstPercentageCount = percentageLogs.Count;
        var second = await handler.Handle(new ApproveTaskEditRequestCommand(RequestId), CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.False(second.IsSuccess);
        Assert.Equal(409, second.StatusCode);
        Assert.Equal(firstEditCount, editLogs.Count);
        Assert.Equal(firstPercentageCount, percentageLogs.Count);
        Assert.Equal("New title", task.Title);
        requests.Verify(x => x.Update(It.IsAny<TaskEditRequest>()), Times.Once);
    }

    [Fact]
    public async Task Handle_SprintAchievedSinceRequest_ReturnsConflictWithoutUpdatingTaskOrRequest()

    {
        var (handler, task, tasks, requests, _, _) = Build(sprintStatus: SprintStatuses.Achieved);

        var result = await handler.Handle(new ApproveTaskEditRequestCommand(RequestId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        Assert.Equal("Original title", task.Title);
        tasks.Verify(x => x.GetActiveAllocationSumByObjectiveIdAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
        requests.Verify(x => x.Update(It.IsAny<TaskEditRequest>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ApprovedDueDateOutsideActiveEventWindow_Rejected()
    {
        var calendarEvents = CalendarEventRepositoryMocks.Empty();
        calendarEvents.Setup(x => x.ListActiveEventWindowsForTaskAsync(
                TenantId, TaskId, ObjectiveId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new ONEVO.Application.Features.WorkManagement.CalendarEvents.RepositoryInterfaces.ActiveEventWindow(
                    Guid.NewGuid(), "Release", new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31))
            });

        var (handler, task, tasks, requests, _, _) = Build(
            requestedDueDate: new DateOnly(2026, 4, 15), calendarEvents: calendarEvents);

        var result = await handler.Handle(new ApproveTaskEditRequestCommand(RequestId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        requests.Verify(x => x.Update(It.IsAny<TaskEditRequest>()), Times.Never);
    }
}
