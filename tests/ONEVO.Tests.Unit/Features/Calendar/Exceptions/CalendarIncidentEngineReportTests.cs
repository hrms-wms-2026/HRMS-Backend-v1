using FluentAssertions;
using ONEVO.Application.Features.Calendar.Exceptions;
using ONEVO.Domain.Features.Calendar.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar.Exceptions;

/// <summary>Plan C2: what attendance evidence a meeting occurrence has - identity matching and the provider-report wait.</summary>
public sealed class CalendarIncidentEngineReportTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 14, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = Start.AddHours(1);
    private static readonly Guid Employee = Guid.NewGuid();
    private static readonly IReadOnlySet<string> Identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "pat@acme.com", "pat.personal@gmail.com" };

    private static CalendarEventMeeting Meeting(DateTimeOffset? syncedAt = null, DateTimeOffset? unavailableAt = null) => new()
    {
        Id = Guid.NewGuid(), Status = CalendarEventMeetingStatuses.Active, LastAttendanceSyncedAt = syncedAt, AttendanceUnavailableAt = unavailableAt,
    };

    private static CalendarEventMeetingAttendance Row(string? email, Guid? employeeId = null, int joinedAfter = 0) => new()
    {
        Id = Guid.NewGuid(), ExternalParticipantEmail = email, EmployeeId = employeeId, JoinedAt = Start.AddMinutes(joinedAfter), LeftAt = End,
    };

    [Fact]
    public void Only_rows_matched_by_work_email_connected_account_or_employee_id_are_evidence()
    {
        var rows = new[] { Row("PAT@acme.com"), Row("pat.personal@gmail.com", joinedAfter: 5), Row(null, Employee, 7), Row("someone@else.com"), Row(null) };

        var (state, spans) = CalendarIncidentEngine.ReportFor(Meeting(syncedAt: End.AddHours(1)), rows, Start, End, Employee, Identities, End.AddHours(2));

        state.Should().Be(CalendarReportState.Received);
        spans.Should().HaveCount(3);
    }

    [Fact]
    public void An_unmatched_report_is_a_received_report_with_no_attendance_for_the_employee()
    {
        var (state, spans) = CalendarIncidentEngine.ReportFor(Meeting(syncedAt: End.AddHours(1)), [Row("someone@else.com")], Start, End, Employee, Identities, End.AddHours(2));

        (state, spans.Count).Should().Be((CalendarReportState.Received, 0));
    }

    [Fact]
    public void Without_a_report_the_rules_wait_until_the_cutoff_then_fall_back_to_the_agent()
    {
        CalendarIncidentEngine.ReportFor(Meeting(), [], Start, End, Employee, Identities, End.AddHours(23)).State
            .Should().Be(CalendarReportState.Pending, "the sync job is still retrying an empty report");
        CalendarIncidentEngine.ReportFor(Meeting(), [], Start, End, Employee, Identities, End.AddHours(24)).State
            .Should().Be(CalendarReportState.Unavailable);
        CalendarIncidentEngine.ReportFor(Meeting(unavailableAt: End.AddHours(24)), [], Start, End, Employee, Identities, End.AddHours(25)).State
            .Should().Be(CalendarReportState.Unavailable);
    }

    [Fact]
    public void A_series_report_synced_before_this_occurrence_ended_is_not_its_report()
        => CalendarIncidentEngine.ReportFor(Meeting(syncedAt: Start.AddDays(-7)), [Row("pat@acme.com")], Start, End, Employee, Identities, End.AddHours(1)).State
            .Should().Be(CalendarReportState.Pending);

    [Fact]
    public void An_event_without_an_active_online_meeting_has_only_agent_evidence()
    {
        CalendarIncidentEngine.ReportFor(null, [], Start, End, Employee, Identities, End.AddHours(1)).State.Should().Be(CalendarReportState.NoOnlineMeeting);
        var cancelled = Meeting();
        cancelled.Status = CalendarEventMeetingStatuses.Cancelled;
        CalendarIncidentEngine.ReportFor(cancelled, [], Start, End, Employee, Identities, End.AddHours(1)).State.Should().Be(CalendarReportState.NoOnlineMeeting);
    }
}
