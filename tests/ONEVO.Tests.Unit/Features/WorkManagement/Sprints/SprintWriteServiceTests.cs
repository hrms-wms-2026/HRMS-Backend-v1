using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Sprints.DTOs;
using ONEVO.Application.Features.WorkManagement.Sprints.Services;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using ONEVO.Domain.Features.WorkManagement.ProjectMembers.Entities;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;
using TaskStatus = ONEVO.Domain.Features.WorkManagement.Tasks.Entities.TaskStatus;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Sprints;

public class SprintWriteServiceTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly Guid CreatorUser = Guid.NewGuid();

    private readonly SprintTestWiring _w = new(TenantId, ProjectId);

    public SprintWriteServiceTests()
    {
        _w.Projects.Setup(x => x.GetByIdForTenantAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Project { Id = ProjectId, TenantId = TenantId, Name = "P", IsActive = true });
        _w.Assignment.Setup(x => x.PrepareAsync(TenantId, It.IsAny<Sprint>(), It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<IReadOnlyCollection<Guid>>(), Actor, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<SprintTaskChangeSet>.Success(SprintTaskChangeSet.Empty));
        _w.Tasks.Setup(x => x.GetBySprintIdAsync(TenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<WorkTask>());
    }

    private static Sprint Sprint(string status) => new()
    {
        Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, Name = "S1", Status = status,
        StartDate = status == SprintStatuses.Draft ? null : new DateOnly(2026, 9, 1),
        EndDate = status == SprintStatuses.Draft ? null : new DateOnly(2026, 9, 14),
        CreatedAt = DateTimeOffset.UtcNow
    };

    [Fact]
    public async Task ValidateCreate_ProjectMissing_NotFound()
    {
        var result = await _w.Writes().ValidateCreateAsync(TenantId, Actor, new SprintCreateInput(Guid.NewGuid(), "S", null, []));

        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task ValidateCreate_TaskMoveRejected_ReturnsPrepareError()
    {
        _w.Assignment.Setup(x => x.PrepareAsync(TenantId, It.IsAny<Sprint>(), It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<IReadOnlyCollection<Guid>>(), Actor, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<SprintTaskChangeSet>.Forbidden("nope"));

        var result = await _w.Writes().ValidateCreateAsync(TenantId, Actor, new SprintCreateInput(ProjectId, "S", null, [Guid.NewGuid()]));

        result.StatusCode.Should().Be(403);
        result.Error.Should().Be("nope");
    }

    [Fact]
    public async Task Create_StampsCreatorAndPosition()
    {
        var position = Guid.NewGuid();

        var result = await _w.Writes().CreateAsync(TenantId, CreatorUser, Actor, new SprintCreateInput(ProjectId, " S1 ", null, []), position);

        result.Value!.CreatedById.Should().Be(CreatorUser);
        result.Value.CreatorPositionObjectiveId.Should().Be(position);
        result.Value.Name.Should().Be("S1");
        result.Value.Status.Should().Be(SprintStatuses.Draft);
        _w.Sprints.Verify(x => x.AddAsync(result.Value, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(SprintStatuses.Complete)]
    [InlineData(SprintStatuses.Achieved)]
    public async Task ValidateEdit_EndedSprint_Conflict(string status)
    {
        var result = await _w.Writes().ValidateEditAsync(Sprint(status), new SprintEditInput("N", null, null, null));

        result.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task ApplyEdit_ActiveSprint_SetsNameGoalAndDates()
    {
        var sprint = Sprint(SprintStatuses.Active);

        var result = await _w.Writes().ApplyEditAsync(TenantId, Actor, sprint,
            new SprintEditInput(" New ", " goal ", new DateOnly(2026, 9, 2), new DateOnly(2026, 9, 20)));

        result.IsSuccess.Should().BeTrue();
        sprint.Name.Should().Be("New");
        sprint.Goal.Should().Be("goal");
        sprint.EndDate.Should().Be(new DateOnly(2026, 9, 20));
    }

    [Fact]
    public async Task ValidateStart_NotDraft_Conflict()
    {
        var result = await _w.Writes().ValidateStartAsync(TenantId, Sprint(SprintStatuses.Active),
            new SprintStartInput(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 14), null));

        result.StatusCode.Should().Be(409);
        result.Error.Should().Be("Only a Draft sprint can be started.");
    }

    [Fact]
    public async Task ApplyStart_DraftBecomesActiveWithDates()
    {
        var sprint = Sprint(SprintStatuses.Draft);

        await _w.Writes().ApplyStartAsync(TenantId, Actor, sprint, new SprintStartInput(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 14), null));

        sprint.Status.Should().Be(SprintStatuses.Active);
        sprint.StartDate.Should().Be(new DateOnly(2026, 9, 1));
    }

    [Fact]
    public async Task ValidateComplete_UnknownDisposition_422()
    {
        var result = await _w.Writes().ValidateCompleteAsync(TenantId, Sprint(SprintStatuses.Active), new SprintCompleteInput("bin", null));

        result.StatusCode.Should().Be(422);
    }

    [Fact]
    public async Task ApplyComplete_Backlog_ClearsIncompleteTasksAndCompletes()
    {
        var sprint = Sprint(SprintStatuses.Active);
        var todo = Guid.NewGuid();
        var task = new WorkTask { Id = Guid.NewGuid(), SprintId = sprint.Id, StatusId = todo };
        _w.Tasks.Setup(x => x.GetBySprintIdAsync(TenantId, sprint.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new List<WorkTask> { task });
        _w.Statuses.Setup(x => x.GetByIdForTenantAsync(TenantId, todo, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TaskStatus { Id = todo, MarksTaskComplete = false });

        await _w.Writes().ApplyCompleteAsync(TenantId, Actor, sprint, new SprintCompleteInput("backlog", null));

        sprint.Status.Should().Be(SprintStatuses.Complete);
        task.SprintId.Should().BeNull();
    }

    [Fact]
    public async Task ApplyComplete_UnfinishedTasksMovedToBacklog_NotifiesIncomplete()
    {
        var sprint = Sprint(SprintStatuses.Active);
        var objectiveId = Guid.NewGuid();
        var todo = Guid.NewGuid();
        var task = new WorkTask { Id = Guid.NewGuid(), SprintId = sprint.Id, StatusId = todo, ObjectiveId = objectiveId };
        _w.Tasks.Setup(x => x.GetBySprintIdAsync(TenantId, sprint.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new List<WorkTask> { task });
        _w.Statuses.Setup(x => x.GetByIdForTenantAsync(TenantId, todo, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TaskStatus { Id = todo, MarksTaskComplete = false });

        var employeeId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        _w.Members.Setup(x => x.ListActiveForObjectiveAsync(TenantId, objectiveId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProjectMember> { new() { EmployeeId = employeeId, ObjectiveId = objectiveId, ProjectId = ProjectId } });
        _w.Membership.Setup(x => x.GetActiveAssigneeAsync(TenantId, employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = employeeId, UserId = userId });

        await _w.Writes().ApplyCompleteAsync(TenantId, Actor, sprint, new SprintCompleteInput("backlog", null));

        _w.Notifications.Verify(x => x.SendTemplatedAsync(
            TenantId, userId, "work_sprint_incomplete",
            It.IsAny<IReadOnlyDictionary<string, string>>(), "sprint", sprint.Id, It.IsAny<CancellationToken>()), Times.Once);
        _w.Notifications.Verify(x => x.SendTemplatedAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), "work_sprint_completed",
            It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ApplyComplete_AllTasksAlreadyComplete_NotifiesCompleted()
    {
        var sprint = Sprint(SprintStatuses.Active);
        var objectiveId = Guid.NewGuid();
        var done = Guid.NewGuid();
        var task = new WorkTask { Id = Guid.NewGuid(), SprintId = sprint.Id, StatusId = done, ObjectiveId = objectiveId };
        _w.Tasks.Setup(x => x.GetBySprintIdAsync(TenantId, sprint.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new List<WorkTask> { task });
        _w.Statuses.Setup(x => x.GetByIdForTenantAsync(TenantId, done, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TaskStatus { Id = done, MarksTaskComplete = true });

        var employeeId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        _w.Members.Setup(x => x.ListActiveForObjectiveAsync(TenantId, objectiveId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProjectMember> { new() { EmployeeId = employeeId, ObjectiveId = objectiveId, ProjectId = ProjectId } });
        _w.Membership.Setup(x => x.GetActiveAssigneeAsync(TenantId, employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = employeeId, UserId = userId });

        await _w.Writes().ApplyCompleteAsync(TenantId, Actor, sprint, new SprintCompleteInput("backlog", null));

        _w.Notifications.Verify(x => x.SendTemplatedAsync(
            TenantId, userId, "work_sprint_completed",
            It.IsAny<IReadOnlyDictionary<string, string>>(), "sprint", sprint.Id, It.IsAny<CancellationToken>()), Times.Once);
        _w.Notifications.Verify(x => x.SendTemplatedAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), "work_sprint_incomplete",
            It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ValidateAchieve_AlreadyAchieved_Conflict()
    {
        var result = await _w.Writes().ValidateAchieveAsync(TenantId, Sprint(SprintStatuses.Achieved));

        result.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task ApplyAchieve_SetsAchieved()
    {
        var sprint = Sprint(SprintStatuses.Complete);

        await _w.Writes().ApplyAchieveAsync(TenantId, Actor, sprint);

        sprint.Status.Should().Be(SprintStatuses.Achieved);
        sprint.AchievedAt.Should().NotBeNull();
    }

    [Theory]
    [InlineData(SprintStatuses.Draft)]
    [InlineData(SprintStatuses.Active)]
    public void ValidateDelete_DraftOrActive_Conflict(string status)
    {
        var result = _w.Writes().ValidateDelete(Sprint(status));

        result.StatusCode.Should().Be(409);
        result.Error.Should().Be("Only a completed or achieved sprint can be deleted.");
    }

    [Fact]
    public async Task ApplyDelete_UnassignsTasks_RemovesSprint()
    {
        var sprint = Sprint(SprintStatuses.Complete);
        var listed = new WorkTask { Id = Guid.NewGuid(), SprintId = sprint.Id };
        var tracked = new WorkTask { Id = listed.Id, SprintId = sprint.Id };
        _w.Tasks.Setup(x => x.GetBySprintIdAsync(TenantId, sprint.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new List<WorkTask> { listed });
        _w.Tasks.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, listed.Id, It.IsAny<CancellationToken>())).ReturnsAsync(tracked);

        await _w.Writes().ApplyDeleteAsync(TenantId, sprint);

        tracked.SprintId.Should().BeNull();
        _w.Sprints.Verify(x => x.Remove(sprint), Times.Once);
    }
}
