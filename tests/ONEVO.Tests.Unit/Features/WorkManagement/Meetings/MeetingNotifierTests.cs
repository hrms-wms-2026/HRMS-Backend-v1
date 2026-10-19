using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Application.Features.WorkManagement.Meetings.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Meetings.Services;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Meetings;

/// <summary>Plan C Task 1: every Phase 2 attendee email is keyed by its row (CalendarEmailKeys) and carries the series
/// .ics - UID = series id, RRULE for the series, RECURRENCE-ID for one occurrence.</summary>
public sealed class MeetingNotifierTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 6, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Start = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);

    private readonly Mock<ICalendarNotificationSender> _sender = new();
    private readonly Mock<IMeetingRepository> _meetings = new();
    private readonly Mock<IMeetingLinkRepository> _links = new();
    private readonly Mock<IEmployeeRepository> _employees = new();

    private readonly MeetingSeries _series;
    private readonly CalendarEventParticipant _organizerRow, _attendeeRow;
    private readonly CalendarEventGuest _guestRow;

    public MeetingNotifierTests()
    {
        var organizer = Guid.NewGuid();
        _series = new MeetingSeries
        {
            Id = Guid.NewGuid(), TenantId = Tenant, OrganizerEmployeeId = organizer, Title = "Weekly sync", DtStart = Start,
            DurationMinutes = 30, Timezone = "UTC", RecurrenceRule = "FREQ=WEEKLY", Revision = 3, Location = "Room 4",
        };
        _organizerRow = new CalendarEventParticipant { Id = Guid.NewGuid(), TenantId = Tenant, MeetingSeriesId = _series.Id, EmployeeId = organizer };
        _attendeeRow = new CalendarEventParticipant { Id = Guid.NewGuid(), TenantId = Tenant, MeetingSeriesId = _series.Id, EmployeeId = Guid.NewGuid() };
        _guestRow = new CalendarEventGuest { Id = Guid.NewGuid(), TenantId = Tenant, MeetingSeriesId = _series.Id, Email = "client@example.com" };
        _meetings.Setup(m => m.ListSeriesParticipantsAsync(Tenant, _series.Id, It.IsAny<CancellationToken>())).ReturnsAsync([_organizerRow, _attendeeRow]);
        _meetings.Setup(m => m.ListSeriesGuestsAsync(Tenant, _series.Id, It.IsAny<CancellationToken>())).ReturnsAsync([_guestRow]);
        _meetings.Setup(m => m.ListOverridesAsync(Tenant, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _employees.Setup(e => e.ListByIdsAsync(Tenant, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, Employee> { [organizer] = new() { Id = organizer, FirstName = "Ada", LastName = "Lead" } });
    }

    private MeetingNotifier Sut() => new(_sender.Object, _meetings.Object, _links.Object, _employees.Object, new FixedClock(Now));

    private sealed class FixedClock(DateTimeOffset now) : ONEVO.Application.Common.ServiceInterfaces.IDateTimeProvider
    {
        public DateTimeOffset UtcNow => now;
        public DateOnly Today => DateOnly.FromDateTime(now.UtcDateTime);
    }

    private MeetingDetails Occurrence(int revision = 2) => new()
    {
        TaskId = Guid.NewGuid(), TenantId = Tenant, SeriesId = _series.Id, OriginalStart = Start.AddDays(7),
        StartAt = Start.AddDays(7).AddHours(1), EndAt = Start.AddDays(7).AddHours(1).AddMinutes(30), Revision = revision,
    };

    [Fact]
    public async Task InviteAsync_KeysEachRecipientByItsRow_AndSkipsTheOrganizer()
    {
        IReadOnlyList<CalendarParticipantRecipient>? participants = null;
        IReadOnlyList<CalendarGuestRecipient>? guests = null;
        CalendarEventIcs? ics = null;
        _sender.Setup(s => s.SendMeetingLinkAsync(Tenant, "Weekly sync", Start, "Room 4", It.IsAny<IReadOnlyList<CalendarParticipantRecipient>>(),
                It.IsAny<IReadOnlyList<CalendarGuestRecipient>>(), "Ada Lead", null, It.IsAny<CalendarEventIcs?>(), It.IsAny<CancellationToken>()))
            .Callback((Guid _, string _, DateTimeOffset _, string? _, IReadOnlyList<CalendarParticipantRecipient> p, IReadOnlyList<CalendarGuestRecipient> g,
                string _, string? _, CalendarEventIcs? i, CancellationToken _) => (participants, guests, ics) = (p, g, i))
            .Returns(Task.CompletedTask);

        await Sut().InviteAsync(_series, [_organizerRow, _attendeeRow], [_guestRow], CancellationToken.None);

        Assert.Equal([new CalendarParticipantRecipient(_attendeeRow.EmployeeId, CalendarEmailKeys.Invite(_attendeeRow.Id))], participants);
        Assert.Equal([new CalendarGuestRecipient("client@example.com", CalendarEmailKeys.Invite(_guestRow.Id))], guests);
        Assert.Equal((_series.Id, "FREQ=WEEKLY", (DateTimeOffset?)null), (ics!.EventId, ics.RecurrenceRule, ics.RecurrenceId));
        _sender.Verify(s => s.NotifyParticipantsAddedAsync(Tenant, "Weekly sync", Start, "Room 4",
            It.Is<IReadOnlyList<Guid>>(ids => ids.SequenceEqual(new[] { _attendeeRow.EmployeeId })), "Ada Lead", It.IsAny<CancellationToken>(),
            null, false, It.IsAny<CalendarEventIcs?>()), Times.Once);
    }

    [Fact]
    public async Task InviteAsync_IsHeldBack_WhileTheSeriesLinkIsBeingProvisioned()
    {
        _links.Setup(l => l.GetActiveForSeriesAsync(Tenant, _series.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CalendarEventMeeting { Status = CalendarEventMeetingStatuses.Provisioning, InvitesPending = true });

        await Sut().InviteAsync(_series, [_attendeeRow], [_guestRow], CancellationToken.None);

        _sender.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task OccurrenceUpdated_UsesTheTaskIdRevisionKey_AndARecurrenceId_WithTheJoinUrl()
    {
        var occurrence = Occurrence();
        _links.Setup(l => l.GetActiveForSeriesAsync(Tenant, _series.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CalendarEventMeeting { Status = CalendarEventMeetingStatuses.Active, JoinUrl = "https://teams.example/j/1" });

        await Sut().OccurrenceUpdatedAsync(_series, occurrence, "Weekly sync (moved)", CancellationToken.None);

        _sender.Verify(s => s.EmailEventChangedKeyedAsync(Tenant, CalendarEventEmailKinds.Updated,
            It.Is<CalendarEventIcs>(i => i.EventId == _series.Id && i.RecurrenceId == occurrence.OriginalStart && i.RecurrenceRule == null && i.EndUtc == occurrence.EndAt),
            "Weekly sync (moved)", occurrence.StartAt, null, "https://teams.example/j/1",
            It.Is<IReadOnlyList<CalendarParticipantRecipient>>(p => p.Single().EmailKey == CalendarEmailKeys.Update(occurrence.TaskId, 2, _attendeeRow.Id)),
            It.Is<IReadOnlyList<CalendarGuestRecipient>>(g => g.Single().EmailKey == CalendarEmailKeys.Update(occurrence.TaskId, 2, _guestRow.Id)),
            "Ada Lead", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SeriesUpdated_UsesTheSeriesRevisionKey_AndTheRule()
    {
        await Sut().SeriesUpdatedAsync(_series, CancellationToken.None);

        _sender.Verify(s => s.EmailEventChangedKeyedAsync(Tenant, CalendarEventEmailKinds.Updated,
            It.Is<CalendarEventIcs>(i => i.EventId == _series.Id && i.RecurrenceRule == "FREQ=WEEKLY" && i.RecurrenceId == null),
            "Weekly sync", Start, "Room 4", null,
            It.Is<IReadOnlyList<CalendarParticipantRecipient>>(p => p.Single().EmailKey == CalendarEmailKeys.Update(_series.Id, 3, _attendeeRow.Id)),
            It.IsAny<IReadOnlyList<CalendarGuestRecipient>>(), "Ada Lead", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OccurrenceCancelled_KeyIncludesTheOriginalStart_AndEffectiveAttendeesOnly()
    {
        var occurrence = Occurrence();
        var added = new MeetingOccurrenceAttendee { Id = Guid.NewGuid(), OccurrenceTaskId = occurrence.TaskId, AttendeeKind = MeetingAttendeeKinds.Employee,
            EmployeeId = Guid.NewGuid(), Change = MeetingAttendeeChanges.Add };
        var removedGuest = new MeetingOccurrenceAttendee { Id = Guid.NewGuid(), OccurrenceTaskId = occurrence.TaskId, AttendeeKind = MeetingAttendeeKinds.Guest,
            Email = "client@example.com", Change = MeetingAttendeeChanges.Remove };
        _meetings.Setup(m => m.ListOverridesAsync(Tenant, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync([added, removedGuest]);

        await Sut().OccurrenceCancelledAsync(_series, occurrence, "Weekly sync", CancellationToken.None);

        _sender.Verify(s => s.EmailEventChangedKeyedAsync(Tenant, CalendarEventEmailKinds.Cancelled, It.IsAny<CalendarEventIcs>(), "Weekly sync",
            occurrence.StartAt, null, null,
            It.Is<IReadOnlyList<CalendarParticipantRecipient>>(p => p.Select(r => r.EmailKey).SequenceEqual(new[]
            {
                CalendarEmailKeys.Cancel(_series.Id, occurrence.OriginalStart, _attendeeRow.Id),
                CalendarEmailKeys.Cancel(_series.Id, occurrence.OriginalStart, added.Id),
            })),
            It.Is<IReadOnlyList<CalendarGuestRecipient>>(g => g.Count == 0), "Ada Lead", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SeriesCancelled_UsesTheSeriesCancelKey()
    {
        await Sut().SeriesCancelledAsync(_series, CancellationToken.None);

        _sender.Verify(s => s.EmailEventChangedKeyedAsync(Tenant, CalendarEventEmailKinds.Cancelled, It.Is<CalendarEventIcs>(i => i.RecurrenceId == null),
            "Weekly sync", Start, "Room 4", null,
            It.Is<IReadOnlyList<CalendarParticipantRecipient>>(p => p.Single().EmailKey == CalendarEmailKeys.Cancel(_series.Id, null, _attendeeRow.Id)),
            It.Is<IReadOnlyList<CalendarGuestRecipient>>(g => g.Single().EmailKey == CalendarEmailKeys.Cancel(_series.Id, null, _guestRow.Id)),
            "Ada Lead", It.IsAny<CancellationToken>()), Times.Once);
        _sender.Verify(s => s.NotifyEventCancelledAsync(Tenant, "Weekly sync",
            It.Is<IReadOnlyList<Guid>>(ids => ids.SequenceEqual(new[] { _attendeeRow.EmployeeId })), "Ada Lead", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Removed_SendsOneKeyedRemovalAndAnInAppNotice()
    {
        await Sut().RemovedAsync(_series, occurrence: null, "Weekly sync", _attendeeRow.Id, _attendeeRow.EmployeeId, email: null, CancellationToken.None);

        _sender.Verify(s => s.EmailEventChangedKeyedAsync(Tenant, CalendarEventEmailKinds.Removed, It.IsAny<CalendarEventIcs>(), "Weekly sync",
            Start, "Room 4", null,
            It.Is<IReadOnlyList<CalendarParticipantRecipient>>(p => p.Single().EmailKey == CalendarEmailKeys.Removed(_series.Id, null, _attendeeRow.Id, Now)),
            It.Is<IReadOnlyList<CalendarGuestRecipient>>(g => g.Count == 0), "Ada Lead", It.IsAny<CancellationToken>()), Times.Once);
        _sender.Verify(s => s.NotifyParticipantRemovedAsync(Tenant, _attendeeRow.EmployeeId, "Weekly sync", "Ada Lead", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void MeetingIcs_SeriesCarriesTheRule_OccurrenceCarriesARecurrenceIdInstead()
    {
        var occurrence = Occurrence();

        var series = MeetingIcs.ForSeries(_series, Now);
        var one = MeetingIcs.ForOccurrence(_series, occurrence, Now);

        Assert.Equal((_series.Id, "FREQ=WEEKLY", (DateTimeOffset?)null, Start.AddMinutes(30)), (series.EventId, series.RecurrenceRule, series.RecurrenceId, series.EndUtc));
        Assert.Equal((_series.Id, (string?)null, (DateTimeOffset?)occurrence.OriginalStart, occurrence.EndAt), (one.EventId, one.RecurrenceRule, one.RecurrenceId, one.EndUtc));
        Assert.Equal(CalendarIcs.Sequence(Now), series.Sequence);
    }
}
