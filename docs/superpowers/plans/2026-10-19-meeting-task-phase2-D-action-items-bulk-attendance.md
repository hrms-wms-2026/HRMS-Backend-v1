# Meetings as Tasks Phase 2 — Plan D: Action Items, Bulk Invite, Attendance Views

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship the rest of the Phase 2 backend: action items raised from a meeting occurrence (D14), bulk invite by department or by a manager's direct reports (D8), the reporting-line attendance view (D16), and private-meeting masking in that view and in monitoring compliance (D17).

**Architecture:**
- Every write goes through what already exists:
  - action items through `CreateTaskCommandHandler` / `TaskCreateApplier` / `TaskWriteService`, with one optional payload field;
  - bulk invite expands to explicit employee ids, which the existing create path (`CreateMeetingRequest.ParticipantEmployeeIds`) and its active-person re-validation consume.
- The reads reuse the Plan C attendance rows (`calendar_event_meeting_attendances` with `occurrence_task_id`) and `EffectiveAttendees`.
- Visibility stays in `IMeetingAccessResolver` for meetings, `ITaskAccessResolver` for tasks, and `IEmployeeHierarchyClosureRepository.GetAncestorChainEmployeeIdsAsync` for the reporting line.

**Tech Stack:** .NET 10, EF Core + Npgsql, MediatR, xUnit + Moq (unit), Testcontainers PostgreSQL (integration, no Moq: hand-written stubs).

**Spec:** `docs/superpowers/specs/2026-10-18-meeting-task-phase2-design.md` — D8, D14, D16, D17, §5.1 (endpoint matrix, leak coverage), §5.2 "Bulk invite", §5.4, §7, §10.

**Base:** `feature/meeting-task-phase2-c` (Plan C, final-review fixes included). New branch `feature/meeting-task-phase2-d`, worktree `C:\Users\User\onexso-worktrees\be-phase2-d`.

## Global Constraints

- **Flags.**
  - Every write is behind the Plan B flags: `MeetingGuard.CheckWritable` gives 503 `meetings_disabled` (Create off) or 409 `meetings_read_only`.
  - `Meetings:BulkInvite` off: the bulk preview returns 409 `bulk_invite_unavailable`. The flag already refuses to start without P-1 (`MeetingsStartupCheck`).
- **404 not 403** for anything the caller may not see. One body: `MeetingReadService.NotFound<T>()` (`meeting_not_found`). Every failure carries the `code` extension.
- **D8:**
  - Groups are a department, or a manager's team, meaning that manager's **direct reports** (`GetDirectReportEmployeeIdsAsync`). There is **no "everyone" option**.
  - The preview expands groups into a snapshot. Duplicates are removed and inactive employees are excluded.
  - Counts: `selected`, `duplicatesRemoved`, `inactiveExcluded`, `alreadyInvited`.
  - **No participant cap** and no new permission. The gate is the same as create.
  - Above `Meetings:ProviderCapacityWarning:{MicrosoftTeams|Zoom}` (defaults 1000 / 300) the organizer gets a warning only.
- **D14:**
  - An action item is a normal task in **any module the creator is a member of** (default: the meeting's module), under that module's existing create rules.
  - The link is the optional `sourceMeetingOccurrenceId` on `task.create` (null for every ordinary task).
  - A viewer sees only the linked tasks they may see under the task rules, with no count leak.
  - The link creator or the organizer can unlink.
- **D16:**
  - The reporting-line view: a participant's **current** reporting-line ancestors see that participant's attendance summary only.
  - The participant sees their own.
  - Guests are never in it.
- **D17:** in the manager attendance view and in monitoring compliance, a private meeting shows as **"Private meeting"**. Its title, organizer, agenda, participant list and link are hidden.
- **Attendance wording:** "Unknown" for pending, unavailable and failed; "No join recorded" only when synced. **Never "Absent".**
- **Emails:** `Email:MaxPerSecond` (default `0` = unlimited) paces calendar invite emails.
- **Logs** carry ids only.

## Review Focus

1. **An action item in a module the viewer cannot see** must not appear in the occurrence's action-item list or in any count. Owned by Task 1: `ActionItems_HiddenTaskIsNotListedOrCounted`.
2. **An approved `task.create` whose meeting occurrence was cancelled or removed meanwhile** still creates the task but writes no link, and the apply does not fail. Owned by Task 1: `ApprovedRequest_ForARemovedOccurrence_CreatesTheTaskWithoutALink`.
3. **A manager who is no longer in the line** (the reporting manager changed) must get 404, not the old team's attendance. Owned by Task 4: `FormerManager_Gets404`.
4. **A bulk group that contains the organizer, or people already invited to the series,** counts them as `alreadyInvited` and never adds them twice. Owned by Task 2: `Preview_CountsAlreadyInvited_IncludingTheOrganizer`.
5. **A private task meeting in monitoring compliance** keeps its usage numbers but masks its title. A legacy private calendar event is masked the same way. Owned by Task 5: `Compliance_PrivateMeetingsAreMasked_BothKinds`.

---

## File Structure

| File | Responsibility |
|---|---|
| `Application/Features/WorkManagement/Tasks/DTOs/TaskWriteInputs.cs` | `TaskCreateInput` + `Guid? SourceMeetingOccurrenceId = null` |
| `Application/Features/WorkManagement/Meetings/RepositoryInterfaces/IMeetingActionItemRepository.cs` + `Infrastructure/.../Meetings/EfMeetingActionItemRepository.cs` | `meeting_action_items` rows |
| `Application/Features/WorkManagement/Meetings/Services/MeetingActionItemService.cs` | Validate the source, link, list (task-visibility filtered), unlink |
| `Application/Features/WorkManagement/Meetings/Services/MeetingBulkInviteService.cs` | Group expansion and counts (D8) |
| `Application/Common/ServiceInterfaces/IEmailRateLimiter.cs` + `Infrastructure/ExternalServices/Email/TokenBucketEmailRateLimiter.cs` | `Email:MaxPerSecond` |
| `Application/Features/WorkManagement/Meetings/Services/EmployeeMeetingAttendanceService.cs` | D16/D17 view |
| `Api/Controllers/Tenant/WorkManagement/EmployeeMeetingAttendanceController.cs` | `GET api/v1/employees/{employeeId}/meeting-attendance` |
| `Infrastructure/Persistence/Repositories/Monitoring/Meetings/EfMeetingComplianceReadRepository.cs` | + task meetings, + private masking |

---

### Task 1: Action items (link on create, list, unlink)

**Files:**
- Modify:
  - `TaskWriteInputs.cs` (`TaskCreateInput`);
  - `CreateTaskCommand.cs`, `Api/Contracts/WorkManagement/Tasks/TaskContracts.cs` (`CreateTaskRequest`) and the tasks controller mapping (+ `Guid? SourceMeetingOccurrenceId = null`);
  - `CreateTaskCommandHandler.cs` (validate the source; link in the direct path);
  - `TaskCreateApplier.cs` (link on apply).
- Create:
  - `IMeetingActionItemRepository` + `EfMeetingActionItemRepository`;
  - `MeetingActionItemService`;
  - `Commands/MeetingActionItemCommands.cs`, `Queries/MeetingActionItemQueries.cs`;
  - two `MeetingsController` routes;
  - `docs/api/meetings-v1.md` rows.
- Test:
  - `tests/ONEVO.Tests.Integration/WorkManagement/Meetings/MeetingActionItemIntegrationTests.cs`;
  - `tests/ONEVO.Tests.Unit/Controllers/Tenant/WorkManagement/MeetingsControllerTests.cs` (+2 routes);
  - the existing task create/applier unit tests stay green.

**Interfaces:**
- `TaskCreateInput(..., Guid? SprintId, Guid? SourceMeetingOccurrenceId = null)`. Old `payload_json` rows deserialize with null.
- `IMeetingActionItemRepository`:
  - `Task<bool> ExistsForTaskAsync(Guid tenantId, Guid taskId, CancellationToken ct)`;
  - `void Add(MeetingActionItem row)`;
  - `Task<IReadOnlyList<MeetingActionItem>> ListForOccurrenceAsync(Guid tenantId, Guid occurrenceTaskId, CancellationToken ct)`;
  - `Task<MeetingActionItem?> GetTrackedAsync(Guid tenantId, Guid occurrenceTaskId, Guid taskId, CancellationToken ct)`;
  - `void Remove(MeetingActionItem row)`.
- `MeetingActionItemService`:
  - `Task<Result<bool>> ValidateSourceAsync(Guid tenantId, Guid callerEmployeeId, Guid occurrenceTaskId, CancellationToken ct)`. It returns 404 `meeting_not_found` unless `IMeetingAccessResolver` resolves the caller for that occurrence and the occurrence is materialized, not removed and not cancelled.
  - `Task LinkAsync(Guid tenantId, Guid occurrenceTaskId, Guid taskId, Guid creatorEmployeeId, CancellationToken ct)`. It runs inside the caller's transaction. It is a no-op if the occurrence no longer exists or is cancelled, or the task already has a link (one link per task, spec 4.1).
  - `Task<Result<IReadOnlyList<MeetingActionItemView>>> ListAsync(Guid tenantId, Guid callerEmployeeId, Guid callerUserId, Guid occurrenceTaskId, CancellationToken ct)`. The caller needs meeting access (404 otherwise). The result is filtered through `ITaskAccessResolver.FilterViewableTasksAsync`.
  - `Task<Result<bool>> UnlinkAsync(Guid tenantId, Guid callerEmployeeId, Guid occurrenceTaskId, Guid taskId, CancellationToken ct)`. Only the link creator or the organizer may unlink (403 `meeting_organizer_required` for anyone else who can see the meeting). The task itself stays.
- `record MeetingActionItemView(Guid TaskId, string ShortId, string Title, string StatusName, Guid ObjectiveId, Guid? AssigneeEmployeeId, Guid LinkedByEmployeeId, DateTimeOffset LinkedAt)`.
- Routes:
  - `GET api/v1/meetings/occurrences/{taskId}/action-items` returns `MeetingActionItemView[]`.
  - `DELETE api/v1/meetings/occurrences/{taskId}/action-items/{actionTaskId}` returns 204.
  - Creating an action item is `POST /api/v1/work/objectives/{objectiveId}/tasks` (the existing `TasksController` route) with `sourceMeetingOccurrenceId`.
- Errors: `meeting_not_found` (404, the source is invisible or gone), `meetings_read_only` on unlink. A create with an invalid source is 404 and creates nothing.

- [ ] **Step 1: Write failing integration tests:**
  - `DirectCreate_LinksTheTaskToTheOccurrence_InOneTransaction`: a module owner creates in the meeting's module. One `meeting_action_items` row with the task id and the occurrence id.
  - `AnotherModuleTheCreatorIsMemberOf_IsAllowed_OthersAre404`.
  - `ViaApproval_LinkIsWrittenOnApply_NotOnRequest_RejectWritesNone`.
  - `ApprovedRequest_ForARemovedOccurrence_CreatesTheTaskWithoutALink` (Review Focus 2).
  - `ActionItems_HiddenTaskIsNotListedOrCounted` (Review Focus 1): an action item in a private module the attendee cannot see is absent from the list.
  - `Unlink_ByCreatorOrOrganizer_AttendeeGets403_TaskStays`.
  - `InvisibleOccurrenceAsSource_Is404_AndCreatesNothing`.
  - `ASplitWithAnActionItem_CancelsTheSurplusInsteadOfDeletingIt` (it uses Plan B's `HasDependentsAsync`; the test pins it with a real row).
- [ ] **Step 2:** Run. Expected: FAIL (`SourceMeetingOccurrenceId` does not exist).
- [ ] **Step 3:** Implement. The direct path calls `LinkAsync` after `_writes.CreateAsync` in the same transaction. The applier calls it after `CreateAsync`; a no-op result never makes the apply `Invalid`.
- [ ] **Step 4:** Run `dotnet test tests/ONEVO.Tests.Integration --filter "FullyQualifiedName~WorkManagement"`, Unit and Architecture. Expected: PASS.
- [ ] **Step 5:** Commit `feat(meetings): action items raised from a meeting occurrence`.

### Task 2: Bulk invite preview (D8)

**Files:**
- Create:
  - `MeetingBulkInviteService.cs`;
  - `Commands/MeetingBulkPreviewQuery.cs` (query + handler);
  - the `MeetingsController` route;
  - `MeetingsOptions.ProviderCapacityWarning` (+ validator: values > 0).
- Modify: `appsettings.json` (`Meetings:ProviderCapacityWarning: { "MicrosoftTeams": 1000, "Zoom": 300 }`), `docs/api/meetings-v1.md`.
- Test:
  - `tests/ONEVO.Tests.Unit/Features/WorkManagement/Meetings/MeetingBulkExpansionTests.cs` (pure expansion);
  - `tests/ONEVO.Tests.Integration/WorkManagement/Meetings/MeetingBulkPreviewIntegrationTests.cs`.

**Interfaces:**
- `POST api/v1/meetings/bulk-preview` with body `MeetingBulkPreviewRequest(Guid? ProjectId, Guid? ObjectiveId, IReadOnlyList<Guid> DepartmentIds, IReadOnlyList<Guid> ManagerEmployeeIds, IReadOnlyList<Guid> EmployeeIds, Guid? SeriesId, string? Platform)`. It returns `MeetingBulkPreviewResponse(IReadOnlyList<MeetingBulkPerson> People, int Selected, int DuplicatesRemoved, int InactiveExcluded, int AlreadyInvited, string? CapacityWarning)`.
- `record MeetingBulkPerson(Guid EmployeeId, string Name, string? Email, string? DepartmentName)`. These are the participant picker's fields; confirm against the frontend picker in Plan E and add nothing else.
- Gate:
  - Without `SeriesId`: `IMeetingPlacementResolver.ResolveAsync(tenantId, caller, projectId, objectiveId)` — the create gate. Its failures (403 `meeting_module_member_required`, `meeting_calendar_write_required`, ...) are returned as they are.
  - With `SeriesId`: the caller must be its organizer (`MeetingGuard.OrganizerOnlyAsync`); it is used to add attendees to an existing series.
  - With `Meetings:BulkInvite` off: 409 `bulk_invite_unavailable`.
- `static MeetingBulkExpansion.Expand(IReadOnlyList<Guid> explicitIds, IReadOnlyList<IReadOnlyList<Guid>> groups, IReadOnlySet<Guid> active, IReadOnlySet<Guid> alreadyInvited) : (IReadOnlyList<Guid> People, int Selected, int Duplicates, int Inactive, int AlreadyInvited)`. The counts:
  - `Selected` = every id before dedup;
  - `Duplicates` = repeats of an id;
  - `Inactive` = distinct ids not in `active`;
  - `AlreadyInvited` = distinct active ids in `alreadyInvited`.
  - `People` = the rest, in first-seen order.
- `alreadyInvited` contains the organizer, plus the series' non-removed participants when `SeriesId` is set.
- Groups:
  - a department's employees come from `employees` where `department_id` is in the list (the department's own employees only; sub-departments are not expanded — ruling: the spec says "a whole department");
  - a manager's team is `GetDirectReportEmployeeIdsAsync`.
- `CapacityWarning` is `"This meeting will have {n} people; {Provider} meetings support up to {limit}."` when `Platform` is set and `People.Count + already-invited > limit`.

- [ ] **Step 1: Write failing unit tests:** `Expand_RemovesDuplicatesAcrossGroups`, `Expand_ExcludesInactive_CountsThemOnce`, `Expand_CountsAlreadyInvited`, `Expand_KeepsFirstSeenOrder`.
- [ ] **Step 2: Write failing integration tests:**
  - `Preview_ExpandsADepartmentAndAManagersDirectReports`;
  - `Preview_CountsAlreadyInvited_IncludingTheOrganizer` (Review Focus 4);
  - `Preview_FlagOff_Is409BulkInviteUnavailable`;
  - `Preview_SameGateAsCreate_NonMemberIs403`;
  - `Preview_AboveProviderCapacity_WarnsButReturnsEveryone`;
  - `CreateWithThePreviewedIds_SkipsSomeoneWhoBecameInactive` (the existing `skipped[]`).
- [ ] **Step 3:** Run. Expected: FAIL.
- [ ] **Step 4:** Implement.
- [ ] **Step 5:** Run the Meetings integration filter, Unit and Architecture. Expected: PASS.
- [ ] **Step 6:** Commit `feat(meetings): bulk invite preview by department or a manager's team`.

### Task 3: Pace calendar invite emails (`Email:MaxPerSecond`)

**Files:**
- Create: `IEmailRateLimiter.cs`, `TokenBucketEmailRateLimiter.cs`, `EmailRateOptions` (bound from `Email:MaxPerSecond`, an int ≥ 0).
- Modify: `CalendarEventInviteEmailOutboxHandler` (await the limiter before each send), DI (singleton), `appsettings.json` (`"MaxPerSecond": 0`), the runbook.
- Test: `tests/ONEVO.Tests.Unit/Infrastructure/Email/TokenBucketEmailRateLimiterTests.cs`, and the existing invite handler tests stay green.

**Interfaces:**
- `interface IEmailRateLimiter { ValueTask WaitAsync(CancellationToken ct); }`.
- `TokenBucketEmailRateLimiter(IOptions<EmailRateOptions>, TimeProvider)`. `0` means it never waits. `n > 0` allows at most `n` sends per rolling second per instance. Implement it as a small lock-guarded sliding window over `TimeProvider` (no new package): keep the start times of the last `n` sends and wait until the oldest is a second old.
- Per instance, not cluster-wide. The runbook says so: set it to the provider limit ÷ the instance count.

- [ ] **Step 1: Write failing tests:** `Zero_NeverWaits`, `Two_PerSecond_TheThirdWaitsForTheNextSecond` (with a hand-written `TimeProvider` subclass whose clock the test advances; the project has no TimeProvider testing package), `Cancellation_StopsTheWait`.
- [ ] **Step 2:** Run. Expected: FAIL.
- [ ] **Step 3:** Implement and wire.
- [ ] **Step 4:** Run the Unit and Calendar integration filters. Expected: PASS.
- [ ] **Step 5:** Commit `feat(email): pace calendar invite emails with Email:MaxPerSecond`.

### Task 4: Reporting-line attendance view (D16, D17)

**Files:**
- Create:
  - `EmployeeMeetingAttendanceService.cs`;
  - `Queries/EmployeeMeetingAttendanceQuery.cs`;
  - `EmployeeMeetingAttendanceController.cs`;
  - `IMeetingAttendanceRepository.ListEmployeeOccurrencesAsync`.
- Modify: `docs/api/meetings-v1.md`.
- Test: `tests/ONEVO.Tests.Integration/WorkManagement/Meetings/EmployeeMeetingAttendanceIntegrationTests.cs`, and a controller unit test.

**Interfaces:**
- `GET api/v1/employees/{employeeId}/meeting-attendance?from=&to=` returns `EmployeeMeetingAttendanceResponse(IReadOnlyList<EmployeeMeetingAttendanceItem> Items)`.
  - The default window is the last 30 days. The maximum is 92 days; beyond that, 400 `meeting_invalid_time`.
  - The caller is the employee themself, or appears in `GetAncestorChainEmployeeIdsAsync(tenant, employeeId)`. Anyone else gets 404 `meeting_not_found`. No role, HR or Owner shortcut exists (spec 5.1).
- `record EmployeeMeetingAttendanceItem(Guid OccurrenceTaskId, DateTimeOffset StartAt, DateTimeOffset EndAt, string Title, string? OrganizerName, string Status, string Display, string Outcome, int? AttendedSeconds)`:
  - `Status`, `Display` and `Outcome` use Plan C's values and wording (`MeetingAttendanceStatuses`, `MeetingAttendanceOutcomes`).
  - D17: for a **manager** caller, a private occurrence has `Title = "Private meeting"` and `OrganizerName = null`. The employee themself sees the title.
  - `OccurrenceTaskId` is always present. Opening it still goes through the meeting's own 404.
- Rows cover the employee's materialized, non-cancelled occurrences where they are an effective employee attendee, that have ended, in the window, newest first. The time is `MeetingAttendanceMatcher.Union` over that occurrence's rows for that employee. Legacy calendar events are not included (spec §6).

- [ ] **Step 1: Write failing tests:**
  - `Self_SeesOwnRows_WithTitles`;
  - `DirectAndSkipLevelManagers_SeeTheSummary`;
  - `FormerManager_Gets404` (Review Focus 3);
  - `HrOrOwnerOutsideTheLine_Gets404`;
  - `PrivateMeeting_IsMaskedForManagers_NotForSelf`;
  - `UnfinishedOrUnsyncedOccurrences_AreUnknown_NeverAbsent`;
  - `PerOccurrenceRemove_IsNotListed`;
  - `WindowOver92Days_Is400`.
- [ ] **Step 2:** Run. Expected: FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run the Meetings integration filter, Unit and Architecture. Expected: PASS.
- [ ] **Step 5:** Commit `feat(meetings): reporting-line meeting attendance with private masking`.

### Task 5: Monitoring compliance — task meetings and private masking (D17)

**Files:**
- Modify:
  - `EfMeetingComplianceReadRepository.cs`;
  - `MeetingApplicationComplianceItem` (+ `bool IsPrivate = false`);
  - `GetEmployeeAppUsageQueryHandler` (no logic change; its tests stay green).
- Test: `tests/ONEVO.Tests.Integration/Monitoring/MeetingComplianceTaskMeetingIntegrationTests.cs`.

**Interfaces:**
- `ListAsync` also returns each materialized, non-cancelled task-meeting occurrence in the window where:
  - the employee is an effective attendee who has not declined;
  - the occurrence has an active link (`calendar_event_meetings.status = 'active'`).
- For those rows: `EventId` = occurrence task id, `StartsAt`/`EndsAt` = `start_at`/`end_at`, `Provider` = the link's provider. This matches the C2 agent key `(TaskId, StartAt)` that `TaskMeetingOccurrenceSource` already reads.
- Every item, legacy or task meeting, whose event or occurrence is private has `Title = "Private meeting"` and `IsPrivate = true`. The usage numbers are unchanged.

- [ ] **Step 1: Write failing tests:**
  - `Compliance_IncludesTaskMeetingOccurrences_WithTheirProviderApp`;
  - `Compliance_PrivateMeetingsAreMasked_BothKinds` (Review Focus 5);
  - `Compliance_DeclinedOrRemovedAttendee_HasNoRow`;
  - `C2_AgentRatio_ReachesTheTaskMeetingView` (end to end through `TaskMeetingOccurrenceSource`).
- [ ] **Step 2:** Run. Expected: FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run the Monitoring and Meetings integration filters, Unit and Architecture. Expected: PASS.
- [ ] **Step 5:** Commit `feat(monitoring): task meetings in meeting compliance, private titles masked`.

### Task 6: Leak coverage, contract, runbook and full verification

**Files:**
- Modify: `tests/ONEVO.Tests.Integration/WorkManagement/Meetings/MeetingLeakIntegrationTests.cs` (+ cases), `docs/api/meetings-v1.md`, `docs/runbooks/meetings-phase2-release.md`.

- [ ] **Step 1:** Add leak cases. An outsider sees no meeting through:
  - the action-item list (404);
  - the employee attendance view (404);
  - the bulk preview with a `SeriesId` (404);
  - monitoring compliance for another employee (no title of a private meeting).
- [ ] **Step 2:** Update the API doc:
  - action items, the bulk preview and the attendance view;
  - the new error code uses;
  - the frontend notes: send the previewed ids; the preview is a snapshot; warnings are not caps.
- [ ] **Step 3:** Update the runbook: `Meetings:BulkInvite` needs P-1 merged; `Email:MaxPerSecond` per instance; `ProviderCapacityWarning`.
- [ ] **Step 4:** Run the full Unit, Architecture and Integration suites (output to files). Expected: all green apart from the two known My Team failures (`LedWorkProgressIntegrationTests.Lead_sees_rolled_up_counts_member_sees_nothing`, `MyTeamCapabilitiesIntegrationTests.CanViewPeopleStatus_requires_both_the_permission_and_a_real_active_coverage_row`).
- [ ] **Step 5:** `has-pending-model-changes` through `.superpowers/tools/ef.sh`. Expected: no changes (Plan D adds no schema).
- [ ] **Step 6:** Commit `docs(meetings): Plan D contract, leak coverage and runbook`.
