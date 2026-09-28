using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.CalendarEvents.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.SetTaskAttributes;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using ONEVO.Tests.Unit.Fakes;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class SetTaskAttributesCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly Guid TaskId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();

    private static (
        SetTaskAttributesCommandHandler Handler,
        WorkTask Task,
        Mock<IMilestoneMembershipCoordinator> Membership,
        Mock<ISprintRepository> Sprints,
        Mock<ICalendarEventRepository> CalendarEvents,
        Mock<ITaskEditLogRepository> EditLogs,
        FakeUnitOfWork UnitOfWork) Build()
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmployeeId);

        var task = new WorkTask
        {
            Id = TaskId,
            TenantId = TenantId,
            ObjectiveId = ObjectiveId,
            Title = "Task",
            Priority = WorkTaskPriorities.Medium,
            DueDate = new DateOnly(2026, 10, 5)
        };
        var tasks = new Mock<IWorkTaskRepository>();
        tasks.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, TaskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(task);

        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Objective { Id = ObjectiveId, TenantId = TenantId });

        var membership = new Mock<IMilestoneMembershipCoordinator>();
        membership.Setup(x => x.IsEffectiveOwnerAsync(TenantId, ObjectiveId, EmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var sprints = new Mock<ISprintRepository>();
        var calendarEvents = CalendarEventRepositoryMocks.Empty();
        var editLogs = new Mock<ITaskEditLogRepository>();
        editLogs.Setup(x => x.AddAsync(It.IsAny<TaskEditLog>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var unitOfWork = new FakeUnitOfWork();

        var handler = new SetTaskAttributesCommandHandler(
            currentUser.Object, identity.Object, tasks.Object, objectives.Object, membership.Object,
            sprints.Object, calendarEvents.Object, editLogs.Object, unitOfWork);
        return (handler, task, membership, sprints, calendarEvents, editLogs, unitOfWork);
    }

    [Fact]
    public async Task SetsPriorityOnly_AndLogsOnlyThatField()
    {
        var x = Build();

        var result = await x.Handler.Handle(
            new SetTaskAttributesCommand(TaskId, WorkTaskPriorities.High, false, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(WorkTaskPriorities.High, x.Task.Priority);
        Assert.Equal(new DateOnly(2026, 10, 5), x.Task.DueDate);
        x.EditLogs.Verify(r => r.AddAsync(It.Is<TaskEditLog>(l =>
            l.Source == TaskEditLogSources.Direct
            && l.NewValuesJson == "{\"priority\":\"high\"}"
            && l.OldValuesJson == "{\"priority\":\"medium\"}"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ClearsDueDate_WhenSetDueDateWithNull()
    {
        var x = Build();

        var result = await x.Handler.Handle(
            new SetTaskAttributesCommand(TaskId, null, true, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(x.Task.DueDate);
        Assert.Equal(WorkTaskPriorities.Medium, x.Task.Priority);
    }

    [Fact]
    public async Task NonOwner_ReturnsForbidden_AndDoesNotSave()
    {
        var x = Build();
        x.Membership.Setup(m => m.IsEffectiveOwnerAsync(
                TenantId, ObjectiveId, EmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await x.Handler.Handle(
            new SetTaskAttributesCommand(TaskId, WorkTaskPriorities.High, false, null), CancellationToken.None);

        Assert.Equal(403, result.StatusCode);
        Assert.Equal(0, x.UnitOfWork.SaveCallCount);
    }

    [Fact]
    public async Task AchievedSprint_ReturnsForbidden()
    {
        var x = Build();
        var sprintId = Guid.NewGuid();
        x.Task.SprintId = sprintId;
        x.Sprints.Setup(s => s.GetByIdForTenantAsync(TenantId, sprintId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sprint { Id = sprintId, TenantId = TenantId, Status = SprintStatuses.Achieved });

        var result = await x.Handler.Handle(
            new SetTaskAttributesCommand(TaskId, WorkTaskPriorities.High, false, null), CancellationToken.None);

        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task DueDateOutsideEventWindow_ReturnsConflict_WithRuleMessage()
    {
        var x = Build();
        x.CalendarEvents.Setup(c => c.ListActiveEventWindowsForTaskAsync(
                TenantId, TaskId, ObjectiveId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new ActiveEventWindow(Guid.NewGuid(), "Launch", new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 10))
            });

        var result = await x.Handler.Handle(
            new SetTaskAttributesCommand(TaskId, null, true, new DateOnly(2026, 11, 1)), CancellationToken.None);

        Assert.Equal(409, result.StatusCode);
        Assert.Equal(
            "Due date 2026-11-01 is outside event window(s): Launch 2026-10-01..2026-10-10. Widen the event first.",
            result.Error);
    }

    [Fact]
    public async Task NoActualChange_SucceedsWithoutEditLog()
    {
        var x = Build();

        var result = await x.Handler.Handle(
            new SetTaskAttributesCommand(TaskId, WorkTaskPriorities.Medium, true, new DateOnly(2026, 10, 5)),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        x.EditLogs.Verify(r => r.AddAsync(It.IsAny<TaskEditLog>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(0, x.UnitOfWork.SaveCallCount);
    }

    [Fact]
    public void Validator_RejectsUnknownPriority_AndEmptyCommand()
    {
        var validator = new SetTaskAttributesCommandValidator();

        var invalidPriority = validator.Validate(new SetTaskAttributesCommand(TaskId, "urgent", false, null));
        var empty = validator.Validate(new SetTaskAttributesCommand(TaskId, null, false, null));

        Assert.False(invalidPriority.IsValid);
        Assert.Contains(invalidPriority.Errors,
            e => e.ErrorMessage == "Priority must be one of: low, medium, high, critical.");
        Assert.False(empty.IsValid);
    }
}
