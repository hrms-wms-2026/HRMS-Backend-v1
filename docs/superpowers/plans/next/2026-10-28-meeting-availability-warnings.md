# Meeting Availability Warnings Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The calendar conflict check also warns about approved leave, public holidays and outside-working-hours, and the Meeting (work item) form shows these warnings without ever blocking a save.

**Architecture:** A pure static `ParticipantAvailabilityRules` computes the new warnings from in-memory inputs. A scoped `ParticipantAvailabilityChecker` batches the data reads and calls the rules. `CheckCalendarConflictsQueryHandler` appends the checker's warnings to its event conflicts and skips the edited meeting series. The Angular meeting form calls the existing `checkConflicts` API (debounced), and a shared warnings list component renders the rows in both forms.

**Tech Stack:** .NET 10, MediatR, EF Core, xUnit + Moq; Angular 21 signals, vitest (`ng test`).

**Spec:** `docs/superpowers/specs/next/2026-10-28-meeting-availability-warnings-design.md` (BE repo). Read it first.

## Global Constraints

- Warnings never block: the meeting form's Create/Save `[disabled]` stays `saving() || !canSave()`.
- Kinds (exact strings): `event`, `leave`, `holiday`, `outside_hours`. Default `event`.
- Labels (exact copy): `On leave`, `Busy`, `Public holiday: {name}`, `Outside working hours ({HH:mm}–{HH:mm})` (en dash), `Non-working day`.
- New kinds use `ConflictingEventId = Guid.Empty`.
- Leave type, reason and hours never leave the server.
- Working window and holiday dates are read in the participant's effective timezone (`ICalendarTimezoneResolver.ResolveForUserAsync(tenantId, employee.UserId)`). With `UserId == Guid.Empty`, use the request timezone (C9).
- Branch `feature/meeting-availability-warnings` in both worktrees: BE `C:\Users\User\onexso-worktrees\be-planner-merge`, FE `C:\Users\User\onexso-worktrees\fe-planner-merge`.
- Running unit tests while the API runs from another checkout: add `--artifacts-path artifacts/claude-test`.
- No new build warnings (the FE pre-push hook checks this).

## Review Focus

1. A meeting crossing midnight in the participant's zone: the holiday and window checks must cover both local dates (Task 1 test `Holiday_on_second_local_day_of_a_cross_midnight_meeting_warns`).
2. The participant's zone differs from the meeting's: 14:00 Colombo is 08:30 London, which is before a London 09:00 start (Task 1 test `Window_read_in_participant_zone`).
3. Leave scope resolution fails (`NoLeaveReadPermission`): the caller still sees "On leave" for themselves and "Busy" for others (Task 2 test `Scope_failure_shows_busy_for_others_and_on_leave_for_self`).
4. The same person appears twice (participant + organiser id sent by the form): one set of warnings, not two (Task 3 test `Duplicate_participant_ids_are_checked_once`).
5. The form check fires while the date is still empty or the duration is invalid: no request (Task 5 test `does_not_check_without_a_valid_time`).

---

### Task 1: `ParticipantAvailabilityRules` (pure)

**Files:**
- Create: `src/ONEVO.Application/Features/Calendar/Queries/CheckCalendarConflicts/ParticipantAvailabilityRules.cs`
- Modify: `src/ONEVO.Application/Features/Calendar/Queries/CheckCalendarConflicts/CheckCalendarConflictsQuery.cs` (add `Kind` to `CalendarConflict`, add `CalendarConflictKinds`)
- Test: `tests/ONEVO.Tests.Unit/Features/Calendar/ParticipantAvailabilityRulesTests.cs`

**Interfaces:**
- Produces:
  - `public static class CalendarConflictKinds { Event = "event"; Leave = "leave"; Holiday = "holiday"; OutsideHours = "outside_hours"; }`
  - `CalendarConflict(..., bool IsBusyOnly = false, string Kind = CalendarConflictKinds.Event)`, with `Kind` appended last so existing callers compile.
  - `public sealed record AvailabilityParticipant(Guid EmployeeId, string EmployeeName, TimeZoneInfo Zone, LegalEntity? LegalEntity, bool LeaveVisible);`
  - `public static IReadOnlyList<CalendarConflict> ParticipantAvailabilityRules.Evaluate(AvailabilityParticipant p, DateTimeOffset start, DateTimeOffset end, bool isAllDay, IReadOnlyList<LeaveRequest> approvedLeave, IReadOnlyList<LeaveCalendarHoliday> holidays)`
- Rules:
  - **Leave:** for each leave whose effective end > start and whose start < end, emit one `leave` row. The effective end is `min(EndAt, start of PartialCancelEffectiveDate in p.Zone)` when set. Overlap = intersection. Title `On leave` if `p.LeaveVisible`, else `Busy`.
  - **Holiday:** the local dates touched by `[start, end)` in `p.Zone`. A holiday counts when `LegalEntityId == p.LegalEntity?.Id` or `LegalEntityId is null`. Dedupe by date. Overlap = that local day ∩ meeting.
  - **Outside hours:** skipped when `isAllDay`. For each touched local date, `PlannerWorkWindows.For(p.LegalEntity, date)`: null → nothing; `!IsWorkingDay` → `Non-working day` row (overlap = day ∩ meeting); otherwise, any part of the meeting before `WorkStart` or after `WorkEnd` gives one row per day labelled with the window, with overlap = the outside part (the earliest-to-latest outside span). Break minutes are never flagged.
  - Order of output: leave, holiday, outside_hours.

- [ ] **Step 1: Write the failing tests** (xUnit; use `TimeZoneInfo.FindSystemTimeZoneById("Asia/Colombo")` and `"Europe/London"`; a `LegalEntity` with `WorkStartTime = 09:00`, `WorkEndTime = 17:00`, `BreakDurationMinutes = 60`, `StandardWorkingDays = "1,2,3,4,5"`):
  - `Approved_leave_overlapping_warns_on_leave`: Kind `leave`, title `On leave`, `ConflictingEventId == Guid.Empty`.
  - `Leave_outside_scope_reads_busy`: `LeaveVisible: false` → title `Busy`, kind `leave`.
  - `Morning_half_day_does_not_warn_on_afternoon_meeting`: leave 09:00–13:00 local, meeting 14:00–15:00 → no leave row.
  - `Partial_cancel_ends_leave_before_effective_date`: leave Mon–Fri, PartialCancelEffectiveDate = Wed, meeting Wed 10:00 → no leave row; meeting Tue 10:00 → one row.
  - `Holiday_for_entity_warns_with_name`: title `Public holiday: Deepavali`.
  - `Holiday_of_other_entity_is_ignored`.
  - `Holiday_on_second_local_day_of_a_cross_midnight_meeting_warns`.
  - `Meeting_inside_window_has_no_outside_row`: 10:00–11:00.
  - `Meeting_ending_after_window_warns_with_window_label`: 16:30–17:30 → title `Outside working hours (09:00–17:00)`, overlap 17:00–17:30.
  - `Meeting_in_break_is_not_flagged`: 12:30–13:00 → no row.
  - `Saturday_reads_non_working_day`.
  - `Window_read_in_participant_zone`: meeting 14:00–14:30 Asia/Colombo for a London participant → outside-hours row.
  - `No_schedule_or_no_entity_means_no_outside_or_holiday_rows`.
  - `All_day_meeting_skips_outside_hours_but_keeps_leave_and_holiday`.
- [ ] **Step 2: Run them and see them fail.** Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~ParticipantAvailabilityRulesTests --artifacts-path artifacts/claude-test`. Expected: compile error (type missing).
- [ ] **Step 3: Implement** `CalendarConflictKinds`, the `Kind` parameter, `AvailabilityParticipant` and `ParticipantAvailabilityRules.Evaluate` as specified.
- [ ] **Step 4: Run the tests and the existing `CheckCalendarConflictsQueryHandlerTests`.** Expected: all pass.
- [ ] **Step 5: Commit** `feat(calendar): rules for leave, holiday and working-hours availability warnings`.

### Task 2: `ParticipantAvailabilityChecker` (data gathering)

**Files:**
- Create: `src/ONEVO.Application/Features/Calendar/Queries/CheckCalendarConflicts/ParticipantAvailabilityChecker.cs`
- Modify: `src/ONEVO.Application/DependencyInjection.cs` (register scoped, next to other Calendar services)
- Test: `tests/ONEVO.Tests.Unit/Features/Calendar/ParticipantAvailabilityCheckerTests.cs`

**Interfaces:**
- Consumes: Task 1 types; `ILeaveRequestReadRepository.ListApprovedCoveringAsync`, `ILeaveCalendarHolidayProvider.ListHolidaysAsync`, `ILegalEntityRepository.GetByIdForTenantAsync`, `ICalendarTimezoneResolver.ResolveForUserAsync`, `ILeaveVisibilityScopeProvider.ResolveForCurrentUserAsync`, `IPositionAssignmentRepository.GetActivePrimaryByEmployeeIdsAsync`, `EmployeeVisibilityScopeMatcher.Includes`.
- Produces: `public interface IParticipantAvailabilityChecker { Task<IReadOnlyList<CalendarConflict>> CheckAsync(Guid tenantId, Guid? callerEmployeeId, IReadOnlyList<Employee> participants, DateTimeOffset start, DateTimeOffset end, bool isAllDay, string requestTimeZone, CancellationToken ct); }` with implementation `ParticipantAvailabilityChecker`.
- Behaviour:
  - One leave read for all ids over the local-date range ±1 day.
  - One holiday read for the distinct legal-entity ids.
  - One legal entity read per distinct id.
  - Leave visibility: `LeaveVisible = participant.Id == callerEmployeeId || (scope is not null && Includes(scope, id, primaryPositionId, DepartmentId, LegalEntityId))`. A failed resolution means scope null.
  - Unknown time zone ids fall back to `requestTimeZone`, then UTC.

- [ ] **Step 1: Write the failing tests** (Moq):
  - `Reads_leave_and_holidays_once_for_all_participants` (Verify `Times.Once`).
  - `Scope_failure_shows_busy_for_others_and_on_leave_for_self`.
  - `Participant_in_scope_sees_on_leave`.
  - `Employee_without_user_uses_request_timezone`.
- [ ] **Step 2: Run, expect a compile failure.**
- [ ] **Step 3: Implement and register** `services.AddScoped<IParticipantAvailabilityChecker, ParticipantAvailabilityChecker>()`.
- [ ] **Step 4: Run the tests. Expected: pass.**
- [ ] **Step 5: Commit** `feat(calendar): gather leave, holidays and schedules for availability warnings`.

### Task 3: Handler, contracts and endpoint

**Files:**
- Modify: `CheckCalendarConflictsQuery.cs` (query gains `bool IsAllDay = false, string? TimeZone = null, Guid? ExcludeMeetingSeriesId = null`)
- Modify: `CheckCalendarConflictsQueryHandler.cs` (optional ctor param `IParticipantAvailabilityChecker? availability = null`, last)
- Modify: `src/ONEVO.Api/Contracts/Calendar/CalendarContracts.cs` (request gains `bool IsAllDay = false, string? TimeZone = null, Guid? ExcludeMeetingSeriesId = null`; view model gains `string Kind`; mapper passes them)
- Modify: `src/ONEVO.Api/Controllers/Tenant/Calendar/CalendarController.cs` `CheckConflicts` (pass the new fields)
- Modify: `docs/api` entry for check-conflicts (find with `grep -rl check-conflicts docs/api`)
- Test: `tests/ONEVO.Tests.Unit/Features/Calendar/CheckCalendarConflictsQueryHandlerTests.cs`; `tests/ONEVO.Tests.Integration/WorkManagement/Meetings/MeetingCalendarIntegrationTests.cs`

**Interfaces:**
- Consumes: Task 2 `IParticipantAvailabilityChecker`.
- Produces: JSON `conflicts[].kind`; request `{ participantEmployeeIds, startDate, endDate, isAllDay?, timeZone?, excludeMeetingSeriesId? }`.
- Handler:
  - Distinct the ids.
  - Skip task meetings with `MeetingSeriesId == ExcludeMeetingSeriesId`.
  - After the per-employee event loop, append `availability.CheckAsync(...)` with the loaded employees and `callerEmployeeId` from `employees.GetDefaultForUserAsync(tenantId, currentUser.UserId)`.

- [ ] **Step 1: Write the failing tests:**
  - Unit `Duplicate_participant_ids_are_checked_once`.
  - Unit `Excluded_series_meetings_are_skipped`: projection returns an item with that `MeetingSeriesId` → no event row.
  - Unit `Availability_rows_are_appended_after_event_rows`.
  - Integration in `MeetingCalendarIntegrationTests`: `Check_conflicts_returns_leave_kind_without_leave_details`. Seed an approved leave; POST check-conflicts; assert `kind == "leave"`, title is `On leave` or `Busy`, and the raw JSON has no `reason` or leave type name.
- [ ] **Step 2: Run, expect failures.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run the unit suite for Calendar, then the integration test alone.** Integration: `dotnet test tests/ONEVO.Tests.Integration --filter FullyQualifiedName~MeetingCalendarIntegrationTests --artifacts-path artifacts/claude-test` (Docker must be running; run one integration suite at a time). Expected: pass.
- [ ] **Step 5: Commit** `feat(calendar): conflict check returns leave, holiday and working-hours warnings`.

### Task 4: FE models and the shared warnings list

**Files:**
- Modify: `src/app/modules/calendar/models/calendar-event.model.ts` (`CalendarConflict.kind?: CalendarConflictKind`; `CheckCalendarConflictsRequest` gains `isAllDay?`, `timeZone?`, `excludeMeetingSeriesId?`)
- Create: `src/app/modules/calendar/ui/availability-warnings/availability-warnings.component.ts` (+ `.spec.ts`)
- Modify: `src/app/modules/calendar/ui/calendar-event-form-modal/calendar-event-form-modal.component.ts` (render conflicts through the new component)

**Interfaces:**
- Produces: `export type CalendarConflictKind = 'event' | 'leave' | 'holiday' | 'outside_hours';`
- Produces: `<app-availability-warnings [conflicts]="..." [checking]="..." [repeating]="..." />` (selector `app-availability-warnings`). Inputs: `conflicts: readonly CalendarConflict[]`, `checking: boolean`, `repeating = false`.
- Rendering:
  - Groups by `employeeName`, with rows ordered leave, holiday, outside_hours, event.
  - A missing `kind` is treated as `event`.
  - Each row shows the title and the local overlap time.
  - Shows `Checking availability…` while checking, `Everyone is available` when empty and not checking, and `Checked for the first occurrence` when `repeating`.
  - Test ids: `availability-warnings`, `availability-row`, `availability-checking`, `availability-clear`, `availability-first-occurrence`.
  - Colours come only from theme tokens (`--color-warning-subtle`, `--color-warning-text`, `--color-text-secondary`).

- [ ] **Step 1: Write the failing specs:**
  - `groups_rows_by_person_in_kind_order`.
  - `treats_missing_kind_as_event`.
  - `shows_checking_then_clear_state`.
  - `shows_first_occurrence_note_when_repeating`.
  - Event form spec: `renders_leave_kind_label` (a conflict `{kind:'leave', conflictingEventTitle:'On leave'}` shows "On leave").
- [ ] **Step 2: Run** `npx ng test --watch=false --include='src/app/modules/calendar/**/*.spec.ts'`. Expected: FAIL.
- [ ] **Step 3: Implement the model changes and the component, then swap the event form's conflict markup for the component.**
- [ ] **Step 4: Run the same command. Expected: pass.**
- [ ] **Step 5: Commit** `feat(calendar): shared availability warnings list with leave, holiday and hours kinds`.

### Task 5: Meeting form check

**Files:**
- Modify: `src/app/modules/work/meetings/ui/meeting-form-modal/meeting-form-modal.component.ts` (+ `.spec.ts`)

**Interfaces:**
- Consumes:
  - `CalendarEventApiService.checkConflicts(payload)` (existing).
  - `AuthStore.employeeId()`.
  - Task 4 `app-availability-warnings`.
- Produces:
  - Signals `availability = signal<readonly CalendarConflict[]>([])` and `checkingAvailability = signal(false)`.
  - Method `scheduleAvailabilityCheck(): void`, debounced 400 ms.
- Behaviour:
  - Triggers on `form.valueChanges` (date, time, durationMinutes, isAllDay, timezone) and on participant changes (`participantIds` effect).
  - No request when the date is empty, or when not all-day and `durationMinutes` is invalid.
  - The ids sent are the distinct `participantIds()` plus `auth.employeeId()` when set.
  - `startDate`/`endDate` are built the same way `buildCreateRequest` builds `start` (reuse its helper). An all-day meeting sends the whole local day.
  - Sends `isAllDay`, `timeZone` (form value) and, in edit mode, `excludeMeetingSeriesId = occurrence().seriesId`.
  - A request error clears `availability`, sets `checkingAvailability(false)` and logs `console.warn`. No toast.
  - The component renders above the button row, with `[repeating]="form.controls.recurrence.value !== 'none'"`.
  - Not shown when `notesOnly()`.

- [ ] **Step 1: Write the failing specs:**
  - `checks_availability_debounced_with_organiser_included`: fake timers; set date/time/participants; advance 400 ms; expect one call whose `participantEmployeeIds` contains `e2` and the organiser id once.
  - `does_not_check_without_a_valid_time`: empty date → no call.
  - `edit_sends_series_to_exclude`.
  - `create_stays_enabled_with_warnings`: availability set to one leave row and the form valid → `meeting-save` not disabled.
  - `failed_check_does_not_block`: API error → no toast, Save enabled.
- [ ] **Step 2: Run** `npx ng test --watch=false --include='src/app/modules/work/meetings/**/*.spec.ts'`. Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run the meetings and calendar spec folders. Expected: pass.**
- [ ] **Step 5: Commit** `feat(meetings): show availability warnings while booking a meeting`.

### Task 6: Verify, push, merge locally

- [ ] **Step 1:** BE: `dotnet build` (no new warnings) and the full unit suite plus architecture tests. FE: `npm run check:warnings` and the meetings + calendar specs.
- [ ] **Step 2:** Push both branches: `git push -u origin feature/meeting-availability-warnings`.
- [ ] **Step 3:** Merge into local `development` / `Development` in the main clones, restart the backend and the tenant frontend, and open the meeting form to see the warnings live.
