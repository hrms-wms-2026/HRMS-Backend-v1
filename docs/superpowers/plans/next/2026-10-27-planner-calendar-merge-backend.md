# Planner = Calendar: Backend Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let the planner plan today and future days, and serve the calendar-merged Planner with a range read and a Focus list.

**Architecture:** The work extends `PlannerService` and `IPlannerRepository` in `ONEVO.Application/Features/WorkManagement/Planner`.
- Two pure helpers hold the day/week maths and the Focus grouping, so they can be unit-tested: `PlannerWorkWindows` and `PlannerFocus`.
- Two new GET endpoints are added to `PlannerController` through the existing `PlannerRequestHandler`.
- All of it stays behind `Planner:Enabled`.

**Tech Stack:** .NET 10, EF Core (PostgreSQL), MediatR, xUnit; integration tests on `MyTeamDb` (Testcontainers).

**Spec:** `docs/superpowers/specs/next/2026-10-27-planner-calendar-merge-design.md` (§5, §4.2, §4.5, and calls C1, C4–C7).

## Global Constraints

- Every endpoint answers **503 `planner_disabled`** while `Planner:Enabled` is false.
- `PlannerService.MinimumBlockMinutes = 15`. Blocks stay inside one local day and never overlap another of the employee's blocks (**409 `planner_block_overlap`**).
- `PlannerService.PlanningHorizonDays = 28`.
  - Editable = own plan, and today ≤ day ≤ today + 28.
  - Past days are read-only: **400 `planner_day_read_only`**.
  - Days after the horizon answer **400 `planner_beyond_horizon`**.
- `PlannerService.MaxRangeDays = 42`. A longer range, or `to < from`, answers **400 `planner_invalid_range`**.
- The Focus list is capped at **50 items per group**. Counts are uncapped.
- Days, weeks and the work window use the **employee's own zone** (`ICalendarTimezoneResolver`, as now) (C1). Weeks start on **Monday**.
- The legal entity's `WorkStartTime`–`WorkEndTime` is read as wall-clock in the employee's zone.
  - It counts only when `AttendanceScheduleResolver.IsScheduleConfigured` is true. Otherwise the window is null (C5).
  - The break is centred in the window (C7).
- Logged minutes = **all** task clocking sessions on the task, by anyone. An open session counts up to now (C4).
- A write never changes the task's due date, estimate or status.
- Run tests with `--artifacts-path artifacts/claude-test` (the API may be running from another worktree). Run integration suites one at a time.

## Review Focus

1. **A week boundary in the employee's zone.** At Sunday 23:30 Colombo (Sunday 18:00 UTC), a task due Monday is "due this week" only on Monday; on Sunday it is backlog. Covered by a test in Task 4.
2. **A cross-day move into a full slot.** Moving a block from today to tomorrow onto another block is 409. The source block is unchanged. Covered by a test in Task 1.
3. **A task unassigned after planning.** Its future block still reads back (`stillAssigned: false`), and the task leaves the Focus list. Covered by a test in Task 4.
4. **An employee with no legal entity or no schedule.** The range answers 200 with `workWindow: null` and `workingMinutes: 0`; nothing throws. Covered by a test in Task 3.
5. **An open clocking session on a focus task.** It counts up to "now" in `loggedMinutes`, so `remainingMinutes` falls during the day. Covered by a test in Task 4.

---

### Task 1: Plan today and future days

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/Planner/Services/PlannerService.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Planner/Contracts/PlannerContracts.cs`
- Modify: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/PlannerController.cs` (summary comment only)
- Test: `tests/ONEVO.Tests.Integration/WorkManagement/Planner/PlannerServiceIntegrationTests.cs`

**Interfaces:**
- Produces:
  - `PlannerService.PlanningHorizonDays` (int, 28).
  - `PlannerErrorCodes.DayReadOnly = "planner_day_read_only"`.
  - `PlannerErrorCodes.BeyondHorizon = "planner_beyond_horizon"`.
  - `PlannerErrorCodes.NotToday` is kept, but no code path returns it any more.
  - Create, move and delete now return the **day of the written block** (`PlannerDayResponse`); a cross-day move returns the target day.
  - `GetDayAsync` accepts any day ≤ today + 28. `Editable` = own && today ≤ day ≤ today + 28.

- [ ] **Step 1: Replace the two "today only" tests with the new rules (failing first)**

The existing tests to replace are `Yesterday_or_tomorrow_is_refused` and `Past_day_is_read_only_and_future_day_is_refused`.

```csharp
[Fact]
public async Task Yesterday_is_read_only_tomorrow_can_be_planned_and_beyond_28_days_is_refused()
{
    var w = await SeedAsync();
    var past = await PlanAsync(w, w.Task, Local(9, dayOffset: -1), Local(10, dayOffset: -1));
    var tomorrow = await PlanAsync(w, w.Task, Local(9, dayOffset: 1), Local(10, dayOffset: 1));
    var last = await PlanAsync(w, w.Task, Local(9, dayOffset: 28), Local(10, dayOffset: 28));
    var beyond = await PlanAsync(w, w.Task, Local(9, dayOffset: 29), Local(10, dayOffset: 29));

    Assert.Equal((400, PlannerErrorCodes.DayReadOnly), (past.StatusCode, past.ErrorCode));
    Assert.True(tomorrow.IsSuccess, tomorrow.Error);
    Assert.Equal(Today.AddDays(1), tomorrow.Value!.Date);              // the written day comes back
    Assert.True(last.IsSuccess, last.Error);
    Assert.Equal((400, PlannerErrorCodes.BeyondHorizon), (beyond.StatusCode, beyond.ErrorCode));
}

[Fact]
public async Task Past_day_reads_read_only_and_a_future_day_reads_editable()
{
    var w = await SeedAsync();
    Assert.False((await DayAsync(w, w.Employee, w.Employee, Today.AddDays(-1))).Value!.Editable);
    Assert.True((await DayAsync(w, w.Employee, w.Employee, Today.AddDays(3))).Value!.Editable);
    var beyond = await DayAsync(w, w.Employee, w.Employee, Today.AddDays(29));
    Assert.Equal((400, PlannerErrorCodes.BeyondHorizon), (beyond.StatusCode, beyond.ErrorCode));
}

[Fact]
public async Task A_block_moves_to_another_day_but_not_onto_another_block_there()
{
    var w = await SeedAsync();
    var today = (await PlanAsync(w, w.Task, Local(9), Local(10))).Value!.Blocks.Single();
    Assert.True((await PlanAsync(w, w.Task, Local(14, dayOffset: 1), Local(15, dayOffset: 1))).IsSuccess);
    await using var db = w.H.NewContext();

    var clash = await Service(db).MoveBlockAsync(w.H.TenantId, w.Employee, today.Id,
        new MovePlannedBlockRequest(Local(14, 30, dayOffset: 1), Local(15, 30, dayOffset: 1)), default);
    var moved = await Service(db).MoveBlockAsync(w.H.TenantId, w.Employee, today.Id,
        new MovePlannedBlockRequest(Local(10, dayOffset: 1), Local(11, dayOffset: 1)), default);

    Assert.Equal((409, PlannerErrorCodes.BlockOverlap), (clash.StatusCode, clash.ErrorCode));
    Assert.Equal(Today.AddDays(1), moved.Value!.Date);
    Assert.Equal(2, moved.Value.Blocks.Count);
}

[Fact]
public async Task A_past_block_cannot_be_moved_or_deleted()
// Seed the block straight into the db one day back (the service refuses to create it), then assert
// 400 planner_day_read_only from both MoveBlockAsync and DeleteBlockAsync.
```

- [ ] **Step 2: Run them and confirm they fail**

Run: `dotnet test tests/ONEVO.Tests.Integration --artifacts-path artifacts/claude-test --filter "FullyQualifiedName~PlannerServiceIntegrationTests"`
Expected: the 4 new tests FAIL (`planner_not_today`); every other test passes.

- [ ] **Step 3: Implement the editable-day rule in `PlannerService`**

- Add the constant and a private `DayCheck(DateOnly day, DateOnly today)` returning null, `DayReadOnly` or `BeyondHorizon`.
- `CheckWriteAsync` checks that the start and the end-1 tick fall on the **same** local day, using `DayCheck` on that day. When they fall on different days, return `InvalidTime`.
- Move and delete also apply `DayCheck` to the block's **current** day (a past block stays put).
- Replace `ComposeTodayAsync` with `ComposeDayOfAsync(tenantId, employeeId, DateTimeOffset instant, Zone zone, ct)`. `GetDayAsync` uses `DayCheck` for future days. Update the class summary.

- [ ] **Step 4: Run the planner integration tests again**

Same command. Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Planner src/ONEVO.Api/Controllers/Tenant/WorkManagement/PlannerController.cs tests/ONEVO.Tests.Integration/WorkManagement/Planner
git commit -m "feat(planner): plan today and the next 28 days; past days are read-only"
```

---

### Task 2: The work window for a day (`PlannerWorkWindows`)

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/Planner/Services/PlannerWorkWindows.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Planner/Contracts/PlannerContracts.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Planner/PlannerWorkWindowsTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record PlannerWorkWindow(TimeOnly WorkStart, TimeOnly WorkEnd, int BreakMinutes, TimeOnly? BreakStart, bool IsWorkingDay, int WorkingMinutes);`
  - `public static class PlannerWorkWindows { public static PlannerWorkWindow? For(LegalEntity? legalEntity, DateOnly day); }`
- Rules:
  - The result is null when `legalEntity` is null or `!AttendanceScheduleResolver.IsScheduleConfigured(legalEntity)`.
  - `IsWorkingDay` comes from `AttendanceScheduleResolver.ParseWorkingDays` (ISO day, Sunday = 7).
  - `WorkingMinutes` = (end − start) − break, floored at 0, and 0 on a non-working day.
  - `BreakStart` is centred: start + ((end − start) − break) / 2, rounded down to 15 minutes. It is null when break is 0 or null.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void Configured_weekday_gives_the_window_minus_the_centred_break()
// 09:00–17:00, break 60, StandardWorkingDays "[1,2,3,4,5]", Timezone "Asia/Colombo", Monday 2026-11-02:
// Assert.Equal(new PlannerWorkWindow(new(9,0), new(17,0), 60, new(12,30), true, 420), PlannerWorkWindows.For(le, new(2026,11,2)));
[Fact] public void Weekend_keeps_the_window_but_has_no_working_minutes()   // Sunday 2026-11-08 → IsWorkingDay false, WorkingMinutes 0
[Fact] public void Overnight_or_missing_times_or_missing_zone_is_null()      // 22:00–06:00 → null; WorkStartTime null → null; Timezone null → null
[Fact] public void No_break_has_no_break_start()                             // BreakDurationMinutes null → (0, null), WorkingMinutes 480
[Fact] public void No_legal_entity_is_null()
```

- [ ] **Step 2: Run them and confirm they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit --artifacts-path artifacts/claude-test --filter "FullyQualifiedName~PlannerWorkWindowsTests"`
Expected: FAIL (type not found).

- [ ] **Step 3: Implement `PlannerWorkWindows.For`** as specified above.

- [ ] **Step 4: Run the tests again.** Expected: 5 PASS.

- [ ] **Step 5: Commit**

```bash
git commit -am "feat(planner): a day's work window from the legal entity schedule" # add the new files first
```

---

### Task 3: `GET /api/v1/planner/me/range`

**Files:**
- Modify:
  - `Planner/Services/PlannerService.cs`
  - `Planner/Contracts/PlannerContracts.cs`
  - `Planner/Commands/PlannerCommands.cs`
  - `Planner/RepositoryInterfaces/IPlannerRepository.cs`
  - `src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/Planner/EfPlannerRepository.cs`
  - `PlannerController.cs`
- Test: `tests/ONEVO.Tests.Integration/WorkManagement/Planner/PlannerRangeIntegrationTests.cs` (new; copy the `SeedAsync`/`Service` helpers' shape from `PlannerServiceIntegrationTests`)

**Interfaces:**
- Consumes: `PlannerWorkWindows.For`, `PlannerService.PlanningHorizonDays` (Tasks 1–2).
- Produces:
  - Contracts:
    - `public sealed record PlannerRangeBlockView(Guid Id, Guid TaskId, Guid ProjectId, string TaskTitle, DateTimeOffset StartAt, DateTimeOffset EndAt, int PlannedMinutes, int ActualMinutes, bool StillAssigned);` (ActualMinutes as in v1: the employee's clocked minutes on that task inside that day, via `PlannerActuals.TaskMinutes`)
    - `public sealed record PlannerRangeDay(DateOnly Date, bool Editable, PlannerWorkWindow? WorkWindow, IReadOnlyList<PlannerRangeBlockView> Blocks, int BlockMinutes, int WorkingMinutes);`
    - `public sealed record PlannerRangeResponse(string Timezone, DateOnly Today, IReadOnlyList<PlannerRangeDay> Days);`
  - `PlannerErrorCodes.InvalidRange = "planner_invalid_range"`.
  - `PlannerTaskInfo` gains `Guid ProjectId` (last positional parameter; update the two existing projections).
  - `IPlannerRepository.GetEmployeeLegalEntityAsync(Guid tenantId, Guid employeeId, CancellationToken ct)` → `Task<LegalEntity?>`. It returns the employee's `LegalEntityId` entity, else the tenant's primary entity, else null.
  - `PlannerService.GetRangeAsync(Guid tenantId, Guid callerEmployeeId, DateOnly from, DateOnly to, CancellationToken ct)` → `Task<Result<PlannerRangeResponse>>`.
  - `GetMyPlannerRangeQuery(DateOnly From, DateOnly To)`, handled by `PlannerRequestHandler`.
  - `[HttpGet("me/range")] MyRange([FromQuery] DateOnly from, [FromQuery] DateOnly to)`. `Respond` is made generic: `Respond<T>(Result<T>)`.
- Rules:
  - One `ListBlocksAsync` for the whole range (the local start of `from` to the local end of `to`); blocks are grouped by local day.
  - One `GetTasksAsync`, one `ListSessionsAsync` and one legal-entity read.
  - `Editable` follows Task 1 (days after the horizon are false, C6).
  - `WorkingMinutes` = `WorkWindow?.WorkingMinutes ?? 0`.

- [ ] **Step 1: Write the failing integration tests**

```csharp
[Fact] public async Task A_week_returns_seven_days_with_their_blocks_minutes_and_edit_flags()
// Plan 09:00–11:00 today and 14:00–15:00 tomorrow; range Today.AddDays(-1)..Today.AddDays(5):
// Assert.Equal(7, r.Days.Count); Assert.False(r.Days[0].Editable); Assert.True(r.Days[1].Editable);
// Assert.Equal((120, 60), (r.Days[1].BlockMinutes, r.Days[2].BlockMinutes)); Assert.Equal(w.ProjectId, r.Days[1].Blocks[0].ProjectId);
[Fact] public async Task Work_window_comes_from_the_employees_legal_entity()
// Set the seeded legal entity to 09:00–17:00, break 60, zone Colombo → Days[1].WorkWindow!.WorkingMinutes == 420 and WorkingMinutes == 420
[Fact] public async Task No_schedule_means_a_null_window_and_zero_working_minutes()   // Review Focus 4
[Fact] public async Task Days_after_the_horizon_are_returned_read_only()               // range Today..Today.AddDays(41): Days[28].Editable, !Days[29].Editable
[Fact] public async Task A_range_over_42_days_or_backwards_is_400_invalid_range()
[Fact] public async Task Planner_disabled_is_503()
```

- [ ] **Step 2: Run them and confirm they fail.** `dotnet test … --filter "FullyQualifiedName~PlannerRangeIntegrationTests"`. Expected: they FAIL.
- [ ] **Step 3: Implement the repository method, the `ProjectId` projection, `GetRangeAsync`, the query and handler, and the controller action** as specified above.
- [ ] **Step 4: Run the range tests and `PlannerServiceIntegrationTests`.** Expected: all PASS.
- [ ] **Step 5: Commit** with `feat(planner): range read for the calendar grid (blocks, work window, edit flags per day)`.

---

### Task 4: `GET /api/v1/planner/me/focus`

**Files:**
- Create: `Planner/Services/PlannerFocus.cs`
- Modify:
  - `PlannerContracts.cs`
  - `IPlannerRepository.cs`
  - `EfPlannerRepository.cs`
  - `PlannerService.cs`
  - `PlannerCommands.cs`
  - `PlannerController.cs`
- Test:
  - `tests/ONEVO.Tests.Unit/Features/WorkManagement/Planner/PlannerFocusTests.cs`
  - `tests/ONEVO.Tests.Integration/WorkManagement/Planner/PlannerFocusIntegrationTests.cs`

**Interfaces:**
- Produces:
  - Repository:
    - `public sealed record PlannerFocusTaskRow(Guid TaskId, Guid ProjectId, string ProjectName, string Title, string Priority, DateOnly? DueDate, decimal? EstimatedHours, int LoggedMinutes);`
    - `IPlannerRepository.ListFocusTasksAsync(Guid tenantId, Guid employeeId, DateTimeOffset now, CancellationToken ct)` → `Task<IReadOnlyList<PlannerFocusTaskRow>>`. It returns every task that is assigned to the employee, not deleted, not complete (the same "not done" test as `ListDueAssignedTasksAsync`) and not a meeting (`TaskKind`). `LoggedMinutes` sums **all** non-deleted sessions on the task, with an open one counted to `now`.
  - Contracts:
    - `public static class PlannerFocusReasons { public const string Overdue = "overdue", DueToday = "due_today", DueThisWeek = "due_this_week", Backlog = "backlog"; }`
    - `public sealed record PlannerFocusItem(Guid TaskId, Guid ProjectId, string ProjectName, string Title, string Priority, DateOnly? DueDate, int? EstimatedMinutes, int LoggedMinutes, int? RemainingMinutes, int PlannedAheadMinutes, DateOnly? FirstPlannedDate, int PlannedDayCount, bool FullyPlanned, string Reason);`
    - `public sealed record PlannerFocusGroup(string Reason, int Count, IReadOnlyList<PlannerFocusItem> Items);`
    - `public sealed record PlannerFocusResponse(DateOnly Today, DateOnly WeekEnd, IReadOnlyList<PlannerFocusGroup> Groups);` The four groups are always present, in the order overdue, due_today, due_this_week, backlog.
  - `public static class PlannerFocus { public const int MaxItemsPerGroup = 50; public static PlannerFocusResponse Build(IReadOnlyList<PlannerFocusTaskRow> rows, IReadOnlyList<PlannedTaskBlock> blocksFromToday, DateOnly today, TimeZoneInfo zone); }`
  - `PlannerService.GetFocusAsync(Guid tenantId, Guid callerEmployeeId, CancellationToken ct)` → `Task<Result<PlannerFocusResponse>>`. It reads the rows and the caller's blocks from the local start of today onward.
  - `GetMyPlannerFocusQuery()` and `[HttpGet("me/focus")] MyFocus()`.
- Rules (spec §4.2):
  - **Reason:**
    - due < today → overdue;
    - due = today → due_today;
    - today < due ≤ WeekEnd (the Sunday of today's Monday-based week) → due_this_week;
    - else (null or later) → backlog.
  - **Remaining:** `max(0, estimate×60 − logged)`; null without an estimate.
  - **FullyPlanned:** remaining is not null and PlannedAheadMinutes ≥ remaining.
  - **Sort within a group:**
    - FullyPlanned last;
    - then priority (critical, high, medium, low; unknown last);
    - then due date ascending, nulls last;
    - then title (ordinal, ignoring case).
  - `Count` is the uncapped total; `Items` keeps the first 50.

- [ ] **Step 1: Write the failing unit tests for `PlannerFocus.Build`** (today = Monday 2026-11-02, Colombo)

```csharp
[Fact] public void Tasks_land_in_the_four_groups_by_due_date()      // due 10-30 → overdue; 11-02 → due_today; 11-08 → due_this_week; 11-09 and null → backlog
[Fact] public void Remaining_is_estimate_less_logged_and_null_without_estimate()   // 5h, 120 logged → 180; 1h, 90 logged → 0; null → null
[Fact] public void Blocks_from_today_on_set_planned_ahead_first_day_and_day_count()
// 60 min Tue + 90 min Thu → (150, 2026-11-03, 2); FullyPlanned when remaining 150, not when 180
[Fact] public void Sorted_fully_planned_last_then_priority_then_due_then_title()
[Fact] public void Each_group_keeps_fifty_items_and_the_full_count()   // 60 backlog rows → Count 60, Items 50
[Fact] public void On_Sunday_night_a_Monday_task_is_backlog_and_becomes_due_today_on_Monday()   // Review Focus 1: today 11-08 vs 11-09
```

- [ ] **Step 2: Run them and confirm they fail.**
- [ ] **Step 3: Implement `PlannerFocus.Build`.**
- [ ] **Step 4: Run the unit tests again.** Expected: 6 PASS.
- [ ] **Step 5: Write the failing integration tests**

```csharp
[Fact] public async Task Focus_lists_assigned_open_tasks_with_project_and_logged_minutes_by_anyone()
// Two sessions on the task: the employee 60 min (closed), a colleague 30 min (closed) → LoggedMinutes 90
[Fact] public async Task An_open_session_counts_up_to_now()                 // Review Focus 5
[Fact] public async Task Done_meeting_deleted_and_unassigned_tasks_are_left_out()
[Fact] public async Task An_unassigned_task_keeps_its_future_block_but_leaves_the_focus_list()   // Review Focus 3 (range still returns it, StillAssigned false)
[Fact] public async Task Planner_disabled_is_503()
```

- [ ] **Step 6: Run them and confirm they fail; then implement `ListFocusTasksAsync`, `GetFocusAsync`, the query and the action.**
- [ ] **Step 7: Run the focus, range and service planner suites.** Expected: all PASS.
- [ ] **Step 8: Commit** with `feat(planner): Focus list - overdue, due today, due this week and backlog, ranked`.

---

### Task 5: API doc and full verification

**Files:**
- Modify: `docs/api/planner-v1.md`. Rename the heading to "Planner API v1.1".
  - Replace the "today only" rules with the horizon rules.
  - Add the two endpoints with example JSON.
  - Add the new error codes.
  - Note that `dueTasks` is kept for compatibility.

- [ ] **Step 1: Update the doc.** Every code and field name must match `PlannerContracts.cs`.
- [ ] **Step 2: Build with no warnings.** `dotnet build --artifacts-path artifacts/claude-test -warnaserror`. Expected: 0 warnings.
- [ ] **Step 3: Run the full unit and architecture suites.** Expected: green.
- [ ] **Step 4: Run the full integration suite on its own.** Expected: only the 4 known My Team failures.
- [ ] **Step 5: Commit** (`docs(planner): v1.1 - future days, range and focus`) and push `feature/planner-calendar-merge`. Do not open a PR.
