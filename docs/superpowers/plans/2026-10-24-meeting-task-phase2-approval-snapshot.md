# Meeting Approval Snapshot Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The approver of a `meeting.create` or `meeting.move` request sees the D5 snapshot frozen at submit time. Nobody else sees it: not the requester, and not an outsider. Nobody sees the raw meeting payload.

**Architecture:**
- At submit time, one builder freezes a `MeetingApprovalSnapshot` into the request payload. `meeting.create` already does this; `meeting.move` does not yet.
- `GetApprovalDetailQueryHandler` is the single read point for both the Approvals page and the Inbox. For meeting actions it:
  - hides the raw payload and the field diff;
  - returns `ExtraJson = {"snapshot":{…}}` only when the caller is a receiver (`ApprovalFeedParticipants.IsReceiver`).
- `WorkInboxSourceProvider` passes that `ExtraJson` through to `InboxItemDetail.ExtraJson`, which the frontend already renders.

**Tech Stack:**
- .NET 10 / C#, MediatR, EF Core + Npgsql.
- Tests: xUnit + Moq (unit); Testcontainers Postgres via `MyTeamDb` (integration).

**Spec:** `docs/superpowers/specs/2026-10-18-meeting-task-phase2-design.md`
- D5 (line 30), access tiers (§5, "Approver … The request snapshot, through approval endpoints only"), surfaces table ("Approval detail | Snapshot only").
- API contract: `docs/api/meetings-v1.md` ("Approvals", "Approval snapshot").

**Worktree / branch:** `C:\Users\User\onexso-worktrees\be-phase2-d`, branch `feature/meeting-task-phase2-d`.
- Commit and push to that branch only.
- No PR, no merge.

## Verified facts this plan rests on (checked in code, b74c85f0)

- `MeetingApprovalSnapshot` already exists (`Meetings/Contracts/MeetingContracts.cs:156`). It has exactly the 11 D5 fields: `Title, Description, Start, DurationMinutes, Timezone, RecurrenceSummary, OrganizerName, ModulePath, ParticipantNames, GuestEmails, Platform`.
- `meeting.create` stores `MeetingCreatePayload(Form, Snapshot)` (`CreateMeetingCommandHandler.cs:84`, built by its private `SnapshotAsync`, line 108).
- **`meeting.move` has no snapshot.** `MeetingMovePayload(SeriesId, OriginalStart, Scope, ProjectId, ObjectiveId)` (`MeetingMoveService.cs:17`) is all that is stored (line 80).
- The leaks today:
  - `GetApprovalDetailQueryHandler.EngineDetailAsync` returns `request.PayloadJson` raw as `RequestedPayloadJson` to anyone who passes `ApprovalFeedParticipants.CanSee`, and `CanSee` includes the requester (`IsSender`).
  - `GET api/v1/work/approvals/{id}` (`WorkApprovalsController.Detail`) exposes that to both requester and approvers: the full form, with participant employee ids and guest emails.
- `WorkInboxSourceProvider.ApprovalDetailAsync` (lines 268-288) never sets `ExtraJson`, so the frontend's snapshot card stays empty.
- `ApprovalChangeSetBuilder.Specs` has no `meeting.*` entries, so `Fields` is `[]` for meetings.
- `IsReceiver` = not the requester AND (resolved approver OR the decider OR can decide now). The decider therefore keeps access after deciding, as D5 requires.

## Global Constraints

- Snapshot JSON = exactly these 11 camelCase keys: `title, description, start, durationMinutes, timezone, recurrenceSummary, organizerName, modulePath, participantNames, guestEmails, platform`. Never the link, attendance or action items (D5).
- Serialize with `new JsonSerializerOptions(JsonSerializerDefaults.Web)`, the casing the meeting payloads already use.
- `ExtraJson` shape: `{"snapshot":{…}}`. The frontend (`inbox-item-card.component.ts:343-350`) accepts this.
- A caller who cannot see the request gets the same 404 body as today ("Approval request not found."). Do not add a new error path.
- A meeting approval stays non-editable (`CanEditPayload: false`) and a revert still returns 409. Both are unchanged.
- Payloads must stay backward compatible. A pending `meeting.move` row stored before this change still deserializes and applies. It shows no snapshot.

## Review Focus

1. **A requester who could also decide** (for example, they own a parent module): `IsReceiver` is false for the requester, so they get no snapshot and no payload. Owned by Task 2: `Meeting_requester_who_can_decide_still_gets_no_snapshot`.
2. **A legacy `meeting.move` row with no snapshot** loads without an exception, shows `ExtraJson = null`, and still applies on approve. Owned by Task 1: `MovePayload_without_snapshot_still_deserializes`, and Task 2: `Meeting_move_without_a_stored_snapshot_shows_none`.
3. **The snapshot JSON carries no form fields** (`participantEmployeeIds`, `form`, `objectiveId`, `joinUrl`) at any depth. Owned by Task 2: `Meeting_create_approver_sees_only_the_snapshot` asserts the exact key set.
4. **Frozen means frozen:** renaming the series or adding a participant after submit does not change what the approver sees. Owned by Task 3: `AMoveRequest_StoresTheSnapshot_FrozenAtSubmit`.
5. **The decider after deciding** still sees the snapshot in history (D5 "keeps only the snapshot"). Owned by Task 2: `Meeting_create_decided_approver_keeps_the_snapshot`.

---

### Task 1: One snapshot builder; `meeting.move` freezes its snapshot

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/Meetings/Services/MeetingApprovalSnapshotBuilder.cs`
- Modify: `…/Meetings/Commands/CreateMeeting/CreateMeetingCommandHandler.cs`: delete `SnapshotAsync` and call the builder.
- Modify: `…/Meetings/Services/MeetingMoveService.cs`: add the payload param and build the snapshot on the approval path.
- Modify: DI registration, next to the other meeting services (search `AddScoped<IMeetingMoveService`).
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Meetings/MeetingApprovalSnapshotBuilderTests.cs` (new), `…/Meetings/CreateMeetingCommandHandlerTests.cs` (constructor update only).

**Interfaces:**
- Produces: `IMeetingApprovalSnapshotBuilder`
  - `Task<MeetingApprovalSnapshot> ForCreateAsync(Guid tenantId, Guid organizerEmployeeId, MeetingPlacement at, CreateMeetingRequest request, CancellationToken ct)`: the existing `SnapshotAsync` body, moved without behaviour change.
  - `Task<MeetingApprovalSnapshot> ForMoveAsync(Guid tenantId, MeetingSeries series, DateTimeOffset originalStart, string scope, Guid toProjectId, Guid toObjectiveId, CancellationToken ct)`
- Produces: `public sealed record MeetingMovePayload(Guid SeriesId, DateTimeOffset OriginalStart, string Scope, Guid ProjectId, Guid ObjectiveId, MeetingApprovalSnapshot? Snapshot = null);`. The new param goes last, defaulted, so old rows still deserialize.
- Consumes: `IEmployeeRepository.ListByIdsAsync`, `IWorkHierarchyService.LoadTreeAsync(...).AncestorChain(...)`, `IMeetingRepository.GetDetailsAsync` / `ListSeriesParticipantsAsync` / `ListSeriesGuestsAsync`, `IMeetingLinkRepository.GetActiveForSeriesAsync`.

`ForMoveAsync` values:

| Field | Value |
|---|---|
| `title`, `description` | Occurrence details if materialized (`GetDetailsAsync(series.Id, originalStart)`), else the series |
| `start` | `details?.StartAt ?? originalStart` |
| `durationMinutes` | From details (`EndAt - StartAt`), else `series.DurationMinutes` |
| `timezone` | `series.Timezone` |
| `recurrenceSummary` | `"This occurrence only"` when scope is `this`; else `series.RecurrenceRule ?? "Does not repeat"` |
| `organizerName` | `"First Last"` of `series.OrganizerEmployeeId`, `"Unknown"` if missing (same rule as create) |
| `modulePath` | **Destination** path, `" / "`-joined like create (see Ruling 1) |
| `participantNames` | Non-deleted series participants except the organizer, in name order |
| `guestEmails` | Series guests' emails |
| `platform` | `GetActiveForSeriesAsync(...)?.Provider`, or null. The provider name only, never the URL. |

- [ ] **Step 1: Write the failing tests** in `MeetingApprovalSnapshotBuilderTests` (Moq repositories):
  - `ForMove_freezes_the_D5_fields_with_the_destination_path`
    - Series "Weekly sync", weekly rule, organizer Priya, participants Priya + Arjun, guest `client@example.com`, active zoom link, destination tree Apollo › Delivery › QA, scope `following`.
    - Expect: `Title == "Weekly sync"`, `RecurrenceSummary == "FREQ=WEEKLY;BYDAY=MO"`, `OrganizerName == "Priya K"`, `ModulePath == "Apollo / Delivery / QA"`, `ParticipantNames == ["Arjun M"]`, `GuestEmails == ["client@example.com"]`, `Platform == "zoom"`.
  - `ForMove_this_scope_says_this_occurrence_only`: expect `RecurrenceSummary == "This occurrence only"` and `Start == details.StartAt` for a detached occurrence.
  - `MovePayload_without_snapshot_still_deserializes`
    - Input: `{"seriesId":"…","originalStart":"2026-11-02T03:30:00Z","scope":"this","projectId":"…","objectiveId":"…"}` deserialized with Web options.
    - Expect: `Snapshot == null` and the other five fields set.
- [ ] **Step 2: Run, expect FAIL.**
  - Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~MeetingApprovalSnapshotBuilderTests"`
  - Expected: compile failure, because `MeetingApprovalSnapshotBuilder` is not defined yet.
- [ ] **Step 3: Implement the builder.**
  - Move `SnapshotAsync` into `ForCreateAsync` unchanged.
  - Add the `Snapshot` param to `MeetingMovePayload`.
  - In `MoveAsync`, build the snapshot only inside `if (!destination.Value!.Direct)`, before `SubmitAsync`, and serialize `payload with { Snapshot = snapshot }`. The direct path does not pay the lookups.
  - Register `IMeetingApprovalSnapshotBuilder` as scoped.
- [ ] **Step 4: Run the tests above plus `CreateMeetingCommandHandlerTests` and `MeetingAppliersGateTests`. Expect PASS.**
- [ ] **Step 5: Commit** `feat(meetings): freeze the D5 snapshot into meeting.move requests`

### Task 2: Approval detail returns the snapshot to receivers only and hides the raw payload

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/Meetings/Services/MeetingApprovalDetail.cs` (static)
- Modify: `…/Approvals/DTOs/ApprovalDetailResponse.cs`: add `string? ExtraJson = null` as the last positional param.
- Modify: `…/Approvals/Queries/GetApprovalDetail/GetApprovalDetailQueryHandler.cs` (`EngineDetailAsync`, the `return` at ~line 158)
- Modify: `…/Inbox/WorkInboxSourceProvider.cs:283-287`: pass `ExtraJson: d.ExtraJson`.
- Modify: `docs/api/meetings-v1.md`, "Approvals" bullets. Add: the approver reads the snapshot from the inbox item detail `extraJson` (`{"snapshot":{…}}`) for both `meeting.create` and `meeting.move`; the payload is never returned.
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Approvals/GetApprovalDetailQueryHandlerTests.cs`, `tests/ONEVO.Tests.Unit/Features/WorkManagement/Inbox/WorkInboxSourceProviderTests.cs`

**Interfaces:**
- Produces: `MeetingApprovalDetail.IsMeetingAction(string actionType) -> bool` (`meeting.create` / `meeting.move`).
- Produces: `MeetingApprovalDetail.ExtraJson(string? payloadJson) -> string?`
  - Returns `{"snapshot":{…}}` from the payload's `snapshot` property.
  - Returns null when the property is missing or null, or the JSON is malformed. It never throws.
  - Re-serializes through `MeetingApprovalSnapshot`, so unknown keys can never pass through.
- Handler rule for meeting actions:
  - `RequestedPayloadJson: null`, `AppliedPayloadJson: null`, `Fields: []`, `CanEditPayload: false`;
  - `ExtraJson: IsReceiver(tree, request, caller) ? MeetingApprovalDetail.ExtraJson(request.PayloadJson) : null`.
  - Other actions are unchanged.

- [ ] **Step 1: Write the failing tests** in `GetApprovalDetailQueryHandlerTests`. Add a meeting.create request built with `MeetingCreatePayload(form, snapshot)` (form with `ParticipantEmployeeIds = [Owner]`, guest `g@x.com`), approver `Owner`, requester `Requester`.
  - `Meeting_create_approver_sees_only_the_snapshot`
    - Caller is `Owner`.
    - Expect: `RequestedPayloadJson == null`, `AppliedPayloadJson == null`, `Fields` empty, `CanEditPayload == false`.
    - Expect: `ExtraJson` parses to an object with the single key `snapshot`, whose key set equals exactly the 11 constraint keys.
    - Expect: `snapshot.title == "Design review"`, and `participantEmployeeIds` appears nowhere in `ExtraJson`.
  - `Meeting_create_requester_gets_no_snapshot_and_no_payload`
    - Caller is `Requester`.
    - Expect: success with `ExtraJson == null` and `RequestedPayloadJson == null`.
  - `Meeting_requester_who_can_decide_still_gets_no_snapshot`: same as above, but the tree makes `Requester` an owner above the module.
  - `Meeting_request_stranger_gets_404`: caller is `Stranger`; expect `StatusCode == 404` with the existing message.
  - `Meeting_create_decided_approver_keeps_the_snapshot`
    - Status `approved`, `DecidedByEmployeeId = Owner`, caller `Owner`.
    - Expect: `ExtraJson` is not null.
  - `Meeting_move_without_a_stored_snapshot_shows_none`
    - A meeting.move payload without `snapshot`, caller `Owner`.
    - Expect: success with `ExtraJson == null`.
  - Unchanged-behaviour guard: the existing `Approver_can_edit_a_pending_task_edit_and_sees_the_diff` still passes.
- [ ] **Step 2: Write the failing test** in `WorkInboxSourceProviderTests`: `Meeting_approval_detail_carries_the_snapshot_as_extra_json`.
  - The sender returns an `ApprovalDetailResponse` with `ExtraJson = "{\"snapshot\":{\"title\":\"T\"}}"` and action `meeting.create`.
  - Expect: `InboxItemDetail.ExtraJson` equals that string, and `EditablePayloadJson == null`.
- [ ] **Step 3: Run, expect FAIL.**
  - Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~GetApprovalDetailQueryHandlerTests|FullyQualifiedName~WorkInboxSourceProviderTests"`
  - Expected: compile failure on `ExtraJson`, then assertion failures once it compiles.
- [ ] **Step 4: Implement** `MeetingApprovalDetail`, the DTO param, the handler branch and the provider pass-through. Update the contract doc.
- [ ] **Step 5: Run the same filter. Expect PASS. Then run the whole unit project:**
  - Run: `dotnet test tests/ONEVO.Tests.Unit`
  - Expected: all pass.
- [ ] **Step 6: Commit** `fix(approvals): meeting approvals show the D5 snapshot to the approver only`

### Task 3: Integration — real Postgres, real handler, three callers

**Files:**
- Create: `tests/ONEVO.Tests.Integration/WorkManagement/Meetings/MeetingApprovalSnapshotIntegrationTests.cs`

**Interfaces:**
- Consumes: Task 1 `IMeetingApprovalSnapshotBuilder` / `MeetingMovePayload.Snapshot`; Task 2 `ApprovalDetailResponse.ExtraJson`.
- Reuses: `MyTeamDb` and `AddPlacement` / `AddEmployee` / `AddMember`, the seeding in `MeetingApprovalIntegrationTests.SeedAsync`, and `MeetingTestServices` plus the `ApprovalsStub` pattern from `MeetingMoveIntegrationTests`.
- Build `GetApprovalDetailQueryHandler` from the EF repositories of `h.NewContext()`. Stub `ICurrentUser` and `ICallerIdentityResolver.ResolveCallerEmployeeIdAsync` per caller.

- [ ] **Step 1: Write the failing tests.**
  - `TheApprover_SeesTheSnapshot_TheRequesterAndAnOutsider_DoNot`
    - Seed a pending meeting.create request with approver = the module lead, requester = a member, and an outsider employee in another project.
    - Approver: `ExtraJson` has `snapshot.title == "Design review"` and `snapshot.modulePath` is set.
    - Requester: success with `ExtraJson == null` and `RequestedPayloadJson == null`.
    - Outsider: 404.
  - `AMoveRequest_StoresTheSnapshot_FrozenAtSubmit`
    - Seed a series in module A and a member organizer.
    - Call `MeetingMoveService.MoveAsync` with `direct: false` (approval path).
    - Rename the series in the DB and add a participant.
    - Run the detail handler as the destination approver. Expect the old title and the participant list from submit time.
- [ ] **Step 2: Run, expect FAIL** (before Tasks 1–2 land; when executing in order, run against the Task 2 commit with the new assertions only).
  - Run: `dotnet test tests/ONEVO.Tests.Integration --filter "FullyQualifiedName~MeetingApprovalSnapshotIntegrationTests"`
  - Expected: FAIL, or GREEN when Tasks 1–2 are already in. In that case, record a ledger ruling that the RED came from the unit tests.
- [ ] **Step 3: Run the meeting integration slice.** Expect PASS.
  - Run: `dotnet test tests/ONEVO.Tests.Integration --filter "FullyQualifiedName~WorkManagement.Meetings|FullyQualifiedName~Approval"`
  - Ignore the two known pre-existing failures: `LedWorkProgressIntegrationTests.Lead_sees_rolled_up_counts_member_sees_nothing` and `MyTeamCapabilitiesIntegrationTests.CanViewPeopleStatus_requires_both_the_permission_and_a_real_active_coverage_row`.
- [ ] **Step 4: Commit** `test(meetings): approval snapshot reaches the approver only (integration)`

### Task 4 (optional; needs your OK, the spec is silent): duration 15–480 minutes (M7)

The frontend form allows 15–480 minutes. The backend only rejects `<= 0`.
- Where: `MeetingCreateValidator.Validate` (line 20) and both edit paths in `MeetingEditService` (occurrence edit ~line 136, split template ~line 225).
- Rule: reject `DurationMinutes < 15 || > 480` unless `IsAllDay`, with 400 `meeting_invalid_time` ("A meeting lasts 15 minutes to 8 hours.").
- The frontend already maps that code.

- [ ] **Step 1: Failing test** in `MeetingRecurrenceAndValidationTests`: `Duration_outside_15_to_480_minutes_is_rejected`.
  - Rejected: 14, 481.
  - Allowed: 15, 480, and all-day with 1440.
- [ ] **Step 2: Failing test** in `MeetingEditIntegrationTests`: `AnEdit_ToFiveMinutes_IsRejected`. Expect 400 `meeting_invalid_time`.
- [ ] **Step 3: Implement.** Use one `MeetingDurations.IsValid(int minutes, bool isAllDay)` used by all three call sites.
- [ ] **Step 4: Run the unit project and the meeting integration slice.**
  - Fixtures that seed durations below 15 through repositories are not affected. Fix only those that go through the validator.
- [ ] **Step 5: Commit** `feat(meetings): meetings last 15 minutes to 8 hours`

---

## Recommendation: `ApprovalChangeSetBuilder` specs, or `ExtraJson` only?

**`ExtraJson` only.** Do not add `meeting.*` specs.

- The change-set model is "current vs requested vs applied" for editable flat fields. A meeting request is neither editable (`canEditPayload: false`) nor flat: the payload is `{form, snapshot}`, and a move is a placement change with no current values to compare.
- Specs would read keys from the raw form. That form holds `participantEmployeeIds` and the guests, so specs would show more than D5 allows and would duplicate the snapshot.
- The frontend already renders the snapshot from `extraJson`. Specs would add a second, conflicting rendering in the same card.

## Rulings in this plan (cost if wrong)

1. **`modulePath` for a move is the destination path.** The approver decides the move into that module. *Cost:* the approver does not see the source module. If you want "From → To", Task 1 changes that one string.
2. **Raw `RequestedPayloadJson` is hidden from everyone for meeting actions,** the requester included, not only the snapshot. Spec: "Approval detail | Snapshot only". *Cost:* the requester's Approvals page shows no form echo. They see the meeting itself once it is approved.
3. **Legacy pending `meeting.move` rows get no snapshot.** A snapshot built at read time would not be "frozen at submit" (D5). *Cost:* an approver of such a row sees the title-only card until it is decided.
4. **The snapshot is built only on the approval path of a move,** not for direct moves. *Cost:* none; direct moves have no approver.

## Deferred (no plan now)

- Meetings flags / availability endpoint (frontend currently learns from the first refused write).
- `MeetingEditResponse` carrying the split occurrence's new `originalStart`.
- Action items on virtual (not yet materialized) occurrences (`ValidateSourceAsync` 404).
- Meetings on the project calendar (`CalendarEventDto` does not merge them).

## Found while starting the preview, not part of this plan

`20261018092918_AddMeetingWorkItemsFoundation` fails on the local DB with `42501: permission denied to set parameter "app.tenant_context_mode"`.
- Cause: it is the only migration that puts `SET app.tenant_context_mode = 'admin'` on a function definition (lines 725, 754, 781). The non-superuser `onevo_migrator` role may not do that.
- The same would happen on any environment whose migrator is not a superuser.
- `20261021181949` already uses the pattern that works: `PERFORM set_config('app.tenant_context_mode', 'admin', true)` in the body.
- Needs its own decision.
