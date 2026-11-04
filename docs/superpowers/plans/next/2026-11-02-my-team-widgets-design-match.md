# My Team Widgets Design Match Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the three My Team widgets look like the 2026-11-02 preview images, give them real grow-by-detail size ladders, and add daily and weekly planned-vs-actual charts.

**Architecture:**
- Backend: two small DTO additions (`ExpectedMinutes`, `Breaks`) and one read endpoint, `GET planner/team/week`. The endpoint runs the existing `TeamScheduleComposer` per person per local day, and a new pure `TeamWeekComposer` sums the results.
- Frontend: rewrite the Working/Break/Leave and schedule layout utils as `classify / resolve / plan` budgets copied from `team-attendance-layout.util.ts`. Add two shared chart components (`planned-actual-day-chart`, `planned-actual-week-chart`), then re-skin the cards and popups.

**Tech Stack:** .NET 9 / MediatR / EF Core / xUnit + Moq (backend); Angular standalone + signals, Tailwind, hand-built HTML/SVG charts, Vitest via `npx ng test --watch=false --include <glob>` (frontend).

**Spec:** `HRMS-Backend-v1/docs/superpowers/specs/next/2026-11-02-my-team-widgets-design-match-design.md`. Preview images are in `docs/design-notes/my-team-widgets-2026-11-02/`.

**Repos / branch:** `HRMS-Backend-v1` and `Hrms--Web-application---front-end---v1`, both on `feature/my-team-three-widgets`. Don't push.

## Global Constraints

- A card never scrolls at any size from 2x2 to 12x8. Bigger cards add sections and rows; graphics never grow. The donut is always `DASHBOARD_DONUT_PX` (140).
- No empty space under content: leftover height goes to more rows or the next section.
- Views come from `injectHostSize()` with `HYSTERESIS = 20`. A 0×0 size keeps the previous view.
- Section heights in the layout utils are **measured in the browser** (Task 14 re-measures them). The numbers below are starting points.
- Planner off: plan figures are 0, coverage is null, clocked time still shows, and every view says "Planner is off".
- No new tables or migrations. Never use `IEmployeeHierarchyClosureRepository` in WorkManagement or Dashboard/Team; use `IEmployeeRepository.ListDirectReportEmployeeIdsAsync`.
- Every new endpoint gets the same gate as its sibling. `find-unauthorized-endpoints.ps1 backend` must report 0.
- Percentages use `formatPercent`. Charts are hand-built (no chart library).
- Run `npm run check:warnings` before each FE commit. Prettier only on files you create.
- Ranking stays the workload ranking (decided 2026-11-02). No "Top performers %".
- Out of scope: Month view in the schedule popup, "Add Schedule" buttons, project/module filters in the Working/Break/Leave popup.

## Review Focus

1. **Reports in different timezones in the week view.** Each person's minutes must land on their own local day. A UTC+05:30 and a UTC−05:00 report clocked at the same instant can fall on different weekdays. Test: Task 3.
2. **Actual time above planned (overtime).** Bars scale to `max(planned, actual)` per chart, so nothing draws past its track. Text shows the true uncapped value. Tests: Task 10.
3. **Team sizes 0, 1, and 60+.** Row budgets never render a half row. The empty state fits mini. "View all N ›" appears only when rows are hidden. Tests: Tasks 6 and 9.
4. **Planner off everywhere.** The card, the popup Day and Week modes, and Team Progress all show the planner-off state with clocked time, never zero-height charts. Tests: Tasks 4, 11, 12 and 13.
5. **Week requested for a future or non-Monday `weekStart`, or the current partial week.** Future → 400 `planner_not_today`. Non-Monday → 400 validation. The current week's future days show planned only. Previous-week totals cover the same elapsed span. Tests: Tasks 3 and 4.

---

## Backend

### Task 1: `ExpectedMinutes` and `Breaks` on team/today members

**Files:**
- Modify: `src/ONEVO.Application/Features/TimeAttendance/Team/DTOs/TeamTodayResponse.cs`
- Modify: `src/ONEVO.Application/Features/TimeAttendance/Team/Services/TeamPresenceMath.cs`
- Modify: `src/ONEVO.Application/Features/TimeAttendance/Queries/AttendanceReadHandlers.cs` (team-today `members.Add`, around line 676)
- Test: `tests/ONEVO.Tests.Unit/Features/TimeAttendance/Team/TeamPresenceMathTests.cs`, `tests/ONEVO.Tests.Unit/Features/TimeAttendance/Team/GetCoveredTeamTodayQueryHandlerTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record TeamTodayBreak(DateTimeOffset Start, DateTimeOffset? End);`
  - `TeamTodayMember` gets two new trailing optional parameters: `int? ExpectedMinutes = null, IReadOnlyList<TeamTodayBreak>? Breaks = null`.
  - `public static int? TeamPresenceMath.ExpectedMinutes(string teamStatus, TimeOnly? start, TimeOnly? end)`

- [ ] **Step 1: Write failing tests in `TeamPresenceMathTests`**

```csharp
[Theory]
[InlineData("working", "09:00", "17:00", 480)]
[InlineData("clocked_out", "09:00", "17:30", 510)]
[InlineData("on_leave", "09:00", "17:00", 0)]
public void ExpectedMinutes_is_the_work_window(string status, string start, string end, int expected)
    => Assert.Equal(expected, TeamPresenceMath.ExpectedMinutes(status, TimeOnly.Parse(start), TimeOnly.Parse(end)));

[Fact]
public void ExpectedMinutes_is_null_without_working_hours()
    => Assert.Null(TeamPresenceMath.ExpectedMinutes("working", null, TimeOnly.Parse("17:00")));
```

Add to `GetCoveredTeamTodayQueryHandlerTests`, using the existing fixture's legal entity with a 09:00–17:00 schedule and one closed break plus one open break:
- `Member_carries_expected_minutes_and_todays_breaks`: assert `ExpectedMinutes == 480`, `Breaks.Count == 2`, the open break has `End == null`, and the breaks are ordered by `Start`.

- [ ] **Step 2: Run them and confirm they fail.** `pwsh ../dev-env/scripts/verify.ps1 backend -Filter "FullyQualifiedName~TeamPresenceMath|FullyQualifiedName~GetCoveredTeamToday"` → compile errors or failures.

- [ ] **Step 3: Implement.**
  - `ExpectedMinutes`: `on_leave` → 0; a missing start or end → null; otherwise `(end - start)` in whole minutes. An end before the start wraps past midnight: add 24h.
  - In the handler, pass `TeamPresenceMath.ExpectedMinutes(mapped.TeamStatus, scheduleResolution.Schedule.Start, scheduleResolution.Schedule.End)`, and pass `employeeBreaks.OrderBy(b => b.BreakStart).Select(b => new TeamTodayBreak(b.BreakStart, b.BreakEnd)).ToList()`.
  - The `employeeBreaks` variable already exists in that loop.

- [ ] **Step 4: Run the same filter again.** Expected: PASS.

- [ ] **Step 5: Commit.** `git commit -m "feat(attendance): team today expected minutes and breaks"`

### Task 2: Breaks and expected minutes on schedule members

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/Planner/TeamSchedule/TeamScheduleResponse.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Planner/TeamSchedule/GetTeamScheduleQueryHandler.cs` (`PresenceAsync`, member build)
- Modify: `src/ONEVO.Application/Features/WorkManagement/Planner/TeamSchedule/TeamScheduleComposer.cs` (`Compose` signature only)
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Planner/GetTeamScheduleQueryHandlerTests.cs`, `TeamScheduleComposerTests.cs`

**Interfaces:**
- Consumes: `TeamTodayMember.ExpectedMinutes`, `TeamTodayMember.Breaks` (Task 1).
- Produces:
  - `public sealed record TeamScheduleBreak(DateTimeOffset Start, DateTimeOffset? End);`
  - `TeamScheduleMember` gets trailing `int? ExpectedMinutes, IReadOnlyList<TeamScheduleBreak> Breaks`.
  - `PresenceAsync` returns `IReadOnlyDictionary<Guid, TeamPresence>`, where `public sealed record TeamPresence(string Status, int? ExpectedMinutes, IReadOnlyList<TeamScheduleBreak> Breaks)` lives in `TeamScheduleResponse.cs`.
  - `TeamScheduleComposer.Compose(TeamScheduleInput, DateTimeOffset now, bool plannerEnabled, TeamPresence? presence = null)`. `TeamScheduleInput.PresenceStatus` stays as it is; the composer prefers `presence?.Status` when one is given.

- [ ] **Step 1: Write failing handler tests.**
  - `Today_members_carry_breaks_and_expected_minutes_from_presence`: mediator returns a member with 2 breaks and `ExpectedMinutes = 480`; assert both are on the schedule member.
  - `Past_day_has_no_breaks_and_null_expected`: `date` = yesterday → `Breaks` empty, `ExpectedMinutes == null`.
  - `Viewer_without_attendance_read_gets_no_breaks`.

- [ ] **Step 2: Run them and confirm they fail.** `-Filter "FullyQualifiedName~TeamSchedule"`.

- [ ] **Step 3: Implement.** Map presence in `PresenceAsync`. Pass it into `Compose`, which copies it onto the member (empty list and null when absent). Update the existing composer tests' calls only where they need the new parameter. Keep `TeamScheduleWidgetDataProvider` output unchanged apart from the new fields.

- [ ] **Step 4: Run `-Filter "FullyQualifiedName~TeamSchedule|FullyQualifiedName~Planner"`.** Expected: PASS.

- [ ] **Step 5: Commit.** `feat(planner): breaks and expected minutes on team schedule`

### Task 3: `TeamWeekComposer` (pure)

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/Planner/TeamSchedule/TeamWeekComposer.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Planner/TeamSchedule/TeamScheduleWeekResponse.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Planner/TeamWeekComposerTests.cs`

**Interfaces:**
- Consumes: `TeamScheduleComposer.Compose`, `TaskTimeAttribution.Attribute(...)` → `AttributionResult.PlannedMinutes`.
- Produces:

```csharp
public sealed record TeamScheduleWeekResponse(DateOnly WeekStart, DateOnly WeekEnd, bool PlannerEnabled, DateTimeOffset AsOf,
    IReadOnlyList<TeamWeekDay> Days, TeamWeekTotals Totals, TeamWeekTotals Previous, IReadOnlyList<TeamWeekMember> Members);
public sealed record TeamWeekDay(DateOnly Date, int PlannedTaskMinutes, int PlannedMeetingMinutes, int ClockedMinutes,
    int ScheduledMinutes, int OutsideSlotMinutes, int NoPlanMinutes);
public sealed record TeamWeekTotals(int PlannedMinutes, int ClockedMinutes, int ScheduledMinutes, int UnscheduledMinutes,
    decimal? CoveragePercent);
public sealed record TeamWeekMember(Guid EmployeeId, string DisplayName, Guid? AvatarFileId, int PlannedMinutes,
    int ClockedMinutes, int ScheduledMinutes, int UnscheduledMinutes, decimal? CoveragePercent);

/// One person's week as the handler prepared it: the seven composed days (person-local dates) and the elapsed planned
/// task minutes for this week and for the same span a week earlier.
public sealed record TeamWeekPersonInput(Guid EmployeeId, string DisplayName, Guid? AvatarFileId,
    IReadOnlyList<(DateOnly Date, TeamScheduleMember Day)> Days, int PlannedElapsedMinutes,
    int PreviousPlannedElapsedMinutes, int PreviousScheduledMinutes, int PreviousClockedMinutes, int PreviousUnscheduledMinutes);

public static class TeamWeekComposer
{
    public static TeamScheduleWeekResponse Compose(DateOnly weekStart, bool plannerEnabled, DateTimeOffset asOf,
        IReadOnlyList<TeamWeekPersonInput> people);
}
```

- **Rules:**
  - `PlannedMinutes` = task + meeting minutes for the whole week.
  - `UnscheduledMinutes` = outside slot + no plan.
  - `CoveragePercent` = `min(100, scheduled ÷ PlannedElapsedMinutes × 100)`, with one decimal, rounded half away from zero. It is null when `PlannedElapsedMinutes == 0` or the Planner is off.
  - `Days` sums each person's composed day **by the person-local date** into the 7 `weekStart … weekStart+6` slots. A person-local date outside the week is ignored.
  - `Members` are ordered by planned desc, then clocked desc, then name.
  - Planner off: all plan fields are 0.

- [ ] **Step 1: Write failing tests:**
  - `Sums_days_into_the_seven_week_slots`: 2 people × 7 days → `Days[0..6]` sums.
  - `Person_local_dates_place_minutes_on_their_own_weekday`: the input day dated Tuesday for a UTC−05:00 person goes to `Days[1]`.
  - `Coverage_uses_elapsed_plan`: scheduled 120, elapsed plan 240, whole-week plan 600 → 50.0.
  - `Coverage_null_without_elapsed_plan`
  - `Previous_totals_come_from_previous_fields`
  - `Members_ordered_by_planned_then_clocked_then_name`
  - `Planner_off_zeroes_plan_and_keeps_clocked`

- [ ] **Step 2: Run them and confirm they fail.** `-Filter "FullyQualifiedName~TeamWeekComposer"`.

- [ ] **Step 3: Implement `Compose`.** It is pure, with no I/O.

- [ ] **Step 4: Run them and confirm they pass.**

- [ ] **Step 5: Commit.** `feat(planner): team week composer`

### Task 4: `GET planner/team/week`

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/Planner/TeamSchedule/GetTeamScheduleWeekQuery.cs`, `GetTeamScheduleWeekQueryHandler.cs`, `GetTeamScheduleWeekQueryValidator.cs`
- Modify: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/PlannerController.cs` (next to `TeamDay`)
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Planner/GetTeamScheduleWeekQueryHandlerTests.cs`; integration test in `tests/ONEVO.Tests.Integration/` next to `TeamTimeReadRepositoryTests`

**Interfaces:**
- Consumes: Task 3. Also the same dependencies as `GetTeamScheduleQueryHandler` (`ITeamTimeReadRepository`, meetings repo, identity, `IEmployeeRepository`, clock, `IOptions<PlannerOptions>`).
- Produces:
  - `public sealed record GetTeamScheduleWeekQuery(DateOnly? WeekStart) : IRequest<Result<TeamScheduleWeekResponse>>;`
  - Route: `[HttpGet("team/week")] public Task<IActionResult> TeamWeek([FromQuery] DateOnly? weekStart, CancellationToken ct)`. It has the same gate as `TeamDay`.

- [ ] **Step 1: Write failing handler tests:**
  - `Defaults_to_callers_current_week_monday`
  - `Future_week_is_400_not_today`
  - `Population_is_transitive_active_reports` (copy the day handler's population test)
  - `Each_person_split_into_their_local_days`
  - `Previous_span_matches_elapsed_part_of_this_week`: now = Wed 12:00 → previous figures cover Mon 00:00 to Wed 12:00 of last week.
  - `Planner_off_keeps_clocked_minutes`
  - In the validator tests: `WeekStart_must_be_monday`

- [ ] **Step 2: Run them and confirm they fail.** `-Filter "FullyQualifiedName~TeamScheduleWeek"`.

- [ ] **Step 3: Implement.**
  - Move `TransitiveReportsAsync` and `Zone` from the day handler into an `internal static class TeamSchedulePopulation` in the same folder, and call it from both handlers. Keep the boundary rule.
  - Read blocks, sessions and meetings **once** for `[min week start, max week end + 7 days)` across all zones, plus the previous week.
  - For each person:
    - compose 7 days with `TeamScheduleComposer.Compose` (it clips to `DayStart/DayEnd`) and no presence
    - `PlannedElapsedMinutes = TaskTimeAttribution.Attribute(blocks, sessions, weekFrom, Min(now, weekTo), now).PlannedMinutes`
    - the previous fields from the same call shifted −7 days, with `now − 7d`
  - Then call `TeamWeekComposer.Compose`.
  - Error codes reuse `PlannerErrorCodes`.

- [ ] **Step 4: Run the unit filter, then the integration test.** The integration test is `Team_week_reads_blocks_sessions_and_meetings_once_per_window`, `dotnet test tests/ONEVO.Tests.Integration -c Release --filter "FullyQualifiedName~TeamScheduleWeek"` (needs Docker). Expected: PASS.

- [ ] **Step 5: Run the full gate.**
  - `pwsh ../dev-env/scripts/verify.ps1 backend` → build PASS, unit and architecture 0 failed.
  - `pwsh ../dev-env/scripts/find-unauthorized-endpoints.ps1 backend` → 0.

- [ ] **Step 6: Commit.** `feat(planner): team week planned vs actual endpoint`

---

## Frontend

All FE paths are relative to `src/app/modules/dashboard/` unless they start with `src/`.

### Task 5: Models, API and stores

**Files:**
- Modify: `models/my-team-today.model.ts`, `models/team-schedule.model.ts`
- Modify: `data-access/team-schedule-api.service.ts`
- Create: `state/team-schedule-week.store.ts` (+ `.spec.ts`)
- Test: `data-access/team-schedule-api.service.spec.ts`

**Interfaces:**
- Produces:
  - `TeamTodayMember.expectedMinutes: number | null`, `TeamTodayMember.breaks: TeamTodayBreak[]`, with `TeamTodayBreak { start: string; end: string | null }`.
  - `TeamScheduleMember.expectedMinutes: number | null`, `TeamScheduleMember.breaks: TeamScheduleBreak[]`.
  - `TeamScheduleWeekResponse`, `TeamWeekDay`, `TeamWeekTotals`, `TeamWeekMember`: camelCase mirrors of Task 3.
  - `TeamScheduleApiService.getTeamWeek(weekStart: string | null): Observable<TeamScheduleWeekResponse>`, which calls `planner/team/week`.
  - `TeamScheduleWeekStore` with `data`, `loading`, `error`, `load(weekStart: string | null)`, `retry()`. Copy the `team-schedule.store.ts` shape. It is lazy: nothing loads until `load` is called.

- [ ] **Step 1: Write failing specs.** The API spec expects `GET …/planner/team/week?weekStart=2026-10-26`. The store spec covers load, then error, then retry.

- [ ] **Step 2: Run them and confirm they fail.** `npx ng test --include='**/team-schedule*' --watch=false`.

- [ ] **Step 3: Implement.** Update the `.testing.ts` fixtures with the new fields (`expectedMinutes: 480, breaks: []`).

- [ ] **Step 4: Run them and confirm they pass.**

- [ ] **Step 5: Commit.** `feat(dashboard): team week models, api, store`

### Task 6: Working/Break/Leave layout ladder

**Files:**
- Rewrite: `utils/team-presence-layout.util.ts` (+ `.spec.ts`)

**Interfaces:**
- Produces:

```ts
export type PresenceView = 'mini' | 'xs' | 'standard';
export const PRESENCE_BREAKPOINTS = { miniHeight: 260, xsWidth: 370, miniInlineWidth: 320, tableWidth: 640 } as const;
export const PRESENCE_CARD_CHROME = { width: 32, height: 68 } as const;
export const PRESENCE_SECTION_PX = { pill: 36, tiles: 72, row: 56, tableHead: 32, tableRow: 40, trend: 150, viewAll: 28, gap: 12 } as const;
export type PresenceColumn = 'status' | 'start' | 'elapsed' | 'remaining' | 'todayHours' | 'progress';
export interface PresencePlan {
  miniInline: boolean;       // mini: stacked bar + legend instead of the donut
  pill: boolean;             // "N employees are currently working ›"
  tiles: boolean;            // standard: the three KPI tiles
  table: boolean;            // rows as a table (width ≥ tableWidth)
  columns: PresenceColumn[]; // table columns that fit, in that priority order
  rows: number;              // person rows that fit
  trend: boolean;            // week working-hours trend panel
}
export function classifyPresenceSize(width: number, height: number): PresenceView;
export function resolvePresenceView(width: number, height: number, previous: PresenceView | null): PresenceView;
export function planPresenceLayout(view: PresenceView, width: number, height: number, members: number): PresencePlan;
```

- **Budget order (standard):**
  1. tiles
  2. at least 3 rows
  3. trend, if 5 rows plus the trend fit
  4. the remaining height goes to rows, capped at `members`
  5. `viewAll` is reserved when `rows < members`
- **xs:** the donut (140), then the pill, then rows.
- **Columns** drop right-to-left by width, in this order: `progress, todayHours, remaining, elapsed, start`. `status` is always kept.

- [ ] **Step 1: Write failing specs:**
  - every threshold, and that hysteresis keeps the previous view within 20px
  - 0×0 → `'standard'`
  - `plan rows never exceed members`
  - `body height minus used sections is under one row height` (no empty space), for heights 204…852 in 108px steps and widths 300…1400
  - `trend only after five rows fit`
  - `no viewAll when everyone fits`
  - `columns drop right to left`

- [ ] **Step 2: Run them and confirm they fail.**

- [ ] **Step 3: Implement.**

- [ ] **Step 4: Run them and confirm they pass.**

- [ ] **Step 5: Commit.** `feat(dashboard): presence layout ladder`

### Task 7: Working/Break/Leave card

**Files:**
- Rewrite: `feature/team-status-card/team-status-card.component.{ts,html,css,spec.ts}`
- Modify: `utils/team-presence-format.util.ts` (+ spec)

**Interfaces:**
- Consumes: Task 6 plan; `TeamTodayMember.expectedMinutes` (Task 5); `KpiTile` from `src/app/shared/ui/kpi-tiles`; `app-working-hours-chart` (Task 8 adds inputs, all optional).
- Produces in the format util:
  - `todayHoursText(m): string` → `"6h / 8h"`, or `"6h"` without an expected value
  - `todayProgress(m): number | null` → 0–100, capped
  - `presencePill(groups): string` → `"8 employees are currently working"`

- **Visuals** (spec 6.1):
  - Tiles: icon `users` / `coffee` / `calendar-x` (pick existing `IconName`s), count, and % of `totalMembers`.
  - Rows: avatar, name, coloured dot + state, start `clockTime`, `"2h 30m left"` in the state colour, a bar, and %.
  - The table has a # column.

- [ ] **Step 1: Write failing component specs.** Use `injectHostSize` stubs at:
  - 204×204 → donut + counts + pill
  - 420×204 → inline bar
  - 300×636 → xs
  - 600×420 → tiles + rows
  - 900×636 → table with all columns
  - 1300×852 → table + trend
  - Also: 60 members at 600×420 → "View all 60 ›"; the card host has `overflow: hidden`, and no element inside has a scroll container.
  - Format util specs for the three new functions.

- [ ] **Step 2: Run them and confirm they fail.** `--include='**/team-status-card/**'`.

- [ ] **Step 3: Implement.** The template switches on `view()` and reads `plan()` for sections. Remove the `wbl-rows` fixed slice and `PRESENCE_MEDIUM_ROWS`.

- [ ] **Step 4: Run the tests, then `npm run check:warnings`.** Expected: PASS and clean.

- [ ] **Step 5: Commit.** `feat(dashboard): working/break/leave design + ladder`

### Task 8: Working-hours chart and the Working/Break/Leave popup

**Files:**
- Modify: `ui/working-hours-chart/working-hours-chart.component.{ts,html,css,spec.ts}`
- Modify: `feature/team-status-detail-dialog/team-status-detail-dialog.component.{ts,html,css,spec.ts}`

**Interfaces:**
- Produces:
  - `WorkingHoursChartComponent` inputs, all optional and backward-compatible: `teamAverage = input(false)` (dashed line at each day's `workedMinutes ÷ peopleWithRecord`), `showLeave = input(false)` (red cap segment sized by `onLeaveCount`, with a tooltip).

- **Popup (spec 6.3):**
  - Tiles: Working, On break, On leave (each with %), Avg working time with `vs last week`, and Attendance rate.
  - Avg working time is this week's sum of `workedMinutes` ÷ sum of `peopleWithRecord`. "vs last week" uses a second `getWeek(previousMonday)` call.
  - **Attendance rate:** use the Team Attendance widget's existing API (`team-attendance` data-access) for the current week if it returns a team rate. Otherwise drop the tile. Record which happened in the Task 14 summary.
  - Donut, the trend with `teamAverage` and `showLeave`, the table with Task 7's columns plus CSV, then the insight banners (only when count > 0).

- [ ] **Step 1: Write failing specs:**
  - chart: `draws team average line`, `leave cap only when showLeave`, `old usage unchanged`
  - dialog: `five tiles incl. avg working vs last week`, `banners hidden at zero`, `csv still exports`

- [ ] **Step 2: Run them and confirm they fail.**

- [ ] **Step 3: Implement.**

- [ ] **Step 4: Run the tests, then `check:warnings`.** Expected: PASS and clean.

- [ ] **Step 5: Commit.** `feat(dashboard): working/break/leave popup design`

### Task 9: Schedule layout ladder

**Files:**
- Rewrite: `utils/team-schedule-layout.util.ts` (+ spec)

**Interfaces:**
- Produces:

```ts
export type ScheduleView = 'mini' | 'xs' | 'rows' | 'timeline';
export const SCHEDULE_BREAKPOINTS = { miniHeight: 260, xsWidth: 370, miniInlineWidth: 320, timelineWidth: 640, sideWidth: 1150, weekWidth: 1150, weekHeight: 800 } as const;
export const SCHEDULE_SECTION_PX = { pill: 36, avatars: 36, row: 52, axis: 24, line: 44, legend: 24, dayChartHead: 64, dayChartRow: 28, weekChart: 220, viewAll: 28, gap: 12 } as const;
export interface SchedulePlan {
  miniInline: boolean; avatars: boolean; pill: boolean;
  rows: number;               // rows (rows view) or timeline lines (timeline view)
  blockLabels: boolean;       // timeline: label blocks ≥ 48px
  dayChartRows: number;       // daily planned-vs-actual chart rows; 0 hides it
  side: boolean;              // right-hand summary / unscheduled / conflicts column
  weekChart: boolean;         // maximum size only
}
export function classifyScheduleSize(width: number, height: number): ScheduleView;
export function resolveScheduleView(width: number, height: number, previous: ScheduleView | null): ScheduleView;
export function planScheduleLayout(view: ScheduleView, width: number, height: number, members: number, plannerEnabled: boolean): SchedulePlan;
```

- **Budget order:**
  1. rows or lines, at least 3
  2. the day chart, once 5 rows fit, with as many rows as fit up to `members`
  3. `side` at ≥ `sideWidth` and enough height
  4. `weekChart` at ≥ `weekWidth × weekHeight`
  5. leftover height goes to more rows
- **Planner off:** `rows` view only (clocked vs expected bars), `dayChartRows = 0`, `weekChart = false`.

- [ ] **Step 1: Write failing specs.** The same family as Task 6: thresholds, hysteresis, no empty space across the size grid, rows ≤ members, the planner-off plan, and the week chart only at the max size.

- [ ] **Step 2: Run them and confirm they fail.**

- [ ] **Step 3: Implement.**

- [ ] **Step 4: Run them and confirm they pass.**

- [ ] **Step 5: Commit.** `feat(dashboard): schedule layout ladder`

### Task 10: Planned-vs-actual chart components

**Files:**
- Create: `ui/planned-actual-day-chart/planned-actual-day-chart.component.{ts,html,css,spec.ts}`
- Create: `ui/planned-actual-week-chart/planned-actual-week-chart.component.{ts,html,css,spec.ts}`
- Create: `utils/planned-actual-format.util.ts` (+ spec)

**Interfaces:**
- Produces:

```ts
// util
export interface PlannedActualRow { id: string; label: string; avatarFileId?: string | null; planned: number; scheduled: number; outside: number; noPlan: number; }
export interface PlannedActualSummary { planned: number; actual: number; inPlanPercent: number | null; unscheduled: number; }
export function summarizePlannedActual(rows: readonly PlannedActualRow[]): PlannedActualSummary;
export function plannedActualScale(rows: readonly PlannedActualRow[]): number; // max(planned, scheduled+outside+noPlan), min 1
export function plannedActualTiles(s: PlannedActualSummary, previous?: PlannedActualSummary | null): KpiTile[]; // Planned · Actual · In plan % · Unscheduled (+ vs-last hints)

// <app-planned-actual-day-chart [rows] [maxRows]>: one row per person, light planned bar, actual stacked in plan / outside slot / no plan, uncapped text.
// <app-planned-actual-week-chart [days] [today] [coverageLine]>: days: { date: string; planned: number; scheduled: number; outside: number; noPlan: number; coveragePercent: number | null }[]
//   Grouped bars per day, coverage line on a right 0–100% axis, today marked, future days planned only.
```

- Colours: reuse the in plan / outside slot / no plan colours from `ui/team-progress-time` (move them into `planned-actual-format.util.ts` and import them back there).

- [ ] **Step 1: Write failing specs:**
  - `scale covers overtime` (planned 60, actual 150 → scale 150, the planned bar is 40% wide, the label reads "2h 30m")
  - `in plan % null without actual`
  - `tiles show vs last week hints`
  - day chart `renders maxRows rows`
  - week chart `seven columns`, `future days have no actual bar`, `coverage line skips null days`, `aria-label summary`

- [ ] **Step 2: Run them and confirm they fail.**

- [ ] **Step 3: Implement.**

- [ ] **Step 4: Run them and confirm they pass.**

- [ ] **Step 5: Commit.** `feat(dashboard): planned vs actual charts`

### Task 11: Schedule card

**Files:**
- Rewrite: `feature/team-schedule-card/team-schedule-card.component.{ts,html,css,spec.ts}`
- Modify: `utils/team-schedule-format.util.ts` (+ spec)

**Interfaces:**
- Consumes: Tasks 5, 9 and 10; `TeamScheduleWeekStore` (loaded only when `plan().weekChart`).
- Produces in the format util:
  - `itemDonut(summary): DonutArc[]` (Completed / In progress / Pending / Meetings)
  - `timelineLabel(item): 'Task' | 'Meeting'`
  - `toPlannedActualRows(members): PlannedActualRow[]`
  - `breakSegments(member)`
  - `workloadLevel(member): 'High' | 'Medium' | 'Low' | null`, with exported thresholds `WORKLOAD_HIGH = 0.9`, `WORKLOAD_MEDIUM = 0.6` over `(plannedTask + plannedMeeting) ÷ expectedMinutes`
- **Visuals (spec 7.1):**
  - mini: the donut with total items and the pill
  - xs: adds the avatar stack
  - rows: segmented plan bar with breaks amber and unscheduled hatched
  - timeline: hour header, avatar + presence, labelled blocks, "No scheduled work" strip, now-line, legend
  - Planner off: clocked vs `expectedMinutes` bars plus the note. Mini and xs show the team's clocked total and a "Planner is off" pill instead of the donut.

- [ ] **Step 1: Write failing specs.** Views at the same size grid as Task 7, plus:
  - `planner off shows clocked vs expected and no timeline`
  - `week chart only at 1300×852 and loads the week store once`
  - `block label hidden under 48px`
  - `no scroll container`
  - format util specs

- [ ] **Step 2: Run them and confirm they fail.**

- [ ] **Step 3: Implement.**

- [ ] **Step 4: Run the tests, then `check:warnings`.** Expected: PASS and clean.

- [ ] **Step 5: Commit.** `feat(dashboard): team schedule design + ladder`

### Task 12: Schedule popup Day/Week

**Files:**
- Modify: `feature/team-schedule-detail-dialog/team-schedule-detail-dialog.component.{ts,html,css,spec.ts}`

**Interfaces:**
- Consumes: Tasks 5, 10 and 11's format helpers.
- **Header:** date navigator (steps by day or by week), `Day | Week` segmented switch, status filter `All | Working | In meeting | On break | On leave | Unscheduled`, search by name, close.
- **Day (spec 7.5):**
  - tiles: Total items, Completed, In progress, Pending, Meetings (with %)
  - main table: # · Employee + presence · Workload level · Timeline · Planned · Actual · Completion
  - side column: summary donut, unscheduled (no action button), conflicts, top workload 3
  - then the existing execution ranking and the day chart
- **Week:** `plannedActualTiles(totals, previous)`, the week chart, and the per-person table (# · Employee · Planned · Actual · Coverage bar + % · Unscheduled). CSV export in both modes.

- [ ] **Step 1: Write failing specs:**
  - `week mode loads week store and steps by 7 days`
  - `future week arrow disabled`
  - `status filter and search narrow both tables`
  - `csv per mode`
  - `planner off week shows clocked only`

- [ ] **Step 2: Run them and confirm they fail.**

- [ ] **Step 3: Implement.**

- [ ] **Step 4: Run the tests, then `check:warnings`.** Expected: PASS and clean.

- [ ] **Step 5: Commit.** `feat(dashboard): team schedule popup day/week`

### Task 13: Team Progress visual pass

**Files:**
- Modify: `feature/team-progress-card/team-progress-card.component.{html,css,ts,spec.ts}`
- Modify: `ui/weekly-completion-chart/*`, `ui/team-progress-time/*`, `ui/team-progress-time-detail/*`
- Modify: `feature/team-progress-detail-dialog/*`
- Modify: `utils/team-progress-layout.util.ts` (only `TEAM_PROGRESS_TIME_PX`, if the chart replaces the band)

**Interfaces:**
- Consumes: `app-planned-actual-week-chart` (Task 10). It accepts the `LedTimeBucket[]` mapping `{ date: from, planned: plannedMinutes, scheduled: scheduledMinutes, outside: 0, noPlan: unscheduledMinutes, coveragePercent: null }`. Month buckets are week-labelled: add an optional `labels` input to the week chart, defaulting to weekday names.
- **Changes (spec 8):**
  - Tiles get the icon on the right, value, label and %.
  - Weekly completion gets stacked bars by status + a % line if its payload has per-status hours; otherwise completed vs allocated + the % line.
  - In tall, large and maximum views, the time band becomes the bucket chart (budget height `TEAM_PROGRESS_TIME_PX.chart = 180`, re-measured in Task 14).
  - The callout becomes a top-3 unscheduled panel.
  - Popup: a "Total allocated" tile, a Project breakdown table (from the card's project rows), employee breakdown search + paging via `app-data-table`, and the planned-vs-worked chart in the Task 10 style.
  - Keep the All / Month / Week select and the workload ranking.

- [ ] **Step 1: Write failing specs:**
  - `tall view shows bucket chart instead of band`
  - `wide view keeps band`
  - `unscheduled panel lists top 3`
  - `popup project breakdown rows`
  - `employee search filters`
  - the existing layout specs still pass (update only the numbers this task changes)

- [ ] **Step 2: Run them and confirm they fail.**

- [ ] **Step 3: Implement.**

- [ ] **Step 4: Run the tests, then `check:warnings`.** Expected: PASS and clean.

- [ ] **Step 5: Commit.** `feat(dashboard): team progress design pass`

### Task 14: Measure, browser pass, gates, summary

**Files:**
- Modify: the three layout utils' `*_SECTION_PX` constants (measured values)
- Create: `HRMS-Backend-v1/docs/reports/work-management/2026-11-02-my-team-widgets-design-match-summary.md`

- [ ] **Step 1: Ask the user to start the API and web app and sign in.** Use the Playwright window at `https://dapi.onexso.com:4200/dashboard/my-team`.

- [ ] **Step 2: Measure every section's real height with `getBoundingClientRect`.** Update the constants and their specs. Re-run `ng test` for the three layout utils. Expected: PASS.

- [ ] **Step 3: For each card, force `gridArea` on the parent of `app-widget-frame` at every w×h from 2×2 to 12×8.** Wait about 3s after each resize. Assert that the frame's `overflow-auto` div has `scrollHeight - clientHeight === 0`. Take screenshots at 2×2, 4×4, 6×6 and 12×8, and of each popup. Expected: 0 overflow everywhere.

- [ ] **Step 4: Repeat with the Planner on.** Set `Planner:Enabled=true` locally. Seed a day and a week of blocks and clock sessions for at least 5 reports through the Planner API from the signed-in page, using a dev script kept in `../dev-env/scripts/`, not `src/`. Take screenshots next to the preview images. Note the seed location in the summary.

- [ ] **Step 5: Time `GET planner/team/week`.** 2 warm-ups, then 20 timed calls (`fetch(url, { credentials: 'include' })`). Expected: p95 < 500 ms.

- [ ] **Step 6: Run the gates:**
  - `verify.ps1 backend`
  - `verify.ps1 backend -Full` (Docker)
  - `verify.ps1 frontend`
  - `npm run check:warnings`
  - eslint on changed files
  - `find-unauthorized-endpoints.ps1 backend`
  - `security-scan.ps1` for both repos with the baselines

  Expected: 0 failed and 0 new findings. Record the real counts.

- [ ] **Step 7: Write the summary report.** Cover what shipped, the screenshots list, timings, real test counts, the attendance-rate decision (Task 8), and anything left.

- [ ] **Step 8: Commit in both repos.** `docs: my team widgets design match summary` plus the measured constants. Don't push. Move the spec and plan to `finished/` only after the user signs off in the browser.
