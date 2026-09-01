using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Commands.UpdateCalendarEvent;
using ONEVO.Application.Features.Calendar.DTOs.Responses;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class UpdateCalendarEventCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid EventId = Guid.NewGuid();
    private static readonly DateTimeOffset Start = new(2026, 9, 10, 9, 0, 0, TimeSpan.Zero);

    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<ICalendarEventRepository> _events = new();
    private readonly Mock<ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository> _employees = new();
    private readonly Mock<ICalendarNotificationSender> _notifications = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();

    private UpdateCalendarEventCommandHandler BuildSut()
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        _currentUser.SetupGet(x => x.UserId).Returns(UserId);
        _employees.Setup(x => x.GetDefaultForUserAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = Guid.NewGuid(), TenantId = TenantId, UserId = UserId, FirstName = "Ada", LastName = "Owner" });
        _events.Setup(x => x.GetParticipantsForEventsAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<CalendarEventParticipant>>());
        _unitOfWork
            .Setup(x => x.ExecuteInTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<ONEVO.Application.Common.Models.Result<CalendarEventItem>>>>(),
                It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<ONEVO.Application.Common.Models.Result<CalendarEventItem>>>, CancellationToken>(
                (action, ct) => action(ct));
        return new UpdateCalendarEventCommandHandler(_currentUser.Object, _events.Object, _employees.Object, _notifications.Object, _unitOfWork.Object);
    }

    [Fact]
    public async Task Handle_EventNotFound_ReturnsNotFound()
    {
        var sut = BuildSut();
        _events.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, EventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CalendarEvent?)null);

        var result = await sut.Handle(
            new UpdateCalendarEventCommand(EventId, "Title", null, Start, Start.AddHours(1), false, null, null, null, CalendarRecurrences.None),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_NotOwner_ReturnsForbidden()
    {
        var sut = BuildSut();
        _events.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, EventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CalendarEvent { Id = EventId, TenantId = TenantId, CreatedById = Guid.NewGuid(), Title = "Old" });

        var result = await sut.Handle(
            new UpdateCalendarEventCommand(EventId, "New Title", null, Start, Start.AddHours(1), false, null, null, null, CalendarRecurrences.None),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_Owner_UpdatesFields()
    {
        var sut = BuildSut();
        var existing = new CalendarEvent { Id = EventId, TenantId = TenantId, CreatedById = UserId, Title = "Old", StartDate = Start, EndDate = Start.AddMinutes(30) };
        _events.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, EventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var result = await sut.Handle(
            new UpdateCalendarEventCommand(EventId, "New Title", "New desc", Start.AddHours(2), Start.AddHours(3), false, "Room 1", null, "#ff0000", CalendarRecurrences.Weekly),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("New Title", existing.Title);
        Assert.Equal(CalendarRecurrences.Weekly, existing.Recurrence);
        _events.Verify(x => x.Update(existing), Times.Once);
    }

    [Fact]
    public async Task Handle_OwnerWithParticipants_NotifiesThem()
    {
        var sut = BuildSut();
        var existing = new CalendarEvent { Id = EventId, TenantId = TenantId, CreatedById = UserId, Title = "Old", StartDate = Start, EndDate = Start.AddMinutes(30) };
        _events.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, EventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        var participantId = Guid.NewGuid();
        _events.Setup(x => x.GetParticipantsForEventsAsync(TenantId, It.Is<IReadOnlyList<Guid>>(ids => ids.Contains(EventId)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<CalendarEventParticipant>>
            {
                [EventId] = [new CalendarEventParticipant { Id = Guid.NewGuid(), TenantId = TenantId, EventId = EventId, EmployeeId = participantId, ResponseStatus = CalendarEventParticipantStatuses.Accepted }]
            });

        var result = await sut.Handle(
            new UpdateCalendarEventCommand(EventId, "New Title", null, Start.AddHours(2), Start.AddHours(3), false, null, null, null, CalendarRecurrences.None),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        _notifications.Verify(x => x.NotifyEventUpdatedAsync(
            TenantId, "New Title", It.Is<IReadOnlyList<Guid>>(ids => ids.Count == 1 && ids[0] == participantId),
            "Ada Owner", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_EndDateBeforeStartDate_ReturnsFailure()
    {
        var sut = BuildSut();

        var result = await sut.Handle(
            new UpdateCalendarEventCommand(EventId, "Title", null, Start, Start.AddHours(-1), false, null, null, null, CalendarRecurrences.None),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task Handle_Owner_DoesNotChangeTimezone()
    {
        var sut = BuildSut();
        var existing = new CalendarEvent { Id = EventId, TenantId = TenantId, CreatedById = UserId, Title = "Old", StartDate = Start, EndDate = Start.AddMinutes(30), Timezone = "Asia/Colombo" };
        _events.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, EventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        await sut.Handle(
            new UpdateCalendarEventCommand(EventId, "New Title", null, Start.AddHours(2), Start.AddHours(3), false, null, null, null, CalendarRecurrences.None),
            CancellationToken.None);

        Assert.Equal("Asia/Colombo", existing.Timezone);
    }
}
