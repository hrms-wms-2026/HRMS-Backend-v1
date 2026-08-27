# Part 2: Add Project Member (project-level invite via the Default Objective)

**Read first:** `docs/superpowers/specs/next/2026-08-20-work-management-project-page-redesign-design.md`
§3.1 for the full "why no migration" reasoning. This part is self-contained — you don't need Part 1, but
Part 3 depends on this part being done first (it wires notifications into the handler this part creates).

**Scope guard:** Work Management module only, same boundary as Part 1. Do not touch CoreHR, org, or any
other module's files.

**Status:** done (backend)

## Goal

A project's owner (`Project.LeadId`) can invite an employee to become a project member, **without**
needing to name a specific (non-default) Objective. This reuses the project's existing, always-present
Default Objective (`Objective.IsDefault == true`) as the underlying invitation target — the invitee ends
up with a `ProjectMember` row scoped to that Default Objective, which is what "being a project member"
already means everywhere else in this codebase today.

**Do not** modify `AddObjectiveMemberCommandHandler`, `AcceptObjectiveInvitationCommandHandler`, or
`RejectObjectiveInvitationCommandHandler`'s existing logic in this part — accept/reject are reused
completely as-is (the invitee already sees this in their existing invitations list, since it's a normal
`ProjectMemberInvitation` row). Part 3 touches `AcceptObjectiveInvitationCommandHandler` only to add a
notification call, not to change its membership logic.

## Files to create

- `src/ONEVO.Application/Features/WorkManagement/Projects/Commands/AddProjectMember/AddProjectMemberCommand.cs`
- `src/ONEVO.Application/Features/WorkManagement/Projects/Commands/AddProjectMember/AddProjectMemberCommandHandler.cs`
- `tests/ONEVO.Tests.Unit/Features/WorkManagement/AddProjectMemberCommandHandlerTests.cs`
- `docs/postman-request/Work Management/Add Project Member.md`

## Files to modify

- `src/ONEVO.Api/Controllers/Tenant/WorkManagement/ProjectsController.cs` — new action
  `POST api/v1/work/projects/{id}/members`.

## Before writing code

Open and read in full:
- `AddObjectiveMemberCommandHandler.cs` (the handler you're structurally mirroring — same DI shape:
  `ICurrentUser`, `ICallerIdentityResolver`, `IMilestoneMembershipCoordinator`,
  `IProjectMemberInvitationRepository`, `IUnitOfWork`; plus `IProjectRepository` and `IObjectiveRepository`
  since this new handler needs to load the Project first, then its Default Objective).
- `IProjectRepository` and `IObjectiveRepository` — find the method that gets a project's Default
  Objective (search for `IsDefault` usages — `CreateProjectCommandHandler` creates it but does not query
  it back; check if a `GetDefaultObjectiveForProjectAsync`-style method already exists on
  `IObjectiveRepository`, or if you need to add one). If none exists, add the smallest possible read
  method (e.g. `GetDefaultObjectiveAsync(Guid tenantId, Guid projectId, ...)`) to `IObjectiveRepository`/
  `EfObjectiveRepository` rather than querying `DbContext` directly from the handler (match the existing
  repository-per-aggregate convention).
- `DeleteProjectCommandHandler.cs` / `AchieveProjectCommandHandler.cs` for the exact `project.LeadId !=
  callerEmployeeId.Value` owner-gate phrasing and `Result<T>.Forbidden(...)` message style — reuse the
  identical wording convention, don't invent new phrasing.

## Tasks (small, do in order, one commit per task)

1. **`AddProjectMemberCommand`**: `record AddProjectMemberCommand(Guid ProjectId, Guid EmployeeId) :
   IRequest<Result<AddObjectiveMemberOutcomeResponse>>` — reuse the existing
   `AddObjectiveMemberOutcomeResponse` DTO as-is (don't invent a new response shape; the outcome —
   already-member vs. a new pending invitation — is identical in both flows).

2. **`AddProjectMemberCommandHandler` — auth + project load**: authenticate, resolve
   `callerEmployeeId` via `ICallerIdentityResolver` (identical boilerplate to every other handler you've
   read this session). Load the project via `IProjectRepository.GetByIdForTenantAsync`; `NotFound` if
   missing or `!project.IsActive`. `Forbidden("Only the project owner can add members.")` if
   `project.LeadId != callerEmployeeId.Value`.
   - Test: non-owner caller → `Forbidden`; inactive/missing project → `NotFound`.

3. **Load the Default Objective**: using whatever repository method Task 0's investigation settled on.
   If somehow absent (shouldn't happen — every project gets one at creation — but defend anyway),
   `Failure("This project has no default milestone; contact support.")` rather than a null-ref.
   - Test: cover this defensive branch with a mocked repository returning null, even though it's not
     reachable via the real creation flow — the handler must not throw.

4. **Resolve target + delegate to the same invitation-creation shape as
   `AddObjectiveMemberCommandHandler`**: `IMilestoneMembershipCoordinator.GetActiveAssigneeAsync` →
   `Failure` if not an active employee. Check `HasActiveMembershipAsync(tenantId, defaultObjective.ProjectId,
   defaultObjective.Id, assignee.Id, ct)` → if already an active member, `Success(AlreadyMember: true,
   Invitation: null)` (same shortcut as the objective flow). Check
   `GetPendingForObjectiveAndEmployeeAsync(tenantId, defaultObjective.Id, assignee.Id, ct)` → `Conflict`
   if a pending invite already exists. Otherwise create the `ProjectMemberInvitation` row identically to
   `AddObjectiveMemberCommandHandler` (same fields: `ProjectId = defaultObjective.ProjectId, ObjectiveId =
   defaultObjective.Id, InvitedEmployeeId = assignee.Id, InviteType = ProjectInvitationTypes.Member,
   Status = Pending, InvitedById = callerEmployeeId.Value`), `AddAsync` + `SaveChangesAsync`.
   - Tests: already-active-member short-circuit returns `AlreadyMember: true`; pending-invite-exists →
     `Conflict`; happy path → `Success`, invitation persisted with `ObjectiveId == defaultObjective.Id`
     and `InviteType == Member`; target employee not active/not found → `Failure`.

5. **Controller action**: `[HttpPost("{id:guid}/members")] [RequirePermission("projects:access")]
   [Idempotent]` (match whatever attributes the sibling `AddObjectiveMember` controller action uses —
   copy them exactly, don't guess). Request body: `{ EmployeeId: Guid }`.
   - No new test needed here beyond the handler tests above unless this repo has controller-level tests
     for sibling actions — if it does, mirror one.

6. **Postman doc**: `docs/postman-request/Work Management/Add Project Member.md`, full 6-section format
   per rule 6 — Source section must link this plan file plus the handler/controller files.

## Data flow

`POST /work/projects/{projectId}/members` `{ employeeId }` → controller → `AddProjectMemberCommand` →
handler resolves caller, loads Project (owner-gate), loads Default Objective, resolves target employee,
creates a `Pending` `ProjectMemberInvitation` scoped to `(ProjectId, DefaultObjectiveId, EmployeeId)` →
**unchanged downstream**: invitee sees it via the existing "my invitations" query
(`MyInvitationsPanelComponent` frontend-side, whatever query backs it server-side — do not touch that
query), accepts via the existing, untouched `AcceptObjectiveInvitationCommandHandler`, which creates
their `ProjectMember` row via `IMilestoneMembershipCoordinator.UpsertMembershipAsync`. They are now a
project member with zero real (non-default) Objectives — exactly the product requirement.

## Security

Authorization is `project.LeadId == callerEmployeeId` — deliberately **not** `defaultObjective.OwnerId`
even though today those are always the same person (set identically at creation) — gate on the field that
actually represents "project owner" so this stays correct if project leadership and default-objective
ownership are ever allowed to diverge later. Tenant isolation: every repository call already goes through
`GetByIdForTenantAsync`/tenant-scoped queries — don't bypass with a raw `DbContext` query.

## Non-functional

Follow the exact CQRS/`Result<T>`/DI pattern of `AddObjectiveMemberCommandHandler` — this handler should
read as a near-twin of that one, differing only in the owner-gate field and where the Objective comes
from (loaded via the project's default, not passed in on the wire).

## Definition of done

- All 6 tasks committed individually.
- `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~WorkManagement` green.
- Full solution `dotnet build` compiles clean.
- `docs/postman-request/Work Management/Add Project Member.md` created, accurate to the real DTOs.
- Manually confirm (via the test suite, not a live click-through) that inviting into the Default
  Objective does NOT let the invitee bypass any achieve/complete gating that assumes a "real" Objective —
  re-read `objective.IsAchieved` check in the mirrored handler; the Default Objective can itself become
  achieved when the whole project is achieved, so an invite to an achieved project should correctly fail
  the same `Failure("Cannot add members to an achieved milestone.")` check the objective-scoped handler has.
