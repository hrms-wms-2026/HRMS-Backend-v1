# My Team Three Widgets Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Upgrade Team Progress with planned-vs-worked time, unscheduled work and workload ranking; add Today's Team Schedule; turn Team Status into Working / Break / Leave.

**Architecture:** One pure attribution service splits clocked task time into scheduled / outside-slot / no-plan. New batched repository reads feed it for a set of tasks (Team Progress, led scope) or a set of employees (Team Schedule, reporting line). Working / Break / Leave extends the existing Team Status query. No new tables. Frontend cards follow the Team Attendance card pattern (host-size views, layout util, 140 px donut, Team-Progress popup shell).

**Tech Stack:** .NET 9, MediatR, EF Core + Postgres (RLS), xUnit; Angular (signals, standalone components), Vitest via `ng test`.

**Spec:** `docs/superpowers/specs/next/2026-10-29-my-team-three-widgets-design.md` (read it before any task).

Repos: BE = `C:/Users/User/Desktop/build/HRMS-Backend-v1`, FE = `C:/Users/User/Desktop/build/Hrms--Web-application---front-end---v1`. FE dashboard root below = `src/app/modules/dashboard`.

## Global Constraints

- Minimum widget size 2x2 (`minW = minH = 2`) on the backend registry and the FE.
- One donut size: `DASHBOARD_DONUT_PX = 140` (`utils/dashboard-donut.util.ts`); never scaled.
- Views come from the card's own size (`injectHostSize()`), thresholds in `utils/<widget>-layout.util.ts`, ~20 px hysteresis.
- Popup shell = `app-modal size="2xl"`, own header, full-screen under 640 px, one `app-kpi-tiles` row, CSV export on tables, no tabs.
- Components use `styleUrl`, never a shared imported styles const. `npm run check:warnings` must report 0.
- Visual tokens as in Team Attendance CSS: slate text scale, tile `color-mix(tone 8%/28%/75%)`, 40 px ranking rows, 52 px tiles.
- Colours: Working `#3b82f6`, Break `#f59e0b`, Leave `#ef4444`; task Completed `#10b981`, In progress `#3b82f6`, Pending/Not started `#cbd5e1`, Meeting `#8b5cf6`, Unscheduled amber stripes `#f59e0b`.
- Estimate hours, planned minutes, clocked minutes and attendance minutes are never summed or put on one axis.
- Zero denominator → `null` from the BE and "—" + reason in the FE; never 0% or 100%.
- Private meeting title for a manager = `"Private meeting"` everywhere (card, popup, tooltip, CSV).
- `Planner:Enabled = false` → `plannerEnabled: false` in responses; FE shows "Planner is off" instead of schedule figures.
- Execution score = `0.6 × itemCompletion + 0.4 × timeCoverage`, label "Execution score (today)".
- Do not apply migrations; none are expected. No `--no-verify`, no suppressed analyzers/tests.
- Commit messages end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Review Focus

1. An employee with **parallel open clock sessions on two tasks** — totals must not exceed real elapsed time per task, and a `parallelClockMinutes` count must surface (test in Task 1).
2. A **session that crosses local midnight** or a **block on an employee in a different timezone** from the manager — each employee's minutes land on *their* local date (tests in Tasks 1 and 6).
3. **Planner off** with clock data present — clocked time still shows, schedule split hidden, nothing reads as 0% (tests in Tasks 6, 8).
4. A **viewer not leave-authorized** for a subject — that subject is never counted under Leave and no leave type appears (test in Task 3).
5. **Empty scope** (leads nothing / no reports / everyone not clocked in) — widgets render an honest empty state, not zeros dressed as data (tests in Tasks 6, 8, 10, 11).

---

### Task 0: Branches (needs the user's go-ahead for the FE commit)

- [ ] **Step 1:** FE: show the user `git status` of `chore/uat-hardening` (uncommitted Quick Actions corner work) and ask whether to commit it there. Do not proceed without a yes.
- [ ] **Step 2:** FE: `git merge origin/development` into `chore/uat-hardening`; resolve conflicts; `npm run check:warnings` and `npx ng build` must pass.
- [ ] **Step 3:** Both repos: `git switch -c feature/my-team-three-widgets`.

---

### Task 1: Task-time attribution service (BE, pure)

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/Planner/Services/TaskTimeAttribution.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Planner/TaskTimeAttributionTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  public sealed record AttributionBlock(Guid TaskId, DateTimeOffset Start, DateTimeOffset End);
  public sealed record AttributionSession(Guid TaskId, DateTimeOffset ClockIn, DateTimeOffset? ClockOut);
  public enum UnscheduledKind { NoPlan, OutsideSlot }
  public sealed record UnscheduledSegment(Guid TaskId, DateTimeOffset Start, DateTimeOffset End, int Minutes, UnscheduledKind Kind);
  public sealed record TaskAttribution(Guid TaskId, int PlannedMinutes, int ClockedMinutes, int ScheduledMinutes, int OutsideSlotMinutes, int NoPlanMinutes);
  public sealed record AttributionResult(int PlannedMinutes, int ClockedMinutes, int ScheduledMinutes, int OutsideSlotMinutes,
      int NoPlanMinutes, int ParallelClockMinutes, IReadOnlyList<TaskAttribution> Tasks, IReadOnlyList<UnscheduledSegment> Unscheduled);
  public static class TaskTimeAttribution
  {
      public static AttributionResult Attribute(IEnumerable<AttributionBlock> blocks, IEnumerable<AttributionSession> sessions,
          DateTimeOffset from, DateTimeOffset to, DateTimeOffset now);
  }
  ```
  One call = one employee, one window. Invariant: `Scheduled + OutsideSlot + NoPlan == Clocked`.

- [ ] **Step 1: Write the failing tests** (xUnit, `Assert.Equal`, style of `PlannerActualsTests`; window = 2026-11-02 local day at +05:30):
  - `Brief_example_splits_scheduled_outside_and_no_plan`: block A 09:00–10:00; session A 09:20–10:20; session B 11:00–11:45 (no block) → Planned 60, Clocked 105, Scheduled 40, OutsideSlot 20, NoPlan 45; `Unscheduled` has 2 segments (A 10:00–10:20 OutsideSlot, B 11:00–11:45 NoPlan).
  - `Two_blocks_of_one_task_count_the_session_once`: blocks A 09–10 and A 11–12; session A 09:30–11:30 → Clocked 120, Scheduled 60, OutsideSlot 60.
  - `Overlapping_sessions_of_one_task_are_merged`: A 09:00–10:00 and A 09:30–10:30 → Clocked 90.
  - `Parallel_sessions_on_two_tasks_are_kept_and_flagged`: A 09–10, B 09:30–10:30 → Clocked 120, ParallelClockMinutes 30.
  - `Open_session_counts_until_now`: A 09:00–open, now 09:45 → Clocked 45.
  - `Session_crossing_midnight_is_clipped_to_the_window`: A from 23:00 previous day to 01:00 → Clocked 60.
  - `Blocks_outside_the_window_are_ignored`, `Zero_or_negative_sessions_are_dropped`.
- [ ] **Step 2:** Run `pwsh ../dev-env/scripts/verify.ps1 backend -Filter "FullyQualifiedName~TaskTimeAttribution"` → FAIL (type missing).
- [ ] **Step 3:** Implement `TaskTimeAttribution.Attribute`. Approach: clip every interval to `[from, min(to, now))` (open session end = `now`); per task, merge intervals (sort + sweep); scheduled = sum of pairwise intersections of the merged session and merged block lists of the same task; tasks with no block → NoPlan, else remainder → OutsideSlot; parallel = (sum of merged per-task clocked) − (length of the union over all tasks). Round minutes at the end of each sum (`Math.Round`, away from zero) and assign the rounding remainder to OutsideSlot/NoPlan so the invariant holds.
- [ ] **Step 4:** Re-run the filter → PASS.
- [ ] **Step 5:** Commit `feat(wm): task time attribution - scheduled, outside slot, no plan`.

---

### Task 2: Batched time reads (BE)

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/Planner/RepositoryInterfaces/ITeamTimeReadRepository.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/Planner/EfTeamTimeReadRepository.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Meetings/RepositoryInterfaces/IMeetingRepository.cs` and `EfMeetingRepository.cs` (add batched method)
- Modify: `src/ONEVO.Infrastructure/DependencyInjection.cs` (register next to `IPlannerRepository`)
- Test: `tests/ONEVO.Tests.Integration/WorkManagement/Planner/TeamTimeReadRepositoryTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  public sealed record TeamBlockRow(Guid EmployeeId, Guid TaskId, DateTimeOffset Start, DateTimeOffset End);
  public sealed record TeamSessionRow(Guid EmployeeId, Guid TaskId, DateTimeOffset ClockIn, DateTimeOffset? ClockOut);
  public sealed record TeamTaskRow(Guid TaskId, string ShortId, string Title, Guid? ObjectiveId, Guid? ProjectId,
      bool IsComplete, decimal ProgressPercent);
  public interface ITeamTimeReadRepository
  {
      Task<IReadOnlyList<TeamBlockRow>> ListBlocksForTasksAsync(Guid tenantId, IReadOnlyCollection<Guid> objectiveIds, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
      Task<IReadOnlyList<TeamSessionRow>> ListSessionsForTasksAsync(Guid tenantId, IReadOnlyCollection<Guid> objectiveIds, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
      Task<IReadOnlyList<TeamBlockRow>> ListBlocksForEmployeesAsync(Guid tenantId, IReadOnlyCollection<Guid> employeeIds, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
      Task<IReadOnlyList<TeamSessionRow>> ListSessionsForEmployeesAsync(Guid tenantId, IReadOnlyCollection<Guid> employeeIds, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
      Task<IReadOnlyDictionary<Guid, TeamTaskRow>> GetTasksAsync(Guid tenantId, IReadOnlyCollection<Guid> taskIds, CancellationToken ct = default);
      Task<IReadOnlyDictionary<Guid, string>> GetTimezonesAsync(Guid tenantId, IReadOnlyCollection<Guid> employeeIds, CancellationToken ct = default);
  }
  // IMeetingRepository
  Task<IReadOnlyDictionary<Guid, IReadOnlyList<EmployeeDayMeeting>>> ListEmployeesDayMeetingsAsync(
      Guid tenantId, IReadOnlyCollection<Guid> employeeIds, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
  ```
  Rules: overlap = `start < to && end > from` (open session: `clockIn < to`); blocks `!IsDeleted`; tasks not deleted, not meetings; "for tasks" = tasks whose `ObjectiveId` is in the given (already subtree-expanded) Module ids, any depth of task (subtasks included); `IsComplete` = status `MarksTaskComplete` or progress ≥ 100 (same as `LedTaskBucket`). `GetTimezonesAsync` returns an IANA id per employee by the same order as `ICalendarTimezoneResolver` (employee `DisplayTimezone`, legal entity `Timezone`, `"UTC"`). The meeting method applies exactly the rules of `ListEmployeeDayMeetingsAsync` (one query per table, grouped in memory) — reuse its predicate, do not fork it.

- [ ] **Step 1:** Write integration tests (Testcontainers, pattern of `PlannerServiceIntegrationTests`): `Blocks_for_tasks_include_subtasks_and_skip_deleted`, `Sessions_for_employees_include_open_session_started_before_window`, `Batched_meetings_match_single_employee_reader` (seed 2 employees, compare to `ListEmployeeDayMeetingsAsync` per employee), `Other_tenant_rows_are_invisible`.
- [ ] **Step 2:** `pwsh ../dev-env/scripts/verify.ps1 backend -Full` (Docker) with the new tests → FAIL.
- [ ] **Step 3:** Implement the repository and the meeting method. Single query per call, `AsNoTracking`, existing indexes. Add an index migration only if `EXPLAIN` on the dapi data shows a seq scan on `planned_task_blocks` by task — report it, do not apply.
- [ ] **Step 4:** Re-run → PASS.
- [ ] **Step 5:** Commit `feat(wm): batched block, session and meeting reads for team widgets`.

---

### Task 3: Working / Break / Leave fields on Team Status (BE)

**Files:**
- Modify: `src/ONEVO.Application/Features/TimeAttendance/Team/DTOs/TeamTodayResponse.cs`
- Modify: `src/ONEVO.Application/Features/TimeAttendance/Queries/AttendanceReadHandlers.cs` (`Handle(GetCoveredTeamTodayQuery…)`, ~line 543)
- Create: `src/ONEVO.Application/Features/TimeAttendance/Team/Services/TeamPresenceMath.cs`
- Modify: `src/ONEVO.Application/Features/Dashboard/Layout/Registry/DashboardWidgetRegistry.cs:30` (title "Working / Break / Leave", description "Who in your team is working, on a break or on leave right now.")
- Test: `tests/ONEVO.Tests.Unit/Features/TimeAttendance/Team/TeamPresenceMathTests.cs`, `GetCoveredTeamTodayQueryHandlerTests.cs`

**Interfaces:**
- Produces (appended to `TeamTodayMember`, nullable, after `Leave`): `DateTimeOffset? StateSince, int? BreakRemainingMinutes, DateTimeOffset? ShiftEndsAt, int AttendanceMinutes`. Appended to `TeamTodayResponse`: `DateTimeOffset AsOf`.
  ```csharp
  public static class TeamPresenceMath
  {
      public static DateTimeOffset? StateSince(string teamStatus, AttendanceRecord? record, IReadOnlyList<BreakRecord> breaks);
      public static int? BreakRemainingMinutes(string teamStatus, int? breakDurationMinutes, int breakUsedMinutes);
      public static DateTimeOffset? ShiftEndsAt(string teamStatus, /* the resolved schedule's end for workDate in zone */ DateTimeOffset? scheduledEnd);
  }
  ```
  `StateSince`: working → `record.ActualStart`; on_break → start of the open break; else null. `BreakRemainingMinutes`: only on_break and only when `breakDurationMinutes > 0`; `max(0, duration − used)`. `ShiftEndsAt`: only working/on_break and only when the schedule has an end. `AttendanceMinutes` = `PlannerActuals.AttendanceMinutes(...)` on the record (0 without one).

- [ ] **Step 1:** Tests: `State_since_is_clock_in_for_working`, `State_since_is_open_break_start_for_on_break`, `Break_remaining_is_null_without_configured_duration`, `Break_remaining_never_negative` (duration 30, used 45 → 0), `Shift_end_null_for_on_leave`; handler: `Unauthorized_leave_viewer_never_gets_on_leave_or_leave_detail` (existing masking still holds with new fields), `As_of_is_now`.
- [ ] **Step 2:** `verify.ps1 backend -Filter "FullyQualifiedName~TimeAttendance.Team"` → FAIL.
- [ ] **Step 3:** Implement; fill the fields inside the existing member loop (no extra queries: breaks, record, schedule are already loaded).
- [ ] **Step 4:** Re-run → PASS. Also run `-Filter "FullyQualifiedName~Dashboard"` (registry snapshot tests may need the new title).
- [ ] **Step 5:** Commit `feat(attendance): team today state-since, break and shift remaining for Working/Break/Leave`.

---

### Task 4: Team working-hours week trend (BE)

**Files:**
- Create: `src/ONEVO.Application/Features/TimeAttendance/Team/Queries/GetTeamWorkingHoursQuery.cs` (+ handler + validator in the same folder pattern as `TeamAttendanceQueries.cs`)
- Modify: `src/ONEVO.Api/Controllers/Tenant/Attendance/TimeTrackingController.cs` — `[HttpGet("team/working-hours")]`, same `[RequirePermission]` as `team/today`.
- Test: `tests/ONEVO.Tests.Unit/Features/TimeAttendance/Team/GetTeamWorkingHoursQueryHandlerTests.cs`

**Interfaces:**
- Produces: `GetTeamWorkingHoursQuery(DateOnly? WeekStart)` → `TeamWorkingHoursResponse(DateOnly WeekStart, DateOnly WeekEnd, IReadOnlyList<TeamWorkingHoursDay> Days)`; `TeamWorkingHoursDay(DateOnly Date, int WorkedMinutes, int BreakMinutes, int OnLeaveCount, int PeopleWithRecord)`. Population and leave masking = exactly `GetCoveredTeamTodayQuery` (extract the coverage + leave-authorization lookup into a private shared helper rather than copying). Worked/break from `attendance_records` (open record → `PlannerActuals.AttendanceMinutes` up to now). `OnLeaveCount` counts only leave-authorized subjects. Week = ISO Monday; default the legal-entity work date's week; future days present with zeros and `PeopleWithRecord = 0`.

- [ ] **Step 1:** Tests: `Seven_days_oldest_first`, `Leave_counted_only_for_authorized_subjects`, `Open_record_counts_until_now`, `Week_start_not_monday_is_rejected` (validator).
- [ ] **Step 2–4:** fail → implement → pass (filter `~GetTeamWorkingHours`).
- [ ] **Step 5:** Commit `feat(attendance): team working hours week trend`.

---

### Task 5: Execution score (BE, pure)

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/Planner/Services/ExecutionScore.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Planner/ExecutionScoreTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  public enum NotRankedReason { None, NoItemsYet, NoPlannedTaskTime, OnLeave, PlannerOff }
  public sealed record ExecutionScoreResult(decimal? ItemCompletionPercent, decimal? TimeCoveragePercent, decimal? Score, NotRankedReason Reason);
  public static class ExecutionScore
  {
      public const decimal ItemWeight = 0.6m, TimeWeight = 0.4m;
      public static ExecutionScoreResult Compute(int dueItems, int completedDueItems, int elapsedPlannedTaskMinutes,
          int scheduledAttributedMinutes, bool onLeave, bool plannerEnabled);
      public static IReadOnlyList<T> Rank<T>(IEnumerable<T> rows, Func<T, ExecutionScoreResult> score, Func<T, string> name);
  }
  ```
  Percent values one decimal, away from zero. Time coverage capped at 100. Score only when both parts exist. `Rank` puts scored rows first by Score desc, then ItemCompletion desc, TimeCoverage desc, name asc; unscored rows after, by name.

- [ ] **Step 1:** Tests: `Score_is_weighted_60_40` (4 due/3 done, 120 planned/90 scheduled → 75.0, 75.0, 75.0); `Coverage_capped_at_100`; `No_due_items_is_not_ranked_NoItemsYet`; `Only_items_no_planned_time_gives_item_part_but_no_score`; `Leave_is_not_ranked`; `Planner_off_is_not_ranked`; `Ties_break_on_item_then_time_then_name`.
- [ ] **Step 2–4:** fail → implement → pass (filter `~ExecutionScore`).
- [ ] **Step 5:** Commit `feat(wm): execution score for today's team schedule`.

---

### Task 6: Today's Team Schedule query + endpoint (BE)

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/Planner/TeamSchedule/GetTeamScheduleQuery.cs`, `GetTeamScheduleQueryHandler.cs`, `TeamScheduleResponse.cs`, `TeamScheduleComposer.cs` (pure: inputs → per-employee row)
- Modify: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/PlannerController.cs` — `[HttpGet("team")] TeamDay([FromQuery] DateOnly? date)`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Planner/TeamScheduleComposerTests.cs`, `GetTeamScheduleQueryHandlerTests.cs`

**Interfaces:**
- Consumes: Task 1 `TaskTimeAttribution.Attribute`, Task 2 `ITeamTimeReadRepository.*ForEmployeesAsync`, `GetTasksAsync`, `GetTimezonesAsync`, `IMeetingRepository.ListEmployeesDayMeetingsAsync`, Task 5 `ExecutionScore`, `IEmployeeHierarchyClosureRepository.GetDescendantEmployeeIdsAsync`, `PlannerActuals.DayBounds/MeetingMinutesInDay`.
- Produces (`TeamScheduleResponse`):
  ```csharp
  record TeamScheduleResponse(DateOnly Date, bool PlannerEnabled, DateTimeOffset AsOf, TeamScheduleSummary Summary, IReadOnlyList<TeamScheduleMember> Members);
  record TeamScheduleSummary(int People, int TasksPlanned, int TasksCompleted, int TasksInProgress, int TasksPending, int Meetings,
      int PeopleWithUnscheduledWork, int UnscheduledMinutes, int Conflicts);
  record TeamScheduleMember(Guid EmployeeId, string DisplayName, Guid? AvatarFileId, string Timezone, string? PresenceStatus,
      int PlannedTaskMinutes, int PlannedMeetingMinutes, int ClockedMinutes, int ScheduledMinutes, int OutsideSlotMinutes, int NoPlanMinutes,
      decimal? ItemCompletionPercent, decimal? TimeCoveragePercent, decimal? Score, string NotRankedReason, int? Rank,
      IReadOnlyList<TeamScheduleItem> Items, IReadOnlyList<TeamScheduleUnscheduled> Unscheduled, IReadOnlyList<TeamScheduleConflict> Conflicts);
  record TeamScheduleItem(string Kind /* task|meeting */, Guid TaskId, string Title, DateTimeOffset Start, DateTimeOffset End,
      string Status /* completed|in_progress|pending|scheduled_not_clocked|upcoming|in_meeting|finished|cancelled */, bool IsPrivate);
  record TeamScheduleUnscheduled(Guid TaskId, string Title, DateTimeOffset Start, DateTimeOffset End, int Minutes, string Kind /* no_plan|outside_slot */);
  record TeamScheduleConflict(string Kind /* block_meeting|parallel_clock */, DateTimeOffset Start, DateTimeOffset End, string FirstTitle, string SecondTitle);
  ```
  Rules (spec 6.2/6.3): population = transitive reports of the caller, active only; each member's day = their own local date `date ?? local today` (a future date → 400 like `planner_not_today`); all reads batched for the union window; meetings masked (`"Private meeting"`) when `IsPrivate`; cancelled meetings appear with status `cancelled` and are excluded from every count; declined meetings omitted; task item status from `TeamTaskRow` + open session; `scheduled_not_clocked` when block start ≤ now, not complete, no session in the block; due items = items whose start ≤ now (meetings count as done when finished); `PresenceStatus` = the Team Status mapping for that employee when the caller has `attendance:read` coverage of them, else null (reuse the Task 3 resolver path, batched). Planner off → `PlannerEnabled = false`, blocks empty, meetings and clocked minutes still filled, scores `PlannerOff`. Gate: authenticated + caller has an employee; no reports → empty members (200). Rank assigned via `ExecutionScore.Rank`.

- [ ] **Step 1:** Composer tests: `No_plan_worker_is_listed_with_unscheduled_and_not_ranked`, `Block_10_to_11_clock_1020_to_1120_gives_40_and_20`, `Private_meeting_title_is_masked`, `Cancelled_meeting_shown_but_not_counted`, `Future_items_do_not_lower_score`, `Block_meeting_overlap_is_a_conflict_but_all_day_is_not`, `Employee_in_other_timezone_uses_own_local_day`. Handler tests: `No_reports_returns_empty`, `Planner_off_keeps_clocked_minutes`, `Future_date_rejected`, `Non_report_never_appears`.
- [ ] **Step 2–4:** fail → implement → pass (filter `~TeamSchedule`).
- [ ] **Step 5:** Commit `feat(planner): today's team schedule query`.

---

### Task 7: Register the `team-schedule` widget (BE)

**Files:**
- Modify: `src/ONEVO.Application/Features/Dashboard/Team/DTOs/MyTeamCapabilitiesResponse.cs` (+ `bool HasReports` before `LegalEntityId`) and `Queries/GetMyTeamCapabilitiesQueryHandler.cs` (true when `GetDirectReportEmployeeIdsAsync` is non-empty)
- Modify: `DashboardWidgetRegistry.cs`: `new("team-schedule", DashboardKeys.MyTeam, "Today's team schedule", "What your team planned for today and how far they are.", 2, 2, 12, 8, true)`; default layout: place it at `GridRect(0, 12, 8, 6)` in the full My Team layout; leave the variant layouts as they are unless a test demands otherwise.
- Modify: `DashboardWidgetAccess.cs` — `TeamSchedule = "team-schedule"`, allowed when `caps is { IsAvailable: true, HasReports: true }`.
- Modify: `src/ONEVO.Api/Dashboard/Widgets/WidgetDataProviders.cs` — `TeamScheduleWidgetDataProvider` mirroring `PlannerController.TeamDay` with `GetTeamScheduleQuery(null)`; register with `Add<...>(services, Key)`.
- Test: existing registry/access/bootstrap tests under `tests/ONEVO.Tests.Unit/Features/Dashboard` + new `Team_schedule_needs_reports`.

- [ ] **Step 1–4:** test → fail → implement → pass (filter `~Dashboard`); also `pwsh ../dev-env/scripts/find-unauthorized-endpoints.ps1 backend` → 0.
- [ ] **Step 5:** Commit `feat(dashboard): team-schedule widget registration`.

---

### Task 8: Team Progress time figures + workload (BE)

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/Leadership/Services/LedTimeProgressCalculator.cs` (pure)
- Create: `src/ONEVO.Application/Features/WorkManagement/Leadership/Queries/GetLedTimeProgress/GetLedTimeProgressQuery.cs`, `…Handler.cs`, `…Validator.cs`
- Modify: `DTOs/LedProgressDetailResponses.cs` (new records below)
- Modify: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/WorkLeadershipController.cs` — `[HttpGet("led-progress/time")]` with `period` (`week|month`, required), `projectId?`, `objectiveId?`; same attributes as `led-progress/period`.
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Leadership/LedTimeProgressCalculatorTests.cs`, `GetLedTimeProgressQueryHandlerTests.cs`

**Interfaces:**
- Consumes: Task 1, Task 2 `*ForTasksAsync`, `GetTimezonesAsync`, `LedScopeFilter.Resolve`, `LedProgressPeriod.Window`, `ExpectedWorkdayCalendar.Build`, leave via the same source `GetMyWeeklyWorkProgressQueryHandler` uses (batched by employee ids).
- Produces:
  ```csharp
  record LedTimeComparison(int PlannedMinutes, int ClockedMinutes, decimal? CoveragePercent);
  record LedTimeBucket(DateOnly From, DateOnly To, int PlannedMinutes, int ScheduledMinutes, int UnscheduledMinutes);
  record LedWorkloadRow(Guid EmployeeId, string DisplayName, int Rank, int PlannedMinutes, int ClockedMinutes, int UnscheduledMinutes,
      int? ExpectedMinutes, decimal? UtilizationPercent);
  record LedUnscheduledRow(Guid EmployeeId, string DisplayName, Guid TaskId, string ShortId, string Title, DateTimeOffset Start, DateTimeOffset End, int Minutes, string Kind);
  record LedTimeProgressResponse(string Period, DateOnly From, DateOnly To, bool PlannerEnabled, DateTimeOffset AsOf,
      int PlannedMinutes, int ClockedMinutes, int ScheduledMinutes, int OutsideSlotMinutes, int NoPlanMinutes, decimal? CoveragePercent,
      int PeopleWithUnscheduledWork, LedTimeComparison Previous, IReadOnlyList<LedTimeBucket> Buckets,
      IReadOnlyList<LedWorkloadRow> Workload, IReadOnlyList<LedUnscheduledRow> Unscheduled, int UnscheduledTotal);
  static class LedTimeProgressCalculator
  {
      static (DateOnly From, DateOnly To) PreviousSameElapsed(LedPeriod period, DateOnly from, DateOnly today);
      // week: from-7 .. today-7; month: first of previous month .. same day-of-month (clamped to month length)
      static LedTimeProgressResponse Build(/* period window, per-employee AttributionResult per local day, names, expected minutes, now */);
  }
  ```
  Rules: window = `LedProgressPeriod.Window(period, today)` with `today` = UTC date (existing convention) but each employee's minutes are attributed per **their local day** inside the window; current period counts up to now. Buckets: week → 7 days; month → ISO weeks overlapping the month. Coverage = scheduled ÷ planned (null when planned = 0). Workload sorted by PlannedMinutes desc, ClockedMinutes desc, name; rank 1..n; `ExpectedMinutes` null when the employee's legal entity has no working hours; utilization not capped. `Unscheduled` = newest 200 segments, `UnscheduledTotal` = full count. Planner off → planned/scheduled/coverage/buckets' planned & scheduled = 0 with `PlannerEnabled = false`; clocked still filled; FE hides the split. People = assignees of in-scope tasks ∪ owners of in-scope blocks/sessions.

- [ ] **Step 1:** Calculator tests: `Previous_week_is_same_elapsed_span`, `Previous_month_clamps_day_31_to_30`, `Unplanned_90_minutes_show_as_no_plan_and_touch_no_allocation` (response has no allocation fields), `Month_buckets_are_iso_weeks`, `Coverage_null_without_plan`, `Workload_ranks_by_planned_then_clocked_then_name` (24 h vs 8 h planned → 24 h ranks 1), `Utilization_over_100_is_kept`. Handler: `Leads_nothing_returns_empty`, `Project_filter_outside_scope_returns_empty`, `Planner_off_keeps_clocked`.
- [ ] **Step 2–4:** fail → implement → pass (filter `~LedTimeProgress`), then `-Filter "FullyQualifiedName~Leadership"` stays green (allocation untouched).
- [ ] **Step 5:** Commit `feat(wm): team progress planned vs worked, unscheduled and workload`.
- [ ] **Step 6:** Perf gate: time `GET work/led-progress/time?period=month` on dapi (local API). Record p95 in the summary report; > 500 ms → stop and report (spec §7), do not add tables.

---

### Task 9: FE Working / Break / Leave card (replaces Team Status card)

**Files (FE):**
- Modify: `models/my-team-today.model.ts` (new member fields + `asOf`), `state/my-team-today.store.ts` if it maps fields
- Create: `utils/team-presence-layout.util.ts` (+ `.spec.ts`), `utils/team-presence-format.util.ts` (+ `.spec.ts`)
- Rewrite: `feature/team-status-card/team-status-card.component.{ts,html,css,spec.ts}` (keep selector and class name)
- Create: `feature/team-status-detail-dialog/team-status-detail-dialog.component.{ts,html,css,spec.ts}`
- Create: `data-access/team-working-hours-api.service.ts` (+ spec) for `attendance/time-tracking/team/working-hours`

**Interfaces:**
- Produces: `PRESENCE_COLORS = { working: '#3b82f6', break: '#f59e0b', leave: '#ef4444' }`; `groupPresence(members): { working, onBreak, onLeave, neutral: { notClockedIn, clockedOut, absent, notScheduled } }` (not_started + absent → notClockedIn only when the label says "not clocked in"; keep the four neutral counts separate in data, join in copy: e.g. "4 not clocked in · 2 clocked out"); `presenceView(width, height, prev): 'mini' | 'medium' | 'large'`.
- Card: title "Working / Break / Leave"; mini = donut (3 states, centre `N / M`) + 3 counts; medium = 3 counter pills + up to 5 rows (avatar, chip, "Since 09:00", time-left bar only when `breakRemainingMinutes` or `shiftEndsAt` exists, else none); large = counters + table (name, state, since, elapsed, remaining or "—", attendance h). Elapsed ticks every 30 s client-side from `stateSince`; order: working, break, leave, then name. Late = small amber tag. Neutral line under the counters. Popup: KPI tiles (Working, Break, Leave, Not clocked in), donut, working-hours bar chart (reuse `ui/attendance-day-chart` styling via a stacked worked/break series; build a small `ui/working-hours-chart` only if `attendance-day-chart` cannot take minutes), member table (shared `app-data-table`) with attention column and CSV export (no leave type for rows without `leave`).

- [ ] **Step 1:** Specs: `groupPresence` counts only working/on_break/on_leave in the 3 states; masked-leave member (status `absent`) lands in neutral; layout util thresholds at 2x2 (204 px), 4x4, 8x6; card renders "—" for remaining when null; popup CSV has no leave column value for masked rows.
- [ ] **Step 2:** `npx ng test --watch=false --include src/app/modules/dashboard/**/team-status*` and `**/team-presence*` → FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Re-run → PASS; `npm run check:warnings` → 0.
- [ ] **Step 5:** Commit `feat(dashboard): Working / Break / Leave replaces Team Status`.

---

### Task 10: FE Today's Team Schedule widget

**Files (FE):**
- Create: `models/team-schedule.model.ts` (mirror of Task 6 response), `data-access/team-schedule-api.service.ts` (+ spec; `GET planner/team?date=`), `state/team-schedule.store.ts` (+ spec; same shape as `team-attendance.store.ts`: `data`, `loadedAt`, `load(date?)`, `retry`)
- Create: `utils/team-schedule-layout.util.ts` (+ spec), `utils/team-schedule-format.util.ts` (+ spec)
- Create: `feature/team-schedule-card/*`, `feature/team-schedule-detail-dialog/*`
- Modify: `widgets/widget-registry.ts` (`'team-schedule': { component: TeamScheduleCardComponent }`), `utils/my-team-widget-visibility.util.ts` (+ spec; mirror `HasReports`), `models/my-team-capabilities.model.ts` (`hasReports`)

**Interfaces:**
- Consumes: Task 6 response, Task 7 `hasReports`.
- Produces: `scheduleView(width, height, prev): 'mini' | 'medium' | 'large'`; `timelineSpan(members): { start: Date, end: Date }` (earliest item/segment start floored to the hour, latest end ceiled, min 8 h, clamp 06:00–22:00 local of the viewer); `taskStatusDonut(summary)` over completed / in progress / pending only.
- Card: title "Today's team schedule"; mini = task-status donut + "N people have unscheduled work" alert row (hidden at 0); medium = rows (avatar, presence chip when present, mini plan bar of items coloured by status/kind, completion % or "No plan", amber "Unscheduled 45m" badge); large = timeline grid (sticky names, hour ticks, now-line, item blocks: task blue / completed green / pending grey / meeting purple, striped amber unscheduled segments, conflict red outline). Planner off → "Planner is off" note instead of plan bars/timeline; clocked minutes still listed. Popup: header date navigator (prev/next day; next disabled at today), KPI tiles (Tasks planned, Completed, In progress, Pending, Meetings, Unscheduled people), timeline with planned/clocked/score columns, "Execution ranking (today)" list with both parts + score and "Not ranked · reason", Unscheduled panel ("Worked without a matching Planner slot."), Conflicts panel, CSV export. Ranking re-sorts only when data reloads.

- [ ] **Step 1:** Specs: layout thresholds; donut never includes meetings; "No plan" shown for a no-plan worker; private meeting renders "Private meeting"; next-day disabled at today; Planner-off note; empty state "No one reports to you" when `members` empty.
- [ ] **Step 2–4:** `npx ng test --watch=false --include src/app/modules/dashboard/**/team-schedule*` fail → implement → pass; `npm run check:warnings` → 0.
- [ ] **Step 5:** Commit `feat(dashboard): today's team schedule widget`.

---

### Task 11: FE Team Progress upgrade

**Files (FE):**
- Modify: `models/team-progress-detail.model.ts` (+ `LedTimeProgress` types from Task 8), `data-access/team-progress-detail-api.service.ts` (+ `getTimeProgress(period, projectId?, objectiveId?)`, spec)
- Modify: `state/my-team-led-progress.store.ts` (+ `period` signal default `'week'`, `time` resource loaded with the card)
- Modify: `utils/team-progress-layout.util.ts` (+ spec) — sections per view: medium adds top-3 workload; wide adds trend + unscheduled callout; large/maximum add planned-vs-worked band + top-5 workload
- Modify: `feature/team-progress-card/*`, `feature/team-progress-detail-dialog/*`
- Create: `utils/team-progress-time-format.util.ts` (+ spec): `comparisonText(current, previous, period)` → "↑ 6 pp vs last week" / "New"; `minutesText(m)` → "3h 20m"

**Interfaces:**
- Consumes: Task 8 response.
- Card: Week ⇄ Month `<select>` in the header (standard views only, `appStopClickPropagation`, same styling as `.ta-period`); existing allocation KPIs/donut/completion untouched; new "Planned vs worked" band = one stacked bar (scheduled blue, outside-slot amber, no-plan amber striped) against a planned marker, coverage % with denominator tooltip; "Unscheduled work · 3h 10m · 2 people" amber callout; workload rows (rank pill, avatar, name, planned h, bar, utilization % or "—"; tooltip "More workload is not better performance."). Popup adds sections "Planned vs worked" (bars per bucket), "Unscheduled work" (table, CSV) and "Workload ranking" (table, CSV) after Status Distribution and Weekly completion; existing sections and filters unchanged; the popup's period select drives both allocation-period and time calls.

- [ ] **Step 1:** Specs: Week→Month refetches time and changes ranking; allocation numbers unchanged by the switch on the "all-time" KPIs; coverage "—" when planned 0; Planner-off note hides the band; existing team-progress specs still pass.
- [ ] **Step 2–4:** `npx ng test --watch=false --include src/app/modules/dashboard/**/team-progress*` fail → implement → pass; `npm run check:warnings` → 0.
- [ ] **Step 5:** Commit `feat(dashboard): team progress week/month, planned vs worked, unscheduled, workload`.

---

### Task 12: Test coverage audit (run last, before the gates)

General rules: assert the real value or state, never only `IsSuccess` / `toHaveBeenCalled()`; test both sides of every
boundary; every "must not happen" case asserts the absence; no shared mutable fixtures between tests. One commit per
section below (`test(<area>): coverage audit - <unit>`).

- [ ] **TaskTimeAttribution:** window edge exactly at `from` and at `to` (half-open: a session ending at `from` adds 0, one starting at `to` adds 0); `now` inside a block; a block of task A while clocked on task B (B = no plan, A scheduled 0); invariant `Scheduled + OutsideSlot + NoPlan == Clocked` checked in every test.
- [ ] **ExecutionScore:** due items 0 / 1; planned minutes 0 / 1; coverage exactly 100 and 101 → 100; score rounding at .x5.
- [ ] **TeamPresenceMath:** break used = duration − 1, = duration, = duration + 1; status `clocked_out` has no `StateSince`.
- [ ] **GetTeamScheduleQuery:** a non-report in the same tenant never appears; another tenant's report never appears; a meeting the employee declined is absent; an organizer is never "declined"; date = today vs yesterday vs tomorrow (rejected).
- [ ] **GetLedTimeProgressQuery:** a session on a task outside the led scope is excluded; a filter to a module outside scope returns empty, not all; month with 28/30/31 days for the previous-same-elapsed window.
- [ ] **GetTeamWorkingHoursQuery / team today:** leave of a non-authorized subject never counted; the caller never appears in their own team.
- [ ] **FE layout utils:** each threshold at exactly the breakpoint, −1 px and +1 px, plus the 20 px hysteresis in both directions.
- [ ] **FE cards/popups:** "—" (not "0%") for every null percentage; "Private meeting" in DOM, tooltip and CSV; Planner-off note replaces schedule figures; ranking order unchanged when only a clock tick happens.

---

### Task 13: Gates, browser pass, report

- [ ] **Step 1:** BE `pwsh ../dev-env/scripts/verify.ps1 backend` (all unit + architecture green) and `-Full` if Docker is up; `find-unauthorized-endpoints.ps1 backend` → 0; `security-scan.ps1 backend -Baseline origin/development` → no new findings.
- [ ] **Step 2:** FE `npm run check:warnings`, `npx ng build`, `npx ng test --watch=false` (note pre-existing failures separately).
- [ ] **Step 3:** Browser pass on dapi.onexso.com:4200 (user signs in; Playwright, force `gridArea` on the parent of `app-widget-frame`): each widget at 2x2, medium, large, and its popup; Planner on and off; screenshots.
- [ ] **Step 4:** Write `docs/reports/work-management/2026-10-29-my-team-three-widgets-summary.md` (what shipped, perf numbers, test counts, known gaps); move spec + plan to `finished/<date>/` per `docs/superpowers/rules/FILE_CREATION_RULES.md` only after the user's browser sign-off.
- [ ] **Step 5:** Commit docs. Do not push without the user's say-so.
