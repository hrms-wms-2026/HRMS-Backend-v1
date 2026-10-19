using FluentAssertions;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Application.Features.WorkManagement.Meetings.RepositoryInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

/// <summary>Calendar P2-2 (plan C1): one in-app reminder per occurrence per participant, shortly before it starts.</summary>
public sealed class CalendarEventReminderServiceTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 9, 0, 0, TimeSpan.Zero);
    private readonly Mock<ICalendarEventRepository> _events = new();
    private readonly Mock<ICalendarRecurrenceExpander> _expander = new();
    private readonly Mock<IEmployeeRepository> _employees = new();
    private readonly Mock<ICalendarNotificationSender> _notifications = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Employee _ada = new() { Id = Guid.NewGuid(), TenantId = TenantId, UserId = Guid.NewGuid(), FirstName = "Ada" };
    private readonly Employee _bob = new() { Id = Guid.NewGuid(), TenantId = TenantId, UserId = Guid.NewGuid(), FirstName = "Bob" };
    private readonly List<CalendarEventReminderLog> _logged = [];
    private readonly HashSet<(Guid, DateTimeOffset, Guid)> _sent = [];

    public CalendarEventReminderServiceTests()
    {
        _events.Setup(e => e.ListWithParticipantsStartingBetweenAsync(TenantId, Now, Now.AddMinutes(15), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _events.Setup(e => e.ListRecurringMastersWithParticipantsAsync(TenantId, Now.AddMinutes(15), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _events.Setup(e => e.GetChildrenForMasterAsync(TenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _events.Setup(e => e.ListSentRemindersAsync(TenantId, Now, Now.AddMinutes(15), It.IsAny<CancellationToken>())).ReturnsAsync(() => _sent.ToHashSet());
        _events.Setup(e => e.AddReminderLogsAsync(It.IsAny<IReadOnlyList<CalendarEventReminderLog>>(), It.IsAny<CancellationToken>()))
            .Callback((IReadOnlyList<CalendarEventReminderLog> logs, CancellationToken _) =>
            {
                _logged.AddRange(logs);
                foreach (var log in logs)
                    _sent.Add((log.EventId, log.OccurrenceStart, log.EmployeeId));
            })
            .Returns(Task.CompletedTask);
        _employees.Setup(e => e.ListByIdsAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, Employee> { [_ada.Id] = _ada, [_bob.Id] = _bob });
    }

    private CalendarEventReminderService Sut() => new(_events.Object, _expander.Object, _employees.Object, _notifications.Object, _unitOfWork.Object);

    private void Participants(Guid eventId, params (Employee Who, string Status)[] people)
        => _events.Setup(e => e.GetParticipantsForEventsAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<CalendarEventParticipant>>
            {
                [eventId] = people.Select(p => new CalendarEventParticipant
                {
                    Id = Guid.NewGuid(), TenantId = TenantId, EventId = eventId, EmployeeId = p.Who.Id, ResponseStatus = p.Status,
                }).ToList(),
            });

    [Fact]
    public async Task An_event_starting_within_the_lead_time_reminds_each_participant_who_did_not_decline_once()
    {
        var evt = new CalendarEvent { Id = Guid.NewGuid(), TenantId = TenantId, Title = "Standup", StartDate = Now.AddMinutes(10), EndDate = Now.AddMinutes(25) };
        _events.Setup(e => e.ListWithParticipantsStartingBetweenAsync(TenantId, Now, Now.AddMinutes(15), It.IsAny<CancellationToken>())).ReturnsAsync([evt]);
        Participants(evt.Id, (_ada, CalendarEventParticipantStatuses.Accepted), (_bob, CalendarEventParticipantStatuses.Rejected));

        var first = await Sut().SendDueAsync(TenantId, Now, default);
        var second = await Sut().SendDueAsync(TenantId, Now, default);

        (first, second).Should().Be((1, 0));
        _notifications.Verify(n => n.NotifyEventReminderAsync(TenantId, _ada.UserId, "Standup", evt.StartDate, It.IsAny<CancellationToken>()), Times.Once);
        _notifications.Verify(n => n.NotifyEventReminderAsync(TenantId, _bob.UserId, It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
        _logged.Should().ContainSingle(l => l.EventId == evt.Id && l.OccurrenceStart == evt.StartDate && l.EmployeeId == _ada.Id);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_recurring_series_reminds_for_its_next_occurrence_unless_that_occurrence_was_cancelled_or_moved()
    {
        var master = new CalendarEvent
        {
            Id = Guid.NewGuid(), TenantId = TenantId, Title = "Weekly sync", StartDate = Now.AddDays(-7).AddMinutes(5),
            EndDate = Now.AddDays(-7).AddMinutes(35), Recurrence = CalendarRecurrences.Weekly, RecurrenceRule = "FREQ=WEEKLY",
        };
        var occurrence = Now.AddMinutes(5);
        _events.Setup(e => e.ListRecurringMastersWithParticipantsAsync(TenantId, Now.AddMinutes(15), It.IsAny<CancellationToken>())).ReturnsAsync([master]);
        _expander.Setup(x => x.Expand("FREQ=WEEKLY", master.StartDate, Now, Now.AddMinutes(15))).Returns([occurrence]);
        Participants(master.Id, (_ada, CalendarEventParticipantStatuses.Pending));

        (await Sut().SendDueAsync(TenantId, Now, default)).Should().Be(1);
        _notifications.Verify(n => n.NotifyEventReminderAsync(TenantId, _ada.UserId, "Weekly sync", occurrence, It.IsAny<CancellationToken>()), Times.Once);

        _sent.Clear();
        _events.Setup(e => e.GetChildrenForMasterAsync(TenantId, master.Id, It.IsAny<CancellationToken>())).ReturnsAsync(
            [new CalendarEvent { Id = Guid.NewGuid(), TenantId = TenantId, RecurrenceParentId = master.Id, RecurrenceOriginalStart = occurrence, IsRecurrenceCancelled = true }]);
        (await Sut().SendDueAsync(TenantId, Now, default)).Should().Be(0);
    }

    [Fact]
    public async Task A_moved_occurrence_uses_its_series_participants()
    {
        var masterId = Guid.NewGuid();
        var moved = new CalendarEvent
        {
            Id = Guid.NewGuid(), TenantId = TenantId, Title = "Weekly sync (moved)", RecurrenceParentId = masterId,
            RecurrenceOriginalStart = Now.AddDays(1), StartDate = Now.AddMinutes(12), EndDate = Now.AddMinutes(40),
        };
        _events.Setup(e => e.ListWithParticipantsStartingBetweenAsync(TenantId, Now, Now.AddMinutes(15), It.IsAny<CancellationToken>())).ReturnsAsync([moved]);
        Participants(masterId, (_ada, CalendarEventParticipantStatuses.Accepted));

        (await Sut().SendDueAsync(TenantId, Now, default)).Should().Be(1);
        _logged.Should().ContainSingle(l => l.EventId == masterId && l.OccurrenceStart == moved.StartDate);
    }

    [Fact]
    public async Task A_task_meeting_occurrence_reminds_its_attendees_who_did_not_decline_and_its_organizer_once()
    {
        var organizer = new Employee { Id = Guid.NewGuid(), TenantId = TenantId, UserId = Guid.NewGuid(), FirstName = "Org" };
        _employees.Setup(e => e.ListByIdsAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, Employee> { [_ada.Id] = _ada, [_bob.Id] = _bob, [organizer.Id] = organizer });
        var series = new MeetingSeries { Id = Guid.NewGuid(), TenantId = TenantId, OrganizerEmployeeId = organizer.Id, Title = "Sync" };
        var occurrence = new MeetingDetails
        {
            TaskId = Guid.NewGuid(), TenantId = TenantId, SeriesId = series.Id, OriginalStart = Now.AddMinutes(10),
            StartAt = Now.AddMinutes(12), EndAt = Now.AddMinutes(42),
        };
        var meetings = new Mock<IMeetingRepository>();
        meetings.Setup(m => m.ListStartingBetweenAsync(TenantId, Now, Now.AddMinutes(15), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new MeetingOccurrenceRow(occurrence, "ACME-M1", "Sync", null, Guid.NewGuid(), Guid.NewGuid())]);
        meetings.Setup(m => m.ListSeriesAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync([series]);
        meetings.Setup(m => m.ListParticipantsForSeriesAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new CalendarEventParticipant { Id = Guid.NewGuid(), TenantId = TenantId, MeetingSeriesId = series.Id, EmployeeId = _ada.Id, ResponseStatus = CalendarEventParticipantStatuses.Accepted },
                new CalendarEventParticipant { Id = Guid.NewGuid(), TenantId = TenantId, MeetingSeriesId = series.Id, EmployeeId = _bob.Id, ResponseStatus = CalendarEventParticipantStatuses.Rejected },
            ]);
        meetings.Setup(m => m.ListOverridesAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var sut = () => new CalendarEventReminderService(_events.Object, _expander.Object, _employees.Object, _notifications.Object, _unitOfWork.Object,
            meetings.Object);

        var first = await sut().SendDueAsync(TenantId, Now, default);
        var second = await sut().SendDueAsync(TenantId, Now, default);

        (first, second).Should().Be((2, 0));
        _logged.Select(l => (l.EventId, l.OccurrenceStart, l.EmployeeId)).Should().BeEquivalentTo(new[]
        {
            (occurrence.TaskId, occurrence.StartAt, _ada.Id), (occurrence.TaskId, occurrence.StartAt, organizer.Id),
        });
        _notifications.Verify(n => n.NotifyEventReminderAsync(TenantId, _bob.UserId, It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
