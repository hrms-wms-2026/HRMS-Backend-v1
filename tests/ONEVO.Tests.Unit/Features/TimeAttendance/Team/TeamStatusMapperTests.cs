using ONEVO.Application.Features.TimeAttendance.Services;
using ONEVO.Application.Features.TimeAttendance.Team.Services;
using ONEVO.Domain.Features.TimeAttendance.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.TimeAttendance.Team;

public sealed class TeamStatusMapperTests
{
    private static AttendanceDayStatusResolution Resolution(
        string status, bool shouldHaveClockedIn = false,
        string? attentionType = null, string? attentionLabel = null, string? attentionSeverity = null)
        => new(status, status, attentionType, attentionLabel, attentionSeverity, shouldHaveClockedIn, 0, false);

    [Theory]
    [InlineData(AttendanceRecord.StatusActive, "working")]
    [InlineData(AttendanceRecord.StatusOnBreak, "on_break")]
    [InlineData(AttendanceRecord.StatusOverBreak, "on_break")]
    [InlineData(AttendanceRecord.StatusClockedOut, "clocked_out")]
    [InlineData(AttendanceRecord.StatusMissingClockOut, "working")]
    [InlineData(AttendanceRecord.StatusWorkedDuringTimeOff, "working")]
    [InlineData(AttendanceRecord.StatusWorkedOnNonWorkingDay, "working")]
    [InlineData(AttendanceRecord.StatusNonWorkingDay, "not_scheduled")]
    [InlineData(AttendanceRecord.StatusNoSchedule, "not_scheduled")]
    [InlineData(AttendanceRecord.StatusPolicyNotConfigured, "not_scheduled")]
    public void Maps_resolver_status_to_team_status_vocabulary(string resolverStatus, string expectedTeamStatus)
    {
        var result = TeamStatusMapper.Map(Resolution(resolverStatus), arrivedLate: false, leaveAuthorizedForSubject: true);
        Assert.Equal(expectedTeamStatus, result.TeamStatus);
    }

    [Fact]
    public void NotClockedIn_before_shift_start_maps_to_not_started()
    {
        var result = TeamStatusMapper.Map(
            Resolution(AttendanceRecord.StatusNotClockedIn, shouldHaveClockedIn: false),
            arrivedLate: false, leaveAuthorizedForSubject: true);

        Assert.Equal("not_started", result.TeamStatus);
    }

    [Fact]
    public void NotClockedIn_after_shift_start_maps_to_absent()
    {
        var result = TeamStatusMapper.Map(
            Resolution(AttendanceRecord.StatusNotClockedIn, shouldHaveClockedIn: true,
                attentionType: "not_clocked_in", attentionLabel: "Still has not clocked in", attentionSeverity: "critical"),
            arrivedLate: false, leaveAuthorizedForSubject: true);

        Assert.Equal("absent", result.TeamStatus);
        Assert.Equal("not_clocked_in", result.AttentionType);
    }

    [Fact]
    public void OnTimeOff_maps_to_on_leave_when_viewer_is_leave_authorized_for_subject()
    {
        var result = TeamStatusMapper.Map(
            Resolution(AttendanceRecord.StatusOnTimeOff), arrivedLate: false, leaveAuthorizedForSubject: true);

        Assert.Equal("on_leave", result.TeamStatus);
        Assert.True(result.ShowLeaveDetail);
    }

    [Fact]
    public void OnTimeOff_masks_to_absent_when_viewer_is_NOT_leave_authorized_for_subject()
    {
        var result = TeamStatusMapper.Map(
            Resolution(AttendanceRecord.StatusOnTimeOff), arrivedLate: false, leaveAuthorizedForSubject: false);

        Assert.Equal("absent", result.TeamStatus);
        Assert.False(result.ShowLeaveDetail);
    }

    [Fact]
    public void WorkedDuringTimeOff_keeps_working_status_but_strips_attention_when_not_leave_authorized()
    {
        var result = TeamStatusMapper.Map(
            Resolution(AttendanceRecord.StatusWorkedDuringTimeOff, attentionType: "worked_during_time_off",
                attentionLabel: "Worked during approved time off", attentionSeverity: "warning"),
            arrivedLate: false, leaveAuthorizedForSubject: false);

        Assert.Equal("working", result.TeamStatus);
        Assert.Null(result.AttentionType);
        Assert.Null(result.AttentionLabel);
        Assert.Null(result.AttentionSeverity);
    }

    [Fact]
    public void WorkedDuringTimeOff_keeps_both_working_status_and_attention_when_leave_authorized()
    {
        var result = TeamStatusMapper.Map(
            Resolution(AttendanceRecord.StatusWorkedDuringTimeOff, attentionType: "worked_during_time_off",
                attentionLabel: "Worked during approved time off", attentionSeverity: "warning"),
            arrivedLate: false, leaveAuthorizedForSubject: true);

        Assert.Equal("working", result.TeamStatus);
        Assert.Equal("worked_during_time_off", result.AttentionType);
    }

    [Fact]
    public void SECURITY_masked_absent_row_carries_no_attention_fields_indistinguishable_from_a_real_no_show()
    {
        var maskedLeave = TeamStatusMapper.Map(
            Resolution(AttendanceRecord.StatusOnTimeOff), arrivedLate: false, leaveAuthorizedForSubject: false);
        var realNoShow = TeamStatusMapper.Map(
            Resolution(AttendanceRecord.StatusNotClockedIn, shouldHaveClockedIn: true,
                attentionType: "not_clocked_in", attentionLabel: "Still has not clocked in", attentionSeverity: "critical"),
            arrivedLate: false, leaveAuthorizedForSubject: false);

        Assert.Null(maskedLeave.AttentionType);
        Assert.Null(maskedLeave.AttentionLabel);
        Assert.Null(maskedLeave.AttentionSeverity);
        // Unauthorized viewer's real no-show KEEPS its attention fields — masking applies only
        // to the leave-caused absence, never to a genuine no-show, since attendance attention
        // itself is not a leave-visibility-gated fact.
        Assert.NotNull(realNoShow.AttentionType);
    }

    [Fact]
    public void Leave_authorized_viewer_keeps_attention_fields_on_a_masked_status_that_has_none_anyway()
    {
        // OnTimeOff never carries attention fields from the resolver regardless of masking —
        // this just pins that authorization doesn't fabricate fields that were never there.
        var result = TeamStatusMapper.Map(
            Resolution(AttendanceRecord.StatusOnTimeOff), arrivedLate: false, leaveAuthorizedForSubject: true);
        Assert.Null(result.AttentionType);
    }

    [Theory]
    [InlineData(AttendanceRecord.StatusActive, true)]
    [InlineData(AttendanceRecord.StatusOnBreak, true)]
    [InlineData(AttendanceRecord.StatusClockedOut, false)]
    [InlineData(AttendanceRecord.StatusNotClockedIn, false)]
    public void IsLate_overlay_applies_only_while_working_or_on_break(string resolverStatus, bool expectIsLate)
    {
        var result = TeamStatusMapper.Map(Resolution(resolverStatus), arrivedLate: true, leaveAuthorizedForSubject: true);
        Assert.Equal(expectIsLate, result.IsLate);
    }

    [Fact]
    public void IsLate_is_false_when_arrival_was_not_late_even_while_working()
    {
        var result = TeamStatusMapper.Map(Resolution(AttendanceRecord.StatusActive), arrivedLate: false, leaveAuthorizedForSubject: true);
        Assert.False(result.IsLate);
    }

    [Fact]
    public void Attention_fields_pass_through_unchanged_for_a_non_masked_row()
    {
        var result = TeamStatusMapper.Map(
            Resolution(AttendanceRecord.StatusOverBreak, attentionType: "over_break",
                attentionLabel: "Break time has exceeded the allowance", attentionSeverity: "warning"),
            arrivedLate: false, leaveAuthorizedForSubject: true);

        Assert.Equal("over_break", result.AttentionType);
        Assert.Equal("Break time has exceeded the allowance", result.AttentionLabel);
        Assert.Equal("warning", result.AttentionSeverity);
    }
}
