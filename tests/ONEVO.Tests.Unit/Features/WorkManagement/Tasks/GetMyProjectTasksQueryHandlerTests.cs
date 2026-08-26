using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetMyProjectTasks;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class GetMyProjectTasksQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid CallerEmployeeIdConst = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();

    private sealed record TaskFixtureData(WorkTask Task, IReadOnlyList<Guid> AssigneeEmployeeIds);

    private static Project ActiveProject() => new()
    {
        Id = ProjectId, TenantId = TenantId, IsActive = true, Name = "Project",
        Identifier = "P1", CreatedAt = DateTimeOffset.UtcNow
    };

    private static TaskFixtureData TaskFixture(
        string title,
        DateOnly? dueDate = null,
        string priority = WorkTaskPriorities.Medium,
        Guid? sprintId = null,
        IReadOnlyList<Guid>? assigneeEmployeeIds = null) =>
        new(
            new WorkTask
            {
                Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId,
                ObjectiveId = Guid.NewGuid(), SprintId = sprintId, ShortId = $"P-{Random.Shared.Next(1, 9999)}",
                Title = title, CategoryId = Guid.NewGuid(), StatusId = Guid.NewGuid(),
                Priority = priority, DueDate = dueDate, CreatedAt = DateTimeOffset.UtcNow
            },
            assigneeEmployeeIds ?? Array.Empty<Guid>());

    private (GetMyProjectTasksQueryHandler Handler, Guid CallerEmployeeId, Project Project) ArrangeMyTasksHandler(
        IReadOnlyList<TaskFixtureData> fixtures,
        IReadOnlyDictionary<Guid, Guid>? openSessionsByTaskId = null)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CallerEmployeeIdConst);

        var project = ActiveProject();
        var projects = new Mock<IProjectRepository>();
        projects.Setup(x => x.GetByIdForTenantAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(project);

        var tasks = new Mock<IWorkTaskRepository>();
        tasks.Setup(x => x.GetByProjectAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(fixtures.Select(fixture => fixture.Task).ToList());

        var assignments = fixtures.SelectMany(fixture => fixture.AssigneeEmployeeIds.Select(employeeId => new TaskAssignment
        {
            Id = Guid.NewGuid(), TaskId = fixture.Task.Id, EmployeeId = employeeId,
            UserId = UserId, AssignedById = UserId, AssignedAt = DateTimeOffset.UtcNow
        })).ToList();
        var assignmentRepository = new Mock<ITaskAssignmentRepository>();
        assignmentRepository.Setup(x => x.GetByTaskIdsAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(assignments);

        var sessionRepository = new Mock<ITaskClockingSessionRepository>();
        sessionRepository.Setup(x => x.GetOpenSessionsForTasksAsync(
                TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(openSessionsByTaskId ?? new Dictionary<Guid, Guid>());

        var handler = new GetMyProjectTasksQueryHandler(
            currentUser.Object, identity.Object, projects.Object, tasks.Object, assignmentRepository.Object,
            sessionRepository.Object);
        return (handler, CallerEmployeeIdConst, project);
    }

    [Fact]
    public async Task Handle_ReturnsOnlyTasksAssignedToCaller()
    {
        var (handler, callerEmployeeId, project) = ArrangeMyTasksHandler(new[]
        {
            TaskFixture("Mine", assigneeEmployeeIds: new[] { CallerEmployeeIdConst }),
            TaskFixture("Someone else's", assigneeEmployeeIds: new[] { Guid.NewGuid() })
        });

        var result = await handler.Handle(new GetMyProjectTasksQuery(project.Id, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!);
        Assert.Equal("Mine", result.Value[0].Title);
        Assert.Equal(callerEmployeeId, result.Value[0].AssigneeEmployeeIds.Single());
    }

    [Fact]
    public async Task Handle_SortsByDueDateAscendingThenPriorityDescending_NullsDueDateLast()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var (handler, _, project) = ArrangeMyTasksHandler(new[]
        {
            TaskFixture("No due date", priority: WorkTaskPriorities.Critical, assigneeEmployeeIds: new[] { CallerEmployeeIdConst }),
            TaskFixture("Due later, high", dueDate: today.AddDays(5), priority: WorkTaskPriorities.High, assigneeEmployeeIds: new[] { CallerEmployeeIdConst }),
            TaskFixture("Due sooner, low", dueDate: today.AddDays(1), priority: WorkTaskPriorities.Low, assigneeEmployeeIds: new[] { CallerEmployeeIdConst }),
            TaskFixture("Same day as sooner, critical", dueDate: today.AddDays(1), priority: WorkTaskPriorities.Critical, assigneeEmployeeIds: new[] { CallerEmployeeIdConst })
        });

        var result = await handler.Handle(new GetMyProjectTasksQuery(project.Id, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            new[] { "Same day as sooner, critical", "Due sooner, low", "Due later, high", "No due date" },
            result.Value!.Select(task => task.Title).ToArray());
    }

    [Fact]
    public async Task Handle_TaskWithOpenSession_IncludesOpenClockSessionEmployeeId()
    {
        var fixture = TaskFixture("Mine", assigneeEmployeeIds: new[] { CallerEmployeeIdConst });
        var openEmployeeId = Guid.NewGuid();
        var (handler, _, project) = ArrangeMyTasksHandler(
            new[] { fixture }, new Dictionary<Guid, Guid> { [fixture.Task.Id] = openEmployeeId });

        var result = await handler.Handle(new GetMyProjectTasksQuery(project.Id, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(openEmployeeId, result.Value![0].OpenClockSessionEmployeeId);
    }

    [Fact]
    public async Task Handle_TaskWithNoOpenSession_HasNullOpenClockSessionEmployeeId()
    {
        var fixture = TaskFixture("Mine", assigneeEmployeeIds: new[] { CallerEmployeeIdConst });
        var (handler, _, project) = ArrangeMyTasksHandler(new[] { fixture });

        var result = await handler.Handle(new GetMyProjectTasksQuery(project.Id, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value![0].OpenClockSessionEmployeeId);
    }

    [Fact]
    public async Task Handle_WithSprintIdFilter_ReturnsOnlyThatSprintsTasks()
    {
        var sprintId = Guid.NewGuid();
        var (handler, _, project) = ArrangeMyTasksHandler(new[]
        {
            TaskFixture("In sprint", sprintId: sprintId, assigneeEmployeeIds: new[] { CallerEmployeeIdConst }),
            TaskFixture("Different sprint", sprintId: Guid.NewGuid(), assigneeEmployeeIds: new[] { CallerEmployeeIdConst })
        });

        var result = await handler.Handle(new GetMyProjectTasksQuery(project.Id, sprintId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!);
        Assert.Equal("In sprint", result.Value[0].Title);
    }
}
