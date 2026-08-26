using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetTaskHistory;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class GetTaskHistoryQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid TaskIdConst = Guid.NewGuid();
    private static readonly Guid EmployeeIdConst = Guid.NewGuid();

    private (GetTaskHistoryQueryHandler Handler, WorkTask Task) ArrangeHistoryHandler(
        IReadOnlyList<TaskEditLog>? editLogs = null,
        IReadOnlyList<TaskStatusChangeLog>? statusChangeLogs = null,
        IReadOnlyList<TaskClockingSession>? sessions = null,
        IReadOnlyList<TaskPercentageLog>? percentageLogs = null,
        IReadOnlyDictionary<Guid, string>? displayNames = null,
        bool taskExists = true)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveDisplayNamesByEmployeeIdAsync(
                TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, IReadOnlyList<Guid> ids, CancellationToken _) =>
                displayNames ?? ids.ToDictionary(id => id, _ => "Employee"));

        var task = new WorkTask { Id = TaskIdConst, TenantId = TenantId, Title = "Task", CreatedAt = At(-20) };
        var tasks = new Mock<IWorkTaskRepository>();
        tasks.Setup(x => x.GetByIdForTenantAsync(TenantId, TaskIdConst, It.IsAny<CancellationToken>()))
            .ReturnsAsync(taskExists ? task : null);

        var editLogRepository = new Mock<ITaskEditLogRepository>();
        editLogRepository.Setup(x => x.GetForTaskAsync(TenantId, TaskIdConst, It.IsAny<CancellationToken>()))
            .ReturnsAsync(editLogs ?? Array.Empty<TaskEditLog>());

        var statusChangeLogRepository = new Mock<ITaskStatusChangeLogRepository>();
        statusChangeLogRepository.Setup(x => x.GetForTaskAsync(TenantId, TaskIdConst, It.IsAny<CancellationToken>()))
            .ReturnsAsync(statusChangeLogs ?? Array.Empty<TaskStatusChangeLog>());

        var sessionRepository = new Mock<ITaskClockingSessionRepository>();
        sessionRepository.Setup(x => x.GetForTaskAsync(TenantId, TaskIdConst, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sessions ?? Array.Empty<TaskClockingSession>());

        var percentageLogRepository = new Mock<ITaskPercentageLogRepository>();
        percentageLogRepository.Setup(x => x.GetForTaskAsync(TenantId, TaskIdConst, It.IsAny<CancellationToken>()))
            .ReturnsAsync(percentageLogs ?? Array.Empty<TaskPercentageLog>());

        var handler = new GetTaskHistoryQueryHandler(
            currentUser.Object, identity.Object, tasks.Object, editLogRepository.Object,
            statusChangeLogRepository.Object, sessionRepository.Object, percentageLogRepository.Object);
        return (handler, task);
    }

    private static DateTimeOffset At(int offsetMinutes) => DateTimeOffset.UtcNow.AddMinutes(offsetMinutes);

    [Fact]
    public async Task Handle_PushSourcedPercentageLog_NestsInsideItsClockSessionEntry_NotAsSeparateEntry()
    {
        var sessionId = Guid.NewGuid();
        var (handler, _) = ArrangeHistoryHandler(
            sessions: new[] { new TaskClockingSession { Id = sessionId, TaskId = TaskIdConst, EmployeeId = EmployeeIdConst, ClockInAt = At(-2), ClockOutAt = At(-1), DurationMinutes = 60 } },
            percentageLogs: new[] { new TaskPercentageLog { Id = Guid.NewGuid(), TaskId = TaskIdConst, EmployeeId = EmployeeIdConst, PreviousPercent = 10, NewPercent = 40, Source = TaskPercentageLogSources.Push, ClockingSessionId = sessionId, ChangedAt = At(-1) } });

        var result = await handler.Handle(new GetTaskHistoryQuery(TaskIdConst), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Entries);
        var entry = result.Value.Entries[0];
        Assert.Equal(TaskHistoryEntryTypes.ClockSession, entry.Type);
        Assert.Equal(40, entry.ClockSession!.PushedPercent);
        Assert.Equal(10, entry.ClockSession.PreviousPercent);
    }

    [Fact]
    public async Task Handle_ManualEditPercentageLog_AppearsAsStandalonePercentageChangeEntry()
    {
        var (handler, _) = ArrangeHistoryHandler(
            percentageLogs: new[] { new TaskPercentageLog { Id = Guid.NewGuid(), TaskId = TaskIdConst, EmployeeId = EmployeeIdConst, PreviousPercent = 40, NewPercent = 20, Source = TaskPercentageLogSources.ManualEdit, ClockingSessionId = null, ChangedAt = At(0) } });

        var result = await handler.Handle(new GetTaskHistoryQuery(TaskIdConst), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Entries);
        Assert.Equal(TaskHistoryEntryTypes.PercentageChange, result.Value.Entries[0].Type);
    }

    [Fact]
    public async Task Handle_OpenSessionWithNoPushYet_AppearsAsClockSessionEntryWithNullPushedPercent()
    {
        var (handler, _) = ArrangeHistoryHandler(
            sessions: new[] { new TaskClockingSession { Id = Guid.NewGuid(), TaskId = TaskIdConst, EmployeeId = EmployeeIdConst, ClockInAt = At(0), ClockOutAt = null } });

        var result = await handler.Handle(new GetTaskHistoryQuery(TaskIdConst), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Entries);
        Assert.Null(result.Value.Entries[0].ClockSession!.PushedPercent);
        Assert.Null(result.Value.Entries[0].ClockSession.ClockOutAt);
    }

    [Fact]
    public async Task Handle_MultipleEntryKinds_SortedNewestFirst()
    {
        var (handler, _) = ArrangeHistoryHandler(
            editLogs: new[] { new TaskEditLog { Id = Guid.NewGuid(), TaskId = TaskIdConst, EmployeeId = EmployeeIdConst, Source = TaskEditLogSources.Direct, OldValuesJson = "{}", NewValuesJson = "{}", ChangedAt = At(-10) } },
            statusChangeLogs: new[] { new TaskStatusChangeLog { Id = Guid.NewGuid(), TaskId = TaskIdConst, EmployeeId = EmployeeIdConst, FromStatusId = Guid.NewGuid(), ToStatusId = Guid.NewGuid(), ChangedAt = At(0) } });

        var result = await handler.Handle(new GetTaskHistoryQuery(TaskIdConst), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Entries.Count);
        Assert.Equal(TaskHistoryEntryTypes.StatusChange, result.Value.Entries[0].Type);
        Assert.Equal(TaskHistoryEntryTypes.Edit, result.Value.Entries[1].Type);
    }

    [Fact]
    public async Task Handle_TwoPushCycles_PairEachPercentageLogWithItsOwnSession()
    {
        var firstSessionId = Guid.NewGuid();
        var secondSessionId = Guid.NewGuid();
        var firstLogId = Guid.NewGuid();
        var secondLogId = Guid.NewGuid();
        var (handler, _) = ArrangeHistoryHandler(
            sessions: new[]
            {
                new TaskClockingSession { Id = firstSessionId, TaskId = TaskIdConst, EmployeeId = EmployeeIdConst, ClockInAt = At(-30), ClockOutAt = At(-25) },
                new TaskClockingSession { Id = secondSessionId, TaskId = TaskIdConst, EmployeeId = EmployeeIdConst, ClockInAt = At(-10), ClockOutAt = At(-5) }
            },
            percentageLogs: new[]
            {
                new TaskPercentageLog { Id = firstLogId, TaskId = TaskIdConst, EmployeeId = EmployeeIdConst, PreviousPercent = 10, NewPercent = 40, Source = TaskPercentageLogSources.Push, ClockingSessionId = firstSessionId, ChangedAt = At(-25) },
                new TaskPercentageLog { Id = secondLogId, TaskId = TaskIdConst, EmployeeId = EmployeeIdConst, PreviousPercent = 40, NewPercent = 70, Source = TaskPercentageLogSources.Push, ClockingSessionId = secondSessionId, ChangedAt = At(-5) }
            });

        var result = await handler.Handle(new GetTaskHistoryQuery(TaskIdConst), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var entries = result.Value!.Entries.Where(entry => entry.Type == TaskHistoryEntryTypes.ClockSession).ToList();
        Assert.Equal(2, entries.Count);
        var first = entries.Single(entry => entry.ClockSession!.SessionId == firstSessionId);
        var second = entries.Single(entry => entry.ClockSession!.SessionId == secondSessionId);
        Assert.Equal(40, first.ClockSession.PushedPercent);
        Assert.Equal(firstLogId, first.ClockSession.PercentageLogId);
        Assert.Equal(70, second.ClockSession.PushedPercent);
        Assert.Equal(secondLogId, second.ClockSession.PercentageLogId);
    }

    [Fact]
    public async Task Handle_UnresolvableEmployeeName_FallsBackToATeammate()
    {
        var (handler, _) = ArrangeHistoryHandler(
            displayNames: new Dictionary<Guid, string>(),
            statusChangeLogs: new[]
            {
                new TaskStatusChangeLog { Id = Guid.NewGuid(), TaskId = TaskIdConst, EmployeeId = EmployeeIdConst, FromStatusId = Guid.NewGuid(), ToStatusId = Guid.NewGuid(), ChangedAt = At(0) }
            });

        var result = await handler.Handle(new GetTaskHistoryQuery(TaskIdConst), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("A teammate", Assert.Single(result.Value!.Entries).EmployeeName);
    }

    [Fact]
    public async Task Handle_TaskNotFound_ReturnsNotFound()
    {
        var (handler, _) = ArrangeHistoryHandler(taskExists: false);

        var result = await handler.Handle(new GetTaskHistoryQuery(TaskIdConst), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }
}
