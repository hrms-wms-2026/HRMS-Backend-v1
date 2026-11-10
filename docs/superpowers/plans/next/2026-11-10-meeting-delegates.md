# Meeting Delegates Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Any employee can let another employee run their meetings. The delegate creates, edits, reschedules and cancels the principal's meetings, and invitees see the principal as the organizer.

**Architecture:** A tenant-owned `meeting_delegates` table holds who may act for whom. `MeetingAccessResolver` treats an active delegate as the series' organizer, so every existing organizer check works unchanged. Create, link, notices and the calendar and planner reads gain an explicit "acting for" principal.

**Tech Stack:** ASP.NET Core, MediatR, EF Core on SQL Server with RLS, xUnit and Moq; Angular with signals and vitest.

**Spec:** `docs/superpowers/specs/2026-11-10-meeting-delegates-design.md`

## Global Constraints

- Backend branch `feat/meeting-delegates-backend` and frontend branch `feat/meeting-delegates-frontend`, both cut from the latest `development` / `Development`. Never push the local run branches.
- Exactly one migration, `AddMeetingDelegates`. It calls `RlsSql.CreatePolicy("meeting_delegates", RlsPredicate.Tenant)`, and the table is added to `RlsTableRegistry`.
- "Active employee" means `EmploymentStatusId` is `EmploymentStatusIds.Active` or `EmploymentStatusIds.OnLeave`, the same set `MeetingCreateApplier` uses.
- New error codes go in `MeetingErrorCodes`: `meeting_delegate_self` (400), `meeting_delegate_exists` (409), `meeting_delegate_required` (403).
- Legacy calendar events (`CalendarEvent`, `EventMeetingLinkService`) are not touched.
- The create popover's form is frozen and is not changed.
- Backend tests: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "<name>"`, then the full unit and architecture suites before each commit. Integration tests need Docker, because they use Testcontainers SQL Server.
- Frontend tests: `npx ng test --watch=false --include=<spec path>`. The pre-push hook runs a build, so a worktree needs `npm ci` first.
- Commit messages are written to a file and passed with `git commit -F`, and end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. Never use `--no-verify`.

## Review Focus

1. **Private meetings must not leak.** A caller who is not a delegate of the principal gets 403 from `forEmployeeId` and no private meeting in any list. Covered in Task 7.
2. **A revoked or inactive delegate** loses access on the next request, including during an edit, and gets no further organizer notices. Covered in Tasks 3 and 6.
3. **A delegate who is also an attendee** gets the Organizer role, not Attendee. Covered in Task 3.
4. **A request that needs approval** is submitted in the principal's name, so the approval and its decision notice reach the principal, not the delegate. Covered in Task 4.
5. **The link always uses the principal's connection.** A delegate whose own Teams account is connected still cannot add a link when the principal has none. Covered in Task 5.

---

### Task 1: The delegation table and repository

**Files:**
- Create: `src/ONEVO.Domain/Features/WorkManagement/Meetings/Entities/MeetingDelegate.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Configurations/WorkManagement/Meetings/MeetingDelegateConfiguration.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Meetings/RepositoryInterfaces/IMeetingDelegateRepository.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/Meetings/EfMeetingDelegateRepository.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/ApplicationDbContext.cs` (add a DbSet)
- Modify: `src/ONEVO.Infrastructure/Persistence/Rls/RlsTableRegistry.cs` (add `["meeting_delegates"] = RlsPredicate.Tenant`)
- Create: migration `AddMeetingDelegates` (generated with `dotnet ef migrations add`, plus the `CreatePolicy` call)
- Modify: the DI registration where `IMeetingActionItemRepository` is registered
- Test: `tests/ONEVO.Tests.Integration/WorkManagement/Meetings/MeetingDelegateRepositoryIntegrationTests.cs`

**Interfaces:**
- Produces `MeetingDelegate : ITenantOwnedEntity` with `Id`, `TenantId`, `PrincipalEmployeeId`, `DelegateEmployeeId`, `CreatedByUserId`, `CreatedAt` and `RevokedAt?`.
- Produces a unique filtered index on `(tenant_id, principal_employee_id, delegate_employee_id) WHERE revoked_at IS NULL`.
- Produces `IMeetingDelegateRepository`:
  - `void Add(MeetingDelegate row)`
  - `Task<MeetingDelegate?> GetActiveTrackedAsync(Guid tenantId, Guid id, CancellationToken ct)`
  - `Task<IReadOnlyList<MeetingDelegate>> ListActiveForPrincipalAsync(Guid tenantId, Guid principalEmployeeId, CancellationToken ct)`
  - `Task<IReadOnlyList<Guid>> ListActiveDelegateIdsAsync(Guid tenantId, Guid principalEmployeeId, CancellationToken ct)`, active employees only
  - `Task<IReadOnlyList<Guid>> ListActivePrincipalIdsAsync(Guid tenantId, Guid delegateEmployeeId, CancellationToken ct)`, active employees only
  - `Task<bool> IsActiveDelegateAsync(Guid tenantId, Guid principalEmployeeId, Guid delegateEmployeeId, CancellationToken ct)`, true only when the row is not revoked and both employees are active

- [ ] **Step 1:** Write `MeetingDelegateRepositoryIntegrationTests`:
  - `IsActiveDelegate_IsTrueForAnActiveGrant_AndFalseOnceRevoked`
  - `IsActiveDelegate_IsFalseWhenEitherEmployeeIsNotActive`
  - `ASecondActiveGrantForThePair_IsRejectedByTheUniqueIndex`
  - `AnotherTenant_CannotSeeTheRows`, the RLS check, in the style of `SqlServerRlsProofTests`
- [ ] **Step 2:** Run them. Expected: they fail to compile, because the types do not exist.
- [ ] **Step 3:** Add the entity, configuration, DbSet, registry entry, migration and repository.
- [ ] **Step 4:** Run the integration tests (Docker on) and `TenantIsolationArchitectureTests`. Expected: pass.
- [ ] **Step 5:** Commit: `feat(meetings): table for meeting delegates`.

### Task 2: Grant, list and revoke

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/Meetings/Commands/MeetingDelegateCommands.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Meetings/Contracts/MeetingDelegateContracts.cs`
- Modify: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/MeetingsController.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Meetings/Contracts/MeetingErrorCodes.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Meetings/MeetingDelegateCommandTests.cs`

**Interfaces:**
- Consumes `IMeetingDelegateRepository` (Task 1).
- Produces `MeetingDelegateView(Guid Id, Guid EmployeeId, string Name, DateTimeOffset CreatedAt)`.
- Produces these routes, all acting on the caller's own list:
  - `GET api/v1/meetings/delegates` returns my delegates.
  - `POST api/v1/meetings/delegates` with `{ delegateEmployeeId }` returns 201 with a `MeetingDelegateView`.
  - `DELETE api/v1/meetings/delegates/{id}` revokes, returning 204, or 404 when the row is not mine.
  - `GET api/v1/meetings/delegates/principals` returns the people I may act for, as a `MeetingDelegateView` list.

- [ ] **Step 1:** Write `MeetingDelegateCommandTests`:
  - `Add_CreatesAnActiveGrantForTheCaller`
  - `Add_Self_Is400MeetingDelegateSelf`
  - `Add_AnInactiveOrUnknownEmployee_Is404`
  - `Add_AnExistingActiveGrant_Is409MeetingDelegateExists`
  - `Revoke_SetsRevokedAt_AndOnlyForTheCallersOwnRow`, where someone else's row gets 404
  - `ListPrincipals_ReturnsOnlyActivePrincipalsOfTheCaller`
- [ ] **Step 2:** Run them. Expected: fail.
- [ ] **Step 3:** Implement the handlers and the controller routes.
- [ ] **Step 4:** Run the tests, then the full unit and architecture suites. Expected: pass.
- [ ] **Step 5:** Commit: `feat(meetings): let anyone name meeting delegates`.

### Task 3: Delegates act as the organizer

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/Meetings/Services/MeetingAccessResolver.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Meetings/Contracts/MeetingContracts.cs` (`MeetingOccurrenceResponse` gains `string? ScheduledByName`)
- Modify: `src/ONEVO.Application/Features/WorkManagement/Meetings/Services/MeetingReadService.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Meetings/MeetingAccessResolverTests.cs` (create it)
- Test: the existing read-service tests for the occurrence view

**Interfaces:**
- `MeetingAccess(string Role, MeetingSeries Series, MeetingDetails? Details, Guid? ActingForEmployeeId = null)`.
- `MeetingAccessResolver` takes `IMeetingDelegateRepository`. In both resolve methods, after the organizer check and before the attendee checks, an active delegate of `series.OrganizerEmployeeId` gets `Organizer`, with `ActingForEmployeeId` set to the organizer.
- `ScheduledByName` is filled only when the role is Organizer and `series.CreatedById` is not the organizer's user.

- [ ] **Step 1:** Write the tests:
  - `AnActiveDelegate_GetsOrganizer_ActingForThePrincipal`, for both series and occurrence
  - `ADelegateWhoIsAlsoAnAttendee_GetsOrganizer`
  - `ARevokedOrInactiveDelegate_GetsNothing`
  - `TheDelegateOfSomeoneElse_GetsNothing`
  - `ScheduledByName_ShowsTheDelegate_ToTheOrganizerRoleOnly`
- [ ] **Step 2:** Run them. Expected: fail.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run the tests and the full suites. Every existing organizer check should still pass untouched.
- [ ] **Step 5:** Commit: `feat(meetings): a delegate runs the principal's meetings as organizer`.

### Task 4: Create a meeting for the principal

**Files:**
- Modify: `MeetingContracts.cs` (`CreateMeetingRequest` gains a trailing `Guid? OrganizerEmployeeId = null`)
- Modify: `src/ONEVO.Application/Features/WorkManagement/Meetings/Commands/CreateMeeting/CreateMeetingCommandHandler.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Meetings/Services/MeetingSeriesFactory.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Meetings/Appliers/MeetingCreateApplier.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Meetings/CreateMeetingCommandHandlerTests.cs`

**Interfaces:**
- `IMeetingSeriesFactory.CreateDirectAsync` gains `Guid createdByUserId` after `organizerUserId`, and the factory sets `CreatedById = createdByUserId`. The applier passes the requester's user id for both.
- When the handler's request names another organizer:
  - It checks `IsActiveDelegateAsync`, or returns 403 `meeting_delegate_required`.
  - It loads the principal's employee row.
  - It uses the principal for `_placements.ResolveAsync`, for the `WorkAction` requester and as the organizer.
  - It passes the principal's user as `organizerUserId` and the caller's user as `createdByUserId`.

- [ ] **Step 1:** Write the tests:
  - `ACreateForAPrincipal_UsesThePrincipalAsOrganizer_AndTheCallerAsCreator`
  - `ACreateForSomeoneWhoDidNotDelegate_Is403MeetingDelegateRequired`
  - `ACreateForAPrincipal_ResolvesPlacementForThePrincipal`
  - `AnApprovalRequestForAPrincipal_IsSubmittedInThePrincipalsName`
  - `NamingYourselfAsOrganizer_BehavesLikeToday`
- [ ] **Step 2:** Run them. Expected: fail.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run the tests and the full suites.
- [ ] **Step 5:** Commit: `feat(meetings): create a meeting on the principal's behalf`.

### Task 5: The link comes from the organizer's account

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/Meetings/Services/SeriesMeetingLinkService.cs` (`RegenerateAsync`)
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Meetings/SeriesMeetingLinkServiceTests.cs`. Extend it if PR #223 is merged, otherwise create it.

**Interfaces:**
- `RegenerateAsync` looks up the connection for the series organizer's user, not `callerUserId`.
- When the caller is not the organizer and the organizer has no usable connection, the 409 `ProviderNotConnected` message names the organizer: "<Name> needs to connect their calendar account to add an online meeting."

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
- Modify: `src/ONEVO.Application/Features/WorkManagement/Meetings/Services/MeetingNotifier.cs` (`NotifyOrganizerAsync` and the RSVP notice near line 202)
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Meetings/MeetingNotifierTests.cs`

**Interfaces:**
- `MeetingNotifier` takes `IMeetingDelegateRepository`.
- A private `OrganizerAndDelegateUserIdsAsync(MeetingSeries)` returns the organizer's user followed by each active delegate's user, without duplicates.
- Each organizer-addressed notice is sent once per user.
- The spec's "meeting unresolved" and "replacement nominated" notices come only from legacy calendar events (`EventMeetingLinkService`, `RespondToCalendarEventCommandHandler`), which are out of scope. A meeting-as-task sends only the link and RSVP notices to its organizer.

- [ ] **Step 1:** Write the tests:
  - `ALinkProblemNotice_ReachesTheOrganizerAndEachActiveDelegate`
  - `AnRsvpNotice_ReachesTheOrganizerAndEachActiveDelegate`
  - `ARevokedDelegate_GetsNoNotice`
- [ ] **Step 2:** Run them. Expected: fail.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run the tests and the full suites.
- [ ] **Step 5:** Commit: `feat(meetings): organizer notices also go to delegates`.

### Task 7: Read the principal's calendar and planner

**Files:**
- Modify: `src/ONEVO.Application/Features/Calendar/Queries/GetCalendarEvents/GetCalendarEventsQuery.cs` and its handler
- Modify: `src/ONEVO.Api/Controllers/Tenant/Calendar/CalendarController.cs` (the `GET` query parameter)
- Modify: `src/ONEVO.Application/Features/WorkManagement/Planner/Commands/PlannerCommands.cs` (`GetMyPlannerRangeQuery` and its handler)
- Modify: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/PlannerController.cs` (`me/range`)
- Test: `tests/ONEVO.Tests.Unit/Features/Calendar/GetCalendarEventsQueryHandlerTests.cs` and a planner range handler test

**Interfaces:**
- `GetCalendarEventsQuery(DateTimeOffset From, DateTimeOffset To, Guid? ForEmployeeId = null)`.
- `GetMyPlannerRangeQuery(DateOnly From, DateOnly To, Guid? ForEmployeeId = null)`.
- An empty value, or the caller themselves, means today's behaviour. For another person, the caller must be their active delegate, or gets 403 `meeting_delegate_required`.
- For a principal, the handler resolves the principal's employee and user, and every existing lookup uses those instead of the caller's.

- [ ] **Step 1:** Write the tests:
  - `ADelegate_SeesThePrincipalsCalendar_PrivateMeetingsIncluded`
  - `ANonDelegate_Is403_AndSeesNothing`
  - `NoForEmployeeId_BehavesLikeToday`
  - The same three for the planner range.
- [ ] **Step 2:** Run them. Expected: fail.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run the tests and the full suites.
- [ ] **Step 5:** Commit: `feat(calendar): a delegate can read the principal's calendar and planner`.

After Task 7, push `feat/meeting-delegates-backend`. A PR is opened only when the user asks.

---

### Task 8: Frontend data access

**Files:**
- Modify: `src/app/modules/work/meetings/data-access/meeting-api.service.ts` and `src/app/modules/work/meetings/models/meeting.models.ts`
- Modify: `src/app/modules/calendar/data-access/calendar-event-api.service.ts`
- Modify: `src/app/modules/work/planner/data-access/planner-api.service.ts`
- Test: the matching `*.spec.ts` files

**Interfaces:**
- `listDelegates()`, `addDelegate(employeeId)`, `revokeDelegate(id)` and `listPrincipals()` on `MeetingApiService`.
- `CreateMeetingRequest.organizerEmployeeId?: string`, and `MeetingOccurrence.scheduledByName?: string | null`.
- The calendar events and planner range calls take an optional `forEmployeeId`, sent as a query parameter only when set.

- [ ] **Step 1:** Write the spec cases: each method calls the right URL and method, and `forEmployeeId` is left out when not set.
- [ ] **Step 2:** Run them. Expected: fail.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run them. Expected: pass.
- [ ] **Step 5:** Commit: `feat(meetings): data access for meeting delegates`.

### Task 9: The "Meeting delegates" dialog

**Files:**
- Create: `src/app/modules/calendar/ui/meeting-delegates-modal/meeting-delegates-modal.component.ts`, following `inbox/ui/approval-delegate-modal`, with the people picker and no dates
- Modify: `src/app/modules/calendar/feature/calendar-page/calendar-page.component.ts` and its `.html` (a header button that opens it)
- Test: `meeting-delegates-modal.component.spec.ts`

- [ ] **Step 1:** Write the specs:
  - `lists my delegates`
  - `adds the picked person`
  - `removes one`
  - `shows the server error for self and duplicate`
- [ ] **Step 2:** Run them. Expected: fail.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run them. Expected: pass.
- [ ] **Step 5:** Commit: `feat(calendar): manage meeting delegates`.

### Task 10: The principal's calendar view

**Files:**
- Modify: `calendar-page.component.ts` and its `.html`
- Test: `calendar-page.component.spec.ts`, or a new `calendar-page.delegates.spec.ts` beside `calendar-page.planner.spec.ts`

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
- [ ] **Step 3:** Implement. Watch the `effect` reads: the planner reload effect must not read any signal that the planner response changes. That loop caused the 429 storm fixed in `3d22739b`.
- [ ] **Step 4:** Run them. Expected: pass.
- [ ] **Step 5:** Commit: `feat(calendar): view and schedule on a principal's calendar`.

### Task 11: "Organized by" and "Scheduled by" in the drawer

**Files:**
- Modify: `src/app/modules/work/meetings/feature/meeting-occurrence-drawer/meeting-occurrence-drawer.component.ts`
- Test: `meeting-occurrence-drawer.component.spec.ts`

- [ ] **Step 1:** Write the specs:
  - `shows Organized by with the organizer's name`
  - `shows Scheduled by when scheduledByName is set`
  - `shows no Scheduled by line when it is null`
- [ ] **Step 2:** Run them. Expected: fail.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run the drawer specs and the calendar specs. Expected: pass.
- [ ] **Step 5:** Commit: `feat(meetings): show who organized and who scheduled a meeting`.

After Task 11, run the frontend lint and the full meeting and calendar specs, then push `feat/meeting-delegates-frontend`.
