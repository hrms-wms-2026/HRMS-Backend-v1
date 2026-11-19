using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Application.Features.Calendar.Exceptions;

public sealed record CalendarRsvpChange(string From, string To, DateTimeOffset At);

public sealed record CalendarAttendanceSpan(DateTimeOffset JoinedAt, DateTimeOffset? LeftAt);

/// <summary>Whether a provider attendance report can be used for a meeting occurrence.</summary>
public enum CalendarReportState
{
    /// <summary>No Teams/Zoom meeting: only the desktop agent can tell.</summary>
    NoOnlineMeeting,
    /// <summary>The provider report is not in yet (retried until the cutoff): attendance rules wait.</summary>
    Pending,
    /// <summary>The provider report arrived; <see cref="CalendarMeetingView.Attendance"/> holds this employee's matched rows.</summary>
    Received,
    /// <summary>No report by the cutoff: the desktop agent's estimate is used.</summary>
    Unavailable,
}

/// <summary>One meeting occurrence as one employee sees it. <see cref="PeopleCount"/> counts the organizer and every
/// participant once. <see cref="Attendance"/> only holds rows matched to this employee (work email or a connected
/// calendar account's email) - unmatched rows are never evidence.</summary>
public sealed record CalendarMeetingView(
    Guid EventId, DateTimeOffset Start, DateTimeOffset End, string Title, bool IsAllDay, bool IsCancelled,
    bool EmployeeIsOrganizer, int PeopleCount, Guid? OrganizerEmployeeId, bool OrganizerIsReportingManager,
    string ResponseStatus, IReadOnlyList<CalendarRsvpChange> RsvpHistory, bool ReplacementAttended,
    CalendarReportState Report, IReadOnlyList<CalendarAttendanceSpan> Attendance, decimal? AgentUsageRatio,
    DateTimeOffset? KeyStart = null)
{
    public TimeSpan Duration => End - Start;

    /// <summary>The occurrence start in the incident key: <see cref="KeyStart"/> when the source keeps a stable one (a task
    /// meeting's original start survives a reschedule), otherwise <see cref="Start"/>. Rules time things by Start.</summary>
    public DateTimeOffset OccurrenceKey => KeyStart ?? Start;

    /// <summary>The answer the employee had given when the meeting started.</summary>
    public string StatusAt(DateTimeOffset at)
    {
        var before = RsvpHistory.Where(h => h.At <= at).OrderBy(h => h.At).LastOrDefault();
        if (before is not null)
            return before.To;
        // No change before then: the first recorded change tells where they started; no history at all = the current answer.
        return RsvpHistory.OrderBy(h => h.At).FirstOrDefault()?.From ?? ResponseStatus;
    }
}

public sealed record CalendarInterval(DateTimeOffset From, DateTimeOffset To)
{
    public bool Overlaps(DateTimeOffset from, DateTimeOffset to) => From < to && from < To;
}

/// <summary>The employee's working hours in their company's time zone; <see cref="Days"/> are ISO weekdays (Mon=1..Sun=7).</summary>
public sealed record CalendarWorkingHours(TimeZoneInfo Zone, TimeOnly Start, TimeOnly End, IReadOnlySet<int> Days);

public sealed record CalendarIncidentInput(
    Guid EmployeeId, DateTimeOffset Now, IReadOnlyList<CalendarMeetingView> Meetings,
    IReadOnlyList<CalendarInterval> Leave, IReadOnlyList<CalendarInterval> Holidays, CalendarWorkingHours? Hours);

/// <summary>One rule match. <see cref="Immediate"/> opens a case at once (an immediate rule, or no-show's manager/1:1 weighting).</summary>
public sealed record CalendarIncidentFinding(
    string RuleCode, Guid EventId, DateTimeOffset OccurrenceStart, string Confidence, IReadOnlyDictionary<string, object?> Evidence,
    bool Immediate = false, string? Severity = null);

/// <summary>A calendar meeting-exception rule (calendar doc §6.2): pure over one employee's meetings.</summary>
public interface ICalendarIncidentRule
{
    string Code { get; }
    IEnumerable<CalendarIncidentFinding> Evaluate(CalendarIncidentInput input);
}

/// <summary>The exclusions every rule applies (calendar doc §6.1).</summary>
public static class CalendarIncidentExclusions
{
    public static bool Excluded(CalendarMeetingView m, CalendarIncidentInput input, bool attendanceRule, bool leaveExcludes = true)
    {
        if (m.IsAllDay || m.IsCancelled || m.EmployeeIsOrganizer || m.PeopleCount <= 1)
            return true;
        if (input.Holidays.Any(h => h.Overlaps(m.Start, m.End)))
            return true;
        if (leaveExcludes && input.Leave.Any(l => l.Overlaps(m.Start, m.End)))
            return true;
        return attendanceRule && (m.ResponseStatus == CalendarEventParticipantStatuses.Rejected || m.ReplacementAttended);
    }

    public static bool AcceptedOrPending(string status)
        => status is CalendarEventParticipantStatuses.Accepted or CalendarEventParticipantStatuses.Pending;

    public static Dictionary<string, object?> Evidence(CalendarMeetingView m, params (string Key, object? Value)[] more)
    {
        var evidence = new Dictionary<string, object?>
        {
            ["title"] = m.Title, ["start"] = m.Start, ["end"] = m.End, ["rsvp"] = m.ResponseStatus,
            ["organizerEmployeeId"] = m.OrganizerEmployeeId,
        };
        foreach (var (key, value) in more)
            evidence[key] = value;
        return evidence;
    }
}

/// <summary>CAL-ATT-NOSHOW: accepted or pending, the meeting ended and the employee never joined (provider) or used a
/// meeting app for less than half of it (agent, "estimated"). Every no-show opens a case (the rule is immediate); severity
/// is high when the reporting manager organised it or it is a 1:1.</summary>
public sealed class NoShowRule : ICalendarIncidentRule
{
    public string Code => CalendarRuleCodes.NoShow;

    public IEnumerable<CalendarIncidentFinding> Evaluate(CalendarIncidentInput input)
    {
        foreach (var m in input.Meetings)
        {
            if (m.End > input.Now || !CalendarIncidentExclusions.AcceptedOrPending(m.ResponseStatus)
                || CalendarIncidentExclusions.Excluded(m, input, attendanceRule: true))
                continue;
            string? confidence = m.Report switch
            {
                CalendarReportState.Received when m.Attendance.Count == 0 => CalendarIncidentConfidence.Provider,
                CalendarReportState.Unavailable or CalendarReportState.NoOnlineMeeting
                    when m.AgentUsageRatio is decimal ratio && ratio < CalendarExceptionDefaults.MinAttendanceRatio => CalendarIncidentConfidence.Agent,
                _ => null,
            };
            if (confidence is null)
                continue;
            var weighted = m.OrganizerIsReportingManager || m.PeopleCount == 2;
            yield return new CalendarIncidentFinding(Code, m.EventId, m.OccurrenceKey, confidence,
                CalendarIncidentExclusions.Evidence(m, ("agentUsageRatio", confidence == CalendarIncidentConfidence.Agent ? m.AgentUsageRatio : null),
                    ("organizedByManager", m.OrganizerIsReportingManager), ("oneToOne", m.PeopleCount == 2)),
                Immediate: weighted, Severity: weighted ? CalendarRuleCatalog.High : null);
        }
    }
}

/// <summary>CAL-ATT-LATE: first join more than <see cref="CalendarExceptionDefaults.LateJoinMinutes"/> after the start (provider only).</summary>
public sealed class LateJoinRule : ICalendarIncidentRule
{
    public string Code => CalendarRuleCodes.Late;

    public IEnumerable<CalendarIncidentFinding> Evaluate(CalendarIncidentInput input)
    {
        foreach (var m in input.Meetings)
        {
            if (m.Report != CalendarReportState.Received || m.Attendance.Count == 0
                || CalendarIncidentExclusions.Excluded(m, input, attendanceRule: true))
                continue;
            var firstJoin = m.Attendance.Min(a => a.JoinedAt);
            var minutesLate = (firstJoin - m.Start).TotalMinutes;
            if (minutesLate > CalendarExceptionDefaults.LateJoinMinutes)
                yield return new CalendarIncidentFinding(Code, m.EventId, m.OccurrenceKey, CalendarIncidentConfidence.Provider,
                    CalendarIncidentExclusions.Evidence(m, ("joinedAt", firstJoin), ("minutesLate", (int)minutesLate)));
        }
    }
}

/// <summary>CAL-ATT-EARLY: left more than <see cref="CalendarExceptionDefaults.EarlyLeaveMinutes"/> before the end, or attended
/// less than half of it (provider). An agent estimate below half is already a no-show, so it is not counted twice.</summary>
public sealed class EarlyLeaveRule : ICalendarIncidentRule
{
    public string Code => CalendarRuleCodes.Early;

    public IEnumerable<CalendarIncidentFinding> Evaluate(CalendarIncidentInput input)
    {
        foreach (var m in input.Meetings)
        {
            if (m.Report != CalendarReportState.Received || m.Attendance.Count == 0 || m.Duration <= TimeSpan.Zero
                || CalendarIncidentExclusions.Excluded(m, input, attendanceRule: true))
                continue;
            var lastLeft = m.Attendance.Max(a => a.LeftAt ?? m.End);
            var ratio = AttendedRatio(m);
            var minutesEarly = (m.End - lastLeft).TotalMinutes;
            if (minutesEarly > CalendarExceptionDefaults.EarlyLeaveMinutes || ratio < CalendarExceptionDefaults.MinAttendanceRatio)
                yield return new CalendarIncidentFinding(Code, m.EventId, m.OccurrenceKey, CalendarIncidentConfidence.Provider,
                    CalendarIncidentExclusions.Evidence(m, ("leftAt", lastLeft), ("attendedRatio", Math.Round(ratio, 2))));
        }
    }

    /// <summary>The share of the meeting covered by the employee's join/leave spans (overlaps merged).</summary>
    public static decimal AttendedRatio(CalendarMeetingView m)
    {
        var spans = m.Attendance
            .Select(a => (From: a.JoinedAt < m.Start ? m.Start : a.JoinedAt, To: (a.LeftAt ?? m.End) > m.End ? m.End : a.LeftAt ?? m.End))
            .Where(s => s.To > s.From).OrderBy(s => s.From).ToList();
        var covered = TimeSpan.Zero;
        DateTimeOffset? curFrom = null, curTo = null;
        foreach (var (from, to) in spans)
        {
            if (curTo is null || from > curTo)
            {
                if (curTo is not null)
                    covered += curTo.Value - curFrom!.Value;
                (curFrom, curTo) = (from, to);
            }
            else if (to > curTo)
                curTo = to;
        }
        if (curTo is not null)
            covered += curTo.Value - curFrom!.Value;
        return (decimal)(covered.TotalSeconds / m.Duration.TotalSeconds);
    }
}

/// <summary>CAL-RSVP-NORESPONSE: the invitation was still unanswered when the meeting started.</summary>
public sealed class NoResponseRule : ICalendarIncidentRule
{
    public string Code => CalendarRuleCodes.NoResponse;

    public IEnumerable<CalendarIncidentFinding> Evaluate(CalendarIncidentInput input)
        => input.Meetings
            .Where(m => m.Start <= input.Now && !CalendarIncidentExclusions.Excluded(m, input, attendanceRule: false)
                        && m.StatusAt(m.Start) == CalendarEventParticipantStatuses.Pending)
            .Select(m => new CalendarIncidentFinding(Code, m.EventId, m.OccurrenceKey, CalendarIncidentConfidence.Provider,
                CalendarIncidentExclusions.Evidence(m, ("rsvpAtStart", CalendarEventParticipantStatuses.Pending))));
}

/// <summary>CAL-RSVP-LATEDECLINE: accepted, then declined less than <see cref="CalendarExceptionDefaults.LateDeclineHours"/> before the start.</summary>
public sealed class LateDeclineRule : ICalendarIncidentRule
{
    public string Code => CalendarRuleCodes.LateDecline;

    public IEnumerable<CalendarIncidentFinding> Evaluate(CalendarIncidentInput input)
    {
        foreach (var m in input.Meetings)
        {
            if (CalendarIncidentExclusions.Excluded(m, input, attendanceRule: false))
                continue;
            var late = m.RsvpHistory.LastOrDefault(h => h.From == CalendarEventParticipantStatuses.Accepted
                && h.To == CalendarEventParticipantStatuses.Rejected
                && h.At < m.Start && h.At >= m.Start.AddHours(-CalendarExceptionDefaults.LateDeclineHours));
            if (late is not null)
                yield return new CalendarIncidentFinding(Code, m.EventId, m.OccurrenceKey, CalendarIncidentConfidence.Provider,
                    CalendarIncidentExclusions.Evidence(m, ("declinedAt", late.At)));
        }
    }
}

/// <summary>CAL-RSVP-UNRESOLVED: the employee's resolution request or replacement nomination was still unanswered by the
/// organizer at the start. Immediate, on the requesting employee (an FYI to their manager).</summary>
public sealed class UnresolvedRequestRule : ICalendarIncidentRule
{
    public string Code => CalendarRuleCodes.Unresolved;

    public IEnumerable<CalendarIncidentFinding> Evaluate(CalendarIncidentInput input)
        => input.Meetings
            .Where(m => m.Start <= input.Now && !CalendarIncidentExclusions.Excluded(m, input, attendanceRule: false)
                        && m.StatusAt(m.Start) is CalendarEventParticipantStatuses.ResolutionRequested or CalendarEventParticipantStatuses.ReplacementNominated)
            .Select(m => new CalendarIncidentFinding(Code, m.EventId, m.OccurrenceKey, CalendarIncidentConfidence.Provider,
                CalendarIncidentExclusions.Evidence(m, ("request", m.StatusAt(m.Start))), Immediate: true));
}

/// <summary>CAL-RSVP-DECLINERATE: at least half of the invitations of the last 30 days declined (minimum 6 invitations).
/// One finding per 30-day window, anchored to the window's first day.</summary>
public sealed class DeclineRateRule : ICalendarIncidentRule
{
    public string Code => CalendarRuleCodes.DeclineRate;

    public IEnumerable<CalendarIncidentFinding> Evaluate(CalendarIncidentInput input)
    {
        var from = input.Now.AddDays(-CalendarExceptionDefaults.LongWindowDays);
        var invites = input.Meetings
            .Where(m => m.Start >= from && m.Start <= input.Now && !CalendarIncidentExclusions.Excluded(m, input, attendanceRule: false))
            .ToList();
        if (invites.Count < CalendarExceptionDefaults.DeclineRateMinInvites)
            yield break;
        var declined = invites.Count(m => m.ResponseStatus == CalendarEventParticipantStatuses.Rejected);
        var rate = (decimal)declined / invites.Count;
        if (rate < CalendarExceptionDefaults.DeclineRate)
            yield break;
        // Anchored to the 30-day period the date falls in, so a nightly run does not record a new incident every day.
        var epoch = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var period = (int)((input.Now - epoch).TotalDays / CalendarExceptionDefaults.LongWindowDays);
        yield return new CalendarIncidentFinding(Code, Guid.Empty, epoch.AddDays(period * CalendarExceptionDefaults.LongWindowDays),
            CalendarIncidentConfidence.Provider,
            new Dictionary<string, object?> { ["invites"] = invites.Count, ["declined"] = declined, ["rate"] = Math.Round(rate, 2) },
            Immediate: true);
    }
}

/// <summary>CAL-LOAD-DOUBLEBOOK: two accepted/pending meetings overlapping by at least
/// <see cref="CalendarExceptionDefaults.DoubleBookOverlapMinutes"/> minutes - recorded on the later one.</summary>
public sealed class DoubleBookRule : ICalendarIncidentRule
{
    public string Code => CalendarRuleCodes.DoubleBook;

    public IEnumerable<CalendarIncidentFinding> Evaluate(CalendarIncidentInput input)
    {
        var meetings = input.Meetings
            .Where(m => CalendarIncidentExclusions.AcceptedOrPending(m.ResponseStatus) && !CalendarIncidentExclusions.Excluded(m, input, attendanceRule: false))
            .OrderBy(m => m.Start).ThenBy(m => m.EventId).ToList();
        var flagged = new HashSet<(Guid, DateTimeOffset)>();
        for (var i = 0; i < meetings.Count; i++)
        {
            for (var j = i + 1; j < meetings.Count && meetings[j].Start < meetings[i].End; j++)
            {
                var overlapFrom = meetings[j].Start > meetings[i].Start ? meetings[j].Start : meetings[i].Start;
                var overlapTo = meetings[j].End < meetings[i].End ? meetings[j].End : meetings[i].End;
                if ((overlapTo - overlapFrom).TotalMinutes < CalendarExceptionDefaults.DoubleBookOverlapMinutes)
                    continue;
                var later = meetings[j];
                if (flagged.Add((later.EventId, later.Start)))
                    yield return new CalendarIncidentFinding(Code, later.EventId, later.OccurrenceKey, CalendarIncidentConfidence.Provider,
                        CalendarIncidentExclusions.Evidence(later, ("overlapsEventId", meetings[i].EventId), ("overlapsTitle", meetings[i].Title),
                            ("overlapMinutes", (int)(overlapTo - overlapFrom).TotalMinutes)));
            }
        }
    }
}

/// <summary>CAL-LOAD-ONLEAVE: a meeting the employee did not decline falls in their approved leave. Immediate; it resolves
/// itself once they decline or are removed.</summary>
public sealed class OnLeaveRule : ICalendarIncidentRule
{
    public string Code => CalendarRuleCodes.OnLeave;

    public IEnumerable<CalendarIncidentFinding> Evaluate(CalendarIncidentInput input)
        => input.Meetings
            .Where(m => m.ResponseStatus != CalendarEventParticipantStatuses.Rejected
                        // Leave is what this rule is about, so it is not an exclusion here.
                        && !CalendarIncidentExclusions.Excluded(m, input, attendanceRule: false, leaveExcludes: false)
                        && input.Leave.Any(l => l.Overlaps(m.Start, m.End)))
            .Select(m => new CalendarIncidentFinding(Code, m.EventId, m.OccurrenceKey, CalendarIncidentConfidence.Provider,
                CalendarIncidentExclusions.Evidence(m), Immediate: true));
}

/// <summary>CAL-LOAD-OVERLOAD: more than <see cref="CalendarExceptionDefaults.DailyMeetingHours"/> hours of accepted meetings in
/// one working day (overlapping meetings counted once) - one finding per day.</summary>
public sealed class OverloadRule : ICalendarIncidentRule
{
    public string Code => CalendarRuleCodes.Overload;

    public IEnumerable<CalendarIncidentFinding> Evaluate(CalendarIncidentInput input)
    {
        var zone = input.Hours?.Zone ?? TimeZoneInfo.Utc;
        var byDay = input.Meetings
            .Where(m => m.ResponseStatus == CalendarEventParticipantStatuses.Accepted && !CalendarIncidentExclusions.Excluded(m, input, attendanceRule: false))
            .GroupBy(m => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(m.Start, zone).DateTime));
        foreach (var day in byDay)
        {
            var hours = Merged(day.Select(m => (m.Start, m.End))).TotalHours;
            if (hours <= CalendarExceptionDefaults.DailyMeetingHours)
                continue;
            var local = day.Key.ToDateTime(TimeOnly.MinValue);
            var dayStart = new DateTimeOffset(local, zone.GetUtcOffset(local));
            yield return new CalendarIncidentFinding(Code, Guid.Empty, dayStart, CalendarIncidentConfidence.Provider,
                new Dictionary<string, object?> { ["day"] = day.Key.ToString("yyyy-MM-dd"), ["meetingHours"] = Math.Round(hours, 1), ["meetings"] = day.Count() });
        }
    }

    private static TimeSpan Merged(IEnumerable<(DateTimeOffset From, DateTimeOffset To)> spans)
    {
        var total = TimeSpan.Zero;
        DateTimeOffset? curFrom = null, curTo = null;
        foreach (var (from, to) in spans.OrderBy(s => s.From))
        {
            if (curTo is null || from > curTo)
            {
                if (curTo is not null)
                    total += curTo.Value - curFrom!.Value;
                (curFrom, curTo) = (from, to);
            }
            else if (to > curTo)
                curTo = to;
        }
        return curTo is null ? total : total + (curTo.Value - curFrom!.Value);
    }
}

/// <summary>CAL-LOAD-OFFHOURS: an accepted meeting outside the employee's working hours or on a non-working day.</summary>
public sealed class OffHoursRule : ICalendarIncidentRule
{
    public string Code => CalendarRuleCodes.OffHours;

    public IEnumerable<CalendarIncidentFinding> Evaluate(CalendarIncidentInput input)
    {
        if (input.Hours is not { } hours)
            yield break;
        foreach (var m in input.Meetings)
        {
            if (m.ResponseStatus != CalendarEventParticipantStatuses.Accepted || CalendarIncidentExclusions.Excluded(m, input, attendanceRule: false))
                continue;
            var start = TimeZoneInfo.ConvertTime(m.Start, hours.Zone);
            var end = TimeZoneInfo.ConvertTime(m.End, hours.Zone);
            var day = DateOnly.FromDateTime(start.DateTime);
            var iso = day.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)day.DayOfWeek;
            var outside = !hours.Days.Contains(iso)
                || TimeOnly.FromDateTime(start.DateTime) < hours.Start
                || DateOnly.FromDateTime(end.DateTime) != day
                || TimeOnly.FromDateTime(end.DateTime) > hours.End;
            if (outside)
                yield return new CalendarIncidentFinding(Code, m.EventId, m.OccurrenceKey, CalendarIncidentConfidence.Provider,
                    CalendarIncidentExclusions.Evidence(m, ("workStart", hours.Start.ToString("HH:mm")), ("workEnd", hours.End.ToString("HH:mm"))));
        }
    }
}
