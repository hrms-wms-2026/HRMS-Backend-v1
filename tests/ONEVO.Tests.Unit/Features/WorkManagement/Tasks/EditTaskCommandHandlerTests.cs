using System.Text.Json;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.EditTask;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class EditTaskCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid TaskId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();

    private (
        EditTaskCommandHandler Handler,
        Mock<IWorkTaskRepository> Tasks,
        List<TaskEditLog> EditLogs,
        Guid CallerEmployeeId,
        WorkTask Task,
        List<TaskPercentageLog> PercentageLogs) Build(
        decimal allocatedHours,
        decimal existingSumExcludingThisTask,
        Sprint? sprint = null,
        string title = "Old",
        string priority = WorkTaskPriorities.Medium,
        int progressPercent = 0,
        Mock<ONEVO.Application.Features.WorkManagement.CalendarEvents.RepositoryInterfaces.ICalendarEventRepository>? calendarEvents = null)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var task = new WorkTask
        {
            Id = TaskId, TenantId = TenantId, ObjectiveId = ObjectiveId, Title = title, ShortId = "T-1",
            Priority = priority, ProgressPercent = progressPercent, EstimatedHours = 10m,
            SprintId = sprint?.Id, CreatedAt = DateTimeOffset.UtcNow
        };

        var tasks = new Mock<IWorkTaskRepository>();
        tasks.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, TaskId, It.IsAny<CancellationToken>())).ReturnsAsync(task);
        tasks.Setup(x => x.GetActiveAllocationSumByObjectiveIdAsync(TenantId, ObjectiveId, TaskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingSumExcludingThisTask);

        var objective = new Objective { Id = ObjectiveId, TenantId = TenantId, AllocatedHours = allocatedHours, IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>())).ReturnsAsync(objective);
        objectives.Setup(x => x.GetTrackedActiveDirectChildrenAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Objective>());

        var slack = new ObjectiveAllocationSlackCalculator(objectives.Object, tasks.Object);

        var sprints = new Mock<ISprintRepository>();
        if (sprint is not null)
        {
            sprints.Setup(x => x.GetByIdForTenantAsync(TenantId, sprint.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(sprint);
        }

        var callerEmployeeId = UserId;
        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(callerEmployeeId);

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
        unitOfWork.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result<WorkTaskResponse>>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result<WorkTaskResponse>>> op, CancellationToken ct) => op(ct));

        var handler = new EditTaskCommandHandler(
            currentUser.Object, tasks.Object, objectives.Object, slack, unitOfWork.Object, sprints.Object,
            identity.Object, editLogRepository.Object, percentageLogRepository.Object,
            (calendarEvents ?? CalendarEventRepositoryMocks.Empty()).Object);

        return (handler, tasks, editLogs, callerEmployeeId, task, percentageLogs);
    }

    [Fact]
    public async Task Handle_IncreaseWithinSlack_Updates()
    {
        var (handler, _, _, _, _, _) = Build(allocatedHours: 100m, existingSumExcludingThisTask: 40m);
        var result = await handler.Handle(new EditTaskCommand(TaskId, "New Title", null, "medium", null, EstimatedHours: 50m, StoryPoints: null, ProgressPercent: null, Reason: null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("New Title", result.Value!.Title);
    }

    [Fact]
    public async Task Handle_IncreaseExceedsSlack_ReturnsConflict()
    {
        var (handler, _, _, _, _, _) = Build(allocatedHours: 100m, existingSumExcludingThisTask: 40m);
        var result = await handler.Handle(new EditTaskCommand(TaskId, "New Title", null, "medium", null, EstimatedHours: 70m, StoryPoints: null, ProgressPercent: null, Reason: null), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        Assert.Contains("\"availableSlackHours\"", result.Error);
        Assert.DoesNotContain("\"AvailableSlackHours\"", result.Error);
    }

    [Fact]
    public async Task Handle_TaskInAchievedSprint_ReturnsForbidden()
    {
        var achieved = new Sprint
        {
            Id = Guid.NewGuid(), TenantId = TenantId, ObjectiveId = ObjectiveId, Name = "S1",
            Status = SprintStatuses.Achieved, CreatedAt = DateTimeOffset.UtcNow
        };
        var (handler, _, _, _, _, _) = Build(allocatedHours: 100m, existingSumExcludingThisTask: 40m, sprint: achieved);
        var result = await handler.Handle(new EditTaskCommand(TaskId, "New Title", null, "medium", null, EstimatedHours: 10m, StoryPoints: null, ProgressPercent: null, Reason: null), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_WhenTitleChanges_WritesTaskEditLogWithOnlyTheChangedField()
    {
        var (handler, _, editLogs, callerEmployeeId, task, _) = Build(
            allocatedHours: 100m, existingSumExcludingThisTask: 40m,
            title: "Old Title", priority: WorkTaskPriorities.Medium, progressPercent: 20);
        var command = new EditTaskCommand(
            task.Id, "New Title", task.Description, task.Priority, task.DueDate,
            task.EstimatedHours, task.StoryPoints, null, null);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var addedLog = Assert.Single(editLogs);
        Assert.Equal(TaskEditLogSources.Direct, addedLog.Source);
        Assert.Equal(callerEmployeeId, addedLog.EmployeeId);
        using var newValues = JsonDocument.Parse(addedLog.NewValuesJson);
        Assert.Single(newValues.RootElement.EnumerateObject());
        Assert.True(newValues.RootElement.TryGetProperty("title", out _));
        Assert.DoesNotContain("\"priority\"", addedLog.NewValuesJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Handle_WhenProgressPercentChanges_WritesManualEditPercentageLog()
    {
        var (handler, _, _, _, task, percentageLogs) = Build(
            allocatedHours: 100m, existingSumExcludingThisTask: 40m,
            title: "T", priority: WorkTaskPriorities.Medium, progressPercent: 100);
        var command = new EditTaskCommand(
            task.Id, task.Title, task.Description, task.Priority, task.DueDate,
            task.EstimatedHours, task.StoryPoints, 40, "Reviewer found incomplete subtasks");

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var logged = Assert.Single(percentageLogs);
        Assert.Equal(TaskPercentageLogSources.ManualEdit, logged.Source);
        Assert.Null(logged.ClockingSessionId);
        Assert.Equal(100, logged.PreviousPercent);
        Assert.Equal(40, logged.NewPercent);
        Assert.Equal(40, task.ProgressPercent);
    }

    [Fact]
    public async Task Handle_WhenProgressPercentChangesToZero_WritesPercentageLogWithZero()
    {
        var (handler, _, _, callerEmployeeId, task, percentageLogs) = Build(
            allocatedHours: 100m, existingSumExcludingThisTask: 40m, progressPercent: 55);
        var command = new EditTaskCommand(
            task.Id, task.Title, task.Description, task.Priority, task.DueDate,
            task.EstimatedHours, task.StoryPoints, 0, "reset progress");

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var log = Assert.Single(percentageLogs);
        Assert.Equal(callerEmployeeId, log.EmployeeId);
        Assert.Equal(55, log.PreviousPercent);
        Assert.Equal(0, log.NewPercent);
        Assert.Equal(0, task.ProgressPercent);
    }

    [Fact]
    public async Task Handle_WhenProgressPercentChangesTo100_WritesPercentageLogWith100()
    {
        var (handler, _, _, _, task, percentageLogs) = Build(
            allocatedHours: 100m, existingSumExcludingThisTask: 40m, progressPercent: 20);
        var command = new EditTaskCommand(
            task.Id, task.Title, task.Description, task.Priority, task.DueDate,
            task.EstimatedHours, task.StoryPoints, 100, null);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(100, Assert.Single(percentageLogs).NewPercent);
    }

    [Fact]
    public async Task Handle_WhenProgressPercentEqualsCurrent_WritesNoPercentageLog()
    {
        var (handler, _, _, _, task, percentageLogs) = Build(
            allocatedHours: 100m, existingSumExcludingThisTask: 40m, progressPercent: 55);
        var command = new EditTaskCommand(
            task.Id, task.Title, task.Description, task.Priority, task.DueDate,
            task.EstimatedHours, task.StoryPoints, 55, "unchanged");

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(55, task.ProgressPercent);
        Assert.Empty(percentageLogs);
    }

    [Fact]
    public async Task Handle_WhenTitleAndProgressChange_WritesBothLogsWithSameTimestampAndExactKeys()
    {
        var (handler, _, editLogs, callerEmployeeId, task, percentageLogs) = Build(
            allocatedHours: 100m, existingSumExcludingThisTask: 40m, title: "Old", progressPercent: 20);
        var command = new EditTaskCommand(
            task.Id, "New", task.Description, task.Priority, task.DueDate,
            task.EstimatedHours, task.StoryPoints, 40, "updated context");

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var editLog = Assert.Single(editLogs);
        var percentageLog = Assert.Single(percentageLogs);
        Assert.Equal(callerEmployeeId, editLog.EmployeeId);
        Assert.Equal(callerEmployeeId, percentageLog.EmployeeId);
        using var oldValues = JsonDocument.Parse(editLog.OldValuesJson);
        using var newValues = JsonDocument.Parse(editLog.NewValuesJson);
        Assert.Equal(2, oldValues.RootElement.EnumerateObject().Count());
        Assert.Equal(2, newValues.RootElement.EnumerateObject().Count());
        Assert.True(newValues.RootElement.TryGetProperty("title", out _));
        Assert.True(newValues.RootElement.TryGetProperty("progressPercent", out _));
        Assert.Equal(editLog.ChangedAt, percentageLog.ChangedAt);
    }

    [Fact]
    public async Task Handle_WhenProgressPercentNotSupplied_WritesNoPercentageLog_AndLeavesPercentUnchanged()
    {
        var (handler, _, _, _, task, percentageLogs) = Build(
            allocatedHours: 100m, existingSumExcludingThisTask: 40m,
            title: "T", priority: WorkTaskPriorities.Medium, progressPercent: 55);
        var command = new EditTaskCommand(
            task.Id, "New Title", task.Description, task.Priority, task.DueDate,
            task.EstimatedHours, task.StoryPoints, null, null);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(percentageLogs);
        Assert.Equal(55, task.ProgressPercent);
    }

    [Fact]
    public async Task Handle_WhenNothingChanges_WritesNoEditLog()
    {
        var (handler, _, editLogs, _, task, _) = Build(
            allocatedHours: 100m, existingSumExcludingThisTask: 40m,
            title: "Same Title", priority: WorkTaskPriorities.Medium, progressPercent: 20);
        var command = new EditTaskCommand(
            task.Id, task.Title, task.Description, task.Priority, task.DueDate,
            task.EstimatedHours, task.StoryPoints, null, null);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(editLogs);
    }

    private static Mock<ONEVO.Application.Features.WorkManagement.CalendarEvents.RepositoryInterfaces.ICalendarEventRepository>
        CalendarWithTaskWindow(DateOnly start, DateOnly end)
    {
        var mock = CalendarEventRepositoryMocks.Empty();
        mock.Setup(x => x.ListActiveEventWindowsForTaskAsync(
                TenantId, TaskId, ObjectiveId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new ONEVO.Application.Features.WorkManagement.CalendarEvents.RepositoryInterfaces.ActiveEventWindow(
                    Guid.NewGuid(), "Release", start, end)
            });
        return mock;
    }

    [Fact]
    public async Task Handle_DueDateOutsideActiveEventWindow_Rejected()
    {
        var (handler, _, _, _, _, _) = Build(
            allocatedHours: 100m, existingSumExcludingThisTask: 40m,
            calendarEvents: CalendarWithTaskWindow(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31)));
        var command = new EditTaskCommand(TaskId, "New Title", null, "medium",
            new DateOnly(2026, 4, 5), null, null, null, null);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task Handle_DueDateWithinActiveEventWindow_Succeeds()
    {
        var (handler, _, _, _, _, _) = Build(
            allocatedHours: 100m, existingSumExcludingThisTask: 40m,
            calendarEvents: CalendarWithTaskWindow(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31)));
        var command = new EditTaskCommand(TaskId, "New Title", null, "medium",
            new DateOnly(2026, 3, 20), null, null, null, null);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
    }
}
