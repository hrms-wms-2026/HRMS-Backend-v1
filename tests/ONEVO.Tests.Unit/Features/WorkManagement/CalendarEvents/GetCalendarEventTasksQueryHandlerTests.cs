using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.CalendarEvents.Queries.GetCalendarEventTasks;
using ONEVO.Application.Features.WorkManagement.CalendarEvents.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Hierarchy;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.CalendarEvents.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.CalendarEvents;

public sealed class GetCalendarEventTasksQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();

    [Fact]
    public async Task Handle_ReturnsUnionOfDirectPicksAndWholeLinkedObjectiveTasks()
    {
        var eventId = Guid.NewGuid();
        var wholeObjectiveId = Guid.NewGuid();
        var otherObjectiveId = Guid.NewGuid();
        var directTaskId = Guid.NewGuid();

        var h = new Harness();
        h.WithEvent(eventId);
        h.WithWholeLinks(wholeObjectiveId);
        h.WithDirectTaskLinks(directTaskId);
        h.WithObjectives(Objective(wholeObjectiveId), Objective(otherObjectiveId));
        h.WithProjectTasks(
            MakeTask(Guid.NewGuid(), wholeObjectiveId),
            MakeTask(Guid.NewGuid(), wholeObjectiveId),
            MakeTask(Guid.NewGuid(), wholeObjectiveId),
            MakeTask(directTaskId, otherObjectiveId));
        h.AllowAllPermissions();

        var result = await h.Handle(new GetCalendarEventTasksQuery(eventId));

        Assert.True(result.IsSuccess);
        Assert.Equal(4, result.Value!.Count); // 3 + 1, no duplicates
    }

    [Fact]
    public async Task Handle_EventWithNoLinkedTasks_ReturnsEmptyList()
    {
        var eventId = Guid.NewGuid();
        var h = new Harness();
        h.WithEvent(eventId);
        h.AllowAllPermissions();

        var result = await h.Handle(new GetCalendarEventTasksQuery(eventId));

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!);
    }

    [Fact]
    public async Task Handle_FiltersOutTasksFromObjectivesTheCallerCannotRead()
    {
        var eventId = Guid.NewGuid();
        var readableObjectiveId = Guid.NewGuid();
        var unreadableObjectiveId = Guid.NewGuid();

        var h = new Harness();
        h.WithEvent(eventId);
        h.WithWholeLinks(readableObjectiveId, unreadableObjectiveId);
        h.WithObjectives(Objective(readableObjectiveId), Objective(unreadableObjectiveId));
        h.WithProjectTasks(
            MakeTask(Guid.NewGuid(), readableObjectiveId),
            MakeTask(Guid.NewGuid(), unreadableObjectiveId));
        h.DenyPermissionsExceptRead(readableObjectiveId);

        var result = await h.Handle(new GetCalendarEventTasksQuery(eventId));

        Assert.True(result.IsSuccess);
        Assert.All(result.Value!, t => Assert.NotEqual(unreadableObjectiveId, t.ObjectiveId));
    }

    private static Objective Objective(Guid id) => new()
    {
        Id = id, TenantId = TenantId, ProjectId = ProjectId, Title = "Module " + id,
        OwnerId = Guid.NewGuid(), StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 2, 1), AllocatedHours = 10m
    };

    private static WorkTask MakeTask(Guid id, Guid objectiveId) => new()
    {
        Id = id, ProjectId = ProjectId, ObjectiveId = objectiveId, Title = "T", ShortId = "T-1", StatusId = Guid.NewGuid()
    };

    private sealed class Harness
    {
        private readonly Mock<ICurrentUser> _currentUser = new();
        private readonly Mock<ICallerIdentityResolver> _identity = new();
        private readonly Mock<ICalendarEventRepository> _events = new();
        private readonly Mock<IProjectMemberRepository> _members = new();
        private readonly Mock<IObjectiveRepository> _objectives = new();
        private readonly Mock<IModuleReadAccess> _readAccess = new();
        private readonly Mock<IPermissionResolver> _permissionResolver = new();
        private readonly Mock<IWorkTaskRepository> _tasks = new();
        private readonly Mock<ITaskStatusRepository> _taskStatuses = new();

        public Harness()
        {
            _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
            _currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
            _currentUser.SetupGet(x => x.UserId).Returns(UserId);
            _identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(EmployeeId);
            _members.Setup(x => x.HasActiveMembershipAsync(TenantId, ProjectId, EmployeeId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            _events.Setup(x => x.ListMembershipsForEventAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<CalendarEventObjective>());
            _events.Setup(x => x.ListTaskMembershipsForEventAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<CalendarEventTask>());
            _objectives.Setup(x => x.GetAllByProjectIdAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Objective>());
            _tasks.Setup(x => x.GetByProjectAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<WorkTask>());
            _taskStatuses.Setup(x => x.GetByIdsForTenantAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<ONEVO.Domain.Features.WorkManagement.Tasks.Entities.TaskStatus>());
        }

        public void WithEvent(Guid eventId)
            => _events.Setup(x => x.GetByIdForTenantAsync(TenantId, eventId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CalendarEvent
                {
                    Id = eventId, TenantId = TenantId, ProjectId = ProjectId, Name = "E", Color = "#000000",
                    StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31),
                    Status = CalendarEventStatuses.Active, CreatedAt = DateTimeOffset.UtcNow, CreatedById = EmployeeId
                });

        public void WithWholeLinks(params Guid[] objectiveIds)
            => _events.Setup(x => x.ListMembershipsForEventAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(objectiveIds.Select(id => new CalendarEventObjective { Id = Guid.NewGuid(), ObjectiveId = id }).ToList());

        public void WithDirectTaskLinks(params Guid[] taskIds)
            => _events.Setup(x => x.ListTaskMembershipsForEventAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(taskIds.Select(id => new CalendarEventTask { Id = Guid.NewGuid(), TaskId = id }).ToList());

        public void WithObjectives(params Objective[] objectives)
            => _objectives.Setup(x => x.GetAllByProjectIdAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(objectives.ToList());

        public void WithProjectTasks(params WorkTask[] tasks)
            => _tasks.Setup(x => x.GetByProjectAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(tasks.ToList());

        public void AllowAllPermissions()
            => _permissionResolver.Setup(x => x.ResolveAsync(UserId, TenantId, null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<string> { "*" });

        public void DenyPermissionsExceptRead(Guid readableObjectiveId)
        {
            _permissionResolver.Setup(x => x.ResolveAsync(UserId, TenantId, null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<string>());
            _readAccess.Setup(x => x.CanReadAsync(TenantId, It.Is<Objective>(o => o.Id == readableObjectiveId), EmployeeId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            _readAccess.Setup(x => x.CanReadAsync(TenantId, It.Is<Objective>(o => o.Id != readableObjectiveId), EmployeeId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);
        }

        public Task<ONEVO.Application.Common.Models.Result<IReadOnlyList<ONEVO.Application.Features.WorkManagement.CalendarEvents.DTOs.Responses.CalendarEventTaskSummaryResponse>>> Handle(GetCalendarEventTasksQuery query)
        {
            var handler = new GetCalendarEventTasksQueryHandler(
                _currentUser.Object, _identity.Object, _events.Object, _members.Object, _objectives.Object,
                _readAccess.Object, _permissionResolver.Object, _tasks.Object, _taskStatuses.Object);
            return handler.Handle(query, CancellationToken.None);
        }
    }
}
