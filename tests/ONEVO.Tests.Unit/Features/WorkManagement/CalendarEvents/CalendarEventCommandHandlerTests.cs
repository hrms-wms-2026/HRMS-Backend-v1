using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.CalendarEvents.Commands.CloseCalendarEvent;
using ONEVO.Application.Features.WorkManagement.CalendarEvents.Commands.CreateCalendarEvent;
using ONEVO.Application.Features.WorkManagement.CalendarEvents.Commands.UpdateCalendarEvent;
using ONEVO.Application.Features.WorkManagement.CalendarEvents.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.CalendarEvents.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.CalendarEvents.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using Moq;

namespace ONEVO.Tests.Unit.Features.WorkManagement.CalendarEvents;

public sealed class CalendarEventCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();

    [Fact]
    public async Task Create_RejectsObjectiveAlreadyInAnotherActiveEvent()
    {
        var (currentUser, identity) = UserContext();
        var projects = new Mock<IProjectRepository>();
        projects.Setup(x => x.GetByIdForTenantAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Project { Id = ProjectId, TenantId = TenantId, Name = "P", Identifier = "P" });
        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetAllByProjectIdAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Objective> { Objective() });
        var events = new Mock<ICalendarEventRepository>();
        events.Setup(x => x.ListActiveMembershipsForObjectivesAsync(TenantId, It.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(ObjectiveId)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ActiveCalendarEventMembership> { new(Guid.NewGuid(), ObjectiveId, "#112233") });
        var unitOfWork = new Mock<IUnitOfWork>();

        var handler = new CreateCalendarEventCommandHandler(
            currentUser.Object, identity.Object, projects.Object, objectives.Object, events.Object, unitOfWork.Object);
        var result = await handler.Handle(
            new CreateCalendarEventCommand(ProjectId, "Launch", "#ABCDEF", new[] { ObjectiveId }), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        events.Verify(x => x.AddAsync(It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_PersistsEventAndMembershipsForValidObjectives()
    {
        var (currentUser, identity) = UserContext();
        var secondObjectiveId = Guid.NewGuid();
        var projects = new Mock<IProjectRepository>();
        projects.Setup(x => x.GetByIdForTenantAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Project { Id = ProjectId, TenantId = TenantId, Name = "P", Identifier = "P" });
        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetAllByProjectIdAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Objective> { Objective(), Objective(secondObjectiveId) });
        var events = new Mock<ICalendarEventRepository>();
        events.Setup(x => x.ListActiveMembershipsForObjectivesAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ActiveCalendarEventMembership>());
        CalendarEvent? added = null;
        IReadOnlyCollection<CalendarEventObjective>? addedMemberships = null;
        events.Setup(x => x.AddAsync(It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>()))
            .Callback<CalendarEvent, CancellationToken>((e, _) => added = e)
            .Returns(Task.CompletedTask);
        events.Setup(x => x.AddMembershipsAsync(It.IsAny<IReadOnlyCollection<CalendarEventObjective>>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyCollection<CalendarEventObjective>, CancellationToken>((m, _) => addedMemberships = m)
            .Returns(Task.CompletedTask);
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<bool>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<bool>> op, CancellationToken ct) => op(ct));
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new CreateCalendarEventCommandHandler(
            currentUser.Object, identity.Object, projects.Object, objectives.Object, events.Object, unitOfWork.Object);
        var result = await handler.Handle(
            new CreateCalendarEventCommand(ProjectId, "Launch", "#ABCDEF", new[] { ObjectiveId, secondObjectiveId }),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Launch", result.Value!.Name);
        Assert.Equal("#ABCDEF", result.Value.Color);
        Assert.Equal(CalendarEventStatuses.Active, result.Value.Status);
        Assert.Equal(new[] { ObjectiveId, secondObjectiveId }, result.Value.ObjectiveIds);
        Assert.NotNull(added);
        Assert.Equal(ProjectId, added!.ProjectId);
        Assert.Equal(EmployeeId, added.CreatedById);
        Assert.NotNull(addedMemberships);
        Assert.Equal(2, addedMemberships!.Count);
        Assert.All(addedMemberships, m => Assert.Equal(added.Id, m.CalendarEventId));
        Assert.Equal(new[] { ObjectiveId, secondObjectiveId }, addedMemberships.Select(m => m.ObjectiveId));
    }

    [Fact]
    public async Task Update_RejectsObjectiveAlreadyInDifferentActiveEvent()
    {
        var (currentUser, identity) = UserContext();
        var eventId = Guid.NewGuid();
        var projects = new Mock<IObjectiveRepository>();
        projects.Setup(x => x.GetAllByProjectIdAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Objective> { Objective() });
        var events = new Mock<ICalendarEventRepository>();
        events.Setup(x => x.GetByIdForTenantAsync(TenantId, eventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CalendarEvent
            {
                Id = eventId, TenantId = TenantId, ProjectId = ProjectId, Name = "Existing", Color = "#000000",
                Status = CalendarEventStatuses.Active, CreatedAt = DateTimeOffset.UtcNow, CreatedById = EmployeeId
            });
        events.Setup(x => x.ListMembershipsForEventAsync(eventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<CalendarEventObjective>());
        events.Setup(x => x.ListActiveMembershipsForObjectivesAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ActiveCalendarEventMembership> { new(Guid.NewGuid(), ObjectiveId, "#112233") });
        var unitOfWork = new Mock<IUnitOfWork>();

        var handler = new UpdateCalendarEventCommandHandler(
            currentUser.Object, identity.Object, projects.Object, events.Object, unitOfWork.Object);
        var result = await handler.Handle(
            new UpdateCalendarEventCommand(eventId, null, null, new[] { ObjectiveId }), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        events.Verify(x => x.Update(It.IsAny<CalendarEvent>()), Times.Never);
    }

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
                Status = CalendarEventStatuses.Active, CreatedAt = DateTimeOffset.UtcNow, CreatedById = EmployeeId
            });
        events.Setup(x => x.ListMembershipsForEventAsync(eventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { membership });
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
}
