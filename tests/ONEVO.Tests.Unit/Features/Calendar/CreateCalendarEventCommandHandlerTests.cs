using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Commands.CreateCalendarEvent;
using ONEVO.Application.Features.Calendar.DTOs.Responses;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class CreateCalendarEventCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTimeOffset Start = new(2026, 9, 10, 9, 0, 0, TimeSpan.Zero);

    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<ICalendarEventRepository> _events = new();
    private readonly Mock<ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository> _employees = new();
    private readonly Mock<ICalendarNotificationSender> _notifications = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();

    private CreateCalendarEventCommandHandler BuildSut()
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        _currentUser.SetupGet(x => x.UserId).Returns(UserId);
        _employees.Setup(x => x.GetDefaultForUserAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = Guid.NewGuid(), TenantId = TenantId, UserId = UserId, FirstName = "Ada", LastName = "Owner" });
        _unitOfWork
            .Setup(x => x.ExecuteInTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<ONEVO.Application.Common.Models.Result<CalendarEventItem>>>>(),
                It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<ONEVO.Application.Common.Models.Result<CalendarEventItem>>>, CancellationToken>(
                (action, ct) => action(ct));
        return new CreateCalendarEventCommandHandler(_currentUser.Object, _events.Object, _employees.Object, _notifications.Object, _unitOfWork.Object);
    }

    [Fact]
    public async Task Handle_Unauthenticated_ReturnsForbidden()
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(false);
        var sut = new CreateCalendarEventCommandHandler(_currentUser.Object, _events.Object, _employees.Object, _notifications.Object, _unitOfWork.Object);

        var result = await sut.Handle(
            new CreateCalendarEventCommand("Title", null, Start, Start.AddHours(1), false, "UTC", null, null, null, CalendarRecurrences.None, []),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_CreatesEventAndParticipants()
    {
        var sut = BuildSut();
        var participantId = Guid.NewGuid();

        var result = await sut.Handle(
            new CreateCalendarEventCommand(
                "Sprint Planning", "Plan the sprint", Start, Start.AddHours(1), false, "UTC",
                "Room 4", null, "#2563EB", CalendarRecurrences.None, [participantId]),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Sprint Planning", result.Value!.Title);
        _events.Verify(x => x.AddAsync(It.Is<CalendarEvent>(e =>
            e.Title == "Sprint Planning" && e.TenantId == TenantId), It.IsAny<CancellationToken>()), Times.Once);
        _events.Verify(x => x.AddParticipantsAsync(
            It.Is<IReadOnlyList<CalendarEventParticipant>>(p => p.Count == 1 && p[0].EmployeeId == participantId),
            It.IsAny<CancellationToken>()), Times.Once);
        _notifications.Verify(x => x.NotifyParticipantsAddedAsync(
            TenantId, "Sprint Planning", Start, "Room 4",
            It.Is<IReadOnlyList<Guid>>(ids => ids.Count == 1 && ids[0] == participantId),
            "Ada Owner", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_NoParticipants_DoesNotNotify()
    {
        var sut = BuildSut();

        var result = await sut.Handle(
            new CreateCalendarEventCommand("Solo block", null, Start, Start.AddHours(1), false, "UTC", null, null, null, CalendarRecurrences.None, []),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        _notifications.Verify(x => x.NotifyParticipantsAddedAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<string?>(),
            It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_EndDateBeforeStartDate_ReturnsFailure()
    {
        var sut = BuildSut();

        var result = await sut.Handle(
            new CreateCalendarEventCommand("Title", null, Start, Start.AddHours(-1), false, "UTC", null, null, null, CalendarRecurrences.None, []),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task Handle_RecurringWithoutRecurrenceRule_ReturnsFailure()
    {
        var sut = BuildSut();

        var result = await sut.Handle(
            new CreateCalendarEventCommand("Title", null, Start, Start.AddHours(1), false, "UTC", null, null, null, CalendarRecurrences.Weekly, []),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task Handle_RecurringWithRecurrenceRule_SetsItOnTheEntity()
    {
        var sut = BuildSut();

        var result = await sut.Handle(
            new CreateCalendarEventCommand("Standup", null, Start, Start.AddMinutes(30), false, "UTC", null, null, null,
                CalendarRecurrences.Weekly, [], RecurrenceRule: "FREQ=WEEKLY"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        _events.Verify(x => x.AddAsync(It.Is<CalendarEvent>(e => e.RecurrenceRule == "FREQ=WEEKLY"), It.IsAny<CancellationToken>()), Times.Once);
    }
}
