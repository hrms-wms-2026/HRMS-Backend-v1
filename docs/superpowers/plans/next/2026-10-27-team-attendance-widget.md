# Team Attendance Widget Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a `team-attendance` widget to the My Team dashboard. It has three size-driven views (Small / Medium / Large) plus an Expanded popup, a working Weekly ↔ Monthly switch, and an employee × date matrix with CSV export.

**Architecture:**
- **Backend:** a new batched `TeamAttendancePeriodReader` runs the existing `AttendancePeriodCalculator.Classify` for every covered employee over one week or month. Two new query handlers sit on top of it: summary, and paged employees. "Today" comes from the existing `GetCoveredTeamTodayQuery`, so this card always agrees with the team-status card.
- **Frontend:** one card component picks its view from its own pixel size (team-progress pattern). A signal store holds `{mode, anchor, departmentId}` and does latest-wins fetching. The popup is an `app-modal`.

**Tech Stack:** .NET 10 / MediatR / EF Core / xUnit + FluentAssertions + NSubstitute (backend), Angular standalone + @ngrx/signals + Vitest via `ng test` (frontend).

**Spec:** `docs/superpowers/specs/next/2026-10-27-team-attendance-widget-design.md`. Read it first. Every task implicitly includes it.

**Repos and branches:**
- Backend: `C:\Users\User\Desktop\build\HRMS-Backend-v1`, branch `feature/team-attendance-widget` (exists).
- Frontend: `C:\Users\User\Desktop\build\Hrms--Web-application---front-end---v1`. Task 6 creates `feature/team-attendance-widget` from `feature/customizable-dashboard`.

## Global Constraints

- Late counts as attended. Rate = (Present + Late) ÷ (Present + Late + Absent) employee-days. Leave and pending are **never** in the denominator. A zero denominator gives `null`.
- Comparison = current rate − previous rate in **percentage points**. An in-progress previous period is clipped to the same elapsed day count.
- Scope = exactly the `GetCoveredTeamTodayQuery` scope (`attendance:read`, `ResolveVisibilityAsync(... IncludeSelf:false, TimeTrackingRead)`, actor stripped). Department/search only narrow it.
- Leave masking: same as `TeamStatusMapper` (`ILeaveVisibilityScopeProvider` + `EmployeeVisibilityScopeMatcher`). An unauthorized leave day counts as **Absent** everywhere.
- Range = an ISO week (Mon–Sun) or a calendar month containing `anchor`. Any other `mode` is a 400. `pageSize` is ≤ 50.
- No new tables, triggers or jobs. A migration is allowed only for an index that a query plan proves missing (expected: none).
- Backend builds use `-c Release`. Never start or leave running an API from the Debug bin (the user runs their own API).
- Frontend: `npm run check:warnings` must pass. Component styles go via `styleUrl`, never an imported shared `styles` const (that hangs the build).
- No push, PR, merge, or migration against a shared DB.

## Review Focus

1. **Today mid-shift:** the summary is called at 10:00 on a weekday. People not yet due show as pending, not Absent, and today's bar uses the team-today engine. Tested in Task 4 (`Summary_TodayUsesTeamTodayStatuses`).
2. **Team larger than the team-today cap (200):** today cells for employees missing from `Members` fall back to Classify for today, and today's snapshot counts use `Summary` (uncapped). Tested in Task 4 (`Summary_TodayCountsUseSummaryNotCappedMembers`).
3. **Viewer without leave visibility:** leave days show and count as Absent in summary, employees rows and export. Tested in Task 3 (`Reader_MasksLeaveAsAbsent_WhenNotLeaveAuthorized`).
4. **Rapid Week→Month→Week clicks:** only the last response is applied. Tested in Task 7 (`setMode_latestResponseWins`).
5. **February in a leap year and month rollover:** the range is 29 days, and the previous month is clipped to a shorter length (31 Mar → 29 Feb). Tested in Task 1 (`Resolve_MonthClipsPreviousToItsLength`).

---

## Backend

### Task 1: Period range + bucket math (pure)

**Files:**
- Create: `src/ONEVO.Application/Features/TimeAttendance/Team/Services/TeamAttendanceMath.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/TimeAttendance/TeamAttendanceMathTests.cs`

**Interfaces — Produces:**
```csharp
public enum TeamAttendanceMode { Week, Month }
public sealed record TeamAttendanceRange(DateOnly From, DateOnly To, DateOnly PrevFrom, DateOnly PrevTo, bool InProgress);
public static class TeamAttendanceBuckets { public const string Present="present", Late="late", Absent="absent", Leave="leave", Pending="pending", Off="off"; }
public static class TeamAttendanceMath
{
    public static bool TryParseMode(string? mode, out TeamAttendanceMode parsed); // "week"|"month", case-insensitive
    public static TeamAttendanceRange Resolve(TeamAttendanceMode mode, DateOnly anchor, DateOnly today);
    // classifyStatus = Classify day status; isLate = date is in Classification.Dates.Late
    public static string Bucket(string classifyStatus, bool isLate, bool leaveAuthorized);
    public static decimal? Rate(int attended, int denominator); // null when denominator == 0; rounded to 1 dp
}
```

Bucket rules (spec §4):
- `present` / `missing_clock_out` → Present, but Late when `isLate`
- `late` → Late
- `absent` → Absent
- `leave` → Leave, or Absent when `!leaveAuthorized`
- `off` and `none` → Off

Pending is produced only by the today merge in Task 4.

`Resolve`:
- InProgress = `today` is within [From, To].
- When in progress, `PrevTo` = PrevFrom + (today − From) days, capped at the previous period's last day.
- Otherwise the whole previous period.

- [ ] **Step 1: Write failing tests**
  - `Resolve_Week_IsIsoMondayToSunday`: anchor 2026-10-28 (Wed) gives 2026-10-26..2026-11-01, prev 2026-10-19..2026-10-25 when today 2026-11-05.
  - `Resolve_InProgressWeek_ClipsPrevious`: anchor = today = 2026-10-28 gives PrevFrom 2026-10-19, PrevTo 2026-10-21, InProgress true.
  - `Resolve_LeapFebruary_Has29Days`: anchor 2028-02-10 gives 2028-02-01..2028-02-29.
  - `Resolve_MonthClipsPreviousToItsLength`: anchor = today = 2028-03-31 gives PrevFrom 2028-02-01, PrevTo 2028-02-29.
  - `Resolve_JanuaryPreviousIsDecemberPriorYear`: anchor 2027-01-15, today 2027-03-01, gives prev 2026-12-01..2026-12-31.
  - `Bucket_*`: one `[Theory]` row per mapping rule, including missing_clock_out+late → Late and leave+unauthorized → Absent.
  - `Rate_ZeroDenominator_IsNull`, and `Rate_24Of24_Is100`.
- [ ] **Step 2:** `dotnet test tests/ONEVO.Tests.Unit -c Release --filter TeamAttendanceMathTests`. Expected: compile failure.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Re-run. Expected: all pass.
- [ ] **Step 5:** Commit `feat(attendance): team attendance period and bucket math`.

### Task 2: Batched excused-dates and late-threshold lookups

**Files:**
- Modify: `src/ONEVO.Application/Features/TimeAttendance/Alerts/IAttendanceAlertRepository.cs` (add to `IAttendanceAlertResolutionLookup`)
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/TimeAttendance/EfAttendanceAlertRepository.cs`
- Modify: `src/ONEVO.Application/Features/TimeAttendance/Services/EmployeeClockInPolicyResolver.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/TimeAttendance/ClockInPolicies/EmployeeClockInPolicyResolverBatchTests.cs`, plus an Ef repo test next to the existing `EfAttendanceAlertRepository` tests if there are any (they follow that file's DB setup)

**Interfaces — Produces:**
```csharp
// IAttendanceAlertResolutionLookup
Task<IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, IReadOnlySet<DateOnly>>>> ExcusedDatesForEmployeesAsync(
    Guid tenantId, IReadOnlyCollection<Guid> employeeIds, DateOnly from, DateOnly to, CancellationToken ct);
// IEmployeeClockInPolicyResolver
Task<IReadOnlyDictionary<Guid, int>> ResolveThresholdsAsync(
    Guid tenantId, Guid legalEntityId, IReadOnlyCollection<Employee> employees, DateOnly date, CancellationToken ct);
```

- `ExcusedDatesForEmployeesAsync` is one query with the same filter as `ExcusedDatesAsync`, grouped by employee.
- `ResolveThresholdsAsync` makes one `ListActiveOnDateAsync` call and one `GetActivePrimaryByEmployeeIdsAsync` call. It uses the passed `Employee.DepartmentId` and applies the same precedence as `ResolveAsync` (employee > position > department > full company), then `ClockInPolicyRules.ThresholdMinutes`. Extract the precedence into a private static picker shared by both methods, so they can't drift. Every NSubstitute fake of `IAttendanceAlertResolutionLookup` / `IEmployeeClockInPolicyResolver` in tests keeps compiling. Interface additions are fine for NSubstitute. Hand-written fakes need the new member.

- [ ] **Step 1: Write failing tests**
  - `ResolveThresholds_MatchesSingleResolvePerEmployee`: 3 employees with an employee-scope, department-scope and full-company policy. Each value equals `ThresholdMinutes(await ResolveAsync(...))`.
  - `ResolveThresholds_IssuesOnePolicyAndOneAssignmentQuery`: `Received(1)` on both repo calls for 3 employees.
- [ ] **Step 2:** Run the filter. Expected: FAIL.
- [ ] **Step 3:** Implement both methods.
- [ ] **Step 4:** `dotnet build src/ONEVO.Application -c Release`, then the full unit suite: `dotnet test tests/ONEVO.Tests.Unit -c Release`. Expected: green except the known baseline.
- [ ] **Step 5:** Commit `feat(attendance): batched excused dates and late thresholds`.

### Task 3: TeamAttendancePeriodReader

**Files:**
- Create: `src/ONEVO.Application/Features/TimeAttendance/Team/Services/TeamAttendancePeriodReader.cs` (interface + impl in one file, as `IEmployeeAttendancePeriodReader` is split. Follow that: put the interface in `ITeamAttendancePeriodReader.cs`)
- Modify: the DI registration where `IEmployeeAttendancePeriodReader` is registered (grep `EmployeeAttendancePeriodReader` in `src/ONEVO.Application/DependencyInjection.cs` or Infrastructure DI)
- Test: `tests/ONEVO.Tests.Unit/Features/TimeAttendance/TeamAttendancePeriodReaderTests.cs`

**Interfaces — Consumes:** Task 1 `TeamAttendanceMath.Bucket`, and Task 2 batch methods. **Produces:**
```csharp
public sealed record TeamAttendanceEmployeeDays(
    Guid EmployeeId, Guid? DepartmentId, IReadOnlyDictionary<DateOnly, string> BucketByDate); // every date in [from,to]
public interface ITeamAttendancePeriodReader
{
    Task<IReadOnlyList<TeamAttendanceEmployeeDays>> LoadAsync(
        Guid tenantId, IReadOnlyCollection<Guid> employeeIds, DateOnly from, DateOnly to, CancellationToken ct = default);
}
```

The reader loads the same inputs as `EmployeeAttendancePeriodReader.LoadAsync`, but **once for all ids**:
- `ListRecordsAsync(ids, from, to, 0, ids.Count * 32)`
- `ListApprovedCoveringAsync(ids, …)`
- `ListHolidayDatesAsync` once
- `employees.ListByIdsAsync`
- each distinct legal entity once via `GetByIdForTenantAsync`
- `ExcusedDatesForEmployeesAsync`
- `ResolveThresholdsAsync` per legal entity (date = `to`)
- `GetActivePrimaryByEmployeeIdsAsync` + `ILeaveVisibilityScopeProvider.ResolveForCurrentUserAsync` once for leave masking

Breaks are skipped: pass an empty `BreakMinutesByDate`.

For each employee, build `AttendancePeriodData`:
- the employee's own legal-entity timezone
- `ExpectedWorkdayCalendar.Build` with that entity's weekdays, the employee's hire/termination dates and `clock.Today`

Then call `Classify` and map every day through `Bucket(status, Dates.Late.Contains(d), leaveAuthorized)`. The executor must make sure `Dates.Late` is post-excuse, as Classify returns it. A late day whose late alert was excused is therefore Present.

- [ ] **Step 1: Write failing tests** (NSubstitute repos, `TimeZoneInfo.Utc`, Mon–Fri entity, today 2026-10-30):
  - `Reader_ClassifiesEachEmployee_SameAsSingleEmployeeClassify`: 2 employees, with a record set covering present, late (by threshold), absent and leave. The buckets equal what `Classify` + `Bucket` give per employee.
  - `Reader_IssuesBatchedQueries`: for 5 employees, `ListRecordsAsync`, `ListApprovedCoveringAsync`, `ListByIdsAsync` and `ExcusedDatesForEmployeesAsync` each `Received(1)`.
  - `Reader_MasksLeaveAsAbsent_WhenNotLeaveAuthorized`: the leave scope excludes the employee, so the leave date bucket = `absent`.
  - `Reader_LateWithMissingClockOut_IsLate`.
  - `Reader_EmptyIds_ReturnsEmpty_NoQueries`.
- [ ] **Step 2:** Run the filter. Expected: FAIL.
- [ ] **Step 3:** Implement + register DI.
- [ ] **Step 4:** Re-run. Expected: pass.
- [ ] **Step 5:** Commit `feat(attendance): batched team attendance period reader`.

### Task 4: Summary + employees queries, controller actions

**Files:**
- Create: `src/ONEVO.Application/Features/TimeAttendance/Team/Queries/TeamAttendanceQueries.cs` (records), `TeamAttendanceQueryHandlers.cs`, `../DTOs/TeamAttendanceResponses.cs`
- Modify: `src/ONEVO.Api/Controllers/Tenant/Attendance/TimeTrackingController.cs` (two actions beside `TeamToday`)
- Test: `tests/ONEVO.Tests.Unit/Features/TimeAttendance/TeamAttendanceQueryHandlerTests.cs`

**Interfaces — Consumes:** Task 1 and Task 3, plus `GetCoveredTeamTodayQuery(200)` via `IMediator` (precedent: `GetEmployeeOverviewSignalsQueryHandler`). **Produces** (JSON camelCase via the default serializer):
```csharp
public sealed record GetTeamAttendanceSummaryQuery(string Mode, DateOnly? Anchor, Guid? DepartmentId) : IRequest<Result<TeamAttendanceSummaryResponse>>;
public sealed record GetTeamAttendanceEmployeesQuery(string Mode, DateOnly? Anchor, Guid? DepartmentId, string? Search, int Page, int PageSize) : IRequest<Result<PagedResult<TeamAttendanceEmployeeRow>>>;

public sealed record TeamAttendanceCounts(int Present, int Late, int Absent, int OnLeave, int Pending);
public sealed record TeamAttendanceTrendPoint(DateOnly Date, int Present, int Late, int Absent, int OnLeave, int Pending, decimal? Rate);
public sealed record TeamAttendancePreviewRow(Guid EmployeeId, string Name, Guid? AvatarFileId, string? Department, string Bucket, DateTimeOffset? ClockInAt);
public sealed record TeamAttendanceDepartment(Guid Id, string Name);
public sealed record TeamAttendanceSummaryResponse(
    string Mode, DateOnly RangeStart, DateOnly RangeEnd, string Timezone, DateOnly Today,
    int EligibleToday, TeamAttendanceCounts TodayCounts, decimal? TodayRate,
    TeamAttendanceCounts PeriodCounts, decimal? PeriodRate, decimal? PreviousRate, decimal? DeltaPp,
    IReadOnlyList<TeamAttendanceTrendPoint> Trend, IReadOnlyList<TeamAttendancePreviewRow> TodayPreview,
    IReadOnlyList<TeamAttendanceDepartment> Departments, int TeamSize, DateTimeOffset CalculatedAtUtc, bool IsProvisional);
public sealed record TeamAttendanceDayCell(DateOnly Date, string Bucket);
public sealed record TeamAttendanceEmployeeRow(Guid EmployeeId, string Name, Guid? AvatarFileId, string? Department,
    IReadOnlyList<TeamAttendanceDayCell> Days, int Attended, int EligibleDays, decimal? Rate);
```

Routes:
- `GET api/v1/attendance/time-tracking/team/attendance-summary?mode=&anchor=&departmentId=`
- `GET …/team/attendance-employees?mode=&anchor=&departmentId=&search=&page=1&pageSize=20`

Both carry `[RequirePermission("attendance:read")]`.

**Handler rules:**
- The scope code is copied from `GetCoveredTeamTodayQuery`. Extract it into a private helper shared by both handlers. Do not touch the original handler.
- `departmentId` filters ids to employees with that `DepartmentId`. Any id not among the covered employees' departments gives an empty result.
- `anchor` defaults to the team-today `WorkDate`. Parse `mode` with `TryParseMode`, failure = `Result.Failure` (400).
- Load the reader once over [PrevFrom, To].

**Today merge:**
- Map the team-today `Members[i]` per employee:
  - `working` / `on_break` / `clocked_out` → `IsLate ? late : present`
  - `absent` → absent
  - `not_started` → pending
  - `on_leave` → leave
  - `not_scheduled` → off
- That value overrides the reader's bucket for `Today`.
- Employees not in `Members` (cap) keep the reader bucket for today.
- `TodayCounts`/`EligibleToday` come from team-today `Summary`:
  - present = Working + OnBreak + ClockedOut − Late
  - late = Late
  - absent = Absent
  - onLeave = OnLeave
  - pending = NotStarted
  - eligible = Total − NotScheduled
- With a department filter, compute today counts from the merged buckets of the filtered ids instead (the Summary is team-wide).

**Rates and other fields:**
- `PeriodRate` is over [From, To], `PreviousRate` over [PrevFrom, PrevTo].
- `DeltaPp` = difference, rounded to 1 dp, null if either side is null.
- `Trend` has one point per date From..To. Future dates get zero counts and a null rate.
- `TodayPreview`: ≤ 6 rows, order late, absent, pending, present, leave, then name. Name and avatar come from `ListEmployeeIdentitiesAsync`. Department names come from `IDepartmentRepository.ListByLegalEntityAsync` for the actor's entity.
- `IsProvisional` = range contains today.

**Employees query:**
- Search by `DisplayName` contains (case-insensitive) on the identities, before paging.
- Order by name.
- Page in memory after scoping. The scope is bounded by visibility, and the page size is clamped to 1..50.

- [ ] **Step 1: Write failing tests** (NSubstitute: reader, mediator, repos):
  - `Summary_NoPermission_Forbidden`
  - `Summary_BadMode_Failure`
  - `Summary_RateExcludesLeaveAndPending`: 3 employees, buckets give 24 attended / 24 denominator → 100.
  - `Summary_DeltaPp_IsPercentagePoints`: prev 90.0, current 96.0 → 6.0.
  - `Summary_TodayUsesTeamTodayStatuses`: the reader says `absent` today, team-today says `not_started` → pending, and pending is not counted in TodayRate.
  - `Summary_TodayCountsUseSummaryNotCappedMembers`
  - `Summary_UnknownDepartment_ReturnsEmptyNotOthers`
  - `Summary_ExcludesActor`
  - `Summary_PreviewOrderedProblemsFirst_Max6`
  - `Employees_MonthHasDaysInMonthCells`: Feb 2028 → 29 cells.
  - `Employees_SearchAndPaging`: pageSize 100 is clamped to 50.
- [ ] **Step 2:** Run the filter. Expected: FAIL.
- [ ] **Step 3:** Implement the handlers, DTOs and controller actions.
- [ ] **Step 4:** Re-run. Then `dotnet build src/ONEVO.Api -c Release` and `dotnet test tests/ONEVO.Tests.Architecture -c Release`. Expected: green (the architecture suite checks endpoint permission attributes).
- [ ] **Step 5:** Commit `feat(attendance): team attendance summary and employees endpoints`.

### Task 5: Dashboard registry, access, provider

**Files:**
- Modify: `src/ONEVO.Application/Features/Dashboard/Layout/Registry/DashboardWidgetRegistry.cs`
  - add `new("team-attendance", DashboardKeys.MyTeam, "Team attendance", "Attendance across your team by week or month.", 3, 3, 12, 8, true)`
  - add MyTeamDefaults `("team-attendance", new GridRect(0, 6, 8, 6))`
  - `MyTeamWithoutTeamStatus` / `MyTeamQuickActionsOnly` stay unchanged: same gate as team-status, so it's hidden whenever team-status is
- Modify: `src/ONEVO.Application/Features/Dashboard/Layout/Access/DashboardWidgetAccess.cs` (`TeamAttendance` const, added under the `CanViewPeopleStatus` condition)
- Modify: `src/ONEVO.Api/Dashboard/Widgets/WidgetDataProviders.cs`
  - `TeamAttendanceWidgetDataProvider : EndpointMirrorWidgetDataProvider`, key `team-attendance`
  - Gate `EndpointGate.For<TimeTrackingController>(nameof(TimeTrackingController.TeamAttendanceSummary))`
  - `LoadAsync` sends `new GetTeamAttendanceSummaryQuery("week", null, null)`
  - registered in `AddDashboardWidgetDataProviders`
- Test: update `DashboardWidgetRegistryTests.cs`, `DashboardWidgetAccessTests.cs`, and the provider/HasProvider + EndpointGate tests (grep `HasProvider` in tests)

- [ ] **Step 1: Update or add tests**
  - The My Team catalog contains `team-attendance`.
  - Default desktop rect is (0,6,8,6). Tablet/mobile derive without overlap (existing overlap test covers it).
  - Access includes `team-attendance` iff `CanViewPeopleStatus`.
  - The provider is keyed and `HasProvider` agrees.
  - Any count assertion for My Team (3) becomes 4.
- [ ] **Step 2:** Run `--filter "FullyQualifiedName~Dashboard"`. Expected: FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Full unit + architecture suites, `-c Release`. Expected: green except the documented baseline. Record the counts.
- [ ] **Step 5:** Commit `feat(dashboard): register team-attendance widget with data provider`.

## Frontend

### Task 6: Models, API service, store

**Files** (all under `src/app/modules/dashboard/`):
- Branch: `git switch -c feature/team-attendance-widget` from `feature/customizable-dashboard`
- Create: `models/team-attendance.model.ts`, `data-access/team-attendance-api.service.ts` (+ `.spec.ts`), `state/team-attendance.store.ts` (+ `.spec.ts`)

**Interfaces — Produces:**
- TS mirrors of the Task 4 DTOs, camelCase, dates as `string` (yyyy-MM-dd). Type `TeamAttendanceBucket = 'present'|'late'|'absent'|'leave'|'pending'|'off'`.
- `TeamAttendanceApiService`:
  - `getSummary(q: TeamAttendancePeriodQuery): Observable<TeamAttendanceSummaryResponse>`
  - `getEmployees(q: TeamAttendancePeriodQuery & { search?: string; page: number; pageSize: number }): Observable<PagedResult<TeamAttendanceEmployeeRow>>`
  - basePath `${environment.apiUrl}/attendance/time-tracking/team`. Params are omitted when null.
  - `TeamAttendancePeriodQuery = { mode: 'week'|'month'; anchor: string | null; departmentId: string | null }`
- `TeamAttendanceStore` (signalStore, providedIn root):
  - state: `{ query: TeamAttendancePeriodQuery, data, loading, error }`
  - methods:
    - `seed(data)`: sets data and also sets query.anchor = data.rangeStart, so ‹ › works from the bootstrap
    - `markLoading()`
    - `load()`
    - `setMode(mode)`: keeps the anchor, as the "today" or current-range anchor
    - `shift(step: -1 | 1)`: moves the anchor by 7 days or 1 month
    - `setDepartment(id | null)`
  - Fetches use an rxjs `Subject` + `switchMap`, so only the latest request's response is applied (`rxMethod` from `@ngrx/signals/rxjs-interop` if the repo already uses it, otherwise a manual subscription).
  - Use a PagedResult type if one already exists in `shared`/`core` (grep `interface PagedResult`).

- [ ] **Step 1: Write failing specs**
  - api: params for `{mode:'month', anchor:'2026-10-01', departmentId:null}` are `mode=month&anchor=2026-10-01` only.
  - store: `seed` sets anchor. `shift(1)` in month mode from 2026-01-31 gives anchor 2026-02-01 (always first-of-month in month mode). `setMode_latestResponseWins`: two pending requests resolve out of order, and data equals the second.
- [ ] **Step 2:** `npx ng test --include="src/app/modules/dashboard/**/team-attendance*.spec.ts" --watch=false`. Expected: FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Re-run. Expected: pass.
- [ ] **Step 5:** Commit `feat(dashboard): team attendance api and store`.

### Task 7: Layout util + shared stacked day chart

**Files:**
- Create: `utils/team-attendance-layout.util.ts` (+ spec), modelled on `team-progress-layout.util.ts`
- Create: `ui/attendance-day-chart/attendance-day-chart.component.{ts,html,css}` (+ spec)

Decision: a new chart, not `weekly-completion-chart`. That one is hours-per-week with a different tooltip/scale, and stacking would change its look.

**Interfaces — Produces:**
- `type TeamAttendanceView = 'small' | 'medium' | 'large'`
- `classifyTeamAttendanceSize(w, h): TeamAttendanceView`, `resolveTeamAttendanceMode(prev, w, h)`, with hysteresis 20px and a zero size keeping prev (or 'small')
- Thresholds:
  - large when h ≥ 580 && w ≥ 700
  - medium when w ≥ 560 (any h ≥ 260), or when h ≥ 580 && w < 700 (stacked medium)
  - else small
- `<app-attendance-day-chart [points]="TeamAttendanceTrendPoint[]" [compact]="boolean" />`
  - stacked bars present (bottom) / late / absent / leave
  - x labels: Mon..Sun for ≤ 7 points, otherwise day numbers, every 5th when compact
  - `role="img"` + aria-label summarising the period rate
  - future points render as empty slots

- [ ] **Step 1: Write failing specs**
  - util: 350×350 → small, 760×350 → medium, 760×640 → large, 500×640 → medium, hysteresis keeps large at 690×640 coming from large.
  - chart: 7 points → 7 `[data-testid=day-bar]` and the Mon label first; 31 points → 31 bars; segment heights proportional to counts.
- [ ] **Step 2:** Run. Expected: FAIL.
- [ ] **Step 3:** Implement. Colours: Present `var(--color-success)`, Late `var(--color-warning-text)`, Absent `var(--color-danger-text)`, Leave `var(--color-accent)` if no info/blue token exists (grep `--color-info`).
- [ ] **Step 4:** Re-run. Expected: pass.
- [ ] **Step 5:** Commit `feat(dashboard): team attendance layout util and day chart`.

### Task 8: Team attendance card (Small / Medium / Large)

**Files:**
- Create: `feature/team-attendance-card/team-attendance-card.component.{ts,html,css,spec.ts}`
- Modify: `widgets/widget-registry.ts` (`'team-attendance': { component: TeamAttendanceCardComponent }`) and its spec if it lists keys

**Interfaces — Consumes:**
- Task 6 store, Task 7 util/chart
- `injectWidgetDataFeed<TeamAttendanceSummaryResponse>({ apply: d => store.seed(d), fetch: () => void store.load(), refreshing: () => store.markLoading() })`, exactly as `team-progress-card`
- ResizeObserver + rAF as in team-progress-card
- Shared: `WidgetCardComponent`, `PopoutHeaderActionsComponent`, `app-select`, `app-kpi-tiles`, `app-progress-ring`, `app-avatar` (`buildFileUrl(avatarFileId)` as team-status-card does), `app-empty-state`, `app-error-state`

**Views:**
- **Header (all):** title "Team attendance", Week/Month `app-select`, ‹ › buttons with aria-labels "Previous period"/"Next period" (Next disabled when the range contains today), range label ("26 Oct – 1 Nov" / "October 2026"), expand button.
- **Small:**
  - `TodayRate` large ("96%" or "—"), label "Attendance today"
  - `app-progress-ring` with (present + late)/eligible and the center "24 / 25"
  - delta line "↑ 6 pp vs last week|month" (green up, red down, hidden when null)
- **Medium:**
  - left: the donut of today's counts (reuse `pie-slices.util` as team-progress does) with `EligibleToday` "Team today" in the center
  - four status rows: count + % of eligible
  - right: `app-attendance-day-chart [compact]="true"` over the period
  - stacks vertically in the narrow-tall medium case
- **Large:**
  - `app-kpi-tiles` with 4 tiles (Present success, Absent danger, Late warning, On leave accent), hint = % of eligible today. Add a pending hint "N not in yet" on Present if pending > 0.
  - below: chart (left) + "Today – Employee Status" list of `TodayPreview` (avatar, name, status chip, clock-in HH:mm in the summary `timezone` or "–") with "View all" opening the popup.
- **States:**
  - loading skeleton
  - `teamSize === 0` → empty-state "No one in your team yet"
  - period with no eligible days → "No working days in this period" in the chart area
  - error → error-state with retry → `store.load()`
  - `isProvisional` → muted "Today in progress" next to the range

- [ ] **Step 1: Write failing specs** (host sets the size via the parent `gridArea` style, as team-progress specs do):
  - one component renders `[data-testid=ta-small]`, `ta-medium` or `ta-large` by size
  - `rate null renders —`
  - changing the select to Month calls `store.setMode('month')`
  - ‹ calls `shift(-1)`, Next is disabled in the current period
  - the large view renders 4 `kpi-tile` and ≤ 6 preview rows
  - Late chip text is "Late"
- [ ] **Step 2:** Run the card spec. Expected: FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Re-run, then `npm run check:warnings` and `npx ng build`. Expected: clean.
- [ ] **Step 5:** Commit `feat(dashboard): team attendance card with three size views`.

### Task 9: Expanded popup with matrix and export

**Files:**
- Create: `feature/team-attendance-detail-dialog/team-attendance-detail-dialog.component.{ts,html,css,spec.ts}`
- Move: `csvCell` + `downloadCsv` from `src/app/modules/people/utils/employee-csv.ts` into `src/app/shared/utils/csv.util.ts` (+ spec). `employee-csv.ts` re-imports them, and its behaviour is unchanged.
- Modify: the card from Task 8 opens the dialog (expand button / View all).

**Interfaces — Consumes:** the same `TeamAttendanceStore` (so the period is shared), `TeamAttendanceApiService.getEmployees`, `app-modal size="2xl"` (full-screen < 640px as team-progress-detail-dialog does), `app-kpi-tiles` (5 tiles), the chart, the donut, `app-avatar`, `app-select` (department, options from `data.departments`, plus "All departments"), and the search `app-input`.

**Contents:**
- 5 tiles: Team = `teamSize`, then Present / Absent / Late / On leave = `periodCounts` labelled "days".
- Daily trend chart (non-compact), and the distribution donut of `periodCounts`.
- Matrix:
  - sticky # + name + department columns, then one column per date in the range (7 or 28–31, horizontally scrollable)
  - cells: icon + `aria-label` per bucket. Present = check (success), Late = clock (warning, visibly distinct), Absent = x (danger), Leave = leave icon (accent), Pending = dot, Off = "–".
  - last column: rate % chip or "—"
- Search is debounced 300ms. Paging is 20 per page with the existing pagination UI if one exists in `shared/ui` (`data-table` has it), otherwise prev/next.
- **Export:** pages through `getEmployees` (pageSize 50) for the current query/department/search. Header: `Name,Department,<dates…>,Attended,Eligible days,Rate %`. Cells use bucket labels "Present/Late/Absent/Leave/Not in yet/Off". File: `team-attendance-<rangeStart>_<rangeEnd>.csv` via `downloadCsv`. The button is disabled while exporting.

- [ ] **Step 1: Write failing specs**
  - opening keeps the store's mode
  - a month query for 2028-02 renders 29 date headers, a week renders 7
  - a Late cell has `aria-label="Late"` and differs from Present
  - typing in search calls `getEmployees` with `search` after debounce
  - department select → `store.setDepartment`
  - export builds a CSV whose name cell `=HYPERLINK(...)` is escaped to `'=HYPERLINK(...)`
  - Esc closes (app-modal behaviour, one smoke assertion)
  - `csv.util.spec`: the moved functions keep the existing escaping cases
- [ ] **Step 2:** Run. Expected: FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Full frontend suite `npx ng test --watch=false`, `npm run check:warnings`, `npx ng build`. Expected: green except the known baseline (notification-settings spec; timeout flakes in work/* that pass alone). Record the counts.
- [ ] **Step 5:** Commit `feat(dashboard): team attendance detail popup with matrix and export`.

### Task 10: Verification pass

- [ ] Backend: `dotnet test tests/ONEVO.Tests.Unit -c Release` and `tests/ONEVO.Tests.Architecture -c Release`. Record the numbers. Integration tests only if Docker is up; otherwise say so.
- [ ] Ask the user to restart their API on the new backend code. Do not start one from Debug.
- [ ] The user signs in to the Playwright browser at https://dapi.onexso.com:4200. Add the widget to My Team. Screenshot Small (3×3), Medium (8×3), Large (8×6) and the popup, in Week and Month each, at desktop and at 375px. Check for no horizontal page scroll at 375px.
- [ ] Spot-check: for one employee and the current week, the matrix cells match that employee's Attendance Overview day list (statuses, not the rate).
- [ ] Measure summary/employees latency from the browser network panel (p50 of 5 loads each, week and month), or state that it wasn't measured.
- [ ] Write `docs/superpowers/reports/TEAM_ATTENDANCE_WIDGET_SUMMARY.md` (backend repo) covering:
  - components reused, with paths
  - files changed
  - why no migration was needed
  - test counts, latency, screenshots list
  - remaining risks
  - "no push/PR/merge/migration done"
