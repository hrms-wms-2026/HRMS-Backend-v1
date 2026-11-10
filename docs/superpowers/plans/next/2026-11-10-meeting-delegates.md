# Meeting Delegates Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Any employee can let another employee run their meetings. The delegate creates, edits, reschedules and cancels the principal's meetings, invitees see the principal as the organizer, and every delegated action is audited.

**Architecture:** A tenant-owned `meeting_delegates` table holds who may act for whom, until when. `MeetingAccessResolver` treats an active delegate as the series' organizer and records who they act for, so the existing organizer checks work unchanged. Create, link, notices, audit and the calendar and planner reads use that "acting for" principal.

**Tech Stack:** ASP.NET Core, MediatR, EF Core on SQL Server with RLS, xUnit and Moq; Angular with signals and vitest.

**Spec:** `docs/superpowers/specs/2026-11-10-meeting-delegates-design.md`

## Global Constraints

- Backend branch `feat/meeting-delegates-backend` and frontend branch `feat/meeting-delegates-frontend`, both cut from the latest `development` / `Development`. Never push the local run branches.
- Exactly one migration, `AddMeetingDelegates`. It calls `RlsSql.CreatePolicy("meeting_delegates", RlsPredicate.Tenant)`, and the table is added to `RlsTableRegistry`.
- **Active employee** means `EmploymentStatusId` is `EmploymentStatusIds.Active` or `EmploymentStatusIds.OnLeave`, the set `MeetingCreateApplier` uses.
- **Active grant** means `revoked_at` is null, `expires_at` is null or later than now (from `IDateTimeProvider`), and both employees are active.
- New error codes go in `MeetingErrorCodes`:
  - `meeting_delegate_self` (400)
  - `meeting_delegate_expiry_invalid` (400)
  - `meeting_delegate_exists` (409)
  - `meeting_delegate_required` (403)
  - `meeting_principal_only` (403, reserved; see Task 3)
- Audit rows use the existing `AuditLog` and `IAuditLogRepository`. They are added before the write's `SaveChangesAsync`. They never contain guest emails, descriptions or notes, and a private meeting's title is `"(private)"`.
- Legacy calendar events (`CalendarEvent`, `EventMeetingLinkService`) are not touched. The create popover's form is frozen and is not changed.
- Backend tests: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "<name>"`, then the full unit and architecture suites before each commit. Integration tests need Docker, because they use Testcontainers SQL Server.
- Frontend tests: `npx ng test --watch=false --include=<spec path>`. The pre-push hook runs a build, so a worktree needs `npm ci` first.
- Commit messages are written to a file and passed with `git commit -F`, and end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. Never use `--no-verify`.

## Review Focus

1. **Private meetings must not leak.** A caller who is not an active delegate gets 403 from `forEmployeeId` and no private meeting anywhere. Covered in Task 7.
2. **A revoked, expired or inactive delegate** loses access on the next request and gets no further notices. Covered in Tasks 1, 3 and 6.
3. **A delegate who is also an attendee** gets Organizer, not Attendee. Covered in Task 3.
4. **A request that needs approval** goes in the principal's name. Covered in Task 4.
5. **The audit row commits with the action, or not at all,** and never carries guest emails or notes. Covered in Task 8.

---

### Task 1: The delegation table and repository

**Files:**
- Create: `src/ONEVO.Domain/Features/WorkManagement/Meetings/Entities/MeetingDelegate.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Configurations/WorkManagement/Meetings/MeetingDelegateConfiguration.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Meetings/RepositoryInterfaces/IMeetingDelegateRepository.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/Meetings/EfMeetingDelegateRepository.cs`
- Modify: `ApplicationDbContext.cs` (a DbSet), `RlsTableRegistry.cs` (`["meeting_delegates"] = RlsPredicate.Tenant`), and the DI registration beside `IMeetingActionItemRepository`
- Create: migration `AddMeetingDelegates`, generated with `dotnet ef migrations add`, plus the `CreatePolicy` call
- Test: `tests/ONEVO.Tests.Integration/WorkManagement/Meetings/MeetingDelegateRepositoryIntegrationTests.cs`

**Interfaces:**
- Produces `MeetingDelegate : ITenantOwnedEntity` with `Id`, `TenantId`, `PrincipalEmployeeId`, `DelegateEmployeeId`, `CreatedByUserId`, `CreatedAt`, `ExpiresAt?` and `RevokedAt?`.
- Produces a unique filtered index on `(tenant_id, principal_employee_id, delegate_employee_id) WHERE revoked_at IS NULL`.
- Produces `IMeetingDelegateRepository`. Every "active" filter takes `DateTimeOffset now`:
  - `void Add(MeetingDelegate row)`
  - `Task<MeetingDelegate?> GetUnrevokedTrackedAsync(Guid tenantId, Guid id, CancellationToken ct)`
  - `Task<MeetingDelegate?> GetUnrevokedTrackedForPairAsync(Guid tenantId, Guid principal, Guid delegateId, CancellationToken ct)`
  - `Task<IReadOnlyList<MeetingDelegate>> ListUnrevokedForPrincipalAsync(Guid tenantId, Guid principal, CancellationToken ct)`
  - `Task<IReadOnlyList<Guid>> ListActiveDelegateIdsAsync(Guid tenantId, Guid principal, DateTimeOffset now, CancellationToken ct)`
  - `Task<IReadOnlyList<Guid>> ListActivePrincipalIdsAsync(Guid tenantId, Guid delegateId, DateTimeOffset now, CancellationToken ct)`
  - `Task<bool> IsActiveDelegateAsync(Guid tenantId, Guid principal, Guid delegateId, DateTimeOffset now, CancellationToken ct)`

- [ ] **Step 1:** Write the tests:
  - `IsActiveDelegate_IsTrueForAnActiveGrant_AndFalseOnceRevoked`
  - `IsActiveDelegate_IsFalseOnceExpired`
  - `IsActiveDelegate_IsFalseWhenEitherEmployeeIsNotActive`
  - `ASecondUnrevokedGrantForThePair_IsRejectedByTheUniqueIndex`
  - `AnotherTenant_CannotSeeTheRows`, in the style of `SqlServerRlsProofTests`
- [ ] **Step 2:** Run them. Expected: they fail to compile.
- [ ] **Step 3:** Implement the entity, configuration, DbSet, registry entry, migration and repository.
- [ ] **Step 4:** Run the tests (Docker on) and `TenantIsolationArchitectureTests`. Expected: pass.
- [ ] **Step 5:** Commit: `feat(meetings): table for meeting delegates`.

### Task 2: Grant, list and revoke

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/Meetings/Commands/MeetingDelegateCommands.cs` and `Contracts/MeetingDelegateContracts.cs`
- Modify: `MeetingsController.cs` and `MeetingErrorCodes.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Meetings/MeetingDelegateCommandTests.cs`

**Interfaces:**
- Consumes Task 1.
- Produces `MeetingDelegateView(Guid Id, Guid EmployeeId, string Name, DateTimeOffset CreatedAt, DateTimeOffset? ExpiresAt, string State)`, where `State` is `"active"` or `"expired"`.
- Produces these routes, all on the caller's own list:
  - `GET api/v1/meetings/delegates`
  - `POST api/v1/meetings/delegates` with `{ delegateEmployeeId, expiresAt? }`, returning 201
  - `DELETE api/v1/meetings/delegates/{id}`, returning 204, or 404 when the row is not mine
  - `GET api/v1/meetings/delegates/principals`, returning the active principals as `MeetingDelegateView` with `EmployeeId` set to the principal
- A POST for a pair whose unrevoked row has expired revokes that row and creates a new one, in one transaction.
- Consumes `IMeetingDelegateAudit` (Task 8). Until Task 8 lands, the handlers take it as a dependency and the tests mock it.

- [ ] **Step 1:** Write the tests:
  - `Add_CreatesAnActiveGrantForTheCaller`
  - `Add_WithAFutureExpiry_StoresIt`
  - `Add_WithAPastExpiry_Is400MeetingDelegateExpiryInvalid`
  - `Add_Self_Is400MeetingDelegateSelf`
  - `Add_AnInactiveOrUnknownEmployee_Is404`
  - `Add_AnActiveExistingGrant_Is409MeetingDelegateExists`
  - `Add_OverAnExpiredGrant_ReplacesIt`
  - `Revoke_SetsRevokedAt_OnlyForTheCallersOwnRow`
  - `List_MarksExpiredGrants`
  - `ListPrincipals_ReturnsOnlyActivePrincipals`
  - `GrantAndRevoke_EachRecordOneAudit`
- [ ] **Step 2:** Run them. Expected: fail.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run the tests and the full suites.
- [ ] **Step 5:** Commit: `feat(meetings): let anyone name meeting delegates`.

### Task 3: Delegates act as the organizer

**Files:**
- Modify: `MeetingAccessResolver.cs`
- Modify: `MeetingContracts.cs` (`MeetingOccurrenceResponse` gains `string? ScheduledByName`)
- Modify: `MeetingReadService.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Meetings/MeetingAccessResolverTests.cs` (new)
- Test: `tests/ONEVO.Tests.Architecture/MeetingOrganizerActionsArchitectureTests.cs` (new)

**Interfaces:**
- `MeetingAccess(string Role, MeetingSeries Series, MeetingDetails? Details, Guid? ActingForEmployeeId = null)`.
- The resolver takes `IMeetingDelegateRepository` and `IDateTimeProvider`. After the organizer check and before the attendee checks, an active delegate of `series.OrganizerEmployeeId` gets `Organizer`, with `ActingForEmployeeId` set to the organizer.
- `ScheduledByName` is filled only for the Organizer role, and only when `series.CreatedById` is not the organizer's user.
- `OrganizerOnlyActions_AreTheReviewedList` reads the source of `src/ONEVO.Application/Features/WorkManagement/Meetings/Services/*.cs`. It asserts that the files containing `MeetingRoles.Organizer` checks are exactly the reviewed set:
  - `MeetingAttendeeService`
  - `MeetingBulkInviteService`
  - `MeetingGuard`
  - `MeetingReadService`
  - `SeriesMeetingLinkService`
  - `MeetingAttendanceReadService`
  - `MeetingActionItemService`
  - `MeetingAccessResolver`

  A new file in that set fails the test, and its message says to decide whether delegates may use it (spec "Access").

- [ ] **Step 1:** Write the tests:
  - `AnActiveDelegate_GetsOrganizer_ActingForThePrincipal`, for series and occurrence
  - `ADelegateWhoIsAlsoAnAttendee_GetsOrganizer`
  - `ARevokedExpiredOrInactiveDelegate_GetsNothing`
  - `TheDelegateOfSomeoneElse_GetsNothing`
  - `TheOrganizer_HasNoActingFor`
  - `ScheduledByName_ShowsTheDelegate_ToTheOrganizerRoleOnly`
  - `OrganizerOnlyActions_AreTheReviewedList`
- [ ] **Step 2:** Run them. Expected: the resolver tests fail, and the architecture test passes, which pins today's list.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run the tests and the full suites. Existing organizer checks must pass untouched.
- [ ] **Step 5:** Commit: `feat(meetings): a delegate runs the principal's meetings as organizer`.

### Task 4: Create a meeting for the principal

**Files:**
- Modify: `MeetingContracts.cs` (`CreateMeetingRequest` gains a trailing `Guid? OrganizerEmployeeId = null`)
- Modify: `CreateMeetingCommandHandler.cs`, `MeetingSeriesFactory.cs` and `MeetingCreateApplier.cs`
- Test: `CreateMeetingCommandHandlerTests.cs`

**Interfaces:**
- `IMeetingSeriesFactory.CreateDirectAsync` gains `Guid createdByUserId` after `organizerUserId`, and the factory sets `CreatedById = createdByUserId`. The applier passes the requester's user id for both.
- When the request names another organizer, the handler:
  - checks `IsActiveDelegateAsync`, or returns 403 `meeting_delegate_required`;
  - uses the principal for placement, for the `WorkAction` requester and as the organizer;
  - passes the principal's user as `organizerUserId` and the caller's user as `createdByUserId`;
  - records `meeting.created_for_principal` through `IMeetingDelegateAudit` (Task 8).

- [ ] **Step 1:** Write the tests:
  - `ACreateForAPrincipal_UsesThePrincipalAsOrganizer_AndTheCallerAsCreator`
  - `ACreateForSomeoneWhoDidNotDelegate_Is403MeetingDelegateRequired`
  - `ACreateForAPrincipal_ResolvesPlacementForThePrincipal`
  - `AnApprovalRequestForAPrincipal_IsSubmittedInThePrincipalsName`
  - `ACreateForAPrincipal_RecordsOneAudit_AndACreateForYourselfRecordsNone`
  - `NamingYourselfAsOrganizer_BehavesLikeToday`
- [ ] **Step 2:** Run them. Expected: fail.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run the tests and the full suites.
- [ ] **Step 5:** Commit: `feat(meetings): create a meeting on the principal's behalf`.

### Task 5: The link comes from the organizer's account

**Files:**
- Modify: `SeriesMeetingLinkService.cs` (`RegenerateAsync`)
- Test: `SeriesMeetingLinkServiceTests.cs`. Extend it if PR #223 is merged, otherwise create it.

**Interfaces:**
- `RegenerateAsync` looks up the connection for the series organizer's user, not `callerUserId`.
- When the caller is not the organizer and the organizer has no usable connection, the 409 message names the organizer: "<Name> needs to connect their calendar account to add an online meeting."

- [ ] **Step 1:** Write the tests:
  - `ADelegateAddingALink_UsesThePrincipalsConnection`
  - `ADelegateWithTheirOwnConnection_StillGets409_WhenThePrincipalHasNone`
  - `TheOrganizer_StillUsesTheirOwnConnection`
- [ ] **Step 2:** Run them. Expected: fail.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run the tests and the full suites.
- [ ] **Step 5:** Commit: `fix(meetings): the online link always comes from the organizer's account`.

### Task 6: Organizer notices also reach delegates

**Files:**
- Modify: `MeetingNotifier.cs` (`NotifyOrganizerAsync` and the RSVP notice near line 202)
- Test: `MeetingNotifierTests.cs`

**Interfaces:**
- `MeetingNotifier` takes `IMeetingDelegateRepository`.
- A private `OrganizerAndDelegateUserIdsAsync(MeetingSeries)` returns the organizer's user followed by each active delegate's user, without duplicates. Each notice goes once per user.
- "Meeting unresolved" and "replacement nominated" come only from legacy calendar events, which are out of scope.

- [ ] **Step 1:** Write the tests:
  - `ALinkProblemNotice_ReachesTheOrganizerAndEachActiveDelegate`
  - `AnRsvpNotice_ReachesTheOrganizerAndEachActiveDelegate`
  - `ARevokedOrExpiredDelegate_GetsNoNotice`
- [ ] **Step 2:** Run them. Expected: fail.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run the tests and the full suites.
- [ ] **Step 5:** Commit: `feat(meetings): organizer notices also go to delegates`.

### Task 7: Read the principal's calendar and planner

**Files:**
- Modify: `GetCalendarEventsQuery.cs` and its handler, and `CalendarController.cs` (`GET`)
- Modify: `PlannerCommands.cs` (`GetMyPlannerRangeQuery` and its handler), and `PlannerController.cs` (`me/range`)
- Test: `GetCalendarEventsQueryHandlerTests.cs` and a planner range handler test

**Interfaces:**
- `GetCalendarEventsQuery(DateTimeOffset From, DateTimeOffset To, Guid? ForEmployeeId = null)`.
- `GetMyPlannerRangeQuery(DateOnly From, DateOnly To, Guid? ForEmployeeId = null)`.
- An empty value, or the caller themselves, means today's behaviour. For another person the caller must be their active delegate, or gets 403 `meeting_delegate_required`. Each existing lookup then uses the principal's employee and user.

- [ ] **Step 1:** Write the tests:
  - `ADelegate_SeesThePrincipalsCalendar_PrivateMeetingsIncluded`
  - `ANonDelegate_Is403_AndSeesNothing`
  - `AnExpiredDelegate_Is403`
  - `NoForEmployeeId_BehavesLikeToday`
  - The same four for the planner range.
- [ ] **Step 2:** Run them. Expected: fail.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run the tests and the full suites.
- [ ] **Step 5:** Commit: `feat(calendar): a delegate can read the principal's calendar and planner`.

### Task 8: Audit every delegated action

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/Meetings/Services/MeetingDelegateAudit.cs`, for `IMeetingDelegateAudit` and its implementation
- Modify, adding one call after each successful change, before save:
  - `MeetingEditService`
  - `MeetingMoveService`
  - `MeetingCancelService`
  - `MeetingAttendeeService`
  - `MeetingBulkInviteService`
  - `SeriesMeetingLinkService`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Meetings/MeetingDelegateAuditTests.cs`, plus one case per service in its existing test file

**Interfaces:**
- `IMeetingDelegateAudit`:
  - `Task RecordAsync(MeetingAccess access, string action, object newValues, object? oldValues, CancellationToken ct)` does nothing when `access.ActingForEmployeeId` is null.
  - `Task RecordDelegationAsync(MeetingDelegate row, string action, CancellationToken ct)` always writes a row.
- Action names, `ResourceType` and the JSON fields are exactly the spec's "Audit log" table. `UserId` is the current user, and `IpAddress` is set where the request has one.
- The private-title mask and the "no guest emails, descriptions or notes" rule are enforced inside `RecordAsync`, so callers cannot get it wrong.

- [ ] **Step 1:** Write the tests:
  - `NoActingFor_WritesNothing`
  - `ADelegatedEdit_WritesOneRow_WithTheDelegateAsUser_AndThePrincipalInTheJson`
  - `APrivateMeetingsTitle_IsMasked`
  - `GuestEmailsDescriptionAndNotes_NeverAppear`
  - `Grant_And_Revoke_UseTheMeetingDelegateResourceType`
  - In each service's tests: `ADelegate<Action>_RecordsItsAudit`, for edit, move, cancel, attendees, bulk invite and link.
- [ ] **Step 2:** Run them. Expected: fail.
- [ ] **Step 3:** Implement the service, register it, and add the calls.
- [ ] **Step 4:** Run the tests and the full suites.
- [ ] **Step 5:** Commit: `feat(meetings): audit what delegates do on a principal's meetings`.

After Task 8, push `feat/meeting-delegates-backend`. A PR is opened only when the user asks.

---

### Task 9: Frontend data access

**Files:**
- Modify: `meeting-api.service.ts`, `meeting.models.ts`, `calendar-event-api.service.ts` and `planner-api.service.ts`, with their specs

**Interfaces:**
- On `MeetingApiService`:
  - `listDelegates()`
  - `addDelegate(employeeId: string, expiresAt?: string)`
  - `revokeDelegate(id: string)`
  - `listPrincipals()`
- `CreateMeetingRequest.organizerEmployeeId?: string`, `MeetingOccurrence.scheduledByName?: string | null`, and `MeetingDelegateView.state: 'active' | 'expired'`.
- The calendar events and planner range calls take an optional `forEmployeeId`, sent only when set.

- [ ] **Step 1:** Write the specs: each method calls the right URL and method, and `forEmployeeId` and `expiresAt` are left out when not set.
- [ ] **Step 2:** Run them. Expected: fail.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run them. Expected: pass.
- [ ] **Step 5:** Commit: `feat(meetings): data access for meeting delegates`.

### Task 10: The "Meeting delegates" dialog

**Files:**
- Create: `src/app/modules/calendar/ui/meeting-delegates-modal/meeting-delegates-modal.component.ts`, following `inbox/ui/approval-delegate-modal`: the people picker plus an optional "Until" date picker
- Modify: `calendar-page.component.ts` and its `.html` (a header button)
- Test: `meeting-delegates-modal.component.spec.ts`

- [ ] **Step 1:** Write the specs:
  - `lists my delegates with Active or Expired`
  - `adds the picked person, with and without an Until date`
  - `removes one`
  - `shows the server error for self, duplicate and a past date`
- [ ] **Step 2:** Run them. Expected: fail.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run them. Expected: pass.
- [ ] **Step 5:** Commit: `feat(calendar): manage meeting delegates`.

### Task 11: The principal's calendar view

**Files:**
- Modify: `calendar-page.component.ts` and its `.html`, and `meeting-form-modal.component.ts`
- Test: a new `calendar-page.delegates.spec.ts` beside `calendar-page.planner.spec.ts`, and the meeting form spec

**Interfaces:**
- `viewingFor = signal<string | null>(null)`. The selector is shown only when `listPrincipals()` returns someone.
- A set `viewingFor` is passed as `forEmployeeId` to the calendar and planner loads.
- On a principal's view, the create popover offers only the meeting tab, and a create sends `organizerEmployeeId`. The popover form itself is not changed.
- The full meeting form gets an "Organizer" select (Me, then each principal), shown only for someone with principals.

- [ ] **Step 1:** Write the specs:
  - `the selector is hidden for someone with no principals`
  - `choosing a principal reloads the calendar and planner for them`
  - `on a principal's calendar the popover offers only the meeting tab`
  - `a meeting created there is sent with organizerEmployeeId`
  - `the meeting form's organizer select sends the chosen principal`
- [ ] **Step 2:** Run them. Expected: fail.
- [ ] **Step 3:** Implement. The planner reload `effect` must not read any signal that the planner response changes. That loop caused the 429 storm fixed in `3d22739b`.
- [ ] **Step 4:** Run them. Expected: pass.
- [ ] **Step 5:** Commit: `feat(calendar): view and schedule on a principal's calendar`.

### Task 12: "Organized by" and "Scheduled by" in the drawer

**Files:**
- Modify: `meeting-occurrence-drawer.component.ts`
- Test: `meeting-occurrence-drawer.component.spec.ts`

- [ ] **Step 1:** Write the specs:
  - `shows Organized by with the organizer's name`
  - `shows Scheduled by when scheduledByName is set`
  - `shows no Scheduled by line when it is null`
- [ ] **Step 2:** Run them. Expected: fail.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run the drawer and calendar specs. Expected: pass.
- [ ] **Step 5:** Commit: `feat(meetings): show who organized and who scheduled a meeting`.

After Task 12, run the frontend lint and the meeting and calendar specs, then push `feat/meeting-delegates-frontend`.
