using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Meetings;
using ONEVO.Application.Features.WorkManagement.Meetings.Contracts;
using ONEVO.Application.Features.WorkManagement.Meetings.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Meetings.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using TaskStatusEntity = ONEVO.Domain.Features.WorkManagement.Tasks.Entities.TaskStatus;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Meetings;

/// <summary>
/// The action-item service on its own: the link is written once, the list is filtered by the task
/// rules, and the linked tasks are fetched in one round trip (not one query per link).
/// </summary>
public class MeetingActionItemServiceTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly Guid OccurrenceTaskId = Guid.NewGuid();
    private static readonly Guid SeriesId = Guid.NewGuid();
    private static readonly DateTimeOffset Start = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);

    private readonly Mock<IMeetingRepository> _meetings = new();
    private readonly Mock<IMeetingAccessResolver> _access = new();
    private readonly Mock<IMeetingActionItemRepository> _links = new();
    private readonly Mock<IWorkTaskRepository> _tasks = new();
    private readonly Mock<ITaskStatusRepository> _statuses = new();
    private readonly Mock<ITaskAccessResolver> _taskAccess = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();

    private MeetingActionItemService Service(MeetingsOptions? options = null) => new(
        Microsoft.Extensions.Options.Options.Create(options ?? new MeetingsOptions { Create = true }),
        _meetings.Object, _access.Object, _links.Object, _tasks.Object, _statuses.Object,
        _taskAccess.Object, _unitOfWork.Object, new Mock<IDateTimeProvider>().Object);

    private void GivenAVisibleOccurrence()
    {
        var details = new MeetingDetails
        {
            TaskId = OccurrenceTaskId, TenantId = TenantId, SeriesId = SeriesId, OriginalStart = Start,
        };
        _meetings.Setup(x => x.GetOccurrenceByTaskAsync(TenantId, OccurrenceTaskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MeetingOccurrenceRow(details, "M-1", "Weekly", null, Guid.NewGuid(), Guid.NewGuid()));
        _access.Setup(x => x.ResolveForOccurrenceAsync(TenantId, EmployeeId, SeriesId, Start, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MeetingAccess(MeetingRoles.Attendee, new MeetingSeries { Id = SeriesId, TenantId = TenantId }, details));
    }

    private static MeetingActionItem Link(Guid taskId) => new()
    {
        Id = Guid.NewGuid(), TenantId = TenantId, OccurrenceTaskId = OccurrenceTaskId, TaskId = taskId,
        CreatedByEmployeeId = EmployeeId, CreatedAt = Start,
    };

    [Fact]
    public async Task ListAsync_FetchesTheLinkedTasksInOneRoundTrip_NotOneQueryPerLink()
    {
        GivenAVisibleOccurrence();
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        _links.Setup(x => x.ListForOccurrenceAsync(TenantId, OccurrenceTaskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { Link(firstId), Link(secondId) });
        _tasks.Setup(x => x.GetByIdsForTenantAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WorkTask>
            {
                new() { Id = firstId, TenantId = TenantId, ShortId = "A-1", Title = "One", StatusId = Guid.NewGuid(), ObjectiveId = Guid.NewGuid() },
                new() { Id = secondId, TenantId = TenantId, ShortId = "A-2", Title = "Two", StatusId = Guid.NewGuid(), ObjectiveId = Guid.NewGuid() },
            });
        _taskAccess.Setup(x => x.FilterViewableTasksAsync(TenantId, UserId, EmployeeId, It.IsAny<IReadOnlyList<WorkTask>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, Guid _, Guid _, IReadOnlyList<WorkTask> tasks, CancellationToken _) => tasks);
        _taskAccess.Setup(x => x.ResolveViewableTasksAsync(TenantId, UserId, It.IsAny<IReadOnlyList<WorkTask>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, Guid _, IReadOnlyList<WorkTask> tasks, CancellationToken _) => tasks);
        _statuses.Setup(x => x.GetByIdsForTenantAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TaskStatusEntity>());
        _tasks.Setup(x => x.GetAssignedEmployeeIdsByTaskIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<Guid>>());

        var result = await Service().ListAsync(TenantId, EmployeeId, UserId, OccurrenceTaskId, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(2, result.Value!.Count);
        _tasks.Verify(x => x.GetByIdsForTenantAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()), Times.Once);
        _tasks.Verify(x => x.GetByIdForTenantAsync(TenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ListAsync_AnOccurrenceTheCallerCannotSeeIs404()
    {
        _meetings.Setup(x => x.GetOccurrenceByTaskAsync(TenantId, OccurrenceTaskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((MeetingOccurrenceRow?)null);

        var result = await Service().ListAsync(TenantId, EmployeeId, UserId, OccurrenceTaskId, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task ListAsync_AnItemWithNoAssignmentHasNoAssignee()
    {
        GivenAVisibleOccurrence();
        var taskId = Guid.NewGuid();
        var statusId = Guid.NewGuid();
        _links.Setup(x => x.ListForOccurrenceAsync(TenantId, OccurrenceTaskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { Link(taskId) });
        _tasks.Setup(x => x.GetByIdsForTenantAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WorkTask>
            {
                new() { Id = taskId, TenantId = TenantId, ShortId = "A-1", Title = "One", StatusId = statusId, ObjectiveId = Guid.NewGuid() },
            });
        _taskAccess.Setup(x => x.FilterViewableTasksAsync(TenantId, UserId, EmployeeId, It.IsAny<IReadOnlyList<WorkTask>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, Guid _, Guid _, IReadOnlyList<WorkTask> tasks, CancellationToken _) => tasks);
        _taskAccess.Setup(x => x.ResolveViewableTasksAsync(TenantId, UserId, It.IsAny<IReadOnlyList<WorkTask>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, Guid _, IReadOnlyList<WorkTask> tasks, CancellationToken _) => tasks);
        _statuses.Setup(x => x.GetByIdsForTenantAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TaskStatusEntity> { new() { Id = statusId, TenantId = TenantId, Name = "Open" } });
        _tasks.Setup(x => x.GetAssignedEmployeeIdsByTaskIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<Guid>>());

        var result = await Service().ListAsync(TenantId, EmployeeId, UserId, OccurrenceTaskId, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Null(result.Value![0].AssigneeEmployeeId);
    }

    [Fact]
    public async Task UnlinkAsync_WhenMeetingsAreDisabled_Is503AndNoLinkIsRemoved()
    {
        var result = await Service(new MeetingsOptions { Create = false })
            .UnlinkAsync(TenantId, EmployeeId, OccurrenceTaskId, Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal((503, MeetingErrorCodes.MeetingsDisabled), (result.StatusCode, result.ErrorCode));
        _links.Verify(x => x.Remove(It.IsAny<MeetingActionItem>()), Times.Never);
    }

    [Fact]
    public async Task UnlinkAsync_WhenMeetingsAreReadOnly_Is409AndNoLinkIsRemoved()
    {
        var result = await Service(new MeetingsOptions { Create = true, ReadOnly = true })
            .UnlinkAsync(TenantId, EmployeeId, OccurrenceTaskId, Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal((409, MeetingErrorCodes.MeetingsReadOnly), (result.StatusCode, result.ErrorCode));
        _links.Verify(x => x.Remove(It.IsAny<MeetingActionItem>()), Times.Never);
    }

    [Fact]
    public async Task LinkAsync_ATaskThatAlreadyHasALinkIsNotLinkedTwice()
    {
        GivenAVisibleOccurrence();
        var taskId = Guid.NewGuid();
        _links.Setup(x => x.ExistsForTaskAsync(TenantId, taskId, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        await Service().LinkAsync(TenantId, OccurrenceTaskId, taskId, EmployeeId, CancellationToken.None);

        _links.Verify(x => x.Add(It.IsAny<MeetingActionItem>()), Times.Never);
    }

    [Fact]
    public async Task LinkAsync_AnOccurrenceThatIsCancelledWritesNoLink()
    {
        var details = new MeetingDetails
        {
            TaskId = OccurrenceTaskId, TenantId = TenantId, SeriesId = SeriesId, OriginalStart = Start,
            CancelledAt = Start.AddHours(-1),
        };
        _meetings.Setup(x => x.GetOccurrenceByTaskAsync(TenantId, OccurrenceTaskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MeetingOccurrenceRow(details, "M-1", "Weekly", null, Guid.NewGuid(), Guid.NewGuid()));

        await Service().LinkAsync(TenantId, OccurrenceTaskId, Guid.NewGuid(), EmployeeId, CancellationToken.None);

        _links.Verify(x => x.Add(It.IsAny<MeetingActionItem>()), Times.Never);
    }
}
