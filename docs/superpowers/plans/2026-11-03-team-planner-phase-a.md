# Team Planner — Phase A (read-only Team view) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A reporting manager switches the Planner to **My team** and sees:
- each direct report's day as columns;
- the week as a load heatmap;
- one person's week, read-only.

The view drills down the reporting line, and a "Team plan today" widget on the My Team dashboard opens it.

**Architecture:**
- **BE:** a new `TeamPlannerService` builds per-person days from the existing planner, meeting and leave reads, applying the existing view rule (self or reporting-line ancestor). It serves three read endpoints under `api/v1/planner`, plus a `CanViewTeamPlan` capability that gates the widget.
- **FE:** a page-scoped `TeamPlannerStore` feeds a `TeamPlannerComponent`, which the calendar page shows when its scope is `team`. One `PlanColumnsGrid` draws both the team day (people as columns) and the person week (days as columns).

**Tech Stack:** ASP.NET Core .NET 10, EF Core + PostgreSQL, xUnit/Moq/Testcontainers · Angular 21 standalone + signals, Vitest (`npx ng test --watch=false --include …`).

**Spec:** `docs/superpowers/specs/2026-11-03-team-planner-design.md` (branch `docs/team-planner`). The approved mockups are the "Main" (heatmap) and "TeamDay" artboards of https://claude.ai/artifact/2uTFdmZ2HxKWYfZHzPcG8K.

**Branches (stacked):**
- BE: worktree `C:\Users\User\onexso-worktrees\be-team-planner-a`, branch `feature/team-planner-a-backend` from `origin/feature/planner-create-popover-backend`.
- FE: worktree `C:\Users\User\onexso-worktrees\fe-team-planner-a`, branch `feature/team-planner-a` from `origin/feature/planner-create-popover`.

## Global Constraints

- Read-only. Phase A writes nothing to anyone's plan. No drag-to-request: that is Phase B.
- **View rule:** the employee themself, or anyone in their reporting-line ancestor chain (`IEmployeeRepository.ListAncestorChainEmployeeIdsAsync`). Anyone else gets **404** (`Planner day not found.`), never 403.
- **Team** = the direct reports of the chosen manager (`ListDirectReportEmployeeIdsAsync`). Drill-down passes `managerId`, and the caller must be that manager or one of their ancestors.
- A private meeting viewed by someone else carries the title `Private meeting` (the existing `PlannerService` constant). The FE draws it as **Busy**, with no title.
- Leave shows status only (`approved` | `pending`), never the leave type or reason.
- **Load:**
  - `load = BlockMinutes + MeetingMinutes`, counting meetings that are not cancelled and not declined.
  - `capacity = WorkingMinutes − approved-leave minutes inside the work window` (floor 0). Pending leave does not reduce capacity.
  - `OnLeave` = a working day whose capacity is 0 because of approved leave.
- **Load bands** (FE), with cell colours from the approved heatmap:

  | Band | Condition | Background / text |
  |---|---|---|
  | `leave` | OnLeave | `#fde68a` / `#78350f` |
  | `none` | load 0 | `#f4f4f5` / `#3f3f46` |
  | `under` | < 50 % | `#dbeafe` / `#1e3a8a` |
  | `mid` | < 90 % | `#93c5fd` / `#1e3a8a` |
  | `full` | ≤ 100 % | `#1d4ed8` / `#ffffff` |
  | `over` | > 100 %, or load > 0 with capacity 0 | `#c2410c` / `#ffffff` |

- **Hours label:** whole or half hours as `"6h"` / `"7.5h"`, anything else as `"6h 40m"`. Minutes 0 shows `"—"`.
- **Week:** request Monday–Sunday. Show only the days on which any member has `WorkingMinutes > 0` or load > 0.
- Planner disabled → every endpoint returns 503 `planner_disabled` (existing helper), and the FE hides the switch and the widget.
- Range at most `PlannerService.MaxRangeDays` (42) days. Otherwise 400 `PlannerErrorCodes.InvalidRange`.
- Do NOT change the create popover form (Event / Meeting / Work item / Time off). Do NOT build the extras from the reference images (Team workload panel, members picker, Realtime panel).
- The Inbox stays the existing Inbox menu. Nothing in Phase A touches it.
- Copy: "Me" · "My team" · "Day" · "Week" · "Today" · "Search people…" · "No direct reports" · "Team view is available to managers with direct reports." · "No one has planned this day" · "Unable to load team plan" · "Retry" · "Team plan today" · "Open team planner" · "On leave" · "Not planned" · "Busy" · "‹ Back to team" · "{Name}'s plan".

## Review Focus

1. **A member in another time zone.** The team day for date D returns each member's D in *their* zone, and the grid draws items by instant in the *viewer's* zone, so a Dubai member's 09:00 sits at 10:30 for a Colombo viewer. Tests: Task 3 (`Team_MemberInAnotherZone_UsesTheirDay`), Task 7 (`positions an item by instant in the viewer zone`).
2. **Someone outside the chain asks for a manager's team or a person's range.** Expected: 404, with no names and no data. Tests: Task 2 (`PersonRange_NotInChain_IsNotFound`), Task 3 (`Team_ManagerNotInCallerChain_IsNotFound`).
3. **A private meeting.** Its title never reaches a viewer who is not the owner. Tests: Task 2 (`PersonRange_PrivateMeeting_IsMaskedForAManager`), Task 7 (`draws a private meeting as Busy`).
4. **Full-day, half-day and pending leave.** No divide by zero. Full-day approved → `leave` band. Half-day approved → capacity halved. Pending → capacity unchanged. Tests: Task 2 (`PersonRange_LeaveRules`), Task 5 (`loadBand` table).
5. **Planner switched off.** Endpoints 503, and the switch and widget are hidden. Tests: Task 3 (`Team_PlannerDisabled_Is503`), Task 10 (`hides My team when the planner is off`).

---

### Task 1: Pending leave read

**Files:**
- Modify: `src/ONEVO.Application/Features/Leave/Request/RepositoryInterfaces/ILeaveRequestReadRepository.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/Leave/Request/EfLeaveRequestReadRepository.cs`
- Test: `tests/ONEVO.Tests.Integration/Leave/LeaveRequestReadRepositoryPendingTests.cs` (new; follow the Testcontainers fixture the other Leave integration tests use)

**Interfaces:**
- Produces: `Task<IReadOnlyList<LeaveRequest>> ListPendingCoveringAsync(Guid tenantId, IReadOnlyCollection<Guid> employeeIds, DateOnly from, DateOnly to, CancellationToken ct = default)`. It uses the same overlap rule as `ListApprovedCoveringAsync`, with `Status == LeaveRequestStatuses.Pending`.

- [ ] **Step 1: Write the failing test**
  - `ListPendingCovering_ReturnsOnlyPendingThatOverlapTheRange`. Seed one employee with:
    - pending 5–6 Nov (in);
    - approved 5 Nov (out);
    - pending 10 Nov (out of range 2–8 Nov);
    - another employee's pending (out).
  - Assert exactly the first is returned.
- [ ] **Step 2: Run, expect a compile failure (method missing).** `dotnet test tests/ONEVO.Tests.Integration --filter "FullyQualifiedName~LeaveRequestReadRepositoryPendingTests"`
- [ ] **Step 3: Implement.** Generalise the private `ListCoveringAsync` to take the status predicate, rather than copying the query.
- [ ] **Step 4: Run the test → PASS.** Then run the existing Leave read tests → PASS.
- [ ] **Step 5: Commit** `feat(leave): list pending leave covering a range`.

### Task 2: Person range (one employee's days, read-only)

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/Planner/Contracts/PlannerContracts.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Planner/Services/PlannerZones.cs`. Move `PlannerService`'s private `Zone` record and `ZoneOfAsync` here as `internal static PlannerZones.ResolveAsync(ICalendarTimezoneResolver, Guid tenantId, Guid employeeId, CancellationToken)`. `PlannerService` calls it; no behaviour change.
- Create: `src/ONEVO.Application/Features/WorkManagement/Planner/Services/TeamPlannerService.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Planner/Commands/PlannerCommands.cs`: add the query and its handler, through the existing `AsCaller`.
- Modify: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/PlannerController.cs`: add `[HttpGet("employees/{employeeId:guid}/range")]`.
- Modify: `src/ONEVO.Infrastructure/DependencyInjection.cs`: `AddScoped<TeamPlannerService>()`.
- Modify: `docs/api/planner-v1.md`: document the endpoint.
- Test: `tests/ONEVO.Tests.Integration/WorkManagement/Planner/TeamPlannerServiceIntegrationTests.cs` (new; same fixture as `PlannerServiceIntegrationTests`).

**Interfaces:**
- Produces (contracts):
  ```csharp
  public sealed record PlannerLeaveView(DateTimeOffset StartAt, DateTimeOffset EndAt, string Status); // "approved" | "pending"
  public sealed record PlannerPersonDay(DateOnly Date, PlannerWorkWindow? WorkWindow, DateTimeOffset? WorkStartAt,
      DateTimeOffset? WorkEndAt, IReadOnlyList<PlannerRangeBlockView> Blocks, IReadOnlyList<PlannerMeetingView> Meetings,
      IReadOnlyList<PlannerLeaveView> Leaves, int BlockMinutes, int MeetingMinutes, int WorkingMinutes, int CapacityMinutes,
      bool OnLeave);
  public sealed record PlannerPersonRangeResponse(Guid EmployeeId, string Name, string Timezone, DateOnly Today,
      IReadOnlyList<PlannerPersonDay> Days);
  public sealed record GetEmployeePlannerRangeQuery(Guid EmployeeId, DateOnly From, DateOnly To)
      : IRequest<Result<PlannerPersonRangeResponse>>;
  ```
- Produces (service):
  - `TeamPlannerService.GetPersonRangeAsync(Guid tenantId, Guid callerEmployeeId, Guid employeeId, DateOnly from, DateOnly to, CancellationToken ct) : Task<Result<PlannerPersonRangeResponse>>`.
  - `internal Task<IReadOnlyList<PlannerPersonDay>> BuildDaysAsync(Guid tenantId, Guid employeeId, PlannerZones.Zone zone, DateOnly from, DateOnly to, bool detail, bool maskPrivate, CancellationToken ct)`. Task 3 reuses it. `detail=false` returns empty `Blocks`, `Meetings` and `Leaves` but keeps every minute field and `OnLeave`.
- `TeamPlannerService` dependencies:
  - `IPlannerRepository`, `IMeetingRepository`, `ICalendarTimezoneResolver`, `IEmployeeRepository`, `ILeaveRequestReadRepository`, `IDateTimeProvider`, `IOptions<PlannerOptions>`;
  - blocks, tasks and work windows exactly as `PlannerService.GetRangeAsync` builds them;
  - meetings via `IMeetingRepository.ListEmployeeDayMeetingsAsync` over the range bounds, masked as `ComposeAsync` does when `maskPrivate`;
  - `WorkStartAt` / `WorkEndAt` = the window's times on that date in the employee's zone (null when not a working day).

- [ ] **Step 1: Write the failing tests** (integration, seeded through the existing planner fixture helpers):
  - `PersonRange_Self_ReturnsBlocksMeetingsLeaveAndMinutes`: assert `BlockMinutes`, `MeetingMinutes` (a declined meeting is not counted) and `WorkingMinutes == 420` on a weekday.
  - `PersonRange_ReportingManager_CanRead`.
  - `PersonRange_NotInChain_IsNotFound`: assert `StatusCode == 404`.
  - `PersonRange_PrivateMeeting_IsMaskedForAManager`: assert `Title == "Private meeting"` for the manager, and the real title for the employee.
  - `PersonRange_LeaveRules`, three days:
    - approved full day → `OnLeave == true`, `CapacityMinutes == 0`;
    - approved 4h inside the window → `CapacityMinutes == WorkingMinutes − 240`;
    - pending full day → `CapacityMinutes == WorkingMinutes`, with `Leaves[0].Status == "pending"`.
  - `PersonRange_TooLong_IsInvalidRange`: 43 days → 400 with `code == PlannerErrorCodes.InvalidRange`.
- [ ] **Step 2: Run → FAIL to compile.** `dotnet test tests/ONEVO.Tests.Integration --filter "FullyQualifiedName~TeamPlannerServiceIntegrationTests"`
- [ ] **Step 3: Implement** the `PlannerZones` move, the contracts, `TeamPlannerService.GetPersonRangeAsync` / `BuildDaysAsync`, the query and handler, the controller action, DI and the API doc. Use the existing `Disabled<T>()` / `NotFound<T>()` result shapes; make them `internal static` on `PlannerService` if needed.
- [ ] **Step 4: Run** the new tests → PASS. Then run `dotnet test tests/ONEVO.Tests.Unit` and `dotnet test tests/ONEVO.Tests.Architecture` → all PASS.
- [ ] **Step 5: Commit** `feat(planner): read one employee's days for the reporting line`.

### Task 3: Team day and team range

**Files:**
- Modify: `PlannerContracts.cs`, `TeamPlannerService.cs`, `PlannerCommands.cs`, `PlannerController.cs`, `docs/api/planner-v1.md`
- Test: `TeamPlannerServiceIntegrationTests.cs` (add)

**Interfaces:**
- Produces:
  ```csharp
  public sealed record TeamPlannerMember(Guid EmployeeId, string Name, int DirectReportCount, string Timezone,
      IReadOnlyList<PlannerPersonDay> Days);
  public sealed record TeamPlannerResponse(Guid ManagerEmployeeId, string ManagerName, DateOnly From, DateOnly To,
      IReadOnlyList<TeamPlannerMember> Members);
  public sealed record GetTeamPlannerDayQuery(DateOnly? Date, Guid? ManagerId) : IRequest<Result<TeamPlannerResponse>>;
  public sealed record GetTeamPlannerRangeQuery(DateOnly From, DateOnly To, Guid? ManagerId) : IRequest<Result<TeamPlannerResponse>>;
  ```
- `TeamPlannerService.GetTeamAsync(Guid tenantId, Guid callerEmployeeId, Guid? managerId, DateOnly from, DateOnly to, bool detail, CancellationToken ct)`:
  - `manager = managerId ?? caller`. The caller must be the manager or in `ListAncestorChainEmployeeIdsAsync(manager)`; otherwise 404.
  - Members = distinct `ListDirectReportEmployeeIdsAsync(tenant, [manager])`, loaded with `ListByIdsAsync`, skipping ids it does not return. Order by `FirstName`, then `LastName`.
  - `Name = $"{FirstName} {LastName}"`.
  - `DirectReportCount` = per member, `ListDirectReportEmployeeIdsAsync(tenant, [member]).Distinct().Count()`.
  - Each member's days come from `BuildDaysAsync(detail, maskPrivate: true)` in **their** zone.
- Endpoints:
  - `GET planner/team?date=&managerId=`: detail, `from = to = date`, where the default date is today in the caller's zone.
  - `GET planner/team/range?from=&to=&managerId=`: summary (`detail: false`).

- [ ] **Step 1: Write the failing tests:**
  - `Team_ReturnsDirectReportsWithTheirDays`: 2 reports + 1 grand-report. Assert 2 members, name order, and that the lead's `DirectReportCount == 1`.
  - `Team_DrillDown_ToAReportsTeam` (caller = grand-manager).
  - `Team_ManagerNotInCallerChain_IsNotFound`.
  - `Team_MemberInAnotherZone_UsesTheirDay`: a member in `Asia/Dubai` and a viewer in `Asia/Colombo`. Assert the member's `Days[0].WorkStartAt` is 09:00 +04:00.
  - `TeamRange_IsSummary`: `Blocks`, `Meetings` and `Leaves` are empty, but `BlockMinutes > 0`.
  - `Team_PlannerDisabled_Is503`: `code == "planner_disabled"`.
  - `Team_NoReports_ReturnsEmptyMembers`.
- [ ] **Step 2: Run → FAIL to compile.**
- [ ] **Step 3: Implement** the contracts, `GetTeamAsync`, the two queries and handlers, the two controller actions and the API doc.
- [ ] **Step 4: Run** the new tests → PASS. Then run the unit and architecture suites → PASS.
- [ ] **Step 5: Commit** `feat(planner): team day and team range reads`.

### Task 4: Capability and dashboard widget slot

**Files:**
- Modify: `src/ONEVO.Application/Features/Dashboard/Team/DTOs/MyTeamCapabilitiesResponse.cs`: add `bool CanViewTeamPlan = false` as the last parameter.
- Modify: `src/ONEVO.Application/Features/Dashboard/Team/Queries/GetMyTeamCapabilitiesQueryHandler.cs`:
  - `CanViewTeamPlan = planner enabled (IOptions<PlannerOptions>) && actor != null && ListDirectReportEmployeeIdsAsync(tenant, [actor.Id]).Count > 0`;
  - `isAvailable` also ORs it.
- Modify: `src/ONEVO.Application/Features/Dashboard/Layout/Access/DashboardWidgetAccess.cs`: `public const string TeamPlanToday = "team-plan-today";`, added for `MyTeam` when `caps is { IsAvailable: true, CanViewTeamPlan: true }`.
- Modify: `src/ONEVO.Application/Features/Dashboard/Layout/Registry/DashboardWidgetRegistry.cs`:
  - definition `new("team-plan-today", DashboardKeys.MyTeam, "Team plan today", "Today's planned load for each direct report.", 2, 2, 12, 8, true)`;
  - in each MyTeam default layout variant, place it below the existing widgets: x 0, width 8, height 6, at the first free row.
- Test: the existing tests for `GetMyTeamCapabilitiesQueryHandler`, `DashboardWidgetAccess` and `DashboardWidgetRegistry` (add cases; find them with `Grep "DashboardWidgetAccess" tests`).

- [ ] **Step 1: Write the failing tests:**
  - `Capabilities_ManagerWithReports_CanViewTeamPlan`;
  - `Capabilities_NoReports_CannotViewTeamPlan`;
  - `Capabilities_PlannerDisabled_CannotViewTeamPlan`;
  - `WidgetAccess_MyTeam_AddsTeamPlanToday_WhenCanViewTeamPlan`;
  - a registry test that each MyTeam default layout containing `team-plan-today` has no overlapping rects.
- [ ] **Step 2: Run → FAIL.** `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~Dashboard"`
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** the unit suite and the architecture suite → PASS.
- [ ] **Step 5: Commit** `feat(dashboard): team plan capability and widget slot`. Push the BE branch.

### Task 5: FE models, API calls and load rules

**Files:**
- Create: `src/app/modules/work/planner/models/team-planner.models.ts`, mirroring Tasks 2–3 in camelCase:
  - `PlannerLeaveView` (`status: 'approved' | 'pending'`)
  - `PlannerPersonDay`
  - `PlannerPersonRangeResponse`
  - `TeamPlannerMember`
  - `TeamPlannerResponse`
- Modify: `src/app/modules/work/planner/data-access/planner-api.service.ts`, keeping the `silent` context:
  - `getTeamDay(date: string | null, managerId: string | null): Observable<TeamPlannerResponse>`
  - `getTeamRange(from: string, to: string, managerId: string | null): Observable<TeamPlannerResponse>`
  - `getEmployeeRange(employeeId: string, from: string, to: string): Observable<PlannerPersonRangeResponse>`
- Modify: `src/app/modules/dashboard/models/my-team-capabilities.model.ts`: `canViewTeamPlan: boolean`.
- Create: `src/app/modules/calendar/utils/team-load.ts` + `team-load.spec.ts`

**Interfaces:**
- Produces:
  - `type LoadBand = 'leave' | 'none' | 'under' | 'mid' | 'full' | 'over'`
  - `loadBand(loadMinutes: number, capacityMinutes: number, onLeave: boolean): LoadBand`
  - `dayLoad(day: PlannerPersonDay): number` (block + meeting minutes)
  - `formatHours(minutes: number): string`
  - `LOAD_BAND_COLORS: Record<LoadBand, { bg: string; fg: string }>` (Global Constraints values)

- [ ] **Step 1: Write the failing spec** `team-load.spec.ts`:
  ```ts
  it.each([
    [0, 480, true, 'leave'], [0, 480, false, 'none'], [180, 480, false, 'under'], [360, 480, false, 'mid'],
    [480, 480, false, 'full'], [510, 480, false, 'over'], [60, 0, false, 'over'],
  ])('loadBand(%i, %i, %s) = %s', (load, cap, leave, band) => expect(loadBand(load, cap, leave)).toBe(band));
  it.each([[0, '—'], [360, '6h'], [450, '7.5h'], [400, '6h 40m']])('formatHours(%i) = %s', (m, s) => expect(formatHours(m)).toBe(s));
  ```
  Also an api spec for each method's URL and params, `managerId` omitted when null (follow `planner-api.service.spec.ts` if it exists, else `HttpTestingController`).
- [ ] **Step 2: Run → FAIL.** `npx ng test --watch=false --include src/app/modules/calendar/utils/team-load.spec.ts --include src/app/modules/work/planner/data-access/planner-api.service.spec.ts`
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run → PASS.**
- [ ] **Step 5: Commit** `feat(planner): team planner models, reads and load rules`.

### Task 6: `TeamPlannerStore`

**Files:**
- Create: `src/app/modules/calendar/state/team-planner.store.ts` + spec. It is page-scoped (`@Injectable()`, provided by the calendar page like `PlannerLayerStore`).

**Interfaces:**
- Consumes: the Task 5 API, `formatHours`, `loadBand` and `dayLoad`.
- Produces:
  - **State:**
    - `mode: Signal<'day' | 'week'>`
    - `date: Signal<string>` (`'YYYY-MM-DD'`, the viewer's date)
    - `trail: Signal<readonly { managerId: string | null; name: string }[]>` (starts `[{ managerId: null, name: 'My team' }]`)
    - `response: Signal<TeamPlannerResponse | null>`
    - `loading`, `error: Signal<string | null>`
  - **Search, sort and filter:**
    - `search: WritableSignal<string>`
    - `sort: WritableSignal<'name' | 'loadDesc' | 'loadAsc'>`
    - `filters: WritableSignal<ReadonlySet<'over' | 'under' | 'leave' | 'notPlanned'>>`
  - **Derived:**
    - `visibleMembers: Signal<readonly TeamPlannerMember[]>`
    - `counts: Signal<{ over: number; under: number; leave: number; notPlanned: number }>`
    - `weekDays: Signal<readonly string[]>`
  - **Person view:**
    - `person: Signal<PlannerPersonRangeResponse | null>`
    - `personError: Signal<string | null>`
  - **Methods:**
    - `load(): Promise<void>`
    - `setMode(m)`, `setDate(d)`, `step(dir: 1 | -1)` (±1 day or ±7 days), `today(zone: string)`
    - `drillDown(managerId: string, name: string)`, `backTo(index: number)`
    - `openPerson(employeeId: string)` (loads that person's Mon–Sun week around `date`), `closePerson()`
- **Rules:**
  - Day mode calls `getTeamDay`. Week mode calls `getTeamRange` Monday–Sunday of `date`.
  - A newer `load` wins: ignore stale responses (request counter).
  - **Member counts, Day mode** (that day): `over` = band `over`, `under` = band `under`, `leave` = `onLeave`, `notPlanned` = band `none` on a working day.
  - **Member counts, Week mode** (summed over the shown days): `over` = any shown day `over`, `under` = week load < 50 % of week capacity, `leave` = any day `onLeave`, `notPlanned` = week load 0.
  - Filters are OR-ed.
  - Search matches the name, case-insensitive.
  - The sort's load ratio = load / capacity, with capacity 0 sorting last.

- [ ] **Step 1: Write the failing spec** (fake `PlannerApiService`):
  - `loads the day for date and null manager`;
  - `week mode asks Monday–Sunday`;
  - `drillDown pushes the trail and reloads with managerId`;
  - `backTo(0) returns to my team`;
  - `ignores a stale response`;
  - `counts and filters` (fixture of 4 members: over / under / leave / not planned);
  - `sort loadDesc puts the fullest first`;
  - `search is case-insensitive`;
  - `openPerson loads that employee's week`;
  - `error sets "Unable to load team plan"`.
- [ ] **Step 2: Run → FAIL.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run → PASS.**
- [ ] **Step 5: Commit** `feat(planner): team planner store`.

### Task 7: `PlanColumnsGrid` (team day + person week)

**Files:**
- Create: `src/app/modules/calendar/ui/plan-columns-grid/plan-columns-grid.component.ts` + spec

**Interfaces:**
- Produces:
  ```ts
  export interface PlanItem { key: string; kind: 'block' | 'meeting' | 'busy' | 'leave' | 'leavePending'; label: string; startIso: string; endIso: string; }
  export interface PlanColumn { key: string; title: string; subtitle: string | null; loadLabel: string | null; band: LoadBand | null;
    loadPct: number; items: readonly PlanItem[]; offHours: readonly { startIso: string; endIso: string }[]; }
  // inputs
  columns = input.required<readonly PlanColumn[]>(); date = input.required<string>(); timezone = input.required<string>();
  fromHour = input(7); toHour = input(20);
  // outputs
  columnOpen = output<string>(); // a header click
  ```
  Plus pure helpers in the same folder (`plan-columns.ts`) that map a member's day, or a person's day, to a `PlanColumn`:
  - meetings → `meeting`, or `busy` when `isPrivate && title === 'Private meeting'`;
  - blocks → `block`, labelled with `taskTitle` (the range view carries no short ID);
  - leaves → `leave` / `leavePending`;
  - `offHours` = the visible day outside `[workStartAt, workEndAt]`, the whole day when there is no window.
- **Layout** (approved "TeamDay" artboard):
  - sticky time column 64px, hour rows 72px;
  - column header with avatar initials, name, subtitle and a load bar coloured by `LOAD_BAND_COLORS`;
  - items absolutely positioned by instant converted to the viewer `timezone` (the existing calendar zone utils);
  - off-hours hatched;
  - more than 7 columns → horizontal scroll with the time column fixed;
  - under 768px → a card list per column (name, load, items with times) instead of the grid.

- [ ] **Step 1: Write the failing spec:**
  - `renders one column per PlanColumn with name and load label`;
  - `positions an item by instant in the viewer zone`: an item 09:00 +04:00 with viewer `Asia/Colombo` → top = (10.5 − fromHour) × 72;
  - `draws a private meeting as Busy`: a mapper test from a `PlannerMeetingView` with `isPrivate: true, title: 'Private meeting'` → kind `busy`, label `Busy`;
  - `draws approved and pending leave`;
  - `shades off-hours`;
  - `header click emits columnOpen(key)`;
  - `narrow screen shows cards` (stub `matchMedia`).
- [ ] **Step 2: Run → FAIL.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run → PASS.**
- [ ] **Step 5: Commit** `feat(planner): plan columns grid for team and person views`.

### Task 8: `TeamWeekHeatmap`

**Files:**
- Create: `src/app/modules/calendar/ui/team-week-heatmap/team-week-heatmap.component.ts` + spec

**Interfaces:**
- Inputs: `members = input.required<readonly TeamPlannerMember[]>()`, `days = input.required<readonly string[]>()`.
- Outputs:
  - `cellOpen = output<{ employeeId: string; date: string }>()`
  - `personOpen = output<string>()`
  - `drillDown = output<{ employeeId: string; name: string }>()` (the "View team ›" link, shown when `directReportCount > 0`)
- **Layout** (approved "Main" artboard):
  - one row per member: avatar, name, "Lead · N reports" subtitle when `directReportCount > 0`;
  - one cell per day with `formatHours(load)`, the % (or "not planned", or "Leave"), and the band colours;
  - week total `"{load} / {capacity}"`, orange when over;
  - legend underneath;
  - under 768px each row becomes a name plus one small coloured chip per day.

- [ ] **Step 1: Write the failing spec:**
  - `one cell per member per day with hours and percent` (6h of 8h → `6h`, `75%`);
  - `leave cell says Leave`;
  - `zero shows — and not planned`;
  - `week total is load over capacity`;
  - `cell click emits cellOpen`;
  - `name click emits personOpen`;
  - `View team emits drillDown only for leads`.
- [ ] **Step 2: Run → FAIL.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run → PASS.**
- [ ] **Step 5: Commit** `feat(planner): team week heatmap`.

### Task 9: `TeamPlannerComponent` (shell and states)

**Files:**
- Create: `src/app/modules/calendar/feature/team-planner/team-planner.component.ts` (+ html if the repo pattern prefers it) + spec

**Interfaces:**
- Consumes: `TeamPlannerStore` (injected from the page), `PlanColumnsGrid`, `TeamWeekHeatmap`, and the Task 7 mappers.
- Input: `timezone = input.required<string>()` (the viewer zone).
- **Toolbar:** Day | Week · ‹ date › · Today · Search people… · Sort ▾ (Name / Load high → low / Load low → high) · Filter chips with counts (Over capacity N · Under-planned N · On leave N · Not planned N, toggling `filters`).
- **Breadcrumb** from `trail` when its length > 1: "My team › Dinesh's team". Clicking a crumb calls `backTo`.
- **Body:**
  - **Day:** `PlanColumnsGrid` of `visibleMembers`.
  - **Week:** `TeamWeekHeatmap`. `cellOpen` → setMode day + setDate + (person stays closed). `personOpen` → `openPerson`.
  - **Person:** a header "‹ Back to team · {Name}'s plan", then a `PlanColumnsGrid` with one column per shown day of the person's week.
- **States:**
  - `response.members.length === 0` → "No direct reports" / "Team view is available to managers with direct reports.";
  - all members' load 0 for the day or week → "No one has planned this day" above the grid;
  - `loading` → 3 skeleton rows;
  - `error` → "Unable to load team plan" + **Retry** (`load()`).

- [ ] **Step 1: Write the failing spec** (real store with a fake API):
  - `day mode renders the columns grid`;
  - `week mode renders the heatmap`;
  - `cell click opens that day`;
  - `name click opens the person view and Back returns`;
  - `breadcrumb drill-down and back`;
  - `filter chip toggles and shows counts`;
  - `no reports state`;
  - `error state retries`;
  - `loading skeleton`.
- [ ] **Step 2: Run → FAIL.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run → PASS.**
- [ ] **Step 5: Commit** `feat(planner): team planner view with drill-down and states`.

### Task 10: Calendar page: Me / My team

**Files:**
- Modify: `src/app/modules/calendar/feature/calendar-page/calendar-page.component.ts` / `.html`
- Test: `src/app/modules/calendar/feature/calendar-page/calendar-page.planner.spec.ts`

**Interfaces:**
- Page:
  - `readonly scope = signal<'me' | 'team'>('me')`;
  - `readonly canViewTeam = computed(() => planner.active() && myTeamCapabilities.capabilities()?.canViewTeamPlan === true)`, loading capabilities once via `MyTeamCapabilitiesStore.loadCapabilities()` if not loaded;
  - provides `TeamPlannerStore`.
- The **Me | My team** segmented switch sits at the start of the page toolbar, only when `canViewTeam()`. In `team` scope:
  - the page renders `<app-team-planner [timezone]="plannerZone()" />` instead of the calendar grid, focus panel and create popover;
  - `closeCreate()` runs.
- **Query params** (read on init, written on change with `replaceUrl`): `scope=team`, `teamView=day|week`, `date=YYYY-MM-DD`, `person=<employeeId>`. The widget deep-links with these.

- [ ] **Step 1: Write the failing tests** (in the planner spec; fake `MyTeamCapabilitiesStore` signal):
  - `shows My team only when the planner is on and canViewTeamPlan`;
  - `hides My team when the planner is off`;
  - `switching to My team renders the team planner and closes the create popover`;
  - `?scope=team&teamView=week&date=2026-11-05 opens the team week`;
  - `?person=e1 opens that person's plan`.
- [ ] **Step 2: Run → FAIL.**
- [ ] **Step 3: Implement.** Keep the user's existing page code untouched apart from the switch and the scope branch.
- [ ] **Step 4: Run** the calendar specs → PASS.
- [ ] **Step 5: Commit** `feat(planner): Me / My team switch on the planner`.

### Task 11: "Team plan today" dashboard widget

**Files:**
- Create: `src/app/modules/dashboard/feature/team-plan-today-card/team-plan-today-card.component.ts` + spec
- Modify: `src/app/modules/dashboard/widgets/widget-registry.ts`: `'team-plan-today': { component: TeamPlanTodayCardComponent }`
- Modify: `src/app/modules/dashboard/widgets/widget-registry.spec.ts` if it lists keys.

**Interfaces:**
- Consumes: `PlannerApiService.getTeamDay(null, null)`, `loadBand`, `dayLoad`, `formatHours`, `LOAD_BAND_COLORS`.
- **Layout:**
  - title "Team plan today" and link "Open team planner ›" → `/planner?scope=team&teamView=day`;
  - one row per member: avatar, name, a load bar (band colour, width `min(100, pct)`), and "6h / 8h";
  - chips "On leave" (`onLeave`) or "Not planned" (load 0 on a working day);
  - a row click → `/planner?scope=team&teamView=day&person=<id>`.
  - Compact size (2×2) shows the first 4 rows plus "+N more". Larger sizes show all rows and scroll inside the card.
- **States:** loading skeleton · error "Unable to load team plan" + Retry · `planner_disabled` (503) → hide the card body with "Planner is off".

- [ ] **Step 1: Write the failing spec:**
  - `lists members with today's load`;
  - `on leave and not planned chips`;
  - `open team planner link`;
  - `row navigates to the person`;
  - `compact shows 4 and +N more`;
  - `error retries`.
- [ ] **Step 2: Run → FAIL.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** the dashboard specs, then the full suite `npx ng test --watch=false` → PASS. Report any unrelated flake by name.
- [ ] **Step 5: Commit** `feat(dashboard): team plan today widget`. Push the FE branch.

---

## Rulings already made for this plan (tell the reviewer)

- **No job title under names.** No cheap per-employee title read exists, so the subtitle is "Lead · N reports" for leads and empty otherwise. *Cost if wrong:* a later read and one line in each component.
- **No virtual scrolling** for more than 25 rows in Phase A. Direct-report lists are normally small, and the grid scrolls sideways past 7 people. *Cost if wrong:* add CDK virtual scroll to the heatmap rows.
- **Team reads load each member separately** (blocks, meetings and leave per member), so the read count grows with team size. *Cost if wrong:* a batched repository read later. Measure on a 25-person team in review.
