# Meetings as Tasks — Phase 2 Plan B: Meeting Core (backend) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Organizers create, approve, read, edit, cancel, re-attend and move meetings that live as `task_kind = 'meeting'` work items, visible only to their organizer and attendees, with the 7-day occurrence generator running on `job_leases`.

**Architecture:**
- A `/api/v1/meetings` API over MediatR commands and queries.
- All placement and permission rules sit in two services:
  - `IMeetingPlacementResolver` (D4: direct, approval or 403);
  - `IMeetingAccessResolver` + `MeetingVisibility.ForCaller` (D6: organizer or attendee, or 404).
- All occurrence writes go through Plan A's `IMeetingOccurrenceWriter` under the series lock (lock → re-read → write).
- Splits and moves live in `MeetingSeriesSplitter`.
- **No email, link or attendance in this plan.** Plan C attaches them to the services built here. `Meetings:Create` stays off until Plan C lands.

**Tech Stack:** .NET 10, EF Core 10 + Npgsql (snake_case), MediatR, xUnit + Moq, Testcontainers (`MyTeamDb`, `MeetingSeed`), Ical.Net (existing).

**Spec:** `docs/superpowers/specs/2026-10-18-meeting-task-phase2-design.md` (§2.1 D2–D11, D13, D18–D20; §5.1–§5.3, §5.5; §7; §8). Plan index: `docs/superpowers/plans/2026-10-18-meeting-task-phase2-index.md`. Built on Plan A (`feature/meeting-task-phase2-a`).

## Global Constraints

- **404 means "does not exist or you may not see it", with one identical body.** This holds for every `/meetings/*` endpoint, and for a meeting id on any `/tasks/*`, sprint or board command (§5.1, §7).
- **No visibility bypass.** Organizer and attendees only. There is no bypass for admin `*`, tenant Owner, project lead, module owner, approver or HR (D6).
- **`/meetings/*` errors are ProblemDetails with a stable `code` extension.** Use `Result.Failure(message, status, code)`; the controller copies `ErrorCode` into `problem.Extensions["code"]` (the `MonitoringActivationController` pattern). Codes are exactly those in §7 (Task 1 lists them).
- **Logs carry ids only**, never titles, agendas or emails (§7).
- **Flags** (Plan A `MeetingsOptions`):
  - `Create` off → every write returns 503 `meetings_disabled`.
  - `ReadOnly` on → every write returns 409 `meetings_read_only`; reads still work.
  - `Background` gates the generator.
- **Writes and locks:**
  - Every write runs in one transaction.
  - The series lock is taken first (`IMeetingOccurrenceWriter.LockSeriesAsync`), the series is **re-read after** the lock, and only then is anything written.
  - Two series are locked smaller id first.
  - `SET LOCAL lock_timeout = '5s'` is set before taking locks; a 55P03 becomes 409 `meeting_busy`.
- **`MeetingOccurrenceRaceLostException`:** roll back and retry the whole transaction once.
- **Status is derived** from time and cancellation, never stored (D10). The values are `scheduled`, `in_progress`, `completed`, `cancelled`.
- **Recurrence** expands in the series' IANA `timezone`; at most 500 occurrences (§5.2).
- **ShortId** is `{Project.Identifier}-M{n}`, from a per-project counter separate from tasks (§4.1).
- **A meeting task's category and status:**
  - Category: the project's **Meeting** system category (`TaskCategorySystemKeys.Meeting`).
  - Status: the project's first **Active** status, as a placeholder. Status is derived; the placeholder never shows (I7).

## Review Focus

1. **A weekly meeting across a daylight-saving change** stays at the same local wall-clock time (09:00 Europe/London before and after 2026-10-25). Pinned in Task 4.
2. **An outsider with every role** (tenant Owner, admin `*`, project lead, module owner, HR, the approver after deciding, a peer) gets 404 on every meeting endpoint and sees no meeting in any list, metric or ShortId sequence. Pinned in Task 15.
3. **An approval decided after the first occurrence started, or applied twice,** becomes Stale with nothing created, or creates exactly one series. Pinned in Task 7.
4. **Two organizers' tabs editing the same occurrence:** the second save gets 409 `meeting_changed` instead of silently overwriting the first. Pinned in Task 9.
5. **A split or move of a series whose future occurrences have attendee overrides or action items:**
  - task ids stay the same;
  - surplus occurrences carrying overrides or action items become Cancelled, not deleted;
  - past occurrences keep their module.

  Pinned in Tasks 10 and 13.

---

## File structure

| Area | Files |
|---|---|
| Contract | `src/ONEVO.Application/Features/WorkManagement/Meetings/Contracts/*.cs` (request/response records, `MeetingErrorCodes`, `MeetingStatuses`); `docs/api/meetings-v1.md` (the frontend handoff) |
| Domain helpers | `Application/Features/WorkManagement/Meetings/Services/` (`MeetingStatusCalculator`, `MeetingRecurrence`, `EffectiveAttendees`, `MeetingAccessResolver`, `MeetingVisibility`, `MeetingPlacementResolver`, `MeetingSeriesFactory`, `MeetingSeriesSplitter`, `MeetingGuard`) |
| Commands / queries | `Application/Features/WorkManagement/Meetings/Commands/{CreateMeeting,EditMeeting,CancelMeeting,MoveMeeting,AddMeetingAttendees,RemoveMeetingAttendee,RespondToMeeting}/`, `Queries/{GetMeetingOccurrence,GetMeetingSeries,ListMeetings}/`; `Appliers/{MeetingCreateApplier,MeetingMoveApplier}.cs` |
| Persistence | `Application/Features/WorkManagement/Meetings/RepositoryInterfaces/IMeetingRepository.cs` + `Infrastructure/Persistence/Repositories/WorkManagement/Meetings/EfMeetingRepository.cs`; `IProjectRepository.IncrementAndGetNextMeetingNumberAsync`; migration `AddProjectMeetingNumber` |
| Jobs | `Infrastructure/Services/WorkManagement/Meetings/MeetingOccurrenceGeneratorJob.cs` |
| API | `src/ONEVO.Api/Controllers/Tenant/WorkManagement/MeetingsController.cs` |
| I7 | `DeleteTaskStatusCommandHandler.cs`, `TaskStatusTemplateChangeApplier.cs` (re-point meeting placeholders) |
| Tests | `tests/ONEVO.Tests.Unit/Features/WorkManagement/Meetings/*`; `tests/ONEVO.Tests.Integration/WorkManagement/Meetings/{MeetingAccess,MeetingCreate,MeetingApproval,MeetingEdit,MeetingSplit,MeetingCancel,MeetingAttendees,MeetingMove,MeetingGenerator,MeetingLeak,MeetingsApi}IntegrationTests.cs` |

---

### Task 1: The API contract (frontend handoff)

**Files:**
- Create: `Application/Features/WorkManagement/Meetings/Contracts/MeetingContracts.cs`
- Create: `Application/Features/WorkManagement/Meetings/Contracts/MeetingErrorCodes.cs`
- Create: `docs/api/meetings-v1.md`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Meetings/MeetingContractsTests.cs`

**Interfaces — Produces:**

All routes are under `api/v1/meetings` with `[Authorize(Policy = "TenantPolicy")]`. `{originalStart}` is an ISO-8601 UTC instant (`2026-11-02T09:00:00Z`). It addresses an occurrence, materialized or not, together with `{seriesId}`.

| Method + route | Body → response | Who |
|---|---|---|
| `POST /` | `CreateMeetingRequest` → 201 `MeetingCreatedResponse` | D4 (project), or `calendar:write` (Office) |
| `GET /?from&to&projectId?` | → `MeetingListItem[]`: the caller's visible materialized occurrences overlapping [from, to] | any; visibility-filtered |
| `GET /occurrences/{taskId}` | → `MeetingOccurrenceResponse` | attendee |
| `GET /series/{seriesId}` | → `MeetingSeriesResponse` | attendee |
| `PATCH /series/{seriesId}/occurrences/{originalStart}` | `EditMeetingRequest` → `MeetingEditResponse` | organizer |
| `POST /series/{seriesId}/occurrences/{originalStart}/cancel` | `CancelMeetingRequest` → 204 | organizer |
| `POST /series/{seriesId}/occurrences/{originalStart}/move` | `MoveMeetingRequest` → `MeetingMoveResponse` | organizer + destination D4 |
| `POST /series/{seriesId}/attendees` | `MeetingAttendeesRequest` → `MeetingAttendeesResponse` | organizer |
| `DELETE /series/{seriesId}/attendees/employees/{employeeId}` · `…/guests/{email}` | → 204 | organizer |
| `POST /series/{seriesId}/occurrences/{originalStart}/attendees` | `MeetingAttendeesRequest` → `MeetingAttendeesResponse` | organizer |
| `DELETE /series/{seriesId}/occurrences/{originalStart}/attendees/employees/{employeeId}` · `…/guests/{email}` | → 204 | organizer |
| `POST /series/{seriesId}/respond` | `RespondToMeetingRequest` → 204 | self (attendee) |

Records (`System.Text.Json` web defaults, camelCase; enums as strings):

```csharp
public sealed record CreateMeetingRequest(Guid? ProjectId, Guid? ObjectiveId, string Title, string? Description,
    string? Location, bool IsPrivate, DateTimeOffset Start, int DurationMinutes, bool IsAllDay, string Timezone,
    string? RecurrenceRule, IReadOnlyList<Guid> ParticipantEmployeeIds, IReadOnlyList<string> GuestEmails,
    string? Platform);                       // "microsoft_teams" | "zoom" | null; ProjectId null = Office meeting
public sealed record SkippedParticipant(Guid EmployeeId, string Reason);   // Reason: "inactive" | "not_found"
public sealed record MeetingCreatedResponse(string Outcome,               // "created" | "requested"
    Guid? SeriesId, Guid? FirstOccurrenceTaskId, Guid? ApprovalRequestId, IReadOnlyList<SkippedParticipant> Skipped);
public sealed record MeetingPersonRef(Guid EmployeeId, string Name);
public sealed record MeetingAttendeeView(string Kind, Guid? EmployeeId, string? Name, string? Email,
    string? Response, string? ResponseReason, string Source);           // Kind "employee"|"guest"; Source "series"|"occurrence"
public sealed record MeetingPlacementView(Guid ProjectId, string ProjectName, Guid ObjectiveId, string ModuleTitle, bool IsOffice);
public sealed record MeetingOccurrenceResponse(Guid TaskId, string ShortId, Guid SeriesId, DateTimeOffset OriginalStart,
    DateTimeOffset StartAt, DateTimeOffset EndAt, bool IsAllDay, string Timezone, string Title, string? Description,
    string? Location, bool IsPrivate, string Status, bool IsDetached, int Revision, int SeriesRevision,
    string? RecurrenceRule, MeetingPlacementView Placement, MeetingPersonRef Organizer, string MyRole,  // "organizer"|"attendee"
    string? MyResponse, IReadOnlyList<MeetingAttendeeView> Attendees, bool CanEdit);
public sealed record MeetingSeriesResponse(Guid SeriesId, string Title, string? Description, string? Location, bool IsPrivate,
    DateTimeOffset DtStart, int DurationMinutes, bool IsAllDay, string Timezone, string? RecurrenceRule, bool IsCancelled,
    int Revision, MeetingPlacementView Placement, MeetingPersonRef Organizer, string MyRole,
    IReadOnlyList<MeetingAttendeeView> Attendees, IReadOnlyList<MeetingListItem> UpcomingOccurrences);
public sealed record MeetingListItem(Guid TaskId, string ShortId, Guid SeriesId, DateTimeOffset OriginalStart,
    DateTimeOffset StartAt, DateTimeOffset EndAt, bool IsAllDay, string Title, string Status, bool IsPrivate,
    Guid ProjectId, Guid ObjectiveId, string MyRole, string? MyResponse);
public sealed record EditMeetingRequest(string Scope,                     // "this" | "following" | "all"
    int Revision, string? Title, string? Description, string? Location, bool? IsPrivate, DateTimeOffset? Start,
    int? DurationMinutes, bool? IsAllDay, string? Timezone, string? RecurrenceRule);
public sealed record MeetingEditResponse(Guid SeriesId, Guid OccurrenceTaskId, bool Split);
public sealed record CancelMeetingRequest(string Scope);                  // "this" | "following" | "all"
public sealed record MoveMeetingRequest(string Scope, Guid ProjectId, Guid ObjectiveId);  // "this" | "following"
public sealed record MeetingMoveResponse(string Outcome, Guid? SeriesId, Guid? ApprovalRequestId);   // "moved" | "requested"
public sealed record MeetingAttendeesRequest(IReadOnlyList<Guid> EmployeeIds, IReadOnlyList<string> GuestEmails);
public sealed record MeetingAttendeesResponse(int Added, IReadOnlyList<SkippedParticipant> Skipped);
public sealed record RespondToMeetingRequest(string Status, string? Reason, DateTimeOffset? OriginalStart); // "accepted"|"rejected"; OriginalStart for a per-occurrence add
```

`MeetingErrorCodes` holds one `public const string` per §7 code:
- `meeting_invalid_time`, `meeting_start_in_past`, `meeting_invalid_timezone`, `meeting_recurrence_invalid`;
- `meeting_module_member_required`, `meeting_module_unavailable`, `meeting_office_not_ready`, `meeting_provider_not_connected`;
- `meetings_disabled`, `meetings_read_only`, `bulk_invite_unavailable`;
- `approval_already_decided`, `meeting_approval_not_editable`;
- `meeting_changed`, `meeting_occurrence_started`, `meeting_scope_invalid`, `meeting_recurrence_empty`, `meeting_busy`;
- `meeting_nothing_to_move`, `meeting_same_module`, `meeting_not_found`.

`MeetingStatuses`: `Scheduled = "scheduled"`, `InProgress = "in_progress"`, `Completed = "completed"`, `Cancelled = "cancelled"`. `MeetingScopes`: `This`, `Following`, `All`.

`docs/api/meetings-v1.md` restates this table and the records as JSON examples. For each endpoint it lists the error codes it returns and the HTTP status of each (§7: 400 validation, 403 visible-but-forbidden, 404 not visible, 409 state/concurrency, 503 disabled). It marks `link` and `attendance` fields as "added by Plan C".

- [ ] **Step 1: Write the failing test.** `MeetingContractsTests.Records_RoundTripAsCamelCaseJson`: serialize a `MeetingOccurrenceResponse` and a `CreateMeetingRequest` with `JsonSerializerDefaults.Web`. Assert the property names `taskId`, `originalStart`, `myRole`, `participantEmployeeIds`, and that it deserializes back equal. `ErrorCodes_AreUniqueSnakeCase`: reflect every const and assert distinct values matching `^[a-z_]+$`.
- [ ] **Step 2: Run, expect FAIL** (types missing).
- [ ] **Step 3: Add the records, constants and `docs/api/meetings-v1.md`.**
- [ ] **Step 4: Run, expect PASS.**
- [ ] **Step 5: Commit** `feat(meetings): /meetings API contract and error codes (frontend handoff)`. **Push right after this task** so Plan E can start.

---

### Task 2: Meeting ShortId counter, Meeting category and placeholder status

**Files:**
- Modify: `Domain/Features/WorkManagement/Projects/Entities/Project.cs` (`public long NextMeetingNumber { get; set; } = 1;`)
- Modify: `Application/Features/WorkManagement/Projects/RepositoryInterfaces/IProjectRepository.cs` + `EfProjectRepository`: `Task<long> IncrementAndGetNextMeetingNumberAsync(Guid tenantId, Guid projectId, CancellationToken ct)`. Use the same atomic `UPDATE … RETURNING` as `IncrementAndGetNextTaskNumberAsync`.
- Create: migration `AddProjectMeetingNumber` (`projects.next_meeting_number bigint NOT NULL DEFAULT 1`)
- Create: `Application/Features/WorkManagement/Meetings/Services/MeetingTaskDefaults.cs`: `Task<Result<(Guid CategoryId, Guid StatusId)>> ResolveAsync(Guid tenantId, Guid projectId, CancellationToken ct)`.
  - Category: the project's `SystemKey == Meeting` category. If missing (a project older than the system category), insert it the way `DefaultTaskCategoryTemplate` does.
  - Status: the first `Active` status by `DisplayOrder`.
- Test: `tests/ONEVO.Tests.Integration/WorkManagement/Meetings/MeetingTaskDefaultsIntegrationTests.cs`

- [ ] **Step 1: Failing tests:**
  - `MeetingNumbers_AreSeparateFromTaskNumbers`: two meeting numbers are 1 and 2, and the next task number is still 1.
  - `MissingMeetingCategory_IsCreatedOnce`: resolved twice → one category row.
  - `PlaceholderStatus_IsTheFirstActiveStatus`.
- [ ] **Step 2: Run, expect FAIL.**
- [ ] **Step 3: Implement; generate the migration** with `ef.sh migrations add AddProjectMeetingNumber`; `has-pending-model-changes` → "No changes".
- [ ] **Step 4: Run, expect PASS.**
- [ ] **Step 5: Commit** `feat(meetings): per-project meeting number, Meeting category and placeholder status`.

---

### Task 3: Status, effective attendees, access and visibility

**Files:**
- Create: `Services/MeetingStatusCalculator.cs`: `static string Of(MeetingDetails d, MeetingSeries s, DateTimeOffset now)`.
  - `cancelled` if either side has `CancelledAt`.
  - `completed` if `now >= EndAt`.
  - `in_progress` if `now >= StartAt`.
  - else `scheduled`.
- Create: `Services/EffectiveAttendees.cs`: `static IReadOnlyList<MeetingAttendeeView> Compute(IReadOnlyList<CalendarEventParticipant> seriesParticipants, IReadOnlyList<CalendarEventGuest> seriesGuests, IReadOnlyList<MeetingOccurrenceAttendee> overrides)`.
  - Result: series minus `remove` plus `add`.
  - The newest `CreatedAt` override per person wins.
  - Guests are matched by `lower(email)`.
  - Soft-deleted participants are excluded.
- Create: `Services/IMeetingAccessResolver.cs` + `MeetingAccessResolver.cs`.
  - `Task<MeetingAccess?> ResolveForOccurrenceAsync(Guid tenantId, Guid callerEmployeeId, Guid seriesId, DateTimeOffset originalStart, CancellationToken ct)` and `…ForSeriesAsync(tenantId, callerEmployeeId, seriesId, ct)`.
  - `MeetingAccess(string Role, MeetingSeries Series)` with Role `organizer` or `attendee`. Null means 404.
  - An occurrence is visible to the organizer; to a series participant not removed for that occurrence; or to an occurrence `add`.
  - A series is visible to the organizer and to series participants or guests' employee matches.
  - **No role, permission or tenant-owner check exists anywhere in this resolver.**
- Create: `Services/MeetingVisibility.cs`: `static IQueryable<MeetingDetails> ForCaller(IQueryable<MeetingDetails> details, ApplicationDbContext db, Guid callerEmployeeId)`. One EF expression: organizer of the series, OR series participant (not soft-deleted) with no `remove` override for that occurrence, OR an `add` override. It lives in Infrastructure (`Persistence/Repositories/WorkManagement/Meetings/MeetingVisibility.cs`) and is the only file allowed to `IgnoreQueryFilters([WorkTaskQueryFilters.ExcludeMeetings])` besides `EfMeetingRepository`. Add both to `ExcludeMeetingsIgnoreAllowList`.
- Test: unit `MeetingStatusCalculatorTests`, `EffectiveAttendeesTests`; integration `MeetingAccessIntegrationTests`.

- [ ] **Step 1: Failing tests:**
  - Status table: 5 rows (scheduled, in progress at start, completed at end, cancelled occurrence, cancelled series beats time).
  - `EffectiveAttendees`: series ± overrides; last instruction wins; guest case-insensitive.
  - Access integration. Seed with `MeetingSeed`: organizer O, series attendee A, attendee R removed for occurrence 2, occurrence-add X on occurrence 1. Outsiders: tenant Owner (role Owner + admin `*`), project lead, module owner, HR (`calendar:write` + `employees:*`), peer.
  - Assert the role per caller per occurrence. `MeetingVisibility.ForCaller` returns exactly the visible occurrence ids per caller, and none for each outsider.
- [ ] **Step 2: Run, expect FAIL.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run (unit + `MeetingAccessIntegrationTests` + architecture), expect PASS.**
- [ ] **Step 5: Commit** `feat(meetings): derived status, effective attendees, single access resolver and visibility expression`.

---

### Task 4: Timezone-aware recurrence and create validation

**Files:**
- Create: `Application/Features/WorkManagement/Meetings/Services/IMeetingRecurrence.cs` + `Infrastructure/Services/WorkManagement/Meetings/IcalNetMeetingRecurrence.cs`.
  - `IReadOnlyList<DateTimeOffset> Occurrences(string? rule, DateTimeOffset dtStart, string timezone, DateTimeOffset from, DateTimeOffset to)`: a null rule gives `[dtStart]` when it lies in range.
  - `Result Validate(string? rule, DateTimeOffset dtStart, string timezone)`: unparsable, or more than 500 occurrences when unbounded → `meeting_recurrence_invalid`; unknown tz → `meeting_invalid_timezone`.
  - Expand with Ical.Net `CalDateTime(localWallTime, tzId)` so the wall time stays fixed across DST. Return UTC instants.
- Create: `Services/MeetingCreateValidator.cs`: `Result Validate(CreateMeetingRequest r, DateTimeOffset now)`.
  - `DurationMinutes > 0`, else `meeting_invalid_time`.
  - `Start > now`, else `meeting_start_in_past`.
  - The timezone and rule rules above.
  - Title 1–500 characters.
  - Guest emails valid, de-duplicated by `lower()`.
- Test: unit `MeetingRecurrenceTests`, `MeetingCreateValidatorTests`.

- [ ] **Step 1: Failing tests:**
  - `Weekly_StaysAtNineLocal_AcrossTheLondonDstChange`. Rule `FREQ=WEEKLY;COUNT=3`, dtStart 2026-10-19 09:00 Europe/London (08:00Z). Expected 08:00Z, 08:00Z, 09:00Z: Oct 19 and 26 → BST ends Oct 25, so Oct 26 is 09:00Z. Compute the expected list with `TimeZoneInfo.FindSystemTimeZoneById("Europe/London")` in the test.
  - `OneOff_ReturnsItsStartOnlyInsideTheRange`.
  - `Over500Occurrences_IsInvalid` (`FREQ=MINUTELY`).
  - `UnknownTimezone_IsInvalid`.
  - Validator: one test per code, plus `GuestEmails_AreDeduplicatedCaseInsensitively`.
- [ ] **Step 2: Run, expect FAIL.**
- [ ] **Step 3: Implement**; register `IMeetingRecurrence` as a singleton.
- [ ] **Step 4: Run, expect PASS.**
- [ ] **Step 5: Commit** `feat(meetings): timezone-aware recurrence and create validation`.

---

### Task 5: Placement resolver (D4) and guards

**Files:**
- Create: `Services/IMeetingPlacementResolver.cs` + `MeetingPlacementResolver.cs`: `Task<Result<MeetingPlacement>> ResolveAsync(Guid tenantId, Guid callerEmployeeId, Guid? projectId, Guid? objectiveId, CancellationToken ct)`.
  - `MeetingPlacement(Guid ProjectId, Guid ObjectiveId, bool IsOffice, bool Direct)`.
  - **Project path:**
    - Module missing, inactive or achieved → 400 `meeting_module_unavailable`.
    - `IWorkHierarchyService.LoadTreeAsync` + `tree.IsAtOrAbove(caller, objectiveId)` → `Direct = true`.
    - Else `IMilestoneMembershipCoordinator.HasActiveMembershipAsync` → `Direct = false` (an approval).
    - Else 403 `meeting_module_member_required`.
  - **Office path** (`projectId == null`):
    - Caller lacks `calendar:write` (`ICurrentUser.HasPermission`) → 403.
    - No Office project → 409 `meeting_office_not_ready` and fire `IOfficeProjectReconciler.ReconcileAsync(tenantId)` (fire-and-forget through `IBackgroundTaskQueue` if the codebase has one, else awaited).
    - Else the Office root module, `Direct = true`.
- Create: `Services/MeetingGuard.cs`: `Result? CheckWritable(MeetingsOptions o)`. `!Create` → 503 `meetings_disabled`; `ReadOnly` → 409 `meetings_read_only`; else null. Also `static Result FromLockTimeout()` → 409 `meeting_busy`.
- Test: integration `MeetingPlacementIntegrationTests`; unit `MeetingGuardTests`.

- [ ] **Step 1: Failing tests:**
  - Owner of the module → Direct.
  - Owner of an ancestor module → Direct.
  - Plain member → approval.
  - Non-member → 403 `meeting_module_member_required`.
  - Inactive module → `meeting_module_unavailable`.
  - Office with `calendar:write` → Direct at the Office root.
  - Office without it → 403.
  - No Office project → `meeting_office_not_ready`.
  - Guard: the three flag states.
- [ ] **Step 2–4:** FAIL → implement → PASS.
- [ ] **Step 5: Commit** `feat(meetings): D4 placement rules and write guards`.

---

### Task 6: Direct create (series + attendees + first occurrences)

**Files:**
- Create: `Services/IMeetingSeriesFactory.cs` + `MeetingSeriesFactory.cs`: `Task<Result<MeetingCreatedResponse>> CreateDirectAsync(Guid tenantId, Guid organizerEmployeeId, Guid organizerUserId, MeetingPlacement placement, CreateMeetingRequest request, string creationPath, Guid? approvalRequestId, CancellationToken ct)`. It runs inside the caller's transaction, in this order:
  1. Insert the series (Revision 0).
  2. Lock it and re-read it.
  3. Participants: the organizer plus `ParticipantEmployeeIds`, de-duplicated and active only; inactive or unknown go to `Skipped`. Insert them as `CalendarEventParticipant { MeetingSeriesId, EventId = null }`. The organizer is `accepted`; others are `pending`.
  4. Guests: insert as `CalendarEventGuest { MeetingSeriesId }`.
  5. Occurrences: one for a one-off, else every occurrence in [now, now + 7 days] (and always the first). Each via `IMeetingOccurrenceWriter.TryMaterializeAsync` with:
     - `ShortId = $"{identifier}-M{IncrementAndGetNextMeetingNumberAsync}"`;
     - category and placeholder from Task 2;
     - title, description, location and privacy from the series;
     - `CreatedById` = the organizer.
  6. Return `created` with the first occurrence's task id.
- Create: `Commands/CreateMeeting/CreateMeetingCommand.cs` (+ handler): `CreateMeetingCommand(CreateMeetingRequest Request) : IRequest<Result<MeetingCreatedResponse>>`. Steps: guard → resolve caller → validate (Task 4) → placement (Task 5). Then:
  - **Direct:** `IUnitOfWork.ExecuteInTransactionAsync` with lock_timeout 5 s → factory. Retry once on `MeetingOccurrenceRaceLostException`; 55P03 → `meeting_busy`.
  - **Approval:** Task 7.
- Create: `RepositoryInterfaces/IMeetingRepository.cs` + `EfMeetingRepository.cs`: `GetSeriesTrackedAsync`, `GetSeriesAsync`, `GetDetailsAsync(tenantId, seriesId, originalStart)`, `GetDetailsByTaskAsync`, `ListSeriesParticipantsAsync`, `ListSeriesGuestsAsync`, `ListOverridesAsync(taskIds)`, `AddSeries`, `AddOverride`. Each method's reads use `IgnoreQueryFilters([WorkTaskQueryFilters.ExcludeMeetings])` only where they join `tasks`.
- Test: integration `MeetingCreateIntegrationTests`.

- [ ] **Step 1: Failing tests:**
  - `OneOff_CreatesOneSeriesOneOccurrenceAndItsAttendees`: the task kind is meeting, ShortId `{id}-M1`, the category is Meeting, the organizer is accepted.
  - `Weekly_MaterializesTheNextSevenDaysOnly`: daily rule → 7 or 8 occurrences inside the window, none beyond.
  - `InactiveParticipants_AreSkippedAndReported`.
  - `CreatesNothing_WhenDisabled` (503) and `…ReadOnly` (409).
  - `OrdinaryTaskList_DoesNotShowTheMeeting`: the existing tasks list query returns only standard tasks.
- [ ] **Step 2–4:** FAIL → implement → PASS.
- [ ] **Step 5: Commit** `feat(meetings): direct create - series, attendees and the first occurrences`.

---

### Task 7: `meeting.create` approval (request, apply, Stale, idempotency)

**Files:**
- Modify: `Domain/Features/WorkManagement/Approvals/Entities/WorkApprovalRequest.cs`. Add `WorkActionTypes.MeetingCreate = "meeting.create"` and `MeetingMove = "meeting.move"` (Task 13), and `WorkTargetTypes.Meeting = "meeting"`.
- Create: `Appliers/MeetingCreateApplier.cs : IApprovalActionApplier` (`ActionType = MeetingCreate`). It deserializes `MeetingCreatePayload(CreateMeetingRequest Form, MeetingApprovalSnapshot Snapshot)`. Then:
  - First start ≤ now, or the requester is no longer an active member of the module → `ApplyOutcome.Stale`.
  - Else `MeetingSeriesFactory.CreateDirectAsync(..., creationPath: Approval, approvalRequestId: request.Id)` → `request.TargetId = seriesId` → `Applied()`.
  - A unique violation on `ux_meeting_series_approval_request_id` → `Applied()` (already applied).
- Create: `Contracts/MeetingApprovalSnapshot.cs` (D5): `Title, Description, Start, DurationMinutes, Timezone, RecurrenceSummary, OrganizerName, ModulePath, ParticipantNames[], GuestEmails[], Platform`.
- Modify: `CreateMeetingCommandHandler`. The approval path builds the snapshot and calls `IWorkApprovalEngine.SubmitAsync(new WorkAction(tenant, projectId, caller, MeetingCreate, Meeting, TargetId: null, title, objectiveId, PositionModuleId: null, payloadJson, null))` → `requested`. Nothing else is written.
- Modify: `GetApprovalDetailQueryHandler`: `CanEditPayload = false` for `meeting.*`. Modify: the payload-edit and revert command handlers → 409 `meeting_approval_not_editable` for `meeting.*`. Register the applier.
- Test: integration `MeetingApprovalIntegrationTests`.

- [ ] **Step 1: Failing tests:**
  - `MemberRequest_WritesOnlyTheApprovalRequest`: 0 series, 0 meeting tasks.
  - `Approve_CreatesTheMeeting_WithTheRequesterAsOrganizer`: `creation_path = approval`.
  - `ApproveAfterTheStartPassed_IsStale_AndCreatesNothing`.
  - `ApproveAfterTheRequesterLeftTheModule_IsStale`.
  - `ApplyingTwice_CreatesOneSeries`: call the applier twice.
  - `ApprovalDetail_IsNotEditable_AndShowsOnlyTheSnapshot`.
  - `Revert_Returns409`.
- [ ] **Step 2–4:** FAIL → implement → PASS.
- [ ] **Step 5: Commit** `feat(meetings): meeting.create approval with Stale and idempotent apply`.

---

### Task 8: Read endpoints (occurrence, series, list)

**Files:**
- Create: `Queries/GetMeetingOccurrence/*`.
  - `GetMeetingOccurrenceQuery(Guid TaskId)`: resolve the details by task, then `ResolveForOccurrenceAsync`. Null → 404 `meeting_not_found`.
  - `GetMeetingOccurrenceAtQuery(Guid SeriesId, DateTimeOffset OriginalStart)`: same, for a virtual occurrence. It returns the series template values and `TaskId = MeetingOccurrenceIds.For(seriesId, originalStart)`. It only applies when the slot is in the series' expansion and not suppressed; else 404.
- Create: `Queries/GetMeetingSeries/*`; `Queries/ListMeetings/*` (`MeetingVisibility.ForCaller` ∩ [from, to] ∩ optional project; ordered by `StartAt`).
- `CanEdit` = role is organizer AND status is not completed or cancelled (D19 notes are via Task 9).
- Test: integration `MeetingReadIntegrationTests`.

- [ ] **Step 1: Failing tests:**
  - The organizer and an attendee read everything.
  - An outsider gets 404 with the same body as an unknown id.
  - A virtual occurrence at the 3rd week returns the deterministic id.
  - A suppressed slot → 404.
  - The list returns only visible occurrences in range, with `myRole` and `myResponse`.
- [ ] **Step 2–4:** FAIL → implement → PASS.
- [ ] **Step 5: Commit** `feat(meetings): occurrence, series and list reads through one visibility rule`.

---

### Task 9: Edit (this / all non-key / notes after the meeting)

**Files:**
- Create: `Commands/EditMeeting/EditMeetingCommand.cs` (+ handler): `EditMeetingCommand(Guid SeriesId, DateTimeOffset OriginalStart, EditMeetingRequest Edit)` → `Result<MeetingEditResponse>`.
  - **Order:** guard → organizer check (non-organizer attendee 403, outsider 404) → transaction + lock + re-read.
  - **Scope `this`:**
    1. Materialize if needed.
    2. `Revision` ≠ occurrence revision → 409 `meeting_changed`.
    3. Started or past: any field except `Description` → 409 `meeting_occurrence_started`. A Description-only edit is allowed (D19) and audited with a `TaskEditLog` row (`field = "description"`, source `meeting_notes`).
    4. Otherwise apply to `tasks` (title, description) and `meeting_details` (time, location, privacy); set `IsDetached = true`; Revision + 1.
  - **Scope `all`:**
    - `Revision` ≠ series revision → `meeting_changed`.
    - Non-key fields only (title, description, location, privacy) → update the series and every future, not-started, non-detached occurrence in the same transaction; series Revision + 1.
    - A time or rule change → Task 10 (internal split).
  - **Scope `following`** → Task 10.
  - Unknown scope → 400 `meeting_scope_invalid`.
- Test: integration `MeetingEditIntegrationTests`.

- [ ] **Step 1: Failing tests:**
  - `EditThis_DetachesOnlyThatOccurrence`.
  - `EditAll_Title_UpdatesFutureNonDetached_LeavesDetachedAndPast`.
  - `StaleRevision_Is409MeetingChanged` (Review Focus 4: two edits with the same revision → second 409).
  - `StartedOccurrence_RejectsTimeEdit_AllowsNotesWithAuditRow`.
  - `Attendee_Gets403_Outsider_Gets404`.
- [ ] **Step 2–4:** FAIL → implement → PASS.
- [ ] **Step 5: Commit** `feat(meetings): edit one occurrence or the whole series; notes after the meeting`.

---

### Task 10: Forward splits (edit following, time/rule edit of all)

**Files:**
- Create: `Services/IMeetingSeriesSplitter.cs` + `MeetingSeriesSplitter.cs`: `Task<Result<MeetingSeries>> SplitAsync(Guid tenantId, MeetingSeries source, DateTimeOffset splitAt, string reason, SeriesTemplate newTemplate, MeetingPlacement? newPlacement, CancellationToken ct)`. `SeriesTemplate(Title, Description, Location, IsPrivate, DtStart, DurationMinutes, IsAllDay, Timezone, RecurrenceRule)`. It runs in the caller's transaction, and the caller holds both series locks, smaller id first. Steps (spec §5.5):
  1. The source rule gets `UNTIL = splitAt − 1 s` (append or replace `UNTIL`, drop `COUNT` after recomputing it); its past occurrences are untouched.
  2. Create the target series: `SplitFromSeriesId`, `SplitReason`, `SplitAt`.
  3. Copy the series participants and guests. RSVP is reset to pending when DtStart's time-of-day or the duration changed (development's split rule), else copied.
  4. List the source's materialized occurrences with `OriginalStart >= splitAt`, in time order. Expand the target from its DtStart for the same count window: [splitAt, max(last materialized, now + 7 days)].
  5. Map by index:
     - the first N new keys → `IMeetingOccurrenceWriter.RemapIntoSeriesAsync` (ids kept);
     - surplus old occurrences → `CancelledAt = now` if they have overrides, action items or attendance rows, else soft-delete the task (and keep the details);
     - missing new keys → `TryMaterializeAsync`.
  6. Copy suppressions with `OriginalStart >= splitAt` through the same index mapping.
  7. A new placement (move) also updates `tasks.project_id` / `objective_id` of the remapped tasks and writes a `TaskEditLog` `objective` row per task.

  Plan C extends `SplitAsync` itself with the inherited link reference (no provider call, no email); Plan B adds no hook or placeholder for it.
- Modify: `EditMeetingCommandHandler`:
  - `following` → split at this occurrence's `OriginalStart` (`edit_following`).
  - `all` with a time/rule change → split at the first not-started occurrence. If none → 409 `meeting_recurrence_empty`.
  - Rule-only change producing no occurrence after P → `meeting_recurrence_empty`.
- Test: unit `MeetingSplitMappingTests` (the index mapping as a pure function `SplitPlan Plan(IReadOnlyList<DateTimeOffset> oldKeys, IReadOnlyList<DateTimeOffset> newKeys)`); integration `MeetingSplitIntegrationTests`.

- [ ] **Step 1: Failing tests:**
  - Unit: equal counts; more old than new (surplus); fewer old than new (missing); time order kept.
  - Integration:
    - `EditFollowing_KeepsTaskIds_AndPastOccurrencesStayOnTheOldSeries`.
    - `TimeEditOfAll_SplitsAtTheFirstNotStarted`.
    - `SurplusWithAnOverride_IsCancelled_PlainSurplusIsSoftDeleted` (Review Focus 5).
    - `Suppressions_AreCopiedAfterThePoint`.
    - `RsvpResets_OnlyWhenTimeOrLengthChanges`.
- [ ] **Step 2–4:** FAIL → implement → PASS.
- [ ] **Step 5: Commit** `feat(meetings): forward splits that keep occurrence ids`.

---

### Task 11: Cancel (one / following / series)

**Files:**
- Create: `Commands/CancelMeeting/*`: `CancelMeetingCommand(Guid SeriesId, DateTimeOffset OriginalStart, string Scope)` → `Result`. Organizer only.
  - **`this`:** materialize if needed, then set `CancelledAt`. A started or past occurrence → 409 `meeting_occurrence_started`.
  - **`following`:** `CancelledAt` on every materialized occurrence ≥ P, plus `UNTIL = P − 1 s` on the series.
  - **`all`:** `meeting_series.CancelledAt` plus every future occurrence.
  - It is idempotent: an already-cancelled scope → 204.
- Test: integration `MeetingCancelIntegrationTests`: three scopes, idempotency, past → 409, the generator never re-materializes a cancelled series (asserted in Task 14).
- [ ] Steps 1–5 as above. Commit `feat(meetings): cancel one, following or the whole series`.

---

### Task 12: Attendees and RSVP

**Files:**
- Create: `Commands/AddMeetingAttendees/*`, `Commands/RemoveMeetingAttendee/*`, `Commands/RespondToMeeting/*`.
  - **Series add:** active employees only; skipped people are reported. Insert the participant, or revive a soft-deleted one the way `GetRemovedParticipantsAsync` does. Delete that person's future `remove` overrides.
  - **Series remove:** soft-delete the participant (the organizer cannot be removed → 400) and delete their future `add` overrides. Access is revoked at once.
  - **Per-occurrence add/remove:** materialize, then insert a `MeetingOccurrenceAttendee` override (`Change = add|remove`).
  - **Respond:**
    - A series participant sets `ResponseStatus`, `ResponseReason`, `RespondedAt` and writes `CalendarEventParticipantRsvpHistory` (`EventId = seriesId`).
    - With `OriginalStart` and an occurrence `add`, it updates that override's response (`EventId` = occurrence task id).
    - Declining keeps access (D18).
- Test: integration `MeetingAttendeesIntegrationTests`:
  - add/remove series;
  - an override cleared by a series add;
  - an occurrence add sees only that occurrence;
  - decline keeps access;
  - a removed person → 404;
  - RSVP history row written.
- [ ] Steps 1–5. Commit `feat(meetings): series and per-occurrence attendees, RSVP`.

---

### Task 13: Moves (forward series move, single occurrence move) + `meeting.move` approval

**Files:**
- Create: `Commands/MoveMeeting/*`: `MoveMeetingCommand(Guid SeriesId, DateTimeOffset OriginalStart, MoveMeetingRequest Move)` → `Result<MeetingMoveResponse>`.
  - Same module → 400 `meeting_same_module`.
  - The destination goes through Task 5's resolver: Direct → apply now; member → `meeting.move` approval request; non-member → 403.
  - **`following`** (`move_forward`): P = the first not-started occurrence, computed when the move is applied. None → 409 `meeting_nothing_to_move`. Then split with `newPlacement` (Task 10). Historical occurrences keep their module.
  - **`this`** (`move_single`, not started only):
    1. Create a one-off series at the destination.
    2. Remap the occurrence into it with the same task id.
    3. Write `MeetingOccurrenceSuppression(source, OriginalStart, Reason = moved_single, MovedToTaskId)`.
    4. Update the task's placement and write a `TaskEditLog` row.
- Create: `Appliers/MeetingMoveApplier.cs`. Re-checks at apply time; nothing upcoming → Stale.
- Test: integration `MeetingMoveIntegrationTests`:
  - forward move keeps history;
  - a single move writes a suppression and keeps the id;
  - the generator never recreates a suppressed slot;
  - a member's move becomes a request;
  - same module → 400;
  - nothing upcoming → 409.
- [ ] Steps 1–5. Commit `feat(meetings): forward and single moves with suppressions; meeting.move approval`.

---

### Task 14: The 7-day occurrence generator job

**Files:**
- Create: `Infrastructure/Services/WorkManagement/Meetings/MeetingOccurrenceGeneratorJob.cs` (`BackgroundService`, hourly; does nothing unless `Meetings:Background`).
  - Each run: `IJobLeaseService.TryAcquireAsync("meetings.occurrence-generator", 10 min)`.
  - For each tenant (`ITenantContextSwitcher`, the existing job pattern), for each recurring, non-cancelled series with occurrences due in [now, now + 7 days]:
    1. Open a transaction.
    2. Lock the series and **re-read it**.
    3. Expand it.
    4. `TryMaterializeAsync` for each missing key (suppressed or existing keys do nothing).
    5. Commit.
  - Between series it calls `RenewAsync`. False → stop the run (spec §7 Jobs).
  - One series failing is logged (ids only) and does not stop the others.
  - No emails.
- Test: integration `MeetingGeneratorIntegrationTests`:
  - `FillsTheNextSevenDays_Idempotently` (run twice → same rows).
  - `SkipsCancelledSeriesAndSuppressedSlots`.
  - `AStaleReadCannotResurrectASplitSlot`: split the series between the generator's tenant scan and its per-series step; the re-read after the lock sees UNTIL and inserts nothing.
  - `LostLease_StopsTheRun`.
- [ ] Steps 1–5. Commit `feat(meetings): hourly 7-day occurrence generator on job_leases`.

---

### Task 15: Placeholder statuses (I7), API controller, leak suite

**Files:**
- Modify: `DeleteTaskStatusCommandHandler` and `TaskStatusTemplateChangeApplier`. Before the "any task uses this status" check, re-point every meeting task on the removed status to the project's first remaining Active status: `IMeetingRepository.RepointPlaceholderStatusAsync(tenantId, projectId, fromStatusId, toStatusId, ct)`, a raw `UPDATE tasks … WHERE task_kind = 'meeting'`. Add `EfMeetingRepository.cs` to `RawSqlOnTasksAllowList`. The user then sees only real tasks in the count and message.
- Create: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/MeetingsController.cs`. The routes are exactly Task 1's table. Every failure maps through one `Error(Result)` helper that sets `problem.Extensions["code"]`.
- Test:
  - Integration `MeetingsApiIntegrationTests` (WebApplicationFactory, the existing API-test harness): one happy path per endpoint, plus the `code` extension present on a 409.
  - `MeetingLeakIntegrationTests`. One test per §5.1 surface, as an outsider with every role (Review Focus 2):
    - task list;
    - task metrics or progress counts (`LedWorkProgress` query);
    - activity feed / `TaskEditLog` read endpoint;
    - task search;
    - exports, if an export endpoint exists;
    - `GET /tasks/{meetingTaskId}` → 404;
    - ShortId numbering of ordinary tasks unaffected;
    - approval feed after the decision shows the snapshot only;
    - an error body identical for unknown and invisible ids.
  - `StatusUsedOnlyByMeetings_CanBeDeleted_AndMeetingsMoveToAnotherPlaceholder`.
- [ ] Steps 1–5. Commit `feat(meetings): /meetings controller, placeholder-status re-pointing, leak suite`.

---

### Task 16: Plan B verification and push

- [ ] Unit, Architecture, Integration all pass, except the 2 known My Team failures. Write the counts in the commit message.
- [ ] `ef.sh migrations has-pending-model-changes` → "No changes".
- [ ] Update `docs/api/meetings-v1.md` if any shape changed during Tasks 2–15, and `docs/runbooks/meetings-phase2-release.md`: Release B needs Plans B + C before `Meetings:Create` is turned on.
- [ ] Commit `docs(meetings): Plan B verification` and push `feature/meeting-task-phase2-b` (no PR).
