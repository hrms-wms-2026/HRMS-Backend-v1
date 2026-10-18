using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Exceptions;
using ONEVO.Application.Features.Calendar.Queries.GetEventMeetingAttendance;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class GetEventMeetingAttendanceQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid EventId = Guid.NewGuid();
    private static readonly Guid OrganizerUser = Guid.NewGuid();

    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<ICalendarEventMeetingRepository> _meetings = new();
    private readonly Mock<ICalendarEventMeetingAttendanceRepository> _attendances = new();
    private readonly Mock<ICalendarEventRepository> _events = new();
    private readonly Mock<ICalendarIncidentRepository> _incidents = new();
    private readonly Mock<ONEVO.Application.Common.RepositoryInterfaces.IEmployeeRepository> _employees = new();

    public GetEventMeetingAttendanceQueryHandlerTests()
    {
        _events.Setup(e => e.GetByIdForTenantAsync(TenantId, EventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CalendarEvent { Id = EventId, TenantId = TenantId, Title = "Sprint review", CreatedById = OrganizerUser });
    }

    private GetEventMeetingAttendanceQueryHandler BuildSut(Guid? callerUserId = null)
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        _currentUser.SetupGet(x => x.UserId).Returns(callerUserId ?? OrganizerUser);
        return new GetEventMeetingAttendanceQueryHandler(_currentUser.Object, _meetings.Object, _attendances.Object, _events.Object);
    }

    private CalendarEventMeeting SyncedMeetingWithOneAttendee()
    {
        var meeting = new CalendarEventMeeting { Id = Guid.NewGuid(), LastAttendanceSyncedAt = DateTimeOffset.UtcNow };
        _meetings.Setup(m => m.GetTrackedByCalendarEventAsync(TenantId, EventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(meeting);
        _attendances.Setup(a => a.GetByMeetingIdAsync(TenantId, meeting.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new CalendarEventMeetingAttendance
                {
                    Id = Guid.NewGuid(), ExternalParticipantName = "Ada Lovelace", ExternalParticipantEmail = "ada@example.com",
                    JoinedAt = DateTimeOffset.UtcNow.AddMinutes(-30), LeftAt = DateTimeOffset.UtcNow, DurationSeconds = 1800
                }
            ]);
        return meeting;
    }

    [Fact]
    public async Task Handle_NoMeeting_ReturnsEmptyUnavailableResult()
    {
        _meetings.Setup(m => m.GetTrackedByCalendarEventAsync(TenantId, EventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CalendarEventMeeting?)null);

        var result = await BuildSut().Handle(new GetEventMeetingAttendanceQuery(EventId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Available.Should().BeFalse();
        result.Value.Attendees.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_MeetingNeverSynced_ReturnsUnavailable()
    {
        _meetings.Setup(m => m.GetTrackedByCalendarEventAsync(TenantId, EventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CalendarEventMeeting { Id = Guid.NewGuid(), LastAttendanceSyncedAt = null });

        var result = await BuildSut().Handle(new GetEventMeetingAttendanceQuery(EventId), CancellationToken.None);

        result.Value!.Available.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_EventNotStored_ReturnsUnavailableWithoutReadingTheMeeting()
    {
        var virtualOccurrenceId = Guid.NewGuid();

        var result = await BuildSut().Handle(new GetEventMeetingAttendanceQuery(virtualOccurrenceId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Available.Should().BeFalse();
        _meetings.Verify(m => m.GetTrackedByCalendarEventAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---- sync status: why there are no rows, never "nobody attended" ----------------------------------------------

    public static TheoryData<CalendarEventMeeting?, string> SyncStatusCases => new()
    {
        { null, "none" },
        { new CalendarEventMeeting { Id = Guid.NewGuid() }, "pending" },
        { new CalendarEventMeeting { Id = Guid.NewGuid(), AttendanceUnavailableAt = DateTimeOffset.UtcNow }, "attendance_unavailable" },
        { new CalendarEventMeeting { Id = Guid.NewGuid(), AttendanceSyncFailedAt = DateTimeOffset.UtcNow, AttendanceSyncError = "reauth_required" }, "sync_failed" },
        { new CalendarEventMeeting { Id = Guid.NewGuid(), LastAttendanceSyncedAt = DateTimeOffset.UtcNow }, "synced" },
    };

    [Theory]
    [MemberData(nameof(SyncStatusCases))]
    public async Task The_organizer_is_told_the_attendance_sync_status(CalendarEventMeeting? meeting, string expected)
    {
        _meetings.Setup(m => m.GetTrackedByCalendarEventAsync(TenantId, EventId, It.IsAny<CancellationToken>())).ReturnsAsync(meeting);
        _attendances.Setup(a => a.GetByMeetingIdAsync(TenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var result = await BuildSut().Handle(new GetEventMeetingAttendanceQuery(EventId), CancellationToken.None);

        result.Value!.SyncStatus.Should().Be(expected);
        result.Value.SyncError.Should().Be(meeting?.AttendanceSyncError);
    }

    // ---- audience: the organizer only - participants and unrelated tenant users are refused ------------------------

    [Fact]
    public async Task The_organizer_sees_the_attendance_rows()
    {
        SyncedMeetingWithOneAttendee();

        var result = await BuildSut(OrganizerUser).Handle(new GetEventMeetingAttendanceQuery(EventId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Available.Should().BeTrue();
        result.Value.Attendees.Should().ContainSingle(a => a.Name == "Ada Lovelace" && a.Email == "ada@example.com");
    }

    [Fact]
    public async Task A_participant_who_is_not_the_organizer_is_forbidden()
    {
        // The caller is invited (a participant row exists) but did not organize the event.
        var participantUser = Guid.NewGuid();
        _events.Setup(e => e.GetParticipantsForEventsAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<CalendarEventParticipant>>
            {
                [EventId] = [new CalendarEventParticipant { EventId = EventId, EmployeeId = Guid.NewGuid() }],
            });
        SyncedMeetingWithOneAttendee();

        var result = await BuildSut(participantUser).Handle(new GetEventMeetingAttendanceQuery(EventId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
        result.Value.Should().BeNull();
        _attendances.Verify(a => a.GetByMeetingIdAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task An_unrelated_tenant_user_is_forbidden()
    {
        SyncedMeetingWithOneAttendee();

        var result = await BuildSut(Guid.NewGuid()).Handle(new GetEventMeetingAttendanceQuery(EventId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
        _meetings.Verify(m => m.GetTrackedByCalendarEventAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _attendances.Verify(a => a.GetByMeetingIdAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---- plan FC: the organizer's Attendance tab also lists the incidents this meeting raised ----------------------

    private GetEventMeetingAttendanceQueryHandler BuildWithIncidents(Guid callerUserId)
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        _currentUser.SetupGet(x => x.UserId).Returns(callerUserId);
        var pat = new ONEVO.Domain.Features.CoreHr.Entities.Employee { Id = Guid.NewGuid(), FirstName = "Pat", LastName = "Doe" };
        _incidents.Setup(i => i.ListForEventAsync(TenantId, EventId, It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            new CalendarMeetingIncident
            {
                Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = pat.Id, EventId = EventId, RuleCode = CalendarRuleCodes.NoShow,
                OccurrenceStart = new DateTimeOffset(2026, 10, 13, 9, 0, 0, TimeSpan.Zero), Confidence = CalendarIncidentConfidence.Agent,
                EvidenceJson = "{\"apps\":[\"zoom.exe\"]}",
            },
        ]);
        _employees.Setup(e => e.ListByIdsAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, ONEVO.Domain.Features.CoreHr.Entities.Employee> { [pat.Id] = pat });
        _meetings.Setup(m => m.GetTrackedByCalendarEventAsync(TenantId, EventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CalendarEventMeeting?)null);
        return new GetEventMeetingAttendanceQueryHandler(_currentUser.Object, _meetings.Object, _attendances.Object,
            _events.Object, _incidents.Object, _employees.Object);
    }

    [Fact]
    public async Task The_organizer_sees_the_incidents_raised_for_the_meeting_as_meeting_facts_only()
    {
        var result = await BuildWithIncidents(OrganizerUser).Handle(new GetEventMeetingAttendanceQuery(EventId), CancellationToken.None);

        var incident = result.Value!.Incidents.Should().ContainSingle().Subject;
        (incident.EmployeeName, incident.Rule, incident.Estimated).Should().Be(("Pat Doe", CalendarRuleCatalog.Specs[CalendarRuleCodes.NoShow].Title, true));
        incident.OccurrenceStart.Should().Be(new DateTimeOffset(2026, 10, 13, 9, 0, 0, TimeSpan.Zero));
        result.Value.Available.Should().BeFalse("no online meeting, but incidents still show");
    }

    [Fact]
    public async Task Anyone_but_the_organizer_is_refused_before_incidents_are_read()
    {
        var result = await BuildWithIncidents(Guid.NewGuid()).Handle(new GetEventMeetingAttendanceQuery(EventId), CancellationToken.None);

        result.StatusCode.Should().Be(403);
        _incidents.Verify(i => i.ListForEventAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
