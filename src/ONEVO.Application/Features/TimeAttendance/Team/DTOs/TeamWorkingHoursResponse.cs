namespace ONEVO.Application.Features.TimeAttendance.Team.DTOs;

/// <summary>Working / Break / Leave popup: the covered team's attendance worked and break minutes per day of one ISO week
/// (My Team widgets design 2026-10-29, section 6.4). Days oldest first; future days are zeros.</summary>
public sealed record TeamWorkingHoursResponse(DateOnly WeekStart, DateOnly WeekEnd, IReadOnlyList<TeamWorkingHoursDay> Days);

/// <param name="OnLeaveCount">Approved leave covering the day, counted only for subjects the caller is leave-authorized for.</param>
/// <param name="PeopleWithRecord">People who clocked in that day.</param>
public sealed record TeamWorkingHoursDay(DateOnly Date, int WorkedMinutes, int BreakMinutes, int OnLeaveCount, int PeopleWithRecord);
