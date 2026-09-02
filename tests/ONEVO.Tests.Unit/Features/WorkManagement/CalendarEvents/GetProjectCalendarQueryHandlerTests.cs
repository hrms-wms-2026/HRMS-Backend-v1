using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.CalendarEvents.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.CalendarEvents.Queries.GetProjectCalendar;
using ONEVO.Application.Features.WorkManagement.CalendarEvents.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.ProjectMembers.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Moq;

namespace ONEVO.Tests.Unit.Features.WorkManagement.CalendarEvents;

public sealed class GetProjectCalendarQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid CallerEmployeeId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid RootId = Guid.NewGuid();
    private static readonly Guid ChildId = Guid.NewGuid();
    private static readonly Guid OtherId = Guid.NewGuid();

    private static DateOnly D(string s) => DateOnly.Parse(s);

    [Fact]
    public async Task Handle_ListsWholeAndPartialEventLinks_PerModule_AndBands()
    {
        var eventA = Guid.NewGuid();
        var eventB = Guid.NewGuid();
        var taskInB = Guid.NewGuid();

        var h = new Harness();
        h.WithObjectives(
            Objective(RootId, isDefault: true, parentId: null, isAchieved: false),
            Objective(ChildId, isDefault: false, parentId: RootId, isAchieved: false),
            Objective(OtherId, isDefault: false, parentId: null, isAchieved: false));
        h.WithActiveMembership(CallerEmployeeId, RootId);
        h.WithWholeLinks(new ActiveCalendarEventMembership(eventA, ChildId, "#111111"));
        h.WithEventHeaders(
            new ActiveEventHeader(eventA, "A", "#111111", D("2026-03-01"), D("2026-03-31")),
            new ActiveEventHeader(eventB, "B", "#222222", D("2026-04-01"), D("2026-04-30")));
        h.WithTaskLinks(new ActiveEventTaskMembership(eventB, taskInB, OtherId));
        h.WithProjectTasks(
            Task(taskInB, OtherId),
            Task(Guid.NewGuid(), OtherId)); // Other has 2 tasks total -> partial 1/2

        var result = await h.Handle();

        Assert.True(result.IsSuccess);
        var modules = result.Value!.Modules;
        Assert.Equal(3, modules.Count);

        var childLink = Assert.Single(modules.Single(m => m.ObjectiveId == ChildId).Events);
        Assert.Equal(ProjectCalendarEventMemberships.Whole, childLink.Membership);

        var otherLink = Assert.Single(modules.Single(m => m.ObjectiveId == OtherId).Events);
        Assert.Equal(ProjectCalendarEventMemberships.Partial, otherLink.Membership);
        Assert.Equal(1, otherLink.TasksInEventCount);
        Assert.Equal(2, otherLink.TaskTotalCount);

        Assert.Empty(modules.Single(m => m.ObjectiveId == RootId).Events);
        Assert.Equal(2, result.Value.Bands.Count);
    }

    [Fact]
    public async Task Handle_ModuleWholeAndStrayTaskInSameEvent_ReturnsSingleWholeLink()
    {
        var eventA = Guid.NewGuid();
        var strayTask = Guid.NewGuid();

        var h = new Harness();
        h.WithObjectives(Objective(ChildId, isDefault: false, parentId: null, isAchieved: false));
        h.WithWholeLinks(new ActiveCalendarEventMembership(eventA, ChildId, "#111111"));
        h.WithEventHeaders(new ActiveEventHeader(eventA, "A", "#111111", D("2026-03-01"), D("2026-03-31")));
        h.WithTaskLinks(new ActiveEventTaskMembership(eventA, strayTask, ChildId));
        h.WithProjectTasks(Task(strayTask, ChildId));

        var result = await h.Handle();

        var link = Assert.Single(result.Value!.Modules.Single(m => m.ObjectiveId == ChildId).Events);
        Assert.Equal(ProjectCalendarEventMemberships.Whole, link.Membership);
    }

    [Fact]
    public async Task Handle_ModuleInTwoEvents_ReturnsBothLinks()
    {
        var eventA = Guid.NewGuid();
        var eventB = Guid.NewGuid();

        var h = new Harness();
        h.WithObjectives(Objective(ChildId, isDefault: false, parentId: null, isAchieved: false));
        h.WithWholeLinks(
            new ActiveCalendarEventMembership(eventA, ChildId, "#111111"),
            new ActiveCalendarEventMembership(eventB, ChildId, "#222222"));
        h.WithEventHeaders(
            new ActiveEventHeader(eventA, "A", "#111111", D("2026-03-01"), D("2026-03-31")),
            new ActiveEventHeader(eventB, "B", "#222222", D("2026-04-01"), D("2026-04-30")));

        var result = await h.Handle();

        Assert.Equal(2, result.Value!.Modules.Single(m => m.ObjectiveId == ChildId).Events.Count);
    }

    [Fact]
    public async Task Handle_NotAuthenticated_ReturnsForbidden()
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(false);

        var handler = new GetProjectCalendarQueryHandler(
            currentUser.Object,
            new Mock<ICallerIdentityResolver>().Object,
            new Mock<IProjectMemberRepository>().Object,
            new Mock<IObjectiveRepository>().Object,
            new Mock<IWorkTaskRepository>().Object,
            new Mock<ICalendarEventRepository>().Object);

        var result = await handler.Handle(new GetProjectCalendarQuery(ProjectId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    private static Objective Objective(Guid id, bool isDefault, Guid? parentId, bool isAchieved) => new()
    {
        Id = id,
        TenantId = TenantId,
        ProjectId = ProjectId,
        ParentObjectiveId = parentId,
        IsDefault = isDefault,
        Title = id == RootId ? "Root" : id == ChildId ? "Child" : "Other",
        OwnerId = Guid.NewGuid(),
        IsActive = true,
        IsAchieved = isAchieved,
        StartDate = new DateOnly(2026, 1, 1),
        EndDate = new DateOnly(2026, 3, 1),
        AllocatedHours = 10m
    };

    private static WorkTask Task(Guid id, Guid objectiveId) => new()
    {
        Id = id, ProjectId = ProjectId, ObjectiveId = objectiveId, Title = "T", ShortId = "T-1"
    };

    private sealed class Harness
    {
        private readonly Mock<ICurrentUser> _currentUser = new();
        private readonly Mock<ICallerIdentityResolver> _identity = new();
        private readonly Mock<IProjectMemberRepository> _members = new();
        private readonly Mock<IObjectiveRepository> _objectives = new();
        private readonly Mock<IWorkTaskRepository> _tasks = new();
        private readonly Mock<ICalendarEventRepository> _events = new();

        public Harness()
        {
            _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
            _currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
            _currentUser.SetupGet(x => x.UserId).Returns(UserId);
            _identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(CallerEmployeeId);
            _members.Setup(x => x.ListForEmployeeInProjectAsync(TenantId, ProjectId, CallerEmployeeId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<ProjectMember>());
            _objectives.Setup(x => x.GetAllByProjectIdAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Objective>());
            _tasks.Setup(x => x.GetByProjectAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<WorkTask>());
            _events.Setup(x => x.ListActiveMembershipsForProjectAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<ActiveCalendarEventMembership>());
            _events.Setup(x => x.ListActiveTaskMembershipsForProjectAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<ActiveEventTaskMembership>());
            _events.Setup(x => x.ListActiveEventHeadersForProjectAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<ActiveEventHeader>());
        }

        public void WithObjectives(params Objective[] objectives)
            => _objectives.Setup(x => x.GetAllByProjectIdAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(objectives.ToList());

        public void WithActiveMembership(Guid employeeId, Guid objectiveId)
            => _members.Setup(x => x.ListForEmployeeInProjectAsync(TenantId, ProjectId, employeeId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<ProjectMember>
                {
                    new()
                    {
                        Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, ObjectiveId = objectiveId,
                        EmployeeId = employeeId, IsActive = true
                    }
                });

        public void WithWholeLinks(params ActiveCalendarEventMembership[] links)
            => _events.Setup(x => x.ListActiveMembershipsForProjectAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(links.ToList());

        public void WithTaskLinks(params ActiveEventTaskMembership[] links)
            => _events.Setup(x => x.ListActiveTaskMembershipsForProjectAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(links.ToList());

        public void WithEventHeaders(params ActiveEventHeader[] headers)
            => _events.Setup(x => x.ListActiveEventHeadersForProjectAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(headers.ToList());

        public void WithProjectTasks(params WorkTask[] tasks)
            => _tasks.Setup(x => x.GetByProjectAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(tasks.ToList());

        public Task<ONEVO.Application.Common.Models.Result<ProjectCalendarResponse>> Handle()
        {
            var handler = new GetProjectCalendarQueryHandler(
                _currentUser.Object, _identity.Object, _members.Object, _objectives.Object,
                _tasks.Object, _events.Object);
            return handler.Handle(new GetProjectCalendarQuery(ProjectId), CancellationToken.None);
        }
    }
}
