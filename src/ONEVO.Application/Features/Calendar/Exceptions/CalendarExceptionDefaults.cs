using ONEVO.Domain.Features.Monitoring.Exceptions.Entities;

namespace ONEVO.Application.Features.Calendar.Exceptions;

/// <summary>The calendar meeting-exception constants (calendar doc §6.3).</summary>
public static class CalendarExceptionDefaults
{
    public const int LateJoinMinutes = 10;
    public const int EarlyLeaveMinutes = 15;
    public const decimal MinAttendanceRatio = 0.50m;
    public const int LateCount = 5;
    public const int EarlyLeaveCount = 5;
    public const int NoResponseCount = 5;
    public const decimal DeclineRate = 0.50m;
    public const int DeclineRateMinInvites = 6;
    public const int LateDeclineHours = 2;
    public const int LateDeclineCount = 3;
    public const int DoubleBookOverlapMinutes = 15;
    public const int DoubleBookCount = 3;
    public const int DailyMeetingHours = 5;
    public const int OverloadDays = 3;
    public const int OffHoursCount = 3;
    public const int AttendanceReportCutoffHours = 24;
    public const int ManagerSlaDays = 3;
    public const int LongWindowDays = 30;
    public const int ShortWindowDays = 7;
    public const int IncidentRetentionDays = 90;
}

public static class CalendarRuleCodes
{
    public const string NoShow = "CAL-ATT-NOSHOW";
    public const string Late = "CAL-ATT-LATE";
    public const string Early = "CAL-ATT-EARLY";
    public const string NoResponse = "CAL-RSVP-NORESPONSE";
    public const string DeclineRate = "CAL-RSVP-DECLINERATE";
    public const string LateDecline = "CAL-RSVP-LATEDECLINE";
    public const string Unresolved = "CAL-RSVP-UNRESOLVED";
    public const string DoubleBook = "CAL-LOAD-DOUBLEBOOK";
    public const string OnLeave = "CAL-LOAD-ONLEAVE";
    public const string Overload = "CAL-LOAD-OVERLOAD";
    public const string OffHours = "CAL-LOAD-OFFHOURS";
}

/// <summary>How one rule rolls up into a case: the case type, how many incidents in how many days open it (0 = every
/// incident opens a case at once), and its severity.</summary>
public sealed record CalendarRuleSpec(string Code, ExceptionType Type, string Title, int Threshold, int WindowDays, string Severity)
{
    public bool IsImmediate => Threshold == 0;
}

public static class CalendarRuleCatalog
{
    public const string Low = "low";
    public const string Medium = "medium";
    public const string High = "high";

    public static readonly IReadOnlyDictionary<string, CalendarRuleSpec> Specs = new[]
    {
        // Immediate: one missed meeting (accepted or unanswered, then absent per the Teams/Zoom report or the desktop
        // app usage) opens a case. Further misses attach to the open case.
        new CalendarRuleSpec(CalendarRuleCodes.NoShow, ExceptionType.MeetingNoShow, "Missed meeting",
            0, CalendarExceptionDefaults.LongWindowDays, Medium),
        new CalendarRuleSpec(CalendarRuleCodes.Late, ExceptionType.MeetingLateJoin, "Joins meetings late",
            CalendarExceptionDefaults.LateCount, CalendarExceptionDefaults.LongWindowDays, Low),
        new CalendarRuleSpec(CalendarRuleCodes.Early, ExceptionType.MeetingEarlyLeave, "Leaves meetings early",
            CalendarExceptionDefaults.EarlyLeaveCount, CalendarExceptionDefaults.LongWindowDays, Low),
        new CalendarRuleSpec(CalendarRuleCodes.NoResponse, ExceptionType.MeetingRsvp, "Does not answer meeting invitations",
            CalendarExceptionDefaults.NoResponseCount, CalendarExceptionDefaults.LongWindowDays, Low),
        new CalendarRuleSpec(CalendarRuleCodes.DeclineRate, ExceptionType.MeetingRsvp, "Declines most meeting invitations",
            0, CalendarExceptionDefaults.LongWindowDays, Low),
        new CalendarRuleSpec(CalendarRuleCodes.LateDecline, ExceptionType.MeetingRsvp, "Declines meetings at the last minute",
            CalendarExceptionDefaults.LateDeclineCount, CalendarExceptionDefaults.LongWindowDays, Low),
        new CalendarRuleSpec(CalendarRuleCodes.Unresolved, ExceptionType.MeetingRsvp, "Meeting request left unanswered by the organizer",
            0, CalendarExceptionDefaults.LongWindowDays, Medium),
        new CalendarRuleSpec(CalendarRuleCodes.DoubleBook, ExceptionType.MeetingConflict, "Double-booked meetings",
            CalendarExceptionDefaults.DoubleBookCount, CalendarExceptionDefaults.ShortWindowDays, Low),
        new CalendarRuleSpec(CalendarRuleCodes.OnLeave, ExceptionType.MeetingConflict, "Meeting during approved leave",
            0, CalendarExceptionDefaults.ShortWindowDays, Medium),
        new CalendarRuleSpec(CalendarRuleCodes.Overload, ExceptionType.MeetingOverload, "Too many meeting hours",
            CalendarExceptionDefaults.OverloadDays, CalendarExceptionDefaults.ShortWindowDays, Low),
        new CalendarRuleSpec(CalendarRuleCodes.OffHours, ExceptionType.MeetingOverload, "Meetings outside working hours",
            CalendarExceptionDefaults.OffHoursCount, CalendarExceptionDefaults.ShortWindowDays, Low),
    }.ToDictionary(s => s.Code);
}
