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
            TenantId, "New Title", It.Is<IReadOnlyList<Guid>>(ids => ids.Count == 1 && ids[0] == participantId),
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

    [Fact]
    public async Task Handle_ThisAndFollowing_CopiesTheParticipantsToTheNewSeriesWithTheirRsvpReset()
    {
        var sut = BuildSut();
        var master = MakeMaster();
        var accepted = new CalendarEventParticipant
        {
            Id = Guid.NewGuid(), TenantId = TenantId, EventId = MasterId, EmployeeId = Guid.NewGuid(),
            ResponseStatus = CalendarEventParticipantStatuses.Accepted, ResponseReason = "ok",
        };
        var rejected = new CalendarEventParticipant
        {
            Id = Guid.NewGuid(), TenantId = TenantId, EventId = MasterId, EmployeeId = Guid.NewGuid(),
            ResponseStatus = CalendarEventParticipantStatuses.Rejected,
        };
        _events.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, MasterId, It.IsAny<CancellationToken>())).ReturnsAsync(master);
        _events.Setup(x => x.GetChildrenForMasterAsync(TenantId, MasterId, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _events.Setup(x => x.GetParticipantsForEventsAsync(TenantId, It.Is<IReadOnlyList<Guid>>(ids => ids.Contains(MasterId)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<CalendarEventParticipant>> { [MasterId] = [accepted, rejected] });
        CalendarEvent? newMaster = null;
        _events.Setup(x => x.AddAsync(It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>()))
            .Callback((CalendarEvent e, CancellationToken _) => newMaster = e).Returns(Task.CompletedTask);
        IReadOnlyList<CalendarEventParticipant>? copied = null;
        _events.Setup(x => x.AddParticipantsAsync(It.IsAny<IReadOnlyList<CalendarEventParticipant>>(), It.IsAny<CancellationToken>()))
            .Callback((IReadOnlyList<CalendarEventParticipant> p, CancellationToken _) => copied = p).Returns(Task.CompletedTask);

        var result = await sut.Handle(MakeCommand(RecurrenceEditScope.ThisAndFollowing), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(copied);
        Assert.Equal(new[] { accepted.EmployeeId, rejected.EmployeeId }, copied!.Select(p => p.EmployeeId));
        Assert.All(copied, p =>
        {
            Assert.Equal(newMaster!.Id, p.EventId);
            Assert.Equal(TenantId, p.TenantId);
            Assert.Equal(CalendarEventParticipantStatuses.Pending, p.ResponseStatus);
            Assert.Null(p.ResponseReason);
            Assert.NotEqual(Guid.Empty, p.Id);
        });
        Assert.Equal(CalendarEventParticipantStatuses.Accepted, accepted.ResponseStatus); // the earlier series keeps its answers
    }

    [Fact]
    public async Task Handle_ThisAndFollowing_SameTimes_KeepsEveryAnswer()
    {
        var sut = BuildSut();
        var master = MakeMaster();
        var accepted = new CalendarEventParticipant
        {
            Id = Guid.NewGuid(), TenantId = TenantId, EventId = MasterId, EmployeeId = Guid.NewGuid(),
            ResponseStatus = CalendarEventParticipantStatuses.Accepted, ResponseReason = "ok",
        };
        _events.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, MasterId, It.IsAny<CancellationToken>())).ReturnsAsync(master);
        _events.Setup(x => x.GetChildrenForMasterAsync(TenantId, MasterId, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _events.Setup(x => x.GetParticipantsForEventsAsync(TenantId, It.Is<IReadOnlyList<Guid>>(ids => ids.Contains(MasterId)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<CalendarEventParticipant>> { [MasterId] = [accepted] });
        IReadOnlyList<CalendarEventParticipant>? copied = null;
        _events.Setup(x => x.AddParticipantsAsync(It.IsAny<IReadOnlyList<CalendarEventParticipant>>(), It.IsAny<CancellationToken>()))
            .Callback((IReadOnlyList<CalendarEventParticipant> p, CancellationToken _) => copied = p).Returns(Task.CompletedTask);

        // Only the title and room change: the occurrence keeps its time and its 30-minute length.
        var result = await sut.Handle(
            MakeCommand(RecurrenceEditScope.ThisAndFollowing) with { StartDate = OriginalStart, EndDate = OriginalStart.AddMinutes(30) },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var only = Assert.Single(copied!);
        Assert.Equal(CalendarEventParticipantStatuses.Accepted, only.ResponseStatus);
        Assert.Equal("ok", only.ResponseReason);
    }

    [Fact]
    public async Task Handle_ThisAndFollowing_CopiesTheGuestsToTheNewSeries()
    {
        var sut = BuildSut();
        var master = MakeMaster();
        _events.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, MasterId, It.IsAny<CancellationToken>())).ReturnsAsync(master);
        _events.Setup(x => x.GetChildrenForMasterAsync(TenantId, MasterId, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _events.Setup(x => x.GetGuestsForEventsAsync(TenantId, It.Is<IReadOnlyList<Guid>>(ids => ids.Contains(MasterId)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<CalendarEventGuest>>
            {
                [MasterId] = [new CalendarEventGuest { Id = Guid.NewGuid(), TenantId = TenantId, EventId = MasterId, Email = "client@example.com" }]
            });
        CalendarEvent? newMaster = null;
        _events.Setup(x => x.AddAsync(It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>()))
            .Callback((CalendarEvent e, CancellationToken _) => newMaster = e).Returns(Task.CompletedTask);
        IReadOnlyList<CalendarEventGuest>? copied = null;
        _events.Setup(x => x.AddGuestsAsync(It.IsAny<IReadOnlyList<CalendarEventGuest>>(), It.IsAny<CancellationToken>()))
            .Callback((IReadOnlyList<CalendarEventGuest> g, CancellationToken _) => copied = g).Returns(Task.CompletedTask);

        var result = await sut.Handle(MakeCommand(RecurrenceEditScope.ThisAndFollowing), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var guest = Assert.Single(copied!);
        Assert.Equal("client@example.com", guest.Email);
        Assert.Equal(newMaster!.Id, guest.EventId);
        Assert.Equal(TenantId, guest.TenantId);
    }

    [Fact]
    public async Task Handle_ThisEventOnly_EmailsTheSeriesAttendeesAboutThatOccurrence()
    {
        var sut = BuildSut();
        var master = MakeMaster();
        var participant = new CalendarEventParticipant { Id = Guid.NewGuid(), TenantId = TenantId, EventId = MasterId, EmployeeId = Guid.NewGuid() };
        _events.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, MasterId, It.IsAny<CancellationToken>())).ReturnsAsync(master);
        _events.Setup(x => x.GetParticipantsForEventsAsync(TenantId, It.Is<IReadOnlyList<Guid>>(ids => ids.Contains(MasterId)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<CalendarEventParticipant>> { [MasterId] = [participant] });
        _events.Setup(x => x.GetGuestsForEventsAsync(TenantId, It.Is<IReadOnlyList<Guid>>(ids => ids.Contains(MasterId)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<CalendarEventGuest>>
            {
                [MasterId] = [new CalendarEventGuest { Id = Guid.NewGuid(), TenantId = TenantId, EventId = MasterId, Email = "client@example.com" }]
            });

        var result = await sut.Handle(MakeCommand(RecurrenceEditScope.ThisEventOnly), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _notifications.Verify(x => x.NotifyEventUpdatedAsync(TenantId, "New Title",
            It.Is<IReadOnlyList<Guid>>(ids => ids.SequenceEqual(new[] { participant.EmployeeId })), "Ada Owner", It.IsAny<CancellationToken>()), Times.Once);
        _notifications.Verify(x => x.EmailEventChangedAsync(TenantId, CalendarEventEmailKinds.Updated,
            It.Is<CalendarEventIcs>(ics => ics.EventId == MasterId && ics.RecurrenceId == OriginalStart),
            "New Title", OriginalStart.AddHours(1), "Room 2", null,
            It.Is<IReadOnlyList<Guid>>(ids => ids.SequenceEqual(new[] { participant.EmployeeId })),
            It.Is<IReadOnlyList<string>>(emails => emails.SequenceEqual(new[] { "client@example.com" })),
            "Ada Owner", It.IsAny<CancellationToken>()), Times.Once);
        _notifications.Verify(x => x.QueueIncidentEvaluationAsync(TenantId,
            It.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(participant.EmployeeId)), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ThisAndFollowing_EmailsTheNewSeriesAttendees()
    {
        var sut = BuildSut();
        var master = MakeMaster();
        var participant = new CalendarEventParticipant { Id = Guid.NewGuid(), TenantId = TenantId, EventId = MasterId, EmployeeId = Guid.NewGuid() };
        _events.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, MasterId, It.IsAny<CancellationToken>())).ReturnsAsync(master);
        _events.Setup(x => x.GetChildrenForMasterAsync(TenantId, MasterId, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _events.Setup(x => x.GetParticipantsForEventsAsync(TenantId, It.Is<IReadOnlyList<Guid>>(ids => ids.Contains(MasterId)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<CalendarEventParticipant>> { [MasterId] = [participant] });
        CalendarEvent? newMaster = null;
        _events.Setup(x => x.AddAsync(It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>()))
            .Callback((CalendarEvent e, CancellationToken _) => newMaster = e).Returns(Task.CompletedTask);

        var result = await sut.Handle(MakeCommand(RecurrenceEditScope.ThisAndFollowing), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _notifications.Verify(x => x.EmailEventChangedAsync(TenantId, CalendarEventEmailKinds.Updated,
            It.Is<CalendarEventIcs>(ics => ics.EventId == newMaster!.Id && ics.RecurrenceRule == "FREQ=WEEKLY"),
            "New Title", OriginalStart.AddHours(1), "Room 2", null,
            It.Is<IReadOnlyList<Guid>>(ids => ids.SequenceEqual(new[] { participant.EmployeeId })),
            It.IsAny<IReadOnlyList<string>>(), "Ada Owner", It.IsAny<CancellationToken>()), Times.Once);
        _notifications.Verify(x => x.QueueIncidentEvaluationAsync(TenantId,
            It.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(participant.EmployeeId)), It.IsAny<CancellationToken>()), Times.Once);
    }
}
