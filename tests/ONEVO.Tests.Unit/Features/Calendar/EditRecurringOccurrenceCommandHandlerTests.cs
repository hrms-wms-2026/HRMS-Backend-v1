using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Commands.EditRecurringOccurrence;
using ONEVO.Application.Features.Calendar.DTOs.Responses;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class EditRecurringOccurrenceCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid MasterId = Guid.NewGuid();
    private static readonly DateTimeOffset SeriesStart = new(2026, 9, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset OriginalStart = new(2026, 9, 8, 9, 0, 0, TimeSpan.Zero);

    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<ICalendarEventRepository> _events = new();
    private readonly Mock<ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository> _employees = new();
    private readonly Mock<ICalendarNotificationSender> _notifications = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();

    private EditRecurringOccurrenceCommandHandler BuildSut()
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
        _events.Setup(x => x.GetChildrenForMasterAsync(TenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _events.Setup(x => x.IncrementRevisionAsync(TenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(1);
        return new EditRecurringOccurrenceCommandHandler(_currentUser.Object, _events.Object, _employees.Object, _notifications.Object, _unitOfWork.Object);
    }

    private static CalendarEvent MakeMaster() => new()
    {
        Id = MasterId, TenantId = TenantId, CreatedById = UserId, Title = "Standup",
        StartDate = SeriesStart, EndDate = SeriesStart.AddMinutes(30),
        Recurrence = CalendarRecurrences.Weekly, RecurrenceRule = "FREQ=WEEKLY",
        SourceType = CalendarEventSourceTypes.Manual
    };

    private EditRecurringOccurrenceCommand MakeCommand(RecurrenceEditScope scope) => new(
        MasterId, OriginalStart, scope, "New Title", "New desc",
        OriginalStart.AddHours(1), OriginalStart.AddHours(1).AddMinutes(30), false, "Room 2", null, "#ff0000");

    [Fact]
    public async Task Handle_InvalidMeetingLink_ReturnsFailure()
    {
        var sut = BuildSut();

        var result = await sut.Handle(
            MakeCommand(RecurrenceEditScope.ThisEventOnly) with { MeetingLink = "not a url" },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task Handle_NotOwner_ReturnsForbidden()
    {
        var sut = BuildSut();
        var master = MakeMaster();
        master.CreatedById = Guid.NewGuid();
        _events.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, MasterId, It.IsAny<CancellationToken>())).ReturnsAsync(master);

        var result = await sut.Handle(MakeCommand(RecurrenceEditScope.AllEvents), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_NotARecurringMaster_ReturnsFailure()
    {
        var sut = BuildSut();
        var notAMaster = MakeMaster();
        notAMaster.Recurrence = CalendarRecurrences.None;
        notAMaster.RecurrenceRule = null;
        _events.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, MasterId, It.IsAny<CancellationToken>())).ReturnsAsync(notAMaster);

        var result = await sut.Handle(MakeCommand(RecurrenceEditScope.AllEvents), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task Handle_AllEvents_UpdatesMasterDirectly()
    {
        var sut = BuildSut();
        var master = MakeMaster();
        _events.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, MasterId, It.IsAny<CancellationToken>())).ReturnsAsync(master);

        var result = await sut.Handle(MakeCommand(RecurrenceEditScope.AllEvents), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("New Title", master.Title);
        _events.Verify(x => x.Update(master), Times.Once);
        _events.Verify(x => x.AddAsync(It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_AllEvents_DoesNotChangeTimezone()
    {
        var sut = BuildSut();
        var master = MakeMaster();
        master.Timezone = "Asia/Colombo";
        _events.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, MasterId, It.IsAny<CancellationToken>())).ReturnsAsync(master);

        await sut.Handle(MakeCommand(RecurrenceEditScope.AllEvents), CancellationToken.None);

        Assert.Equal("Asia/Colombo", master.Timezone);
    }

    [Fact]
    public async Task Handle_AllEventsWithParticipants_NotifiesThem()
    {
        var sut = BuildSut();
        var master = MakeMaster();
        var participantId = Guid.NewGuid();
        _events.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, MasterId, It.IsAny<CancellationToken>())).ReturnsAsync(master);
        _events.Setup(x => x.GetParticipantsForEventsAsync(TenantId, It.Is<IReadOnlyList<Guid>>(ids => ids.Contains(MasterId)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<CalendarEventParticipant>>
            {
                [MasterId] = [new CalendarEventParticipant { Id = Guid.NewGuid(), TenantId = TenantId, EventId = MasterId, EmployeeId = participantId, ResponseStatus = CalendarEventParticipantStatuses.Accepted }]
            });

        var result = await sut.Handle(MakeCommand(RecurrenceEditScope.AllEvents), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _notifications.Verify(x => x.NotifyEventUpdatedAsync(
            TenantId, "New Title", It.IsAny<DateTimeOffset>(), It.IsAny<string?>(), It.IsAny<string?>(), It.Is<IReadOnlyList<CalendarParticipantRecipient>>(r => r.Count == 1 && r[0].EmployeeId == participantId), It.IsAny<IReadOnlyList<CalendarGuestRecipient>>(),
            "Ada Owner", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ThisEventOnly_NoExistingChild_CreatesDetachedRow()
    {
        var sut = BuildSut();
        var master = MakeMaster();
        _events.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, MasterId, It.IsAny<CancellationToken>())).ReturnsAsync(master);
        _events.Setup(x => x.GetTrackedChildByOriginalStartAsync(TenantId, MasterId, OriginalStart, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CalendarEvent?)null);

        var result = await sut.Handle(MakeCommand(RecurrenceEditScope.ThisEventOnly), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _events.Verify(x => x.AddAsync(It.Is<CalendarEvent>(e =>
            e.RecurrenceParentId == MasterId && e.RecurrenceOriginalStart == OriginalStart && e.Title == "New Title"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ThisEventOnly_NewDetachedChild_InheritsMasterTimezone()
    {
        var sut = BuildSut();
        var master = MakeMaster();
        master.Timezone = "Asia/Colombo";
        _events.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, MasterId, It.IsAny<CancellationToken>())).ReturnsAsync(master);
        _events.Setup(x => x.GetTrackedChildByOriginalStartAsync(TenantId, MasterId, OriginalStart, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CalendarEvent?)null);

        await sut.Handle(MakeCommand(RecurrenceEditScope.ThisEventOnly), CancellationToken.None);

        _events.Verify(x => x.AddAsync(It.Is<CalendarEvent>(e => e.Timezone == "Asia/Colombo"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ThisEventOnly_ExistingChild_UpdatesIt()
    {
        var sut = BuildSut();
        var master = MakeMaster();
        var existingChild = new CalendarEvent
        {
            Id = Guid.NewGuid(), TenantId = TenantId, RecurrenceParentId = MasterId, RecurrenceOriginalStart = OriginalStart,
            Title = "Old", StartDate = OriginalStart, EndDate = OriginalStart.AddMinutes(30)
        };
        _events.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, MasterId, It.IsAny<CancellationToken>())).ReturnsAsync(master);
        _events.Setup(x => x.GetTrackedChildByOriginalStartAsync(TenantId, MasterId, OriginalStart, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingChild);

        var result = await sut.Handle(MakeCommand(RecurrenceEditScope.ThisEventOnly), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("New Title", existingChild.Title);
        _events.Verify(x => x.Update(existingChild), Times.Once);
        _events.Verify(x => x.AddAsync(It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ThisAndFollowing_SplitsSeriesAndReparentsLaterChildren()
    {
        var sut = BuildSut();
        var master = MakeMaster();
        var earlierChild = new CalendarEvent
        {
            Id = Guid.NewGuid(), TenantId = TenantId, RecurrenceParentId = MasterId,
            RecurrenceOriginalStart = OriginalStart.AddDays(-7), Title = "Earlier override"
        };
        var laterChild = new CalendarEvent
        {
            Id = Guid.NewGuid(), TenantId = TenantId, RecurrenceParentId = MasterId,
            RecurrenceOriginalStart = OriginalStart.AddDays(7), Title = "Later override"
        };
        _events.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, MasterId, It.IsAny<CancellationToken>())).ReturnsAsync(master);
        _events.Setup(x => x.GetChildrenForMasterAsync(TenantId, MasterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([earlierChild, laterChild]);
        _events.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, laterChild.Id, It.IsAny<CancellationToken>())).ReturnsAsync(laterChild);

        var result = await sut.Handle(MakeCommand(RecurrenceEditScope.ThisAndFollowing), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains("UNTIL=", master.RecurrenceRule);
        _events.Verify(x => x.AddAsync(It.Is<CalendarEvent>(e =>
            e.RecurrenceParentId == null && e.Recurrence == CalendarRecurrences.Weekly && e.Title == "New Title"),
            It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(MasterId, earlierChild.RecurrenceParentId); // untouched - before the split point
        _events.Verify(x => x.Update(laterChild), Times.Once); // re-parented - on/after the split point
    }

    private static CalendarEvent RecurringMaster(Guid masterId) => new()
    {
        Id = masterId, TenantId = TenantId, CreatedById = UserId, Title = "Standup",
        StartDate = new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero), EndDate = new DateTimeOffset(2026, 10, 1, 9, 30, 0, TimeSpan.Zero),
        Recurrence = CalendarRecurrences.Weekly, RecurrenceRule = "FREQ=WEEKLY;BYDAY=TH"
    };

    private void SetupAttendees(Guid masterId, CalendarEventParticipant participant, CalendarEventGuest guest)
    {
        _events.Setup(x => x.GetParticipantsForEventsAsync(TenantId, It.Is<IReadOnlyList<Guid>>(ids => ids.Contains(masterId)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<CalendarEventParticipant>> { [masterId] = [participant] });
        _events.Setup(x => x.GetGuestsForEventsAsync(TenantId, It.Is<IReadOnlyList<Guid>>(ids => ids.Contains(masterId)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<CalendarEventGuest>> { [masterId] = [guest] });
    }

    private static EditRecurringOccurrenceCommand Command(Guid masterId, RecurrenceEditScope scope, DateTimeOffset originalStart) => new(
        masterId, originalStart, scope, "New Title", null, originalStart, originalStart.AddMinutes(30), false, null, null, null);

    [Fact]
    public async Task Handle_ThisAndFollowing_CopiesParticipantsAndGuestsOntoTheNewSeries()
    {
        var sut = BuildSut();
        var masterId = Guid.NewGuid();
        var master = RecurringMaster(masterId);
        _events.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, masterId, It.IsAny<CancellationToken>())).ReturnsAsync(master);
        var participant = new CalendarEventParticipant { Id = Guid.NewGuid(), TenantId = TenantId, EventId = masterId, EmployeeId = Guid.NewGuid(), ResponseStatus = CalendarEventParticipantStatuses.Accepted };
        var guest = new CalendarEventGuest { Id = Guid.NewGuid(), TenantId = TenantId, EventId = masterId, Email = "vendor@example.com" };
        SetupAttendees(masterId, participant, guest);
        CalendarEvent? newMaster = null;
        _events.Setup(x => x.AddAsync(It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>()))
            .Callback<CalendarEvent, CancellationToken>((e, _) => newMaster = e).Returns(Task.CompletedTask);

        var result = await sut.Handle(Command(masterId, RecurrenceEditScope.ThisAndFollowing, master.StartDate.AddDays(14)), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _events.Verify(x => x.AddParticipantsAsync(
            It.Is<IReadOnlyList<CalendarEventParticipant>>(rows => rows.Count == 1 && rows[0].EventId == newMaster!.Id
                && rows[0].EmployeeId == participant.EmployeeId && rows[0].Id != participant.Id
                && rows[0].ResponseStatus == CalendarEventParticipantStatuses.Accepted),
            It.IsAny<CancellationToken>()), Times.Once);
        _events.Verify(x => x.AddGuestsAsync(
            It.Is<IReadOnlyList<CalendarEventGuest>>(rows => rows.Count == 1 && rows[0].EventId == newMaster!.Id
                && rows[0].Email == "vendor@example.com" && rows[0].Id != guest.Id),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ThisAndFollowing_NotifiesTheCopiedAttendeesBeforeSaving()
    {
        var sut = BuildSut();
        var masterId = Guid.NewGuid();
        var master = RecurringMaster(masterId);
        _events.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, masterId, It.IsAny<CancellationToken>())).ReturnsAsync(master);
        SetupAttendees(masterId,
            new CalendarEventParticipant { Id = Guid.NewGuid(), TenantId = TenantId, EventId = masterId, EmployeeId = Guid.NewGuid() },
            new CalendarEventGuest { Id = Guid.NewGuid(), TenantId = TenantId, EventId = masterId, Email = "vendor@example.com" });
        var calls = new List<string>();
        _notifications.Setup(x => x.NotifyEventUpdatedAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<IReadOnlyList<CalendarParticipantRecipient>>(), It.IsAny<IReadOnlyList<CalendarGuestRecipient>>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("notify")).Returns(Task.CompletedTask);
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).Callback(() => calls.Add("save")).ReturnsAsync(1);

        await sut.Handle(Command(masterId, RecurrenceEditScope.ThisAndFollowing, master.StartDate.AddDays(14)), CancellationToken.None);

        Assert.Equal(new[] { "notify", "save" }, calls);
    }

    [Fact]
    public async Task Handle_ThisEventOnly_NotifiesTheSeriesAttendees_KeyedOnTheOccurrenceRow()
    {
        var sut = BuildSut();
        var masterId = Guid.NewGuid();
        var master = RecurringMaster(masterId);
        _events.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, masterId, It.IsAny<CancellationToken>())).ReturnsAsync(master);
        var participant = new CalendarEventParticipant { Id = Guid.NewGuid(), TenantId = TenantId, EventId = masterId, EmployeeId = Guid.NewGuid() };
        var guest = new CalendarEventGuest { Id = Guid.NewGuid(), TenantId = TenantId, EventId = masterId, Email = "vendor@example.com" };
        SetupAttendees(masterId, participant, guest);
        var originalStart = master.StartDate.AddDays(7);
        _events.Setup(x => x.GetTrackedChildByOriginalStartAsync(TenantId, masterId, originalStart, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CalendarEvent?)null);
        CalendarEvent? child = null;
        _events.Setup(x => x.AddAsync(It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>()))
            .Callback<CalendarEvent, CancellationToken>((e, _) => child = e).Returns(Task.CompletedTask);
        var calls = new List<string>();
        _notifications.Setup(x => x.NotifyEventUpdatedAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<IReadOnlyList<CalendarParticipantRecipient>>(), It.IsAny<IReadOnlyList<CalendarGuestRecipient>>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("notify")).Returns(Task.CompletedTask);
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).Callback(() => calls.Add("save")).ReturnsAsync(1);

        await sut.Handle(Command(masterId, RecurrenceEditScope.ThisEventOnly, originalStart), CancellationToken.None);

        Assert.Equal(new[] { "notify", "save" }, calls);
        _notifications.Verify(x => x.NotifyEventUpdatedAsync(
            TenantId, "New Title", originalStart, It.IsAny<string?>(), It.IsAny<string?>(),
            It.Is<IReadOnlyList<CalendarParticipantRecipient>>(r => r.Single().EmailKey == $"update:{child!.Id}:1:{participant.Id}"),
            It.Is<IReadOnlyList<CalendarGuestRecipient>>(g => g.Single().EmailKey == $"update:{child!.Id}:1:{guest.Id}"),
            "Ada Owner", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_AllEvents_NotifiesBeforeSaving()
    {
        var sut = BuildSut();
        var masterId = Guid.NewGuid();
        var master = RecurringMaster(masterId);
        _events.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, masterId, It.IsAny<CancellationToken>())).ReturnsAsync(master);
        SetupAttendees(masterId,
            new CalendarEventParticipant { Id = Guid.NewGuid(), TenantId = TenantId, EventId = masterId, EmployeeId = Guid.NewGuid() },
            new CalendarEventGuest { Id = Guid.NewGuid(), TenantId = TenantId, EventId = masterId, Email = "vendor@example.com" });
        var calls = new List<string>();
        _notifications.Setup(x => x.NotifyEventUpdatedAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<IReadOnlyList<CalendarParticipantRecipient>>(), It.IsAny<IReadOnlyList<CalendarGuestRecipient>>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("notify")).Returns(Task.CompletedTask);
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).Callback(() => calls.Add("save")).ReturnsAsync(1);

        await sut.Handle(Command(masterId, RecurrenceEditScope.AllEvents, master.StartDate), CancellationToken.None);

        Assert.Equal(new[] { "notify", "save" }, calls);
        Assert.Equal(1, master.Revision);
    }
}
