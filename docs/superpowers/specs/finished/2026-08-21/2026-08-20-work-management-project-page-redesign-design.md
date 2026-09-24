# Work Management — Project Page Redesign (design)

**Status:** Approved by user 2026-08-20. Backend implementation plan written same day; frontend plan
follows once backend is deployed (user's explicit order: backend first).

**Scope:** Work Management module only, both repos (`HRMS-Backend-v1`, `Hrms--Web-application---front-end---v1`).
Do not touch other modules (`organization`, `layouts/main-layout`, `public`, admin/platform, CoreHR)
in either repo — a teammate owns that code. See `docs/superpowers/rules/PROCESS_RULES.md` and this
repo's existing Work Management conventions.

## 1. Goal

Redesign the `/work` Projects page: card grid → single-row list, with a side "Explanation" panel that
loads a selected project's full detail. Add a 4-step creation wizard. Add project-level membership
(today membership is Objective-scoped only) without a schema migration, by reusing the project's
existing auto-created "Default Objective". Add in-app notifications, routed through the existing
Outbox, for project-member-invited and project-member-accepted.

## 2. Current state (verified against code 2026-08-20, not assumed from older docs)

- `Project` entity already has: `CategoryId` (required FK to `ProjectCategory`), `Name`, `Identifier`,
  `Description`, `LeadId`, `StartDate`, `TargetDate`, `Color`, `AllocatedHours`, `CompletedHours`,
  `IsActive`, `IsAchieved`/`AchievedAt`. No `Logo`/`Banner` columns — the cover image is a separate
  `EntityAsset` row (`OwnerType=Project`, `AssetPurpose="project_cover"`).
- `CreateProjectCommandHandler` already makes the creator both `Project.LeadId` and a `ProjectMember`
  of the auto-created Default Objective (`Objective.IsDefault = true`, owned by the same creator).
  Project categories already exist as a full CRUD-adjacent lookup (`GET /work/project-categories`).
- `ProjectMember`/`ProjectMemberInvitation` both have a **required, non-nullable `ObjectiveId`** —
  membership is always Objective-scoped by schema today. There is no project-level-only membership row.
- The existing `AddObjectiveMemberCommandHandler` → `ProjectMemberInvitation` (Pending) →
  `AcceptObjectiveInvitationCommandHandler` → `IMilestoneMembershipCoordinator.UpsertMembershipAsync`
  flow is the only membership-request mechanism today, and it is objective-owner-gated
  (`objective.OwnerId != callerEmployeeId`), not project-owner-gated.
- `INotificationDispatcher.SendTemplatedAsync` exists and is called **synchronously, inline, inside the
  handler's own transaction** at 9 existing call sites (task/sprint/allocation events) — it is **not**
  routed through the Outbox today. The Outbox (`IOutboxWriter.EnqueueAsync` → `OutboxMessage` row →
  `OutboxProcessor` background poller → `IOutboxMessageHandler` keyed by `Type`) is a separate, proven
  mechanism used today only for transactional emails.
- Neither project-member-invited nor project-member-accepted has any notification today (grepped both
  handlers directly — confirmed absent, not just undocumented).

## 3. Key decisions

### 3.1 Project-level membership: reuse the Default Objective, no migration

Rejected alternative: making `ObjectiveId` nullable on `ProjectMember`/`ProjectMemberInvitation` (a real
migration + new partial unique index + repository changes). Chosen instead: every Project already has
exactly one Default Objective, auto-created at creation, owned by the project's `LeadId`. "Add a project
member" = invite them into that Default Objective, through a **new, separately-gated** command
(`AddProjectMemberCommandHandler`) that checks `project.LeadId == callerEmployeeId` instead of
`objective.OwnerId == callerEmployeeId`. Everything downstream — the `ProjectMemberInvitation` row, the
existing `MyInvitationsPanelComponent` UI, `AcceptObjectiveInvitationCommandHandler`,
`IMilestoneMembershipCoordinator` — is reused completely unchanged. Adding a member to a *real* (non-
default) Objective later continues to use the existing, untouched `AddObjectiveMemberCommandHandler`.

**Why this is safe:** `IMilestoneMembershipCoordinator.GetActiveAssigneeAsync` does not require the
target to already be a project member — a brand-new person can already be invited straight into any
objective today (confirmed in `AddObjectiveMemberCommandHandler.cs`), so routing through the Default
Objective introduces no new invariant the coordinator doesn't already support.

### 3.2 Banner image: new, separate from Logo

`UploadPurposeCatalog` gets a new purpose `project_banner` (image rules identical to `project_cover`:
5MB, png/jpeg/webp). A second, independent optional upload on create — this is a real backend addition,
not a rename of Logo.

### 3.3 Release date dropped from the create form

`CreateProjectCommand.ReleaseDate` becomes optional; when absent, the handler defaults the "Initial
Release" `ProjectVersion`'s `ReleaseCalendarEntry.ScheduledDate` to `request.TargetDate`. `Description`
stays (unchanged, already existed).

### 3.4 Notifications: first Outbox-routed notification pair (deliberately, per explicit user request)

The user explicitly asked for these two new notifications to go through the Outbox — this is a
deliberate deviation from the 9 existing synchronous call sites, not a retrofit of them (those stay
exactly as they are; out of scope). A new generic `WorkNotificationOutboxHandler` (payload: tenant,
recipient user id, template code, placeholders, related-entity type/id) is added so any future
Work Management notification can also opt into async/Outbox dispatch the same way, without inventing a
new mechanism per event.

- **`work_project_member_invited`** — fired by the new `AddProjectMemberCommandHandler`, always (this
  handler only ever represents a project-level invite).
- **`work_project_member_accepted`** — fired from `AcceptObjectiveInvitationCommandHandler`, but
  **only when the accepted invitation's objective `IsDefault == true`** (i.e. it was really a
  project-level invite). Accepting a real, non-default Objective invitation continues to send no
  notification — that was not asked for and is out of scope.

## 4. Frontend (design agreed, plan to follow after backend ships)

- Grid removed. Each list row: project name, owner avatar, an allocated/completed-hours progress bar
  (new `app-progress-bar`, since only a circular `progress-ring` exists today).
- First project auto-selected on page load; selecting a row loads a side `ProjectExplanationCardComponent`
  (details, member avatar icons only, `View project` / `Achieve project` buttons, an owner-only `Member`
  button opening a new members-management popup modeled on the existing `ObjectiveMembersPopupComponent`).
- Hand-built page header replaced with the existing, unused-in-this-module `app-breadcrumb-header`.
- Create modal becomes a 4-step wizard (Category tabs → Details incl. Description, no release date →
  Members, optional, staged client-side → Preview & confirm), following the existing hand-rolled wizard
  convention from `AddEmployeeWizardComponent` (no shared Stepper component exists in this codebase).

## 5. Non-functional / conventions to follow

- CQRS command/query + handler pattern under `Features/WorkManagement/...`, `Result<T>` return
  convention, `[RequirePermission]` + `[Idempotent]` controller attributes matching sibling endpoints.
- Every new/changed handler resolves identity via `ICallerIdentityResolver` — never compares
  `_currentUser.UserId` directly against an ownership column.
- Every finished endpoint gets a `docs/postman-request/Work Management/<Endpoint Name>.md` file per
  `PROCESS_RULES.md` rule 6 (method+route, auth/permission/idempotency line, description, request/response
  body examples, error-status table, Source section).
- RLS/tenant isolation: no new tables in this plan, so no new RLS policy is needed — all new rows go
  through existing tenant-scoped entities (`ProjectMemberInvitation`, `EntityAsset`, `OutboxMessage`,
  `Notification`), whose policies already exist.

## 6. Testing

Full `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~WorkManagement` and the existing
architecture test suite must stay green throughout. Each plan part lists its own new/changed test files.
A `dotnet build` of the whole solution is acceptable to confirm compilation, but per
`feedback_scope_work_management_only`, do not `dotnet run`/kill any other running process while doing so.

## 7. Out of scope (explicitly, to prevent scope creep)

- Migrating the 9 existing synchronous `INotificationDispatcher` call sites to the Outbox.
- Any notification for accepting a *non-default* Objective invitation.
- Any change to `calendar_events` or other Core HR / other-module tables.
- Frontend implementation (separate plan, after backend ships).
