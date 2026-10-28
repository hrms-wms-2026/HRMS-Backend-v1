namespace ONEVO.Application.Features.WorkManagement.Planner.TeamSchedule;

/// <summary>Today's Team Schedule (My Team widgets design 2026-10-29, section 6.2): the caller's transitive reports, each on
/// their own local day. PlannerEnabled false = no plan figures (blocks, scheduled and unscheduled minutes, scores); meetings
/// and clocked minutes still filled.</summary>
public sealed record TeamScheduleResponse(DateOnly Date, bool PlannerEnabled, DateTimeOffset AsOf, TeamScheduleSummary Summary,
    IReadOnlyList<TeamScheduleMember> Members);

/// <summary>Task counts are per distinct (person, task); cancelled meetings are not counted.</summary>
public sealed record TeamScheduleSummary(int People, int TasksPlanned, int TasksCompleted, int TasksInProgress, int TasksPending,
    int Meetings, int PeopleWithUnscheduledWork, int UnscheduledMinutes, int Conflicts);

/// <param name="PresenceStatus">The Team Status status when the caller has attendance:read coverage of this person (today
/// only), else null.</param>
/// <param name="NotRankedReason">none | no_items_yet | no_planned_task_time | on_leave | planner_off.</param>
public sealed record TeamScheduleMember(Guid EmployeeId, string DisplayName, Guid? AvatarFileId, string Timezone, string? PresenceStatus,
    int PlannedTaskMinutes, int PlannedMeetingMinutes, int ClockedMinutes, int ScheduledMinutes, int OutsideSlotMinutes, int NoPlanMinutes,
    decimal? ItemCompletionPercent, decimal? TimeCoveragePercent, decimal? Score, string NotRankedReason, int? Rank,
    IReadOnlyList<TeamScheduleItem> Items, IReadOnlyList<TeamScheduleUnscheduled> Unscheduled, IReadOnlyList<TeamScheduleConflict> Conflicts);

/// <param name="Kind">task | meeting.</param>
/// <param name="Status">Tasks: completed | in_progress | pending | scheduled_not_clocked. Meetings: upcoming | in_meeting |
/// finished | cancelled.</param>
/// <param name="Title">"Private meeting" for a private meeting.</param>
public sealed record TeamScheduleItem(string Kind, Guid TaskId, string Title, DateTimeOffset Start, DateTimeOffset End, string Status,
    bool IsPrivate);

/// <param name="Kind">no_plan | outside_slot.</param>
public sealed record TeamScheduleUnscheduled(Guid TaskId, string Title, DateTimeOffset Start, DateTimeOffset End, int Minutes, string Kind);

/// <param name="Kind">block_meeting | parallel_clock.</param>
public sealed record TeamScheduleConflict(string Kind, DateTimeOffset Start, DateTimeOffset End, string FirstTitle, string SecondTitle);
