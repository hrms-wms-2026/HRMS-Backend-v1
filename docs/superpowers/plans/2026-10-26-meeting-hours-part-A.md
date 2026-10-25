# Meeting Hours (Part A) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** When a meeting occurrence ends, it carries `duration × attendees` hours that count in its module's and project's completed hours and in the Team Progress hour columns, but never in task counts.

**Architecture:**
- An hourly stamping pass, run in the existing meeting background job, finds occurrences that have ended and are not yet stamped. For each one it:
  - computes person-hours with the existing `EffectiveAttendees` rule;
  - writes `EstimatedHours = CompletedHours` on the meeting task;
  - cascades the delta up the objective/project chain (logic extracted from `MoveTaskStatusCommandHandler`);
  - marks `meeting_details.hours_stamped_at`.
- The summary SQL counts meeting rows in `completed_hours` only.

**Tech Stack:** .NET 10, EF Core 10 migrations, PostgreSQL, xUnit + Moq, Testcontainers (`MyTeamDb`).

**Spec:** `docs/superpowers/specs/2026-10-26-meeting-hours-and-day-planner-design.md` (Part A).

**Branch:** a new branch `feature/meeting-hours` cut from `feature/meeting-task-phase2-d`. Commit and push it; no PR.

## Global Constraints

- Hours for one occurrence = `durationMinutes / 60 × N`, where N is the count of distinct employees who are the organizer, or an effective attendee (`EffectiveAttendees.Compute`) whose `Response` is not `rejected`. Guests are not counted. Round to 2 decimals.
- A cancelled occurrence (its own `CancelledAt`, or `MeetingStatusCalculator.CancelledWithSeries`) is stamped with **0**.
- Stamp once per occurrence. A second run changes nothing (`hours_stamped_at` is set). Attendee changes after stamping are ignored.
- Before stamping, a meeting task contributes 0 h, because `estimated_hours` is null.
- Summaries:
  - meeting rows add to `completed_hours` only;
  - `not_started_hours`, `in_progress_hours`, `task_count`, `unestimated_task_count` and `earliest_open_due_date` keep excluding `task_kind = 'meeting'`.
- Lookback for ended occurrences that were never materialized: 7 days, my call per the spec ("materialized at stamping time").
- The stamping pass runs only when `Meetings:Background` is on, under the existing generator lease.

## Review Focus

1. **A meeting whose organizer declined their own series** still counts the organizer: N ≥ 1 for a non-cancelled occurrence. Owned by Task 2: `Organizer_always_counts`.
2. **A per-occurrence removal of a series participant** must not count that person; a per-occurrence add must. Owned by Task 2: `Occurrence_overrides_change_the_count`.
3. **A job run that crashes halfway** must not double-count on the next run. Stamping and the cascade must be in one transaction per occurrence. Owned by Task 3: `Stamping_twice_adds_hours_once`.
4. **A meeting task whose module was moved after the meeting** cascades to the module it is in at stamping time. That is the task's current `ObjectiveId`. Owned by Task 3: `Cascades_up_the_current_module_chain`.
5. **Team Progress counts** stay unchanged when a meeting is stamped. Owned by Task 1: `Stamped_meeting_adds_completed_hours_but_no_count`.

---

### Task 1: Migration — `hours_stamped_at` and the summary SQL

**Files:**
- Modify: `src/ONEVO.Domain/Features/WorkManagement/Meetings/Entities/MeetingDetails.cs` (add `public DateTimeOffset? HoursStampedAt { get; set; }`).
- Modify: the EF configuration for `MeetingDetails` (column `hours_stamped_at`, nullable).
- Create: migration `AddMeetingHoursStamping`, with a timestamp later than `20261021181949`. It contains:
  - the column;
  - `CREATE OR REPLACE FUNCTION wm_refresh_module_hour_summary`, a copy of `20261021181949`'s function where:
    - the join drops `AND t.task_kind IS DISTINCT FROM 'meeting'`;
    - every aggregate except `completed_hours` adds `AND t.task_kind IS DISTINCT FROM 'meeting'` to its `FILTER`, or for `COUNT(t.id)` becomes `COUNT(t.id) FILTER (WHERE t.task_kind IS DISTINCT FROM 'meeting')`;
    - `completed_hours` becomes `SUM(t.estimated_hours) FILTER (WHERE x.done OR t.task_kind = 'meeting')`.
  - the same catalog guard and backfill as `20261021181949`.
- Modify test: `tests/ONEVO.Tests.Integration/WorkManagement/Meetings/ModuleHourSummaryMeetingExclusionIntegrationTests.cs`.

**Interfaces:**
- Produces: `MeetingDetails.HoursStampedAt`, which Task 3 uses.

- [ ] **Step 1: Write the failing test** `Stamped_meeting_adds_completed_hours_but_no_count` in the summary test class.
  - Setup: one standard task (5 h, not done) and one meeting occurrence whose task has `estimated_hours = 3`, in the same module.
  - Expect: `task_count == 1`, `unestimated == 0`, `not_started == 5`, `completed_hours == 3`.
  - Keep the existing test showing that an unstamped meeting (`estimated_hours` null) changes nothing.
  - Change the existing assertion that "a meeting with `estimated_hours` set does not move hours": it now moves `completed_hours` only.
- [ ] **Step 2: Run it. Expect FAIL** (`completed_hours == 0`).
  - Run: `dotnet test tests/ONEVO.Tests.Integration --filter "FullyQualifiedName~ModuleHourSummaryMeetingExclusionIntegrationTests"`
- [ ] **Step 3: Implement** the entity property, the configuration and the migration (`dotnet ef migrations add AddMeetingHoursStamping`, then add the SQL).
- [ ] **Step 4: Run the class and `MeetingSchemaIntegrationTests`. Expect PASS.** Confirm `dotnet ef migrations has-pending-model-changes` is clean.
- [ ] **Step 5: Commit** `feat(meetings): count stamped meeting hours in module completed hours`

### Task 2: Person-hours rule

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/Meetings/Services/MeetingPersonHours.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Meetings/MeetingPersonHoursTests.cs`

**Interfaces:**
- Produces: `public static decimal MeetingPersonHours.Compute(int durationMinutes, Guid organizerEmployeeId, IReadOnlyList<EffectiveAttendee> attendees, bool cancelled)`

- [ ] **Step 1: Write the failing tests:**
  - `Five_invited_one_declined_one_guest_is_three_hours`: 60 min; organizer plus 3 employees, one of them `rejected`, and 1 guest → `3.00`.
  - `Organizer_always_counts`: the organizer's own response is `rejected` → still counted.
  - `Occurrence_overrides_change_the_count`: build attendees with `EffectiveAttendees.Compute` from series participants A and B, a remove override for B and an add override for C → A, C and the organizer = 3.
  - `Cancelled_is_zero`.
  - `Thirty_minutes_two_people_is_one_hour`.
- [ ] **Step 2: Run, expect FAIL** (the type is missing).
  - Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~MeetingPersonHoursTests"`
- [ ] **Step 3: Implement** `Compute`: distinct employee ids, the organizer added, `Math.Round(minutes / 60m × n, 2)`.
- [ ] **Step 4: Run, expect PASS.**
- [ ] **Step 5: Commit** `feat(meetings): person-hours for an ended occurrence`

### Task 3: Stamping pass, shared completed-hours cascade, job wiring

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/Common/Services/CompletedHoursCascade.cs`: `ICompletedHoursCascade.ApplyAsync(Guid tenantId, Guid objectiveId, decimal delta, CancellationToken ct)`, the body moved from `MoveTaskStatusCommandHandler.CascadeCompletedHoursAsync`.
- Modify: `MoveTaskStatusCommandHandler` to use it (no behaviour change; its tests stay green).
- Create: `src/ONEVO.Application/Features/WorkManagement/Meetings/Services/MeetingHoursStamper.cs`: `Task<int> StampTenantAsync(Guid tenantId, CancellationToken ct)`.
- Modify: `IMeetingRepository` and `EfMeetingRepository`:
  - `ListEndedUnstampedTrackedAsync(Guid tenantId, DateTimeOffset now, int take, CancellationToken ct)`, which returns `meeting_details` where `end_at <= now` and `hours_stamped_at` is null, oldest first;
  - `ListActiveRecurringSeriesIdsAsync` already exists, for the virtual lookback.
- Modify: `MeetingOccurrenceGeneratorJob`, which calls `StampTenantAsync` after `RunForTenantAsync` for each tenant.
- Modify: DI registrations.
- Test: `tests/ONEVO.Tests.Integration/WorkManagement/Meetings/MeetingHoursStampingIntegrationTests.cs`

**Interfaces:**
- Consumes: Task 1 `HoursStampedAt`; Task 2 `MeetingPersonHours.Compute`; `EffectiveAttendees.Compute`; `IMeetingOccurrenceMaterializer.EnsureAsync`.
- Produces: `MeetingHoursStamper` (job-only).

`StampTenantAsync` steps:
1. For each active recurring series, materialize the occurrences in `[now − 7 days, now]` whose end has passed. Use `IMeetingOccurrenceMaterializer.EnsureAsync`; it skips suppressed or cancelled keys as it already does.
2. Page through `ListEndedUnstampedTrackedAsync`, 200 at a time. For each occurrence, in one transaction:
   - compute the hours;
   - set the task's `EstimatedHours = CompletedHours = h`;
   - call `ICompletedHoursCascade.ApplyAsync(task.ObjectiveId, h)`;
   - set `HoursStampedAt = now`;
   - save.

- [ ] **Step 1: Write the failing tests** (`MeetingTestServices` and `MeetingSeed`):
  - `An_ended_occurrence_gets_person_hours_and_cascades`: 60 min, organizer plus 2 accepted plus 1 declined, in module M under root R.
    - Expect: task `EstimatedHours == CompletedHours == 3`.
    - Expect: M and R `CompletedHours` and the project's `CompletedHours` each +3.
    - Expect: `HoursStampedAt` set.
  - `Stamping_twice_adds_hours_once`: run twice; the totals are unchanged after the second run.
  - `A_future_or_running_occurrence_is_not_stamped`.
  - `A_cancelled_occurrence_is_stamped_with_zero`.
  - `An_ended_virtual_occurrence_is_materialized_and_stamped`: a weekly series whose last week's occurrence was never materialized → it now has a task with hours.
  - `Cascades_up_the_current_module_chain`: move the meeting to module N before it ends, then stamp → N's chain gets the hours and M's chain does not.
  - `An_office_meeting_stamps_the_office_project_only`.
- [ ] **Step 2: Run, expect FAIL.**
  - Run: `dotnet test tests/ONEVO.Tests.Integration --filter "FullyQualifiedName~MeetingHoursStampingIntegrationTests"`
- [ ] **Step 3: Implement** the cascade extraction, the stamper, the repository method, the job call and DI.
- [ ] **Step 4: Run the new class, `MoveTaskStatus*` tests, and the meeting integration slice. Expect PASS.**
  - Run: `dotnet test tests/ONEVO.Tests.Integration --filter "FullyQualifiedName~WorkManagement.Meetings|FullyQualifiedName~MoveTaskStatus"` and `dotnet test tests/ONEVO.Tests.Unit`
- [ ] **Step 5: Docs.**
  - `docs/api/meetings-v1.md`: a short "Hours" section with the rule above.
  - `docs/runbooks/meetings-phase2-release.md`: stamping needs `Meetings:Background`.
- [ ] **Step 6: Commit** `feat(meetings): stamp ended occurrences with person-hours`
