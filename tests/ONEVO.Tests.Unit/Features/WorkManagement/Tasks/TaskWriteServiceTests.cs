using FluentAssertions;
using Moq;
using ONEVO.Application.Features.WorkManagement.CalendarEvents.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using TaskStatusEntity = ONEVO.Domain.Features.WorkManagement.Tasks.Entities.TaskStatus;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class TaskWriteServiceTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();
    private static readonly Guid CategoryId = Guid.NewGuid();
    private static readonly Guid SprintId = Guid.NewGuid();
    private static readonly Guid StatusId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid ActorEmployeeId = Guid.NewGuid();

    private readonly Mock<IObjectiveRepository> _objectives = new();
    private readonly Mock<IProjectRepository> _projects = new();
    private readonly Mock<IWorkTaskRepository> _tasks = new();
    private readonly Mock<ITaskStatusRepository> _statuses = new();
    private readonly Mock<ISprintRepository> _sprints = new();
    private readonly Mock<ITaskCategoryRepository> _categories = new();
    private readonly Mock<IObjectiveAllocationSlackCalculator> _slack = new();
    private readonly Mock<ICalendarEventRepository> _calendar = CalendarEventRepositoryMocks.Empty();
    private readonly Mock<ISprintActivityLogRepository> _sprintLogs = new();
    private readonly Mock<ITaskEditLogRepository> _editLogs = new();
    private readonly Mock<ITaskPercentageLogRepository> _percentageLogs = new();

    private readonly Objective _objective = new()
    {
        Id = ObjectiveId, TenantId = TenantId, ProjectId = ProjectId, OwnerId = Guid.NewGuid(),
        IsActive = true, AllocatedHours = 100m, CreatedAt = DateTimeOffset.UtcNow
    };

    public TaskWriteServiceTests()
    {
        _objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_objective);
        _projects.Setup(x => x.GetByIdForTenantAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Project { Id = ProjectId, TenantId = TenantId, Identifier = "WEB", IsActive = true });
        _projects.Setup(x => x.IncrementAndGetNextTaskNumberAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(12L);
        _statuses.Setup(x => x.GetProjectTemplateAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TaskStatusEntity>
            {
                new() { Id = StatusId, TenantId = TenantId, ProjectId = ProjectId, Name = "To Do", Category = TaskStatusCategories.NotStarted }
            });
        _categories.Setup(x => x.GetByIdForTenantAsync(TenantId, CategoryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TaskCategory { Id = CategoryId, TenantId = TenantId, ProjectId = ProjectId, Name = "Task" });
        _sprints.Setup(x => x.GetByIdForTenantAsync(TenantId, SprintId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sprint { Id = SprintId, TenantId = TenantId, ProjectId = ProjectId, Name = "S1", Status = SprintStatuses.Active });
        _slack.Setup(x => x.CalculateAsync(TenantId, It.IsAny<Objective>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(50m);
    }

    private TaskWriteService Build() => new(
        _objectives.Object, _projects.Object, _tasks.Object, _statuses.Object, _sprints.Object, _categories.Object,
        _slack.Object, _calendar.Object, _sprintLogs.Object, _editLogs.Object, _percentageLogs.Object);

    private static TaskCreateInput CreateInput(DateOnly? due = null, decimal? estimate = null, Guid? sprintId = null)
        => new(ObjectiveId, "Build it", null, CategoryId, WorkTaskPriorities.Medium, due, estimate, null, sprintId);

    private static WorkTask ExistingTask(Guid? sprintId = null) => new()
    {
        Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, ObjectiveId = ObjectiveId,
        ShortId = "WEB-1", Title = "Old", Priority = WorkTaskPriorities.Low, StatusId = StatusId,
        CategoryId = CategoryId, SprintId = sprintId, ProgressPercent = 10, CreatedAt = DateTimeOffset.UtcNow
    };

    private static TaskEditInput EditInput(int? progress = null, Guid? sprintId = null)
        => new("New", null, WorkTaskPriorities.High, null, null, null, progress, "because", sprintId);

    [Fact]
    public async Task ValidateCreate_InactiveObjective_NotFound()
    {
        _objective.IsActive = false;

        var result = await Build().ValidateCreateAsync(TenantId, CreateInput());

        Assert.Equal(404, result.StatusCode);
        Assert.Equal("Objective not found.", result.Error);
    }

    [Fact]
    public async Task ValidateCreate_DueDateOutsideModuleEventWindow_Conflict()
    {
        _calendar.Setup(x => x.ListActiveEventWindowsForObjectiveAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new ActiveEventWindow(Guid.NewGuid(), "Release", new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31)) });

        var result = await Build().ValidateCreateAsync(TenantId, CreateInput(due: new DateOnly(2026, 4, 10)));

        Assert.Equal(409, result.StatusCode);
        Assert.StartsWith("Due date 2026-04-10 is outside event window(s): Release 2026-03-01..2026-03-31.", result.Error);
    }

    [Fact]
    public async Task ValidateCreate_EstimateAboveSlack_ConflictWithSlackJson()
    {
        var result = await Build().ValidateCreateAsync(TenantId, CreateInput(estimate: 60m));

        Assert.Equal(409, result.StatusCode);
        Assert.Contains("\"availableSlackHours\":50", result.Error);
    }

    [Fact]
    public async Task ValidateCreate_NoDefaultStatus_422()
    {
        _statuses.Setup(x => x.GetProjectTemplateAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TaskStatusEntity> { new() { Id = Guid.NewGuid(), Category = TaskStatusCategories.Done } });

        var result = await Build().ValidateCreateAsync(TenantId, CreateInput());

        Assert.Equal(422, result.StatusCode);
        Assert.Equal("No task statuses configured for this milestone yet.", result.Error);
    }

    [Fact]
    public async Task Create_StampsCreatorPositionAndCreatedBy_AndLogsSprintAdd()
    {
        var position = Guid.NewGuid();

        var result = await Build().CreateAsync(TenantId, UserId, ActorEmployeeId, CreateInput(sprintId: SprintId), position);

        Assert.True(result.IsSuccess);
        var task = result.Value!;
        Assert.Equal(UserId, task.CreatedById);
        Assert.Equal(position, task.CreatorPositionObjectiveId);
        Assert.Equal("WEB-12", task.ShortId);
        Assert.Equal(StatusId, task.StatusId);
        Assert.Equal(SprintId, task.SprintId);
        _tasks.Verify(x => x.AddAsync(task, It.IsAny<CancellationToken>()), Times.Once);
        _sprintLogs.Verify(x => x.AddAsync(It.Is<SprintActivityLog>(l =>
            l.SprintId == SprintId && l.Action == SprintActivityActions.TasksAdded && l.EmployeeId == ActorEmployeeId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ValidateEdit_AchievedSprint_Forbidden()
    {
        var frozen = Guid.NewGuid();
        _sprints.Setup(x => x.GetByIdForTenantAsync(TenantId, frozen, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sprint { Id = frozen, ProjectId = ProjectId, Status = SprintStatuses.Achieved });

        var result = await Build().ValidateEditAsync(TenantId, ExistingTask(frozen), _objective, EditInput());

        Assert.Equal(403, result.StatusCode);
        Assert.Equal("This task's sprint has been achieved and is now frozen.", result.Error);
    }

    [Fact]
    public async Task ValidateEdit_TargetSprintInOtherProject_Conflict()
    {
        var other = Guid.NewGuid();
        _sprints.Setup(x => x.GetByIdForTenantAsync(TenantId, other, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sprint { Id = other, ProjectId = Guid.NewGuid(), Status = SprintStatuses.Active });

        var result = await Build().ValidateEditAsync(TenantId, ExistingTask(), _objective, EditInput(sprintId: other));

        Assert.Equal(409, result.StatusCode);
        Assert.Equal("Target sprint must belong to the same project.", result.Error);
    }

    [Fact]
    public async Task ApplyEdit_WritesEditLogWithSourceAndApprovalRequestId_AttributedToActor()
    {
        var task = ExistingTask();
        var requestId = Guid.NewGuid();

        var result = await Build().ApplyEditAsync(TenantId, ActorEmployeeId, task, _objective, EditInput(),
            TaskEditLogSources.ApprovedRequest, requestId);

        Assert.True(result.IsSuccess);
        Assert.Equal("New", task.Title);
        Assert.Equal(WorkTaskPriorities.High, task.Priority);
        Assert.NotNull(task.UpdatedAt);
        _editLogs.Verify(x => x.AddAsync(It.Is<TaskEditLog>(l =>
            l.EmployeeId == ActorEmployeeId && l.Source == TaskEditLogSources.ApprovedRequest &&
            l.EditRequestId == requestId && l.Reason == "because" &&
            l.OldValuesJson.Contains("Old") && l.NewValuesJson.Contains("New")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ApplyEdit_ProgressChange_WritesPercentageLog()
    {
        var task = ExistingTask();

        var result = await Build().ApplyEditAsync(TenantId, ActorEmployeeId, task, _objective, EditInput(progress: 60),
            TaskEditLogSources.Direct, null);

        Assert.True(result.IsSuccess);
        Assert.Equal(60, task.ProgressPercent);
        _percentageLogs.Verify(x => x.AddAsync(It.Is<TaskPercentageLog>(l =>
            l.EmployeeId == ActorEmployeeId && l.PreviousPercent == 10 && l.NewPercent == 60 &&
            l.Source == TaskPercentageLogSources.ManualEdit && l.Reason == "because"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void Restore_ClearsIsDeletedAndDeletedAt()
    {
        var task = new WorkTask { Id = Guid.NewGuid(), TenantId = TenantId, IsDeleted = true, DeletedAt = DateTimeOffset.UtcNow };
        var service = Build();
        service.Restore(task);
        task.IsDeleted.Should().BeFalse();
        task.DeletedAt.Should().BeNull();
    }
}
