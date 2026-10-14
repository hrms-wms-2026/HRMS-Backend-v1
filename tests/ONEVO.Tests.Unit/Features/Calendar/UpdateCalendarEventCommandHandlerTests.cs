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
        _events.Setup(x => x.GetGuestsForEventsAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<CalendarEventGuest>>());
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
            TenantId, "New Title", It.IsAny<DateTimeOffset>(), It.IsAny<string?>(), It.IsAny<string?>(), It.Is<IReadOnlyList<CalendarParticipantRecipient>>(r => r.Count == 1 && r[0].EmployeeId == participantId), It.IsAny<IReadOnlyList<CalendarGuestRecipient>>(),
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
    public async Task Handle_InvalidMeetingLink_ReturnsFailure()
    {
        var sut = BuildSut();

        var result = await sut.Handle(
            new UpdateCalendarEventCommand(EventId, "Title", null, Start, Start.AddHours(1), false, null, "not a url", null, CalendarRecurrences.None),
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

    [Fact]
    public async Task Handle_NotifiesBeforeTheSave_SoTheOutboxRowsCommitWithTheChange()
    {
        var sut = BuildSut();
        var existing = new CalendarEvent { Id = EventId, TenantId = TenantId, CreatedById = UserId, Title = "Old", StartDate = Start, EndDate = Start.AddMinutes(30) };
        _events.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, EventId, It.IsAny<CancellationToken>())).ReturnsAsync(existing);
        _events.Setup(x => x.GetParticipantsForEventsAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<CalendarEventParticipant>>
            {
                [EventId] = [new CalendarEventParticipant { Id = Guid.NewGuid(), TenantId = TenantId, EventId = EventId, EmployeeId = Guid.NewGuid() }]
            });
        var calls = new List<string>();
        _notifications.Setup(x => x.NotifyEventUpdatedAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<IReadOnlyList<CalendarParticipantRecipient>>(), It.IsAny<IReadOnlyList<CalendarGuestRecipient>>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("notify")).Returns(Task.CompletedTask);
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).Callback(() => calls.Add("save")).ReturnsAsync(1);

        await sut.Handle(
            new UpdateCalendarEventCommand(EventId, "New Title", null, Start, Start.AddHours(1), false, null, null, null, CalendarRecurrences.None),
            CancellationToken.None);

        Assert.Equal(new[] { "notify", "save" }, calls);
    }

    [Fact]
    public async Task Handle_IncrementsTheRevision_AndKeysParticipantAndGuestEmailsOnIt()
    {
        var sut = BuildSut();
        var existing = new CalendarEvent { Id = EventId, TenantId = TenantId, CreatedById = UserId, Title = "Old", StartDate = Start, EndDate = Start.AddMinutes(30), Revision = 4 };
        _events.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, EventId, It.IsAny<CancellationToken>())).ReturnsAsync(existing);
        var participantRow = new CalendarEventParticipant { Id = Guid.NewGuid(), TenantId = TenantId, EventId = EventId, EmployeeId = Guid.NewGuid() };
        var guestRow = new CalendarEventGuest { Id = Guid.NewGuid(), TenantId = TenantId, EventId = EventId, Email = "vendor@example.com" };
        _events.Setup(x => x.GetParticipantsForEventsAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<CalendarEventParticipant>> { [EventId] = [participantRow] });
        _events.Setup(x => x.GetGuestsForEventsAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<CalendarEventGuest>> { [EventId] = [guestRow] });

        await sut.Handle(
            new UpdateCalendarEventCommand(EventId, "New Title", null, Start, Start.AddHours(1), false, "Room 2", "https://zoom.us/j/9", null, CalendarRecurrences.None),
            CancellationToken.None);

        Assert.Equal(5, existing.Revision);
        _notifications.Verify(x => x.NotifyEventUpdatedAsync(
            TenantId, "New Title", Start, "Room 2", "https://zoom.us/j/9",
            It.Is<IReadOnlyList<CalendarParticipantRecipient>>(r => r.Single().EmailKey == $"update:{EventId}:5:{participantRow.Id}"),
            It.Is<IReadOnlyList<CalendarGuestRecipient>>(g => g.Single().Email == "vendor@example.com" && g.Single().EmailKey == $"update:{EventId}:5:{guestRow.Id}"),
            "Ada Owner", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_NoParticipantsOrGuests_NotifiesNoOne_AndStillSaves()
    {
        var sut = BuildSut();
        var existing = new CalendarEvent { Id = EventId, TenantId = TenantId, CreatedById = UserId, Title = "Old", StartDate = Start, EndDate = Start.AddMinutes(30) };
        _events.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, EventId, It.IsAny<CancellationToken>())).ReturnsAsync(existing);

        var result = await sut.Handle(
            new UpdateCalendarEventCommand(EventId, "New Title", null, Start, Start.AddHours(1), false, null, null, null, CalendarRecurrences.None),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        _notifications.VerifyNoOtherCalls();
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
