using FluentAssertions;
using ONEVO.Application.Features.Calendar.Exceptions;
using ONEVO.Domain.Features.Calendar.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar.Exceptions;

/// <summary>Calendar meeting-exception rules (plan C2, calendar doc §6.2) and their exclusions (§6.1).</summary>
public sealed class CalendarIncidentRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 18, 0, 0, TimeSpan.Zero); // Wednesday
    private static readonly DateTimeOffset T = new(2026, 10, 14, 9, 0, 0, TimeSpan.Zero);

    private static CalendarMeetingView M(
        DateTimeOffset? start = null, int minutes = 60, string status = CalendarEventParticipantStatuses.Accepted,
        CalendarReportState report = CalendarReportState.Received, IReadOnlyList<CalendarAttendanceSpan>? attendance = null,
        decimal? agent = null, int people = 4, bool organizer = false, bool allDay = false, bool cancelled = false, bool managerOrganised = false,
        bool replacementAttended = false, IReadOnlyList<CalendarRsvpChange>? history = null, Guid? id = null)
    {
        var s = start ?? T;
        return new CalendarMeetingView(id ?? Guid.NewGuid(), s, s.AddMinutes(minutes), "Planning", allDay, cancelled, organizer, people,
            Guid.NewGuid(), managerOrganised, status, history ?? [], replacementAttended, report, attendance ?? [], agent);
    }

    private static CalendarIncidentInput In(params CalendarMeetingView[] meetings) => new(Guid.NewGuid(), Now, meetings, [], [], null);

    private static CalendarAttendanceSpan Span(int fromMin, int? toMin) => new(T.AddMinutes(fromMin), toMin is null ? null : T.AddMinutes(toMin.Value));

    // ---- exclusions (all rules) -----------------------------------------------------------

    public static TheoryData<string, CalendarMeetingView> Excluded => new()
    {
        { "all-day", M(allDay: true) },
        { "cancelled", M(cancelled: true) },
        { "organizer", M(organizer: true) },
        { "only participant", M(people: 1) },
        { "declined (attendance)", M(status: CalendarEventParticipantStatuses.Rejected) },
        { "replacement attended", M(replacementAttended: true) },
    };

    [Theory]
    [MemberData(nameof(Excluded))]
    public void A_no_show_is_never_recorded_for_an_excluded_meeting(string because, CalendarMeetingView meeting)
        => new NoShowRule().Evaluate(In(meeting)).Should().BeEmpty(because);

    [Fact]
    public void Approved_leave_or_a_holiday_during_the_meeting_excludes_it()
    {
        var meeting = M();
        var interval = new CalendarInterval(T.AddHours(-1), T.AddHours(2));
        new NoShowRule().Evaluate(In(meeting) with { Leave = [interval] }).Should().BeEmpty();
        new NoShowRule().Evaluate(In(meeting) with { Holidays = [interval] }).Should().BeEmpty();
        new NoShowRule().Evaluate(In(meeting)).Should().ContainSingle();
    }

    // ---- attendance ------------------------------------------------------------------------

    [Fact]
    public void No_show_needs_a_finished_meeting_and_a_final_report()
    {
        new NoShowRule().Evaluate(In(M(start: Now.AddMinutes(-30)))).Should().BeEmpty("it has not ended");
        new NoShowRule().Evaluate(In(M(report: CalendarReportState.Pending))).Should().BeEmpty("the provider report may still come");
        var found = new NoShowRule().Evaluate(In(M())).Single();
        (found.Confidence, found.Immediate).Should().Be((CalendarIncidentConfidence.Provider, false));
    }

    [Fact]
    public void Without_a_provider_report_the_agent_estimate_decides_and_is_labelled_estimated()
    {
        new NoShowRule().Evaluate(In(M(report: CalendarReportState.Unavailable, agent: 0.2m))).Single().Confidence.Should().Be(CalendarIncidentConfidence.Agent);
        new NoShowRule().Evaluate(In(M(report: CalendarReportState.NoOnlineMeeting, agent: 0.7m))).Should().BeEmpty();
        new NoShowRule().Evaluate(In(M(report: CalendarReportState.NoOnlineMeeting, agent: null))).Should().BeEmpty("no evidence at all");
    }

    [Fact]
    public void A_no_show_is_immediate_and_high_when_the_manager_organised_it_or_it_was_a_one_to_one()
    {
        new NoShowRule().Evaluate(In(M(managerOrganised: true))).Single().Should().Match<CalendarIncidentFinding>(f => f.Immediate && f.Severity == "high");
        new NoShowRule().Evaluate(In(M(people: 2))).Single().Immediate.Should().BeTrue();
    }

    [Fact]
    public void Joining_the_meeting_is_not_a_no_show()
        => new NoShowRule().Evaluate(In(M(attendance: [Span(0, 60)]))).Should().BeEmpty();

    [Theory]
    [InlineData(10, false)]
    [InlineData(11, true)]
    public void Late_join_is_more_than_ten_minutes_after_the_start(int joinedAfter, bool late)
        => new LateJoinRule().Evaluate(In(M(attendance: [Span(joinedAfter, 60)]))).Should().HaveCount(late ? 1 : 0);

    [Fact]
    public void Late_join_is_provider_only()
        => new LateJoinRule().Evaluate(In(M(report: CalendarReportState.Unavailable, agent: 0.6m))).Should().BeEmpty();

    [Theory]
    [InlineData(0, 45, false)] // left 15 min early: allowed
    [InlineData(0, 44, true)]  // 16 min early
    [InlineData(0, 25, true)]  // under half
    public void Early_leave_is_more_than_fifteen_minutes_early_or_under_half_attended(int from, int to, bool early)
        => new EarlyLeaveRule().Evaluate(In(M(attendance: [Span(from, to)]))).Should().HaveCount(early ? 1 : 0);

    [Fact]
    public void Attendance_spans_that_overlap_count_once()
        => EarlyLeaveRule.AttendedRatio(M(attendance: [Span(0, 40), Span(20, 50)])).Should().BeApproximately(50m / 60m, 0.001m);

    // ---- RSVP ------------------------------------------------------------------------------

    [Fact]
    public void No_response_is_an_invitation_still_pending_at_the_start()
    {
        new NoResponseRule().Evaluate(In(M(status: CalendarEventParticipantStatuses.Pending))).Should().ContainSingle();
        new NoResponseRule().Evaluate(In(M(status: CalendarEventParticipantStatuses.Accepted,
            history: [new(CalendarEventParticipantStatuses.Pending, CalendarEventParticipantStatuses.Accepted, T.AddMinutes(30))])))
            .Should().ContainSingle("they answered only after it started");
        new NoResponseRule().Evaluate(In(M(status: CalendarEventParticipantStatuses.Accepted,
            history: [new(CalendarEventParticipantStatuses.Pending, CalendarEventParticipantStatuses.Accepted, T.AddDays(-1))])))
            .Should().BeEmpty();
    }

    [Theory]
    [InlineData(-119, true)]
    [InlineData(-121, false)]
    public void Late_decline_is_accepted_then_declined_within_two_hours_of_the_start(int minutesBefore, bool late)
        => new LateDeclineRule().Evaluate(In(M(status: CalendarEventParticipantStatuses.Rejected,
                history: [new(CalendarEventParticipantStatuses.Accepted, CalendarEventParticipantStatuses.Rejected, T.AddMinutes(minutesBefore))])))
            .Should().HaveCount(late ? 1 : 0);

    [Fact]
    public void An_unanswered_resolution_request_at_the_start_is_immediate()
        => new UnresolvedRequestRule().Evaluate(In(M(status: CalendarEventParticipantStatuses.ResolutionRequested)))
            .Single().Immediate.Should().BeTrue();

    [Theory]
    [InlineData(6, 3, true)]
    [InlineData(6, 2, false)]
    [InlineData(5, 5, false)] // under the 6-invitation minimum
    public void Decline_rate_is_half_or_more_of_at_least_six_invitations(int invites, int declined, bool fires)
    {
        var meetings = Enumerable.Range(0, invites).Select(i => M(start: Now.AddDays(-i - 1),
            status: i < declined ? CalendarEventParticipantStatuses.Rejected : CalendarEventParticipantStatuses.Accepted)).ToArray();
        new DeclineRateRule().Evaluate(In(meetings)).Should().HaveCount(fires ? 1 : 0);
    }

    // ---- load ------------------------------------------------------------------------------

    [Theory]
    [InlineData(45, true)]  // overlap 15 minutes
    [InlineData(46, false)] // overlap 14 minutes
    public void Double_booking_is_two_meetings_overlapping_by_fifteen_minutes_recorded_on_the_later_one(int secondStartsAfter, bool booked)
    {
        var first = M(start: Now.AddDays(1));
        var second = M(start: Now.AddDays(1).AddMinutes(secondStartsAfter), status: CalendarEventParticipantStatuses.Pending);
        var found = new DoubleBookRule().Evaluate(In(first, second)).ToList();
        found.Should().HaveCount(booked ? 1 : 0);
        if (booked)
            found[0].EventId.Should().Be(second.EventId);
    }

    [Fact]
    public void A_meeting_during_approved_leave_is_immediate_until_it_is_declined()
    {
        var meeting = M(start: Now.AddDays(2));
        var input = In(meeting) with { Leave = [new CalendarInterval(Now.AddDays(2).AddHours(-2), Now.AddDays(2).AddHours(3))] };
        new OnLeaveRule().Evaluate(input).Single().Immediate.Should().BeTrue();
        new OnLeaveRule().Evaluate(input with { Meetings = [meeting with { ResponseStatus = CalendarEventParticipantStatuses.Rejected }] }).Should().BeEmpty();
    }

    [Fact]
    public void Overload_is_more_than_five_meeting_hours_in_one_day_counting_overlaps_once()
    {
        var day = new DateTimeOffset(2026, 10, 12, 8, 0, 0, TimeSpan.Zero);
        var sixHours = new[] { M(start: day, minutes: 180), M(start: day.AddHours(3), minutes: 180) };
        var overlapping = new[] { M(start: day, minutes: 180), M(start: day.AddHours(1), minutes: 180) }; // 4h together
        new OverloadRule().Evaluate(In(sixHours)).Should().ContainSingle();
        new OverloadRule().Evaluate(In(overlapping)).Should().BeEmpty();
    }

    [Fact]
    public void Off_hours_is_an_accepted_meeting_outside_working_hours_or_days()
    {
        var hours = new CalendarWorkingHours(TimeZoneInfo.Utc, new TimeOnly(9, 0), new TimeOnly(17, 0), new HashSet<int> { 1, 2, 3, 4, 5 });
        var inside = M(start: new DateTimeOffset(2026, 10, 14, 10, 0, 0, TimeSpan.Zero));
        var evening = M(start: new DateTimeOffset(2026, 10, 14, 16, 30, 0, TimeSpan.Zero));
        var saturday = M(start: new DateTimeOffset(2026, 10, 17, 10, 0, 0, TimeSpan.Zero));
        new OffHoursRule().Evaluate(In(inside, evening, saturday) with { Hours = hours }).Select(f => f.EventId)
            .Should().BeEquivalentTo([evening.EventId, saturday.EventId]);
        new OffHoursRule().Evaluate(In(evening)).Should().BeEmpty("no working hours are configured");
    }
}
