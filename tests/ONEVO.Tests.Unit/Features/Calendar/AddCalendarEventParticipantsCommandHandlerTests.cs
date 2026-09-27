using FluentAssertions;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Commands.AddCalendarEventParticipants;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class AddCalendarEventParticipantsCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid EventId = Guid.NewGuid();
    private static readonly Guid NewEmployeeId = Guid.NewGuid();
    private static readonly Guid AlreadyParticipatingEmployeeId = Guid.NewGuid();

    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<ICalendarEventRepository> _events = new();
    private readonly Mock<ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository> _employees = new();
    private readonly Mock<ICalendarNotificationSender> _notifications = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();

    private AddCalendarEventParticipantsCommandHandler BuildSut()
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        _currentUser.SetupGet(x => x.UserId).Returns(UserId);
        _unitOfWork.Setup(u => u.ExecuteInTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<ONEVO.Application.Common.Models.Result<CalendarEventParticipantsResult>>>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<ONEVO.Application.Common.Models.Result<CalendarEventParticipantsResult>>>, CancellationToken>((op, ct) => op(ct));
        _employees.Setup(e => e.GetDefaultForUserAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = Guid.NewGuid(), TenantId = TenantId, UserId = UserId, FirstName = "Ada", LastName = "Owner" });
        return new AddCalendarEventParticipantsCommandHandler(
            _currentUser.Object, _events.Object, _employees.Object, _notifications.Object, _unitOfWork.Object);
    }

    private static CalendarEvent MakeEvent() => new()
    {
        Id = EventId, TenantId = TenantId, Title = "Sprint planning", CreatedById = UserId,
        StartDate = DateTimeOffset.UtcNow.AddHours(1), EndDate = DateTimeOffset.UtcNow.AddHours(2),
        MeetingLink = "https://us05web.zoom.us/j/123"
    };

    [Fact]
    public async Task Handle_NotOrganizer_ReturnsForbidden()
    {
        var evt = MakeEvent();
        evt.CreatedById = Guid.NewGuid();
        _events.Setup(e => e.GetTrackedByIdForTenantAsync(TenantId, EventId, It.IsAny<CancellationToken>())).ReturnsAsync(evt);

        var result = await BuildSut().Handle(new AddCalendarEventParticipantsCommand(EventId, [NewEmployeeId]), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Handle_EventNotFound_ReturnsNotFound()
    {
        _events.Setup(e => e.GetTrackedByIdForTenantAsync(TenantId, EventId, It.IsAny<CancellationToken>())).ReturnsAsync((CalendarEvent?)null);

        var result = await BuildSut().Handle(new AddCalendarEventParticipantsCommand(EventId, [NewEmployeeId]), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Handle_NewEmployeeId_AddsParticipantAndNotifiesWithTheEventsMeetingLink()
    {
        var evt = MakeEvent();
        _events.Setup(e => e.GetTrackedByIdForTenantAsync(TenantId, EventId, It.IsAny<CancellationToken>())).ReturnsAsync(evt);
        _events.Setup(e => e.GetParticipantsForEventsAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<CalendarEventParticipant>> { [EventId] = [] });
        _employees.Setup(e => e.GetByIdAsync(TenantId, NewEmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = NewEmployeeId, TenantId = TenantId, UserId = Guid.NewGuid(), FirstName = "Kiru", LastName = "B" });

        var result = await BuildSut().Handle(new AddCalendarEventParticipantsCommand(EventId, [NewEmployeeId]), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Participants.Should().ContainSingle(p => p.EmployeeId == NewEmployeeId && p.EmployeeName == "Kiru B");
        _events.Verify(e => e.AddParticipantsAsync(
            It.Is<IReadOnlyList<CalendarEventParticipant>>(list => list.Count == 1 && list[0].EmployeeId == NewEmployeeId),
            It.IsAny<CancellationToken>()), Times.Once);
        _notifications.Verify(n => n.NotifyParticipantsAddedAsync(
            TenantId, evt.Title, evt.StartDate, evt.Location,
            It.Is<IReadOnlyList<Guid>>(ids => ids.Single() == NewEmployeeId),
            "Ada Owner", It.IsAny<CancellationToken>(), evt.MeetingLink), Times.Once);
    }

    [Fact]
    public async Task Handle_EmployeeAlreadyParticipating_SkipsDuplicateAndDoesNotNotifyAgain()
    {
        var evt = MakeEvent();
        _events.Setup(e => e.GetTrackedByIdForTenantAsync(TenantId, EventId, It.IsAny<CancellationToken>())).ReturnsAsync(evt);
        _events.Setup(e => e.GetParticipantsForEventsAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<CalendarEventParticipant>>
            {
                [EventId] = [new CalendarEventParticipant { EmployeeId = AlreadyParticipatingEmployeeId, EventId = EventId, ResponseStatus = CalendarEventParticipantStatuses.Accepted }]
            });
        _employees.Setup(e => e.GetByIdAsync(TenantId, AlreadyParticipatingEmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = AlreadyParticipatingEmployeeId, TenantId = TenantId, UserId = Guid.NewGuid(), FirstName = "Saif", LastName = "A" });

        var result = await BuildSut().Handle(new AddCalendarEventParticipantsCommand(EventId, [AlreadyParticipatingEmployeeId]), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Participants.Should().ContainSingle();
        _events.Verify(e => e.AddParticipantsAsync(It.IsAny<IReadOnlyList<CalendarEventParticipant>>(), It.IsAny<CancellationToken>()), Times.Never);
        _notifications.Verify(n => n.NotifyParticipantsAddedAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<string?>(),
            It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<string?>()), Times.Never);
    }
}
