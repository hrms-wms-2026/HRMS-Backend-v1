# Meetings as Tasks — Phase 2 Plan C: Calendar Engine for Series Owners

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give a Phase 2 meeting the calendar capabilities a calendar event already has:
- invites, update and cancel emails with an .ics;
- a shared Teams/Zoom link owned by the series;
- per-occurrence attendance;
- C2 incidents and reminders;
- the merged `GET /calendar` and conflict check;
- push to the organizer's external calendar.

**Architecture:** The provider, sender, email-key and .ics layers already take plain values (tenant, title, start, recipients, correlation id). The "meeting owner" abstraction therefore lives at that layer:
- a series owner passes its **link id** where a legacy event passes its event id;
- the series' rows carry `owner_kind = meeting_series`.

New series orchestration sits in `Features/WorkManagement/Meetings/Services` next to Plan B's services: `MeetingNotifier`, `SeriesMeetingLinkService` and `MeetingAttendanceSyncService`. Legacy `EventMeetingLinkService` and the one-shot attendance jobs keep their behaviour. They already skip series-owned links (Plan A guard).

**Tech Stack:** .NET 10, EF Core 10 / Npgsql, MediatR, xUnit + Moq (unit), Testcontainers PostgreSQL (integration; `MyTeamDb` / `MeetingSeed` / `MeetingTestServices` harness; no Moq in the integration project, so it uses hand-written stubs).

**Spec:** `docs/superpowers/specs/2026-10-18-meeting-task-phase2-design.md` (§3, §5.1 endpoint matrix, §5.3 emails, §5.7, §5.8, §5.9, §5.10, §7, §10 "Engine integration" and "Phase 0 parity").

**Base:** `feature/meeting-task-phase2-b`, before its final-review fixes. The review fixes are merged in when they land.

## Global Constraints

**Feature flags**
- Every Plan C write path is behind the Plan B flags.
  - `Meetings:ReadOnly` blocks every link and recheck write with 409 `meetings_read_only`.
  - `Meetings:Create` off gives 409 `meetings_disabled`.
- Background work (the attendance job, the provisioning outbox handler's retries) runs only with `Meetings:Background` on. The jobs hold a `job_leases` lease named after the job.

**Emails and correlation**
- Email keys:
  - invite: `invite:{participantOrGuestRowId}`;
  - update: `update:{seriesId}:{revision}:{rowId}` for "all", and `update:{taskId}:{revision}:{rowId}` for one occurrence;
  - cancel: `cancel:{seriesId}:{originalStart ticks | "series"}:{rowId}`;
  - removed: `cancel:{seriesId}:series:{rowId}`;
  - link: `link:{linkId}:{gen}:{rowId}`;
  - link removed: `link-removed:{linkId}:{gen}:{rowId}`.

  Reuse `CalendarEmailKeys`; never invent a second scheme.
- .ics:
  - UID = `CalendarIcs.Uid(seriesId)`;
  - one occurrence adds RECURRENCE-ID = `original_start`;
  - SEQUENCE = `CalendarIcs.Sequence(now)`;
  - the series carries its RRULE.

  Every Phase 2 invite, update, cancel and link email carries an .ics. A link-removed email carries none, as in legacy.
- Correlation for series links: `CalendarMeetingCorrelation.TeamsExternalId(linkId, gen)` / `ZoomToken(linkId, gen)`. The provider cancel's outbox key is `meeting-cancel:{linkId}:{gen}`.

**Attendance**
- Statuses and their display:

  | Status | Display |
  |---|---|
  | `pending` | "Unknown" |
  | `provisional` | No label fixed: the plan shows the records so far but names no label |
  | `synced` | "No join recorded" for a person without rows |
  | `attendance_unavailable` | "Unknown" |
  | `sync_failed` | "Unknown" |
  | `not_applicable` | Not fixed: the meeting has no link |

  Never "Absent".
- A status only moves forward along this order: `pending → provisional → synced`. The terminal statuses (`attendance_unavailable`, `sync_failed`) never replace `synced`.
- The rules, from `MeetingAttendanceOptions`:
  - checkpoints `[30m, 2h, 24h]` after `end_at`;
  - match window `[start_at − 60m, end_at]`;
  - the nearest start wins, the earlier start on a tie;
  - one retry `FailedRetryAfter` (72 h) after a failure;
  - a manual recheck until `end_at + 7d`.

**Privacy and conventions**
- Privacy:
  - only the organizer gets the organizer join URL and the link status;
  - attendees get the join URL only;
  - outsiders get 404;
  - a private meeting shows as "Private meeting" to C2 rules, evidence and reviewers.
- Logs carry ids only, never titles or emails.
- Never print `.env` secrets. Migrations: none expected (Plan A shipped the schema). If one becomes necessary, generate it through the scratch `ef.sh` helper.

## Review Focus

1. **The same link is shared across a split** (Teams and Zoom):
   - neither series' cancel cancels it at the provider while the other still has an upcoming occurrence;
   - after both are released and nothing is "needed", exactly one provider cancel is queued.

   Pinned in Task 5.
2. **A weekly series on one link:**
   - three weekly sessions are assigned to three occurrences;
   - a mid-meeting rejoin (two sessions in one occurrence) is unioned, not double-counted;
   - re-running a checkpoint adds no duplicate rows.

   Pinned in Task 7.
3. **A provider error at every checkpoint** shows `sync_failed` ("Unknown"), never "No join recorded". A report that shows up late after `attendance_unavailable` is recovered by the 72 h retry or a manual recheck. Pinned in Task 7.
4. **An outsider's calendar** shows no Phase 2 meeting. Their conflict check reports a meeting they cannot see as `Busy` with time only (no title, no id). Pinned in Task 11.
5. **A split must not duplicate C2 incidents:** an occurrence keeps its task id and original_start, so the incident key `(employee, rule, task id, original_start)` survives. Pinned in Task 9.

---

## File Structure

| File | Responsibility |
|---|---|
| `Application/Features/Calendar/Services/ICalendarNotificationSender.cs` (+impl) | + `EmailEventChangedKeyedAsync` (keyed update/cancel/removed) |
| `Application/Features/WorkManagement/Meetings/Services/MeetingIcs.cs` | Series/occurrence `CalendarEventIcs` |
| `.../Meetings/Services/MeetingNotifier.cs` | All Phase 2 attendee emails and in-app notices |
| `.../Meetings/RepositoryInterfaces/IMeetingLinkRepository.cs` + `Infrastructure/.../Meetings/EfMeetingLinkRepository.cs` | Series link rows: claim, generation, reference, release, "needed" |
| `.../Meetings/Services/SeriesMeetingLinkService.cs` | Provisioning outcome, link emails, fallback, release, provider cancel |
| `.../Meetings/OutboxHandlers/MeetingLinkProvisionOutboxHandler.cs` | Runs the provider call after the claim commits |
| `.../Meetings/Commands/MeetingLinkCommands.cs`, `Queries/MeetingLinkQueries.cs` | Link status/retry/remove/regenerate/cleanup |
| `Application/Features/Calendar/ServiceInterfaces/IMeetingAttendanceSessionClient.cs` + Graph/Zoom implementations | Provider sessions with ids and records |
| `.../Meetings/Services/MeetingAttendanceMatcher.cs` | Pure: session→occurrence assignment, interval union |
| `.../Meetings/Services/MeetingAttendanceSyncService.cs` + `Infrastructure/.../Meetings/MeetingAttendanceJob.cs` | Per-link sync, status machine, checkpoints, recheck |
| `.../Meetings/Queries/MeetingAttendanceQueries.cs` | Organizer list / own row |
| `Application/Features/Calendar/Exceptions/IEmployeeMeetingOccurrenceSource.cs` (+ legacy + task impls) | C2 engine input |
| `Application/Features/Calendar/Services/CalendarEventReminderService.cs` | + materialized task-meeting occurrences |
| `Application/Features/Calendar/Queries/GetCalendarEvents/*`, `CheckCalendarConflicts/*` | Merge Phase 2 occurrences; busy-only conflicts |
| `Infrastructure/Services/Calendar/CalendarSyncService.cs` | Push the organizer's occurrences |
| `Api/Controllers/Tenant/WorkManagement/MeetingsController.cs`, `docs/api/meetings-v1.md` | New routes and contract |

---

### Task 1: Keyed change emails, meeting .ics and `MeetingNotifier`

**Files:**
- Modify: `src/ONEVO.Application/Features/Calendar/Services/ICalendarNotificationSender.cs`, `CalendarNotificationSender.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Meetings/Services/MeetingIcs.cs`, `MeetingNotifier.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Meetings/MeetingNotifierTests.cs`, `tests/ONEVO.Tests.Unit/Features/Calendar/CalendarNotificationSenderKeyedTests.cs`

**Interfaces:**
- Produces:
  - `Task EmailEventChangedKeyedAsync(Guid tenantId, string kind, CalendarEventIcs ics, string title, DateTimeOffset start, string? location, string? meetingLink, IReadOnlyList<CalendarParticipantRecipient> participants, IReadOnlyList<CalendarGuestRecipient> guests, string organizerName, CancellationToken ct)`. It sends one keyed `calendar_event_invite_email` outbox message per recipient key, through the existing `KeyedEmailAsync`, and requires the caller's transaction.
  - `static class MeetingIcs { CalendarEventIcs ForSeries(MeetingSeries s, DateTimeOffset now); CalendarEventIcs ForOccurrence(MeetingSeries s, MeetingDetails d, DateTimeOffset now); }`.
    - Occurrence: `RecurrenceId = d.OriginalStart`, `EndUtc = d.EndAt`.
    - Series: `EndUtc = DtStart + duration`, `RecurrenceRule = s.RecurrenceRule`.
  - `interface IMeetingNotifier` with:
    - `InviteAsync(series, IReadOnlyList<CalendarEventParticipant> participants, IReadOnlyList<CalendarEventGuest> guests, string? joinUrl, ct)`. The organizer is never emailed. In-app goes through `NotifyParticipantsAddedAsync(..., sendInviteEmail:false)`.
    - `SeriesUpdatedAsync(series, ct)`: all current series attendees, key `Update(series.Id, series.Revision, row)`.
    - `OccurrenceUpdatedAsync(series, details, title, effective attendees, ct)`: key `Update(details.TaskId, details.Revision, row)`, with RECURRENCE-ID.
    - `OccurrenceCancelledAsync(series, details, title, effective attendees, ct)`: key `Cancel(series.Id, details.OriginalStart, row)`.
    - `SeriesCancelledAsync(series, ct)`: key `Cancel(series.Id, null, row)`. "This and following" uses it with the original P in the key.
    - `RemovedAsync(series, recipient, ct)`: kind `removed`, plus in-app `NotifyParticipantRemovedAsync`.
    - `RsvpAnsweredAsync(series, responderEmployeeId, accepted, ct)`: the organizer's in-app notice.

  Each method reads recipients itself, takes the organizer name from the organizer employee and never calls SaveChanges.

- [ ] **Step 1: Write failing tests.** Each test asserts on the sender mock's recipients and keys, or on the .ics fields:
  - `InviteAsync_KeysEachRecipientByItsRow_AndSkipsTheOrganizer`
  - `OccurrenceUpdated_UsesTheTaskIdRevisionKey_AndRecurrenceId`
  - `SeriesUpdated_UsesTheSeriesRevisionKey_AndTheRule`
  - `OccurrenceCancelled_KeyIncludesOriginalStartTicks`
  - `MeetingIcs_ForSeries_HasRRule_ForOccurrence_HasRecurrenceIdNotRRule`
  - `EmailEventChangedKeyed_EnqueuesOneKeyedMessagePerRecipient`
- [ ] **Step 2:** Run `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~MeetingNotifier|FullyQualifiedName~SenderKeyed"`. Expected: FAIL (types missing).
- [ ] **Step 3:** Implement. `MeetingNotifier` gets the employee display names from `IEmployeeRepository.ListByIdsAsync`. Register `IMeetingNotifier` as scoped.
- [ ] **Step 4:** Run the same tests and the architecture tests. Expected: PASS.
- [ ] **Step 5:** Commit `feat(meetings): keyed change emails, meeting .ics and the meeting notifier`.

### Task 2: Wire notifications into Plan B's write paths

**Files:**
- Modify (Plan B services, `.../Meetings/Services/`):
  - `MeetingSeriesFactory` (invites after the occurrences, unless a platform is requested; Task 4 then holds them);
  - `MeetingEditService` (this / all / notes / split);
  - `MeetingSeriesSplitter`;
  - `MeetingCancelService`;
  - `MeetingAttendeeService` (adds, removes, RSVP);
  - `MeetingMoveService` (no email: the move changes placement only, spec D20);
  - `MeetingOccurrenceGenerator` (no email).
- Test: `tests/ONEVO.Tests.Integration/WorkManagement/Meetings/MeetingNotificationIntegrationTests.cs`, plus the `MeetingTestServices` wiring (a real `MeetingNotifier` over a real `CalendarNotificationSender`, with outbox rows asserted).

**Interfaces:**
- Consumes: `IMeetingNotifier` (Task 1).
- Produces: no new public API. Every email is a keyed outbox row of type `calendar_event_invite_email`.

The integration tests:
- [ ] **Step 1: Write failing tests**, each asserting the exact outbox idempotency keys:
  - `DirectCreate_QueuesOneInvitePerAttendee_WithTheSeriesIcs`. No platform, two participants and one guest: 3 keyed rows, none for the organizer.
  - `ApprovedCreate_QueuesInvitesOnApply_NotOnSubmit`.
  - `EditThis_EmailsEffectiveAttendees_KeyedByTaskRevision`. A per-occurrence add gets the email; a per-occurrence remove does not.
  - `EditAll_NonKey_EmailsSeriesAttendees_KeyedBySeriesRevision`.
  - `EditNotesAfterTheMeeting_SendsNoEmail`. D19: the edit log is written, but no outbox row.
  - `CancelOne_And_CancelSeries_UseTheirCancelKeys_AndRetryingSendsNothingNew`.
  - `SeriesRemove_EmailsTheRemovedPersonACancel_AndNotifiesInApp`.
  - `Respond_NotifiesTheOrganizerInApp`.
  - `GeneratorAndMove_SendNoEmail`.
- [ ] **Step 2:** Run `dotnet test tests/ONEVO.Tests.Integration --filter FullyQualifiedName~MeetingNotificationIntegrationTests`. Expected: FAIL.
- [ ] **Step 3: Implement.**
  - Each call sits inside the service's existing transaction, after the write and before its final SaveChanges.
  - The split emails as **one** series update of the new series, keyed with the new series id and revision 0. Old-series attendees who are not in the new series are none (the attendees are copied).
  - RSVP calls `RsvpAnsweredAsync` only when the status actually changed.
- [ ] **Step 4:** Run the Meetings integration filter plus Unit and Architecture. Expected: PASS. Every Plan B test stays green, with stubs added where a constructor grew.
- [ ] **Step 5:** Commit `feat(meetings): invites, update, cancel and removal emails with .ics`.

### Task 3: Series link repository (claim, generations, references, release)

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/Meetings/RepositoryInterfaces/IMeetingLinkRepository.cs`, `src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/Meetings/EfMeetingLinkRepository.cs`
- Test: `tests/ONEVO.Tests.Integration/WorkManagement/Meetings/MeetingLinkRepositoryIntegrationTests.cs`

**Interfaces:**
- Produces, on `IMeetingLinkRepository`:
  - `Task<CalendarEventMeeting?> GetActiveForSeriesAsync(Guid tenantId, Guid seriesId, CancellationToken ct)`: the link of the series' unreleased reference, untracked.
  - `Task<CalendarEventMeeting?> GetTrackedAsync(Guid tenantId, Guid linkId, CancellationToken ct)`.
  - `Task<Guid?> TryClaimForSeriesAsync(Guid tenantId, Guid seriesId, Guid connectionId, string provider, bool invitesPending, DateTimeOffset now, CancellationToken ct)`:
    - inserts `calendar_event_meetings` (`owner_kind = meeting_series`, `calendar_event_id = null`, status `provisioning`, generation 1) and a `meeting_series_links` row (`origin = created`) in one statement batch;
    - `UNIQUE (series_id) WHERE released_at IS NULL` decides the race;
    - returns the link id, or null when the series already holds a reference.
  - `Task<int?> TryClaimNewGenerationAsync(Guid tenantId, Guid linkId, Guid connectionId, string provider, DateTimeOffset now, bool invitesPending, CancellationToken ct)`: the legacy conditional `failed/cancelled → provisioning`, generation + 1, matching on `id` instead of the event.
  - `TryClaimUnknownAsync`, `TryMarkStaleProvisioningUnknownAsync`, `TryMarkUnknownUnresolvedAsync`: the same conditional updates by link id.
  - `Task AddInheritedReferenceAsync(Guid tenantId, Guid fromSeriesId, Guid toSeriesId, DateTimeOffset now, CancellationToken ct)`: copies the active reference as `inherited_on_split`. It does nothing when the source has no reference.
  - `Task ReleaseAsync(Guid tenantId, Guid seriesId, DateTimeOffset now, CancellationToken ct)`: sets `released_at`.
  - `Task<int> CountActiveReferencesAsync(Guid tenantId, Guid linkId, CancellationToken ct)`.
  - `Task<bool> IsNeededAsync(Guid tenantId, Guid linkId, DateTimeOffset now, TimeSpan lateRecheckWindow, CancellationToken ct)`. True while any referencing series (released or not) has a materialized, non-cancelled occurrence with `end_at > now`, or a virtual future slot (series not cancelled, rule not ended); an occurrence with `attendance_finalized_at IS NULL` and a link; or `end_at + lateRecheckWindow > now`.
  - `Task SetOccurrenceLinkAsync(Guid tenantId, Guid seriesId, Guid linkId, DateTimeOffset fromStart, CancellationToken ct)`: a bulk update of `meeting_details.calendar_event_meeting_id` for the series' occurrences with `start_at >= fromStart`.
  - `Task<Guid?> LockLinkAsync(Guid tenantId, Guid linkId, CancellationToken ct)`: `SELECT ... FOR UPDATE`. It is used by the release decision.

  This file is added to the raw-SQL-on-tasks allow-list only if it touches `tasks`. It does not: it touches `meeting_details` and the link tables.

- [ ] **Step 1: Write failing tests:**
  - `TwoConcurrentClaimsForOneSeries_ExactlyOneWins` (mirrors `MeetingClaimIntegrationTests`).
  - `NewGeneration_OnlyFromFailedOrCancelled`.
  - `InheritedReference_SharesTheLink_AndCounts2`.
  - `Release_ThenCountActive_Is1`.
  - `IsNeeded_TrueWhileAnyReferencingSeriesHasAnUpcomingOccurrence_FalseAfterBothEndedAndFinalizedAndWindowClosed`.
  - `SetOccurrenceLink_TouchesOnlyFromStartOnward`.
  - `LegacyClaimSql_IsUnchangedForCalendarEvents` (the existing `MeetingClaimIntegrationTests` still pass).
- [ ] **Step 2:** Run the filter. Expected: FAIL.
- [ ] **Step 3:** Implement with `ExecuteSqlInterpolatedAsync` / `ExecuteUpdateAsync`, in the style of `EfCalendarEventMeetingRepository`.
- [ ] **Step 4:** Run the Calendar and Meetings integration filters plus Architecture. Expected: PASS.
- [ ] **Step 5:** Commit `feat(meetings): series-owned link rows - claim, generations, references, release`.

### Task 4: Provision a series link (create with platform, outbox handler, link emails, fallback)

**Files:**
- Create: `.../Meetings/Services/SeriesMeetingLinkService.cs`, `.../Meetings/OutboxHandlers/MeetingLinkProvisionOutboxHandler.cs`
- Modify:
  - `MeetingSeriesFactory`: platform → claim + keyed outbox `meeting_link_provision:{linkId}:{gen}`, with invites held.
  - `OutboxMessageTypes` (+`MeetingLinkProvision`).
  - `MeetingOccurrenceMaterializer`: a new occurrence takes the series' active link id.
  - `MeetingSeriesSplitter`: `AddInheritedReferenceAsync` plus `SetOccurrenceLinkAsync` on the new series. No provider call, no email.
- Test: `tests/ONEVO.Tests.Integration/WorkManagement/Meetings/MeetingLinkProvisioningIntegrationTests.cs` (fake `IMeetingProvisioner` stub), `tests/ONEVO.Tests.Unit/Features/WorkManagement/Meetings/MeetingLinkProvisionOutboxHandlerTests.cs`

**Interfaces:**
- Consumes: `IMeetingLinkRepository` (Task 3), `IMeetingProvisioner` (existing), `IMeetingNotifier` (Task 1).
- Produces:
  - `SeriesMeetingLinkService.ProvisionAsync(Guid tenantId, Guid linkId, bool recovery, CancellationToken ct) : Task<Result<MeetingLinkResponse>>`.
    - Re-reads the link, its referencing series (the `created` reference) and the organizer's connection.
    - Calls the provisioner with `EventId = linkId`, the series title and the first upcoming occurrence's start/end (series DtStart for a one-off). The attendee emails are the effective attendees of that occurrence.
    - Then runs the legacy outcome table in a transaction:
      - **Created:** status active and join URL set; `SetOccurrenceLinkAsync(from now)`; link emails `Link(linkId, gen, row)` to every series attendee, with the series .ics and join URL; `InvitesPending = false`.
      - **Ambiguous:** `provisioning_unknown` plus the LinkUnknown notice.
      - **Other failures:** `failed` plus the LinkFailed notice.
      - **Not connected:** `failed` with reason `not_connected`.
      - On every non-Created outcome, a pending invite is sent once **without** a link (P1-5), through `IMeetingNotifier.InviteAsync(joinUrl: null)`.
      - A series cancelled during the call: the link becomes `cancelled`, a keyed provider cancel is queued, and nobody is emailed.
  - `record MeetingLinkResponse(Guid LinkId, string Provider, string Status, string? JoinUrl, string? OrganizerJoinUrl, string? FailureReason, int Generation)`.
  - `MeetingLinkProvisionPayload(Guid TenantId, Guid LinkId, int Generation)`.
  - The handler runs `ProvisionAsync(recovery: status is still provisioning and older than EventMeetingLinkService.StaleAfter)`. It is a no-op unless the link is in `provisioning` with that generation.

- [ ] **Step 1: Write failing tests:**
  - `CreateWithTeams_ClaimsALink_HoldsInvites_AndQueuesOneProvisionMessage` (no invite rows yet).
  - `ProvisionCreated_SetsTheLinkOnUpcomingOccurrences_AndEmailsTheLinkAsTheInvite`.
  - `ProvisionFailed_SendsTheInvitesWithoutALinkOnce_AndNotifiesTheOrganizer` (running it twice gives no new rows).
  - `ProvisionAmbiguous_IsProvisioningUnknown_WithFallbackInvites`.
  - `OrganizerNotConnected_CreatesWithoutALink_AndTheResponseSaysSo`. Spec P1-5: the create succeeds with `link: { status: "failed", failureReason: "not_connected" }`.
  - `SplitInheritsTheLinkReference_WithNoProviderCallAndNoEmail`.
  - `GeneratorMaterializedOccurrence_CarriesTheSeriesLink`.
  - Unit: `Handler_IgnoresAStaleGeneration`, `Handler_PassesRecoveryOnARetriedMessage`.
- [ ] **Step 2:** Run the filters. Expected: FAIL.
- [ ] **Step 3:** Implement. `MeetingCreatedResponse` gets an optional `MeetingLinkResponse? Link`, a backward-compatible additive field.
- [ ] **Step 4:** Run the Meetings integration filter, Unit, and Architecture. Expected: PASS.
- [ ] **Step 5:** Commit `feat(meetings): provision the series link after commit; link email doubles as the invite`.

### Task 5: Link endpoints, release, "needed" rule and the provider cancel

**Files:**
- Create: `.../Meetings/Commands/MeetingLinkCommands.cs`, `.../Meetings/Queries/MeetingLinkQueries.cs`
- Modify:
  - `SeriesMeetingLinkService` (+ `RetryAsync`, `RemoveAsync`, `RegenerateAsync`, `ConfirmCleanupAsync`, `ReleaseAsync`);
  - `MeetingCancelService` (series cancel / "following" past the last occurrence → `ReleaseAsync`);
  - `MeetingsController`;
  - `docs/api/meetings-v1.md`.
- Test: `tests/ONEVO.Tests.Integration/WorkManagement/Meetings/MeetingLinkLifecycleIntegrationTests.cs`, `tests/ONEVO.Tests.Unit/Controllers/Tenant/WorkManagement/MeetingsControllerTests.cs` (+3 routes)

**Interfaces:**
- Routes (all under `api/v1/meetings/series/{seriesId}/link`):

  | Route | Who | Behaviour |
  |---|---|---|
  | `GET` | Attendee: `{ joinUrl }`. Organizer: the full `MeetingLinkResponse` | 404 for outsiders |
  | `POST` (create or regenerate, body `{ platform }`) | Organizer | An active link gives 409 `meeting_link_exists`, unless `regenerate: true` |
  | `POST /retry` | Organizer | Same generation; only from `provisioning_unknown`/`failed` |
  | `DELETE` | Organizer | Release this series' reference, then the provider cancel decision. Link-removed emails go to this series' attendees |
  | `POST /cleanup-confirmed` | Organizer | `unresolved → cancelled` (legacy ConfirmEventMeetingCleanup) |

- Release decision, `ReleaseAsync(tenantId, seriesId, ct)`:
  1. Lock the link row (`LockLinkAsync`) and release the reference.
  2. If `CountActiveReferencesAsync == 0 && !IsNeededAsync(...)`, set the status to `cancelled` and enqueue a keyed `CalendarMeetingCancel` with key `meeting-cancel:{linkId}:{gen}`. The payload reuses `CalendarMeetingCancelPayload`, with the organizer user and employee ids and the series title.
  3. Otherwise the link stays active, and the "parked" state shows in the link status as `releasePending: true`.
- Regenerate creates a **new** link for this series only: the old reference is released, then a new claim is made. The other series keep the old link.
- Error codes: `meeting_link_exists`, `meeting_link_in_progress`, `meeting_link_status_unknown`, `meeting_link_unresolved` (from `MeetingErrorCodes`; add the ones that are missing, and document them).

- [ ] **Step 1: Write failing tests:**
  - `CancelOneOfTwoSeriesSharingALink_QueuesNoProviderCancel`.
  - `ReleasingBoth_AndNothingNeeded_QueuesExactlyOneProviderCancel`.
  - `ReleasingTheLast_WhileAttendanceIsUnfinalized_ParksTheCancel`.
  - `RegenerateAffectsOnlyThatSeries`.
  - `AttendeeGetsJoinUrlOnly_OrganizerGetsStatus_Outsider404`.
  - `RemoveSendsLinkRemovedToThisSeriesOnly`.
  - `ReadOnlyBlocksEveryLinkWrite`.
  - Controller unit tests for the routes.
- [ ] **Step 2:** Run. Expected: FAIL.
- [ ] **Step 3:** Implement. The handlers stay thin, in the Plan B pattern: the service plus `ICurrentUser`/`ICallerIdentityResolver`.
- [ ] **Step 4:** Run the Meetings integration filter, Unit and Architecture. Expected: PASS.
- [ ] **Step 5:** Commit `feat(meetings): link endpoints, reference release and the shared-link cancel rule`.

### Task 6: Provider attendance sessions (Teams reports, Zoom past instances)

**Files:**
- Create: `src/ONEVO.Application/Features/Calendar/ServiceInterfaces/IMeetingAttendanceSessionClient.cs`
- Modify: `src/ONEVO.Infrastructure/ExternalServices/Calendar/MicrosoftGraphMeetingClient.cs`, `ZoomMeetingClient.cs`, DI registration
- Test: `tests/ONEVO.Tests.Unit/Features/Calendar/MicrosoftGraphMeetingClientTests.cs`, `ZoomMeetingClientTests.cs` (new cases, using the existing fake-handler style)

**Interfaces:**
- Produces:
  - `record ProviderAttendanceSession(string ProviderSessionId, DateTimeOffset Start, DateTimeOffset? End, IReadOnlyList<ProviderAttendanceRecord> Records)`.
  - `record ProviderAttendanceRecord(string ParticipantKey, string? Name, string? Email, DateTimeOffset JoinedAt, DateTimeOffset? LeftAt)`.
    - `ParticipantKey`: the Teams record `id` (or `identity.id`); the Zoom participant `participant_user_id ?? id ?? lower(email) ?? name`.
  - `interface IMeetingAttendanceSessionClient { string Provider { get; } Task<IReadOnlyList<ProviderAttendanceSession>> ListSessionsAsync(string accessToken, string externalMeetingId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct); }`. Each implementation lists every session whose start is in `[from, to]`.
    - Teams: the report `id`, `meetingStartDateTime`, `meetingEndDateTime`.
    - Zoom: the instance `uuid` and `start_time`. The end is the latest `leave_time` of its participants.
  - Both throw `MeetingProviderException` on failure. A Zoom 4711 "missing scope" maps to `MeetingProviderFailureKind.Permanent`, reason `missing_scope`. Add `Reason` to the exception if it is absent.
- [ ] **Step 1: Write failing tests:**
  - `Teams_ListSessions_ReturnsEveryReportInRange_WithIntervals`.
  - `Teams_ListSessions_FollowsNextLink`.
  - `Zoom_ListSessions_ReturnsEachInstance_DoubleEncodesSlashUuid`.
  - `Zoom_MissingScope_IsPermanentWithReason`.
- [ ] **Step 2:** Run. Expected: FAIL.
- [ ] **Step 3:** Implement, reusing `GetAllPagesAsync` and the existing Zoom paging. Register both as `IEnumerable<IMeetingAttendanceSessionClient>`.
- [ ] **Step 4:** Run the Calendar unit tests. Expected: PASS. The existing `GetAttendanceAsync` tests stay unchanged.
- [ ] **Step 5:** Commit `feat(calendar): provider attendance sessions with ids for per-occurrence sync`.

### Task 7: Per-occurrence attendance sync (matcher, status machine, checkpoints, job, recheck)

**Files:**
- Create: `.../Meetings/Services/MeetingAttendanceMatcher.cs`, `MeetingAttendanceSyncService.cs`, `.../Meetings/RepositoryInterfaces/IMeetingAttendanceRepository.cs`, `Infrastructure/.../Meetings/EfMeetingAttendanceRepository.cs`, `Infrastructure/Services/WorkManagement/Meetings/MeetingAttendanceJob.cs`
- Modify:
  - `MeetingOccurrenceMaterializer` / `SeriesMeetingLinkService`: when an occurrence gets a link, set `attendance_status = pending` and `attendance_next_check_at = end_at + Checkpoints[0]`; without a link, `not_applicable`.
  - The controller: `POST .../occurrences/{originalStart}/attendance/recheck`.
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Meetings/MeetingAttendanceMatcherTests.cs`, `tests/ONEVO.Tests.Integration/WorkManagement/Meetings/MeetingAttendanceSyncIntegrationTests.cs` (stub session client)

**Interfaces:**
- `static class MeetingAttendanceMatcher`:
  - `IReadOnlyDictionary<string, Guid> Assign(IReadOnlyList<(string SessionId, DateTimeOffset Start)> unassigned, IReadOnlyList<(Guid TaskId, DateTimeOffset Start, DateTimeOffset End)> occurrences, TimeSpan before)`. A session goes to the occurrence whose window `[Start − before, End]` contains its start; the nearest start wins, the earlier on a tie; no match leaves it unassigned.
  - `TimeSpan Union(IEnumerable<(DateTimeOffset Joined, DateTimeOffset? Left)> intervals, DateTimeOffset openEndAt)`.
- `MeetingAttendanceSyncService`:
  - `SyncLinkAsync(Guid tenantId, Guid linkId, DateTimeOffset now, CancellationToken ct)`. For the link's occurrences that are due (`attendance_next_check_at <= now`, not finalized):
    1. Fetch the sessions in `[min(start) − before, max(end)]`.
    2. Upsert the sessions on (`calendar_event_meeting_id`, `provider_session_id`).
    3. Assign the unassigned ones.
    4. Upsert the records on (`attendance_session_id`, `participant_key`, `joined_at`), resolving the employee by work email or a connected-account email.
    5. Advance each due occurrence:

       | Situation | Status | Next |
       |---|---|---|
       | Has sessions, checkpoint < last | `provisional` | Next checkpoint |
       | Has sessions, last checkpoint | `synced` | `attendance_finalized_at = now` |
       | No sessions before the last checkpoint | Stays `pending` | Next checkpoint |
       | No sessions at the last checkpoint | `attendance_unavailable` | `next_check = now + FailedRetryAfter`, once |
       | Provider error before the last checkpoint | Unchanged, `attendance_last_error = reason` | Next checkpoint |
       | Provider error at the last checkpoint | `sync_failed` | `next_check = now + FailedRetryAfter`, once; then finalized |
    6. Never move back from `synced`.
    7. After an occurrence settles, queue the C2 evaluation for its effective attendees.
  - `RecheckAsync(tenantId, callerEmployeeId, seriesId, originalStart, ct) : Result<MeetingAttendanceStatusResponse>`:
    - the organizer only;
    - 409 `attendance_recheck_expired` after `end_at + LateRecheckWindow`;
    - 409 `attendance_check_in_progress` while a sync holds the link lock (`pg_try_advisory_xact_lock` on `link:{linkId}`);
    - otherwise it syncs that occurrence now. A recheck may also move `unavailable`/`sync_failed` forward to `synced`.
- `MeetingAttendanceJob`: every 15 minutes, `Meetings:Background` + a lease named `meetings.attendance`, per tenant and per link, one failure isolated per link. Three failures in a row for the same link log an error-level alert.
- [ ] **Step 1: Write failing unit tests:**
  - `Assign_WindowIncludesSixtyMinutesBefore`.
  - `Assign_NearestStartWins_EarlierOnTie`.
  - `Assign_TwoSessionsInOneOccurrence`.
  - `Assign_NoMatchStaysUnassigned`.
  - `Union_MergesOverlapsAndRejoins`.
  - `Union_OpenIntervalEndsAtOccurrenceEnd`.
- [ ] **Step 2: Write failing integration tests:**
  - `ThreeWeeklySessionsOnOneLink_LandOnThreeOccurrences`.
  - `TwoSessionsInOneOccurrence_AreBothAssigned_AndTimeIsTheUnion`.
  - `RepeatedCheckpoints_AddNoDuplicateRows`.
  - `ProviderErrorAtEveryCheckpoint_IsSyncFailed_NotNoJoin`.
  - `NoSessionsAtTheLastCheckpoint_IsUnavailable_ThenTheRetryRecoversALateReport`.
  - `RecheckAfterSevenDays_Is409Expired`.
  - `StatusNeverMovesBackFromSynced`.
  - `LegacyCalendarEventLinks_AreNotTouched`.
- [ ] **Step 3:** Run both. Expected: FAIL.
- [ ] **Step 4:** Implement. Register the job like `MeetingOccurrenceGeneratorJob`.
- [ ] **Step 5:** Run the Meetings integration filter, Unit and Architecture. Expected: PASS.
- [ ] **Step 6:** Commit `feat(meetings): per-occurrence attendance - sessions, union, checkpoints, recovery`.

### Task 8: Attendance reads

**Files:**
- Create: `.../Meetings/Queries/MeetingAttendanceQueries.cs`
- Modify: the controller, `docs/api/meetings-v1.md`
- Test: `tests/ONEVO.Tests.Integration/WorkManagement/Meetings/MeetingAttendanceReadIntegrationTests.cs`

**Interfaces:**
- `GET api/v1/meetings/series/{seriesId}/occurrences/{originalStart}/attendance` returns `MeetingAttendanceResponse(string Status, string Display, IReadOnlyList<MeetingAttendanceRow> Rows, IReadOnlyList<MeetingUnmatchedSession>? Unmatched)`:
  - `Display` ∈ {`Unknown`, `Recorded`, `Not applicable`}.
  - `MeetingAttendanceRow(string Kind, Guid? EmployeeId, string? Email, string DisplayName, int? AttendedSeconds, string Outcome)`, where `Outcome` ∈ {`joined`, `no_join_recorded`, `unknown`}.
  - The organizer gets every effective attendee (guests included) plus the unmatched sessions of the link (`MeetingUnmatchedSession(DateTimeOffset Start, DateTimeOffset? End)`).
  - An attendee gets only their own row and `Unmatched = null`.
  - Outsiders get 404.
  - `no_join_recorded` appears only when the status is `synced`.
- [ ] **Step 1: Write failing tests:**
  - `Organizer_SeesEveryone_IncludingGuests_AndUnmatchedSessions`.
  - `Attendee_SeesOnlyOwnRow`.
  - `BeforeSynced_EveryoneWithoutRowsIsUnknown`.
  - `Synced_WithoutRowsIsNoJoinRecorded_NeverAbsent`.
  - `Outsider404`.
- [ ] **Step 2:** Run. Expected: FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run. Expected: PASS.
- [ ] **Step 5:** Commit `feat(meetings): occurrence attendance for the organizer and for attendees`.

### Task 9: C2 engine reads task-meeting occurrences (`IEmployeeMeetingOccurrenceSource`)

**Files:**
- Create: `src/ONEVO.Application/Features/Calendar/Exceptions/IEmployeeMeetingOccurrenceSource.cs`, `LegacyCalendarOccurrenceSource.cs`, `src/ONEVO.Application/Features/WorkManagement/Meetings/Services/TaskMeetingOccurrenceSource.cs`
- Modify: `CalendarIncidentEngine.BuildInputAsync` (consume the sources), DI
- Test: `tests/ONEVO.Tests.Unit/Features/Calendar/Exceptions/CalendarIncidentEngineInputTests.cs` (parity: the existing cases are unchanged), `tests/ONEVO.Tests.Integration/WorkManagement/Meetings/MeetingIncidentEngineIntegrationTests.cs`

**Interfaces:**
- `interface IEmployeeMeetingOccurrenceSource { Task<IReadOnlyList<CalendarMeetingView>> ListAsync(EmployeeOccurrenceContext context, CancellationToken ct); }`.
  - The context carries: tenant, employee, `from`/`to`, the identity set, the manager id, the agent map and `now`.
- **Legacy source:** today's `BuildInputAsync` loop, moved verbatim.
- **Task source:** the employee's visible materialized occurrences in the window, as an organizer or an effective attendee.
  - `SeriesId` field = the **occurrence task id**; `Start` = `original_start`, so the incident key survives splits.
  - The `End`/`Start` used for rules is `start_at`/`end_at`.
  - The title is `"Private meeting"` when `is_private`.
  - RSVP comes from the series participant or the occurrence override. RSVP history comes from `calendar_event_participant_rsvp_history` with `event_id` ∈ {series id, task id}.
  - Report state:

    | Occurrence | State |
    |---|---|
    | No link | `NoOnlineMeeting` |
    | `synced` | `Received`, with the spans from the occurrence's attendance rows that match this employee |
    | `attendance_unavailable` / `sync_failed` | `Unavailable` |
    | Otherwise | `Pending` |
- `CalendarMeetingView` is unchanged.
- [ ] **Step 1: Write failing tests:**
  - `TaskMeeting_IsEvaluated_WithTheOccurrenceTaskIdAndOriginalStart`.
  - `SplitDoesNotDuplicateIncidents` (evaluate, split at the occurrence, evaluate again: same incident count).
  - `PrivateTaskMeeting_TitleIsMasked`.
  - `SyncedOccurrence_SpansComeFromItsOwnRows`.
  - `LegacyInput_IsUnchanged` (every existing `CalendarIncidentEngineInputTests` stays green).
- [ ] **Step 2:** Run. Expected: FAIL on the new cases only.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run the Calendar unit tests and the Meetings integration filter. Expected: PASS.
- [ ] **Step 5:** Commit `feat(calendar): C2 engine evaluates task-meeting occurrences`.

### Task 10: Reminders for task-meeting occurrences

**Files:**
- Modify: `src/ONEVO.Application/Features/Calendar/Services/CalendarEventReminderService.cs` (+ an `IMeetingRepository` read)
- Test: `tests/ONEVO.Tests.Unit/Features/Calendar/CalendarEventReminderServiceTests.cs` (+ cases), `tests/ONEVO.Tests.Integration/WorkManagement/Meetings/MeetingReminderIntegrationTests.cs`

**Interfaces:**
- `IMeetingRepository.ListStartingBetweenAsync(Guid tenantId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct) : IReadOnlyList<MeetingOccurrenceRow>`. It lists materialized, non-cancelled occurrences.
- The reminder log uses `EventId = occurrence task id`, `OccurrenceStart = start_at`. The recipients are the effective attendees who have not declined, organizer included.
- [ ] **Step 1: Write failing tests:**
  - `TaskMeetingReminder_FiresOncePerOccurrenceAndAttendee`.
  - `DeclinedAttendee_GetsNoReminder`.
  - `PerOccurrenceRemove_GetsNoReminder`.
  - `CancelledOccurrence_GetsNoReminder`.
- [ ] **Step 2:** Run. Expected: FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run. Expected: PASS. The legacy reminder tests are unchanged.
- [ ] **Step 5:** Commit `feat(calendar): start reminders for task-meeting occurrences`.

### Task 11: `GET /calendar` merge and conflict check

**Files:**
- Modify: `GetCalendarEventsQueryHandler`, `CheckCalendarConflictsQueryHandler`, `CalendarEventItem` (DTO), `CalendarConflict` (DTO)
- Create: `.../Meetings/Services/MeetingCalendarProjection.cs`
- Test: `tests/ONEVO.Tests.Integration/WorkManagement/Meetings/MeetingCalendarIntegrationTests.cs`, the existing calendar unit tests (still green)

**Interfaces:**
- `CalendarEventItem` gains additive, nullable fields: `string? ItemKind` (`"meeting"` for Phase 2), `Guid? MeetingSeriesId`, `Guid? MeetingTaskId`, `string? MeetingStatus`.
- `MeetingCalendarProjection.ListAsync(tenantId, callerEmployeeId, from, to, ct) : IReadOnlyList<CalendarEventItem>` returns:
  - the materialized visible occurrences, through `IMeetingRepository.ListVisibleAsync`;
  - plus the virtual occurrences from the recurrence expansion of every series the caller attends, excluding materialized keys, suppressions and cancelled series. For each virtual one: `Id = MeetingOccurrenceIds.For(series, start)`, `OriginalStart = start`, `MeetingTaskId = null`. The title and agenda come from the series template.
  - Cancelled occurrences are omitted.
- `GetCalendarEvents` appends the projection, then computes `HasConflict` over the merged list.
- `CalendarConflict` gains `bool IsBusyOnly`. For a Phase 2 meeting the caller cannot see, the conflict is `EventId = Guid.Empty`, `Title = "Busy"`, `IsBusyOnly = true`, with time only. Legacy behaviour is unchanged.
- [ ] **Step 1: Write failing tests:**
  - `Calendar_ShowsMaterializedAndVirtualOccurrences_WithDeterministicIds`.
  - `Calendar_ExcludesSuppressedAndCancelled`.
  - `Calendar_VirtualIdEqualsTheTaskIdItLaterBecomes`.
  - `Outsider_SeesNoMeeting`.
  - `Conflicts_InvisibleMeetingIsBusyOnly_VisibleOneHasItsTitle`.
- [ ] **Step 2:** Run. Expected: FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run the Calendar unit tests and the Meetings integration filter. Expected: PASS.
- [ ] **Step 5:** Commit `feat(calendar): merge Phase 2 meetings into GET /calendar and conflicts`.

### Task 12: Push the organizer's occurrences to their external calendar

**Files:**
- Modify: `src/ONEVO.Infrastructure/Services/Calendar/CalendarSyncService.cs`, `IExternalCalendarEventLinkRepository` (+ `GetTrackedByMeetingTaskAndConnectionAsync`), `IMeetingRepository` (+ `ListOrganizerOccurrencesChangedSinceAsync(tenantId, organizerEmployeeId, since, ct)`, using `tasks.updated_at`/`created_at` and `meeting_details` changes; Plan B's writes stamp `UpdatedAt`)
- Test: `tests/ONEVO.Tests.Unit/Features/Calendar/CalendarSyncServiceMeetingPushTests.cs` (or extend the existing sync tests), `tests/ONEVO.Tests.Integration/WorkManagement/Meetings/MeetingExternalPushIntegrationTests.cs`

**Interfaces:**
- `PushAsync` also pushes, for a connection whose user is a meeting organizer, the changed materialized occurrences:
  - an insert or patch with title or "Busy" (private), and time and location;
  - the link through `ExternalCalendarEventLink.MeetingTaskId`;
  - a cancelled occurrence deletes its remote event and its link.

  This happens only when the sync direction allows pushing. The same batch limit and watermark apply (the combined list is ordered by change time).
- Pull already ignores links with `CalendarEventId == null` (Plan A).
- [ ] **Step 1: Write failing tests:**
  - `OrganizerOccurrence_IsPushedOnce_AndPatchedOnEdit`.
  - `CancelledOccurrence_DeletesTheRemoteCopy`.
  - `PulledBackCopy_NeverDuplicates`.
  - `AttendeesConnection_DoesNotPushSomeoneElsesMeeting`.
  - `PrivateOccurrence_PushesBusy`.
- [ ] **Step 2:** Run. Expected: FAIL.
- [ ] **Step 3:** Implement. The provider delete methods exist (Google `DeleteEventAsync` / Graph `DeleteEventAsync`); if one is missing, add it with a client test.
- [ ] **Step 4:** Run. Expected: PASS.
- [ ] **Step 5:** Commit `feat(calendar): push the organizer's meeting occurrences to their external calendar`.

### Task 13: Contract, runbook and full verification

**Files:**
- Modify: `docs/api/meetings-v1.md` (link, attendance and calendar fields; error codes), `docs/runbooks/meetings-phase2-release.md` (the Zoom scopes `meeting:read:list_past_participants`, `meeting:read:list_past_instances`; users reconnect)
- Test: full suites

- [ ] **Step 1:** Run `dotnet test tests/ONEVO.Tests.Unit`, `tests/ONEVO.Tests.Architecture` and `tests/ONEVO.Tests.Integration` (output to files). Expected: all green, apart from the two known My Team failures (`LedWorkProgressIntegrationTests.Lead_sees_rolled_up_counts_member_sees_nothing`, `MyTeamCapabilitiesIntegrationTests.CanViewPeopleStatus_requires_both_the_permission_and_a_real_active_coverage_row`).
- [ ] **Step 2:** `dotnet ef migrations has-pending-model-changes` through the scratch helper. Expected: no changes.
- [ ] **Step 3:** Commit `docs(meetings): Plan C contract and runbook`.
