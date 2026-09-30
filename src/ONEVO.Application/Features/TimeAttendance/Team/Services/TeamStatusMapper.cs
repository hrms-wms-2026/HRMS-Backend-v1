using ONEVO.Application.Features.TimeAttendance.Services;
using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Application.Features.TimeAttendance.Team.Services;

/// <summary>My Team's Team Status vocabulary (My Team spec §8.1.2, §8.1.6): "working",
/// "on_break", "clocked_out", "late" (overlay), "absent", "not_started", "on_leave",
/// "not_scheduled". Pure: maps the existing AttendanceDayStatusResolver output plus the record's
/// persisted arrival punctuality (AttendanceRecord.Status == "late", set once at clock-in - a
/// distinct concept from the resolver's own live day-state "Status") into that vocabulary,
/// applying leave masking (spec D6, review clarification: for a subject the caller is not
/// Leave-authorized for, EVERY absent row - whether the true cause is a no-show or a masked
/// approved-leave day - carries NO attention fields, so the two causes are indistinguishable to
/// an attendance-only viewer).</summary>
public static class TeamStatusMapper
{
    public static TeamStatusResult Map(
        AttendanceDayStatusResolution resolution, bool arrivedLate, bool leaveAuthorizedForSubject)
    {
        var (teamStatus, showLeaveDetail) = resolution.Status switch
        {
            AttendanceRecord.StatusActive => ("working", false),
            AttendanceRecord.StatusOnBreak => ("on_break", false),
            AttendanceRecord.StatusOverBreak => ("on_break", false),
            AttendanceRecord.StatusClockedOut => ("clocked_out", false),
            AttendanceRecord.StatusMissingClockOut => ("working", false),
            AttendanceRecord.StatusWorkedDuringTimeOff => ("working", false),
            AttendanceRecord.StatusWorkedOnNonWorkingDay => ("working", false),
            AttendanceRecord.StatusOnTimeOff => leaveAuthorizedForSubject ? ("on_leave", true) : ("absent", false),
            AttendanceRecord.StatusNonWorkingDay => ("not_scheduled", false),
            AttendanceRecord.StatusNoSchedule => ("not_scheduled", false),
            AttendanceRecord.StatusPolicyNotConfigured => ("not_scheduled", false),
            AttendanceRecord.StatusNotClockedIn => resolution.ShouldHaveClockedIn ? ("absent", false) : ("not_started", false),
            _ => ("not_scheduled", false),
        };

        // Masking (spec D6 / review clarification): an unauthorized viewer's "absent" row - no-show
        // or masked leave alike - carries no attention fields, so the two causes are indistinguishable.
        var maskAttention = teamStatus == "absent" && !leaveAuthorizedForSubject && resolution.Status == AttendanceRecord.StatusOnTimeOff;
        var isLate = arrivedLate && teamStatus is "working" or "on_break";

        return new TeamStatusResult(
            teamStatus,
            isLate,
            maskAttention ? null : resolution.AttentionType,
            maskAttention ? null : resolution.AttentionLabel,
            maskAttention ? null : resolution.AttentionSeverity,
            showLeaveDetail);
    }
}

/// <summary>ShowLeaveDetail tells the caller whether to attach leave type/end-date to this row -
/// true only for "on_leave" (already leave-authorized by construction, per Map above).</summary>
public sealed record TeamStatusResult(
    string TeamStatus, bool IsLate, string? AttentionType, string? AttentionLabel, string? AttentionSeverity, bool ShowLeaveDetail);
