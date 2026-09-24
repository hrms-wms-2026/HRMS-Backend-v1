using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.CalendarEvents.DTOs.Responses;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.CalendarEvents.Commands.CloseCalendarEvent;
using ONEVO.Application.Features.WorkManagement.CalendarEvents.Commands.CreateCalendarEvent;
using ONEVO.Application.Features.WorkManagement.CalendarEvents.Commands.UpdateCalendarEvent;
using ONEVO.Application.Features.WorkManagement.CalendarEvents.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.CalendarEvents.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Moq;

namespace ONEVO.Tests.Unit.Features.WorkManagement.CalendarEvents;

public sealed class CalendarEventCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();

    private static readonly DateOnly WindowStart = new(2026, 3, 1);
    private static readonly DateOnly WindowEnd = new(2026, 3, 31);

    // ----- Create -----

    [Fact]
    public async Task Create_AllowsModuleAlreadyInAnotherActiveEvent()
    {
        var h = new CreateHarness();
        h.WithObjectives(Objective());
        // module has no tasks -> no window work; the old "objective already in an event" block is gone.

        var result = await h.Handle(NewCreate(objectiveIds: new[] { ObjectiveId }));

        Assert.True(result.IsSuccess);
        Assert.Equal(WindowStart, result.Value!.StartDate);
        Assert.Equal(WindowEnd, result.Value.EndDate);
    }

    [Fact]
    public async Task Create_PersistsEventWithDatesAndDirectTaskLinks()
    {
        var taskId = Guid.NewGuid();
        var h = new CreateHarness();
        h.WithObjectives(Objective());
        h.WithProjectTasks(MakeTask(taskId, due: new DateOnly(2026, 3, 10)));

        var result = await h.Handle(NewCreate(taskIds: new[] { taskId }));

        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { taskId }, result.Value!.TaskIds);
        Assert.Single(h.AddedTaskMemberships!);
        Assert.Equal(taskId, h.AddedTaskMemberships!.Single().TaskId);
    }

    [Theory]
    [InlineData("2026-02-28")]
    [InlineData("2026-04-01")]
    public async Task Create_RejectsTaskWithDueDateOutsideWindow(string due)
    {
        var taskId = Guid.NewGuid();
        var h = new CreateHarness();
        h.WithObjectives(Objective());
        h.WithProjectTasks(MakeTask(taskId, due: DateOnly.Parse(due)));

        var result = await h.Handle(NewCreate(taskIds: new[] { taskId }));

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }

    [Theory]
    [InlineData("2026-03-01")] // exactly on start
    [InlineData("2026-03-31")] // exactly on end
    public async Task Create_AcceptsTaskWithDueDateOnWindowBoundary(string due)
    {
        var taskId = Guid.NewGuid();
        var h = new CreateHarness();
        h.WithObjectives(Objective());
        h.WithProjectTasks(MakeTask(taskId, due: DateOnly.Parse(due)));

        var result = await h.Handle(NewCreate(taskIds: new[] { taskId }));

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Create_RejectsTaskWithNoDueDate()
    {
        var taskId = Guid.NewGuid();
        var h = new CreateHarness();
        h.WithObjectives(Objective());
        h.WithProjectTasks(MakeTask(taskId, due: null));

        var result = await h.Handle(NewCreate(taskIds: new[] { taskId }));

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task Create_RejectsModuleLinkedTaskOutsideWindow()
    {
        var taskId = Guid.NewGuid();
        var h = new CreateHarness();
        h.WithObjectives(Objective());
        h.WithObjectiveTasks(ObjectiveId, MakeTask(taskId, due: new DateOnly(2026, 5, 1)));

        var result = await h.Handle(NewCreate(objectiveIds: new[] { ObjectiveId }));

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task Create_RejectsTaskAlreadyInAnotherActiveEvent()
    {
        var taskId = Guid.NewGuid();
        var h = new CreateHarness();
        h.WithObjectives(Objective());
        h.WithProjectTasks(MakeTask(taskId, due: new DateOnly(2026, 3, 10)));
        h.WithTaskAlreadyLinked(taskId, "Release 1.0");

        var result = await h.Handle(NewCreate(taskIds: new[] { taskId }));

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task Create_RejectsUnknownProjectTask()
    {
        var h = new CreateHarness();
        h.WithObjectives(Objective());

        var result = await h.Handle(NewCreate(taskIds: new[] { Guid.NewGuid() }));

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    // ----- Update -----

    [Fact]
    public async Task Update_AllowsModuleInAnotherActiveEvent()
    {
        var h = new UpdateHarness(WindowStart, WindowEnd);
        h.WithObjectives(Objective());

        var result = await h.Handle(new UpdateCalendarEventCommand(
            h.EventId, null, null, null, null, new[] { ObjectiveId }, null));

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Update_NarrowingWindowThatOrphansMemberTask_Rejected()
    {
        var taskId = Guid.NewGuid();
        var h = new UpdateHarness(WindowStart, WindowEnd);
        h.WithObjectives(Objective());
        h.WithExistingTaskLinks(taskId);
        h.WithProjectTasks(MakeTask(taskId, due: new DateOnly(2026, 3, 20)));

        var result = await h.Handle(new UpdateCalendarEventCommand(
            h.EventId, null, null, null, new DateOnly(2026, 3, 10), null, null));

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task Update_EmptyTaskIds_ClearsAllTaskLinks()
    {
        var taskId = Guid.NewGuid();
        var h = new UpdateHarness(WindowStart, WindowEnd);
        h.WithObjectives(Objective());
        h.WithExistingTaskLinks(taskId);
        h.WithProjectTasks(MakeTask(taskId, due: new DateOnly(2026, 3, 20)));

        var result = await h.Handle(new UpdateCalendarEventCommand(
            h.EventId, null, null, null, null, null, Array.Empty<Guid>()));

        Assert.True(result.IsSuccess);
        Assert.Single(h.RemovedTaskMemberships!);
        Assert.Empty(result.Value!.TaskIds);
    }

    [Fact]
    public async Task Update_AddTaskOutsideWindow_Rejected()
    {
        var taskId = Guid.NewGuid();
        var h = new UpdateHarness(WindowStart, WindowEnd);
        h.WithObjectives(Objective());
        h.WithProjectTasks(MakeTask(taskId, due: new DateOnly(2026, 4, 15)));

        var result = await h.Handle(new UpdateCalendarEventCommand(
            h.EventId, null, null, null, null, null, new[] { taskId }));

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task Update_DateOnlyEdit_KeepsMembers()
    {
        var taskId = Guid.NewGuid();
        var h = new UpdateHarness(WindowStart, WindowEnd);
        h.WithObjectives(Objective());
        h.WithExistingTaskLinks(taskId);
        h.WithProjectTasks(MakeTask(taskId, due: new DateOnly(2026, 3, 20)));

        var result = await h.Handle(new UpdateCalendarEventCommand(
            h.EventId, null, null, new DateOnly(2026, 2, 15), new DateOnly(2026, 4, 10), null, null));

        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { taskId }, result.Value!.TaskIds);
        Assert.Empty(h.RemovedTaskMemberships!);
    }

    // ----- Close -----

    [Fact]
    public async Task Close_ArchivesEventAndKeepsMemberships()
    {
        var (currentUser, identity) = UserContext();
        var eventId = Guid.NewGuid();
        var membership = new CalendarEventObjective { Id = Guid.NewGuid(), CalendarEventId = eventId, ObjectiveId = ObjectiveId };
        var events = new Mock<ICalendarEventRepository>();
        events.Setup(x => x.GetByIdForTenantAsync(TenantId, eventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CalendarEvent
            {
                Id = eventId, TenantId = TenantId, ProjectId = ProjectId, Name = "Existing", Color = "#000000",
                StartDate = WindowStart, EndDate = WindowEnd,
                Status = CalendarEventStatuses.Active, CreatedAt = DateTimeOffset.UtcNow, CreatedById = EmployeeId
            });
        events.Setup(x => x.ListMembershipsForEventAsync(eventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { membership });
        events.Setup(x => x.ListTaskMembershipsForEventAsync(eventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<CalendarEventTask>());
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<bool>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<bool>> op, CancellationToken ct) => op(ct));
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new CloseCalendarEventCommandHandler(
            currentUser.Object, identity.Object, events.Object, unitOfWork.Object);
        var result = await handler.Handle(new CloseCalendarEventCommand(eventId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(CalendarEventStatuses.Archived, result.Value!.Status);
        Assert.Equal(EmployeeId, result.Value.ArchivedById);
        events.Verify(x => x.Update(It.Is<CalendarEvent>(e => e.Status == CalendarEventStatuses.Archived)), Times.Once);
        events.Verify(x => x.RemoveMemberships(It.IsAny<IReadOnlyCollection<CalendarEventObjective>>()), Times.Never);
    }

    // ================= helpers =================

    private static CreateCalendarEventCommand NewCreate(
        IReadOnlyList<Guid>? objectiveIds = null, IReadOnlyList<Guid>? taskIds = null)
        => new(ProjectId, "Launch", "#ABCDEF", WindowStart, WindowEnd,
            objectiveIds ?? Array.Empty<Guid>(), taskIds ?? Array.Empty<Guid>());

    private static (Mock<ICurrentUser> CurrentUser, Mock<ICallerIdentityResolver> Identity) UserContext()
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);
        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmployeeId);
        return (currentUser, identity);
    }

    private static Objective Objective(Guid? id = null) => new()
    {
        Id = id ?? ObjectiveId, TenantId = TenantId, ProjectId = ProjectId, Title = "Objective", OwnerId = EmployeeId,
        StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 2, 1), AllocatedHours = 10m
    };

    private static WorkTask MakeTask(Guid id, DateOnly? due) => new()
    {
        Id = id, ProjectId = ProjectId, ObjectiveId = ObjectiveId, Title = "T", ShortId = "T-1", DueDate = due
    };

    private sealed class CreateHarness
    {
        private readonly Mock<IProjectRepository> _projects = new();
        private readonly Mock<IObjectiveRepository> _objectives = new();
        private readonly Mock<IWorkTaskRepository> _tasks = new();
        private readonly Mock<ICalendarEventRepository> _events = new();
        private readonly Mock<IUnitOfWork> _uow = new();

        public IReadOnlyCollection<CalendarEventTask>? AddedTaskMemberships { get; private set; }

        public CreateHarness()
        {
            _projects.Setup(x => x.GetByIdForTenantAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Project { Id = ProjectId, TenantId = TenantId, Name = "P", Identifier = "P" });
            _objectives.Setup(x => x.GetAllByProjectIdAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Objective>());
            _tasks.Setup(x => x.GetByProjectAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<WorkTask>());
            _tasks.Setup(x => x.GetByObjectiveIdAsync(TenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<WorkTask>());
            _events.Setup(x => x.ListActiveTaskLinksForTasksAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<ActiveCalendarEventTaskLink>());
            _events.Setup(x => x.AddTaskMembershipsAsync(It.IsAny<IReadOnlyCollection<CalendarEventTask>>(), It.IsAny<CancellationToken>()))
                .Callback<IReadOnlyCollection<CalendarEventTask>, CancellationToken>((m, _) => AddedTaskMemberships = m)
                .Returns(Task.CompletedTask);
            _uow.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<bool>>>(), It.IsAny<CancellationToken>()))
                .Returns((Func<CancellationToken, Task<bool>> op, CancellationToken ct) => op(ct));
            _uow.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        }

        public void WithObjectives(params Objective[] objectives)
            => _objectives.Setup(x => x.GetAllByProjectIdAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(objectives.ToList());

        public void WithProjectTasks(params WorkTask[] tasks)
            => _tasks.Setup(x => x.GetByProjectAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(tasks.ToList());

        public void WithObjectiveTasks(Guid objectiveId, params WorkTask[] tasks)
            => _tasks.Setup(x => x.GetByObjectiveIdAsync(TenantId, objectiveId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(tasks.ToList());

        public void WithTaskAlreadyLinked(Guid taskId, string eventName)
            => _events.Setup(x => x.ListActiveTaskLinksForTasksAsync(TenantId, It.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(taskId)), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<ActiveCalendarEventTaskLink> { new(Guid.NewGuid(), taskId, eventName) });

        public Task<Result<CalendarEventResponse>> Handle(CreateCalendarEventCommand command)
        {
            var (currentUser, identity) = UserContext();
            var handler = new CreateCalendarEventCommandHandler(
                currentUser.Object, identity.Object, _projects.Object, _objectives.Object,
                _tasks.Object, _events.Object, _uow.Object);
            return handler.Handle(command, CancellationToken.None);
        }
    }

    private sealed class UpdateHarness
    {
        private readonly Mock<IObjectiveRepository> _objectives = new();
        private readonly Mock<IWorkTaskRepository> _tasks = new();
        private readonly Mock<ICalendarEventRepository> _events = new();
        private readonly Mock<IUnitOfWork> _uow = new();

        public Guid EventId { get; } = Guid.NewGuid();
        public IReadOnlyCollection<CalendarEventTask>? RemovedTaskMemberships { get; private set; }

        public UpdateHarness(DateOnly start, DateOnly end)
        {
            _events.Setup(x => x.GetByIdForTenantAsync(TenantId, EventId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CalendarEvent
                {
                    Id = EventId, TenantId = TenantId, ProjectId = ProjectId, Name = "Existing", Color = "#000000",
                    StartDate = start, EndDate = end, Status = CalendarEventStatuses.Active,
                    CreatedAt = DateTimeOffset.UtcNow, CreatedById = EmployeeId
                });
            _events.Setup(x => x.ListMembershipsForEventAsync(EventId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<CalendarEventObjective>());
            _events.Setup(x => x.ListTaskMembershipsForEventAsync(EventId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<CalendarEventTask>());
            _events.Setup(x => x.ListActiveTaskLinksForTasksAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<ActiveCalendarEventTaskLink>());
            _objectives.Setup(x => x.GetAllByProjectIdAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Objective>());
            _tasks.Setup(x => x.GetByProjectAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<WorkTask>());
            _tasks.Setup(x => x.GetByObjectiveIdAsync(TenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<WorkTask>());
            _events.Setup(x => x.RemoveTaskMemberships(It.IsAny<IReadOnlyCollection<CalendarEventTask>>()))
                .Callback<IReadOnlyCollection<CalendarEventTask>>(m => RemovedTaskMemberships = m);
            _uow.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<bool>>>(), It.IsAny<CancellationToken>()))
                .Returns((Func<CancellationToken, Task<bool>> op, CancellationToken ct) => op(ct));
            _uow.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        }

        public void WithObjectives(params Objective[] objectives)
            => _objectives.Setup(x => x.GetAllByProjectIdAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(objectives.ToList());

        public void WithProjectTasks(params WorkTask[] tasks)
            => _tasks.Setup(x => x.GetByProjectAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(tasks.ToList());

        public void WithExistingTaskLinks(params Guid[] taskIds)
            => _events.Setup(x => x.ListTaskMembershipsForEventAsync(EventId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(taskIds.Select(id => new CalendarEventTask
                {
                    Id = Guid.NewGuid(), CalendarEventId = EventId, TaskId = id
                }).ToList());

        public Task<Result<CalendarEventResponse>> Handle(UpdateCalendarEventCommand command)
        {
            var (currentUser, identity) = UserContext();
            var handler = new UpdateCalendarEventCommandHandler(
                currentUser.Object, identity.Object, _objectives.Object, _tasks.Object, _events.Object, _uow.Object);
            return handler.Handle(command, CancellationToken.None);
        }
    }
}
