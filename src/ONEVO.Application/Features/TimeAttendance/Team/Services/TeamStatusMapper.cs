using ONEVO.Application.Features.TimeAttendance.Services;
using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Application.Features.TimeAttendance.Team.Services;

/// <summary>My Team's Team Status vocabulary (spec §8.1.2, §8.1.6): "working", "on_break",
/// "clocked_out", "late" (overlay), "absent", "not_started", "on_leave", "not_scheduled". Pure:
/// maps the existing AttendanceDayStatusResolver output plus the record's persisted arrival
/// punctuality (AttendanceRecord.Status == "late", set once at clock-in - a distinct concept from
/// the resolver's own live day-state "Status") into that vocabulary, applying leave masking (spec
/// §8.1.2 rule 5 / D6): for a subject the caller is not Leave-authorized for, an `on_time_off` day
/// masks to "absent" with no attention fields (indistinguishable from a real no-show), AND a
/// `worked_during_time_off` day keeps its "working" status but has its attention fields stripped -
/// the leave fact behind the attention is masked even though the underlying clock-in activity
/// itself is not a leave-gated fact.</summary>
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

        // Masking (spec §8.1.2 rule 5): an unauthorized viewer's "absent" row - no-show or masked
        // leave alike - carries no attention fields, so the two causes are indistinguishable; and
        // a "worked during time off" row keeps its working status but loses the attention fields
        // that would otherwise reveal the leave behind it.
        var maskAttention = !leaveAuthorizedForSubject
            && (resolution.Status == AttendanceRecord.StatusOnTimeOff
                || resolution.Status == AttendanceRecord.StatusWorkedDuringTimeOff);
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
