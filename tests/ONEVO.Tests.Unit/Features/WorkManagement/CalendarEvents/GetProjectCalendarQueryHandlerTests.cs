using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.CalendarEvents.Queries.GetProjectCalendar;
using ONEVO.Application.Features.WorkManagement.CalendarEvents.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.ProjectMembers.Entities;
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

    [Fact]
    public async Task Handle_ReturnsEveryObjective_AndCascadesCanEdit_AndJoinsEventColor()
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CallerEmployeeId);

        var root = Objective(RootId, isDefault: true, parentId: null, isAchieved: false);
        var child = Objective(ChildId, isDefault: false, parentId: RootId, isAchieved: false);
        var unrelated = Objective(OtherId, isDefault: false, parentId: null, isAchieved: true);

        var members = new Mock<IProjectMemberRepository>();
        members.Setup(x => x.ListForEmployeeInProjectAsync(TenantId, ProjectId, CallerEmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProjectMember> { new()
            {
                Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, ObjectiveId = RootId,
                EmployeeId = CallerEmployeeId, IsActive = true
            }});

        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetAllByProjectIdAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Objective> { root, child, unrelated });

        var events = new Mock<ICalendarEventRepository>();
        var eventId = Guid.NewGuid();
        events.Setup(x => x.ListActiveMembershipsForProjectAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ActiveCalendarEventMembership>
            {
                new(eventId, ChildId, "#123456")
            });

        var handler = new GetProjectCalendarQueryHandler(
            currentUser.Object, identity.Object, members.Object, objectives.Object, events.Object);

        var result = await handler.Handle(new GetProjectCalendarQuery(ProjectId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value!.Count);
        Assert.False(result.Value.Single(x => x.ObjectiveId == RootId).CanEdit);
        Assert.True(result.Value.Single(x => x.ObjectiveId == ChildId).CanEdit);
        Assert.False(result.Value.Single(x => x.ObjectiveId == OtherId).CanEdit);
        Assert.Equal(eventId, result.Value.Single(x => x.ObjectiveId == ChildId).CalendarEventId);
        Assert.Equal("#123456", result.Value.Single(x => x.ObjectiveId == ChildId).CalendarEventColor);
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
}
