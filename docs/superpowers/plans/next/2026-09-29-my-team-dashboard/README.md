# My Team Dashboard — Implementation Plan (index)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship attendance-only "My Team" V1: capability discovery, Team Status, Approvals & Exceptions, Team Progress, and Priority Actions, each authorized by its owning domain.

**Architecture:** Domain-owned endpoints (TimeAttendance `team/today`, WorkManagement `led-progress`) plus a thin `Features/Dashboard/Team` composition feature (`capabilities`, `action-items`) that only calls domain-declared interfaces. **Frontend route model revised 2026-09-30** (spec §5): no separate `/dashboard/team` route or toggle. My Team is a capability-gated section appended below the existing personal cards on the single `/dashboard` page, plus a root capabilities store and four independent widgets, each individually capability-gated and user-customizable (show/hide/reorder) within its allowed set. This affects only Parts 7-8 (frontend, not yet started) - Parts 1-6 (backend) are unchanged.

**Tech stack:**
- Backend: .NET 10, EF Core 10 / Npgsql, MediatR, xUnit + Moq + FluentAssertions, Testcontainers PostgreSQL.
- Frontend: Angular 21 standalone + signals, `@ngrx/signals`, Vitest via `ng test`.

**Spec:** `docs/superpowers/specs/next/2026-09-29-my-team-dashboard-design.md` (backend repo, commit `f89efd1c`). Executors read the spec section each part cites.

## Parts (execute in order; each part file is self-contained)

| Part | File | Repo | Depends on |
|---|---|---|---|
| 1 | `part-1-resolver-performance.md` | backend | — |
| 2 | `part-2-shared-extractions.md` | backend | — |
| 3 | `part-3-led-work-progress.md` | backend | Part 2 (`ObjectiveTreeExpander`, `TaskProgressClassifier`) |
| 4 | `part-4-team-today.md` | backend | Part 1 (memo), Part 2 (leave scope provider + matcher) |
| 5 | `part-5-action-items.md` | backend | Part 1, Part 3 (`IWorkLeadershipService`) |
| 6 | `part-6-capabilities.md` | backend | Parts 1, 3 |
| 7 | `part-7-frontend-foundation.md` | frontend | Part 6 endpoint contract |
| 8 | `part-8-frontend-sections.md` | frontend | Parts 3–5 endpoint contracts, Part 7 |
| 9 | `part-9-verification.md` | both | all |

**Not in this plan:** **SEC-MON-SCOPE** is a parallel security remediation with its own spec and plan (spec §19.1).
- It does **not** block these parts.
- It **must** merge before Phase 2 live activity can start or ship.

## Global Constraints (every task implicitly includes these; copied from the spec)

- No new permission codes. No migrations. No new tables (no `PriorityAction` table).
- No role-name checks anywhere.
- Never build one shared `teamEmployeeIds` and reuse it across sections.
- WorkManagement code never references `IEmployeeAuthorityResolver`, `IEmployeeVisibilityScopeResolver`, `IEmployeeHierarchyClosureRepository` or `ManagementCoverageRecord`.
- Backend authorization is the boundary; the frontend capability only decides what is offered.
- V1 scope is the caller's active legal entity, resolved exactly like the attendance handlers do:
  `employees.GetDefaultForUserAsync(tenantId, userId).LegalEntityId`.
- No My Team endpoint returns activity, idle, meeting, presence or screenshot data.
  `canViewLiveActivity` is always `false`.
- No Redis, no distributed cache, no cross-request cache of authorization scope. The only cache
  is the request-scoped resolver memo (Part 1).
- `action-items`:
  - no numeric round-trip budget yet;
  - required: no population-size N+1 (equal command counts across small and large fixtures),
    existing predicates reused, one coverage expansion per request, and a per-source baseline
    printed by a test.
- My Day (`/dashboard`) is unchanged except for rendering the mode switch. No Team Snapshot card.

## Execution notes (read once)

**Workspaces.** Use git worktrees only. The frontend main checkout has someone else's staged
work. Never commit from it.

```bash
git -C "C:/Users/User/OneDrive/Desktop/onexso/HRMS-Backend-v1" fetch origin
git -C "C:/Users/User/OneDrive/Desktop/onexso/HRMS-Backend-v1" -c core.longpaths=true worktree add -b feature/my-team-dashboard /tmp/be_myteam origin/development
git -C "C:/Users/User/OneDrive/Desktop/onexso/front end-org/Hrms--Web-application---front-end---v1" fetch origin
git -C "C:/Users/User/OneDrive/Desktop/onexso/front end-org/Hrms--Web-application---front-end---v1" -c core.longpaths=true worktree add -b feature/my-team-dashboard /tmp/fe_myteam origin/Development
```

**Backend test commands** (run from `/tmp/be_myteam`):
- Unit: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~<Name>"`
- Architecture: `dotnet test tests/ONEVO.Tests.Architecture`
- Integration (Docker required): `dotnet test tests/ONEVO.Tests.Integration --filter "FullyQualifiedName~<Name>"`

**Frontend test commands** (run from `/tmp/fe_myteam`):
- `npx ng test --watch=false --include "<glob>"`
- `npx ng build`

**Commit and push.** Commit after every task with the message shown. Never push without the
user's explicit go-ahead.

## Plan-level decisions made while grounding the spec in code (review these)

1. **Absence indistinguishability (strengthens spec D6).** For a subject the caller is **not**
   Leave-authorized for, every `absent` row carries **no** attention fields, whether the cause is
   a no-show or masked approved leave. Otherwise "Absent + critical 'not clocked in'" versus
   "Absent with no attention" would reveal who is on leave. For Leave-authorized subjects,
   no-shows keep the critical attention. (Part 4.)
2. **Clock-in policy status.** Team Status passes policy status `"configured"` to
   `AttendanceDayStatusResolver.Resolve`, exactly like the employee list's batched attendance
   summary (`EfEmployeeRepository.ListVisibleAsync`). The per-employee policy resolution in
   `AttendanceTodayStateService` is not batchable today and would be a population-size N+1.
   (Part 4.)
3. **Identity lookups.** `ICallerIdentityResolver.ResolveIdentitiesByEmployeeIdAsync` loops one
   query per id. The spec text called it "one batch"; it is not. Dashboard code uses the real
   batch `IEmployeeRepository.ListByIdsAsync` instead. (Parts 3, 5.)
4. **Leave actionability.** The existing pending-approvals handler calls `GetStateAsync` per
   pending row (an N+1). The leave action source uses a new batched
   `ILeaveApprovalRepository.ListApprovalModeInputsAsync` feeding the **same**
   `LeaveApprovalModeEvaluator.IsActionable`. The existing list endpoint is left unchanged.
   (Part 5.)
5. **Paged attendance inboxes** (location, work-area, device change). Count and oldest-N are
   taken by calling the **existing** `ListApprovalInboxAsync` with `take: 0` (the count comes
   back in `TotalCount`) and then `skip: total - n`. That reuses the repository predicate with
   zero repository changes, and each workflow exposes one small public method for it.
   (Part 5.)
6. **Priority Actions "exact top 5" caveat.** The `monitoring.exception` source returns its
   oldest 5 across Open ∪ Escalated, ordered escalated-first. So group 1 (Escalated) is exact.
   Group 2 (Open) is exact only when fewer than 5 are escalated; otherwise it shows the oldest
   Open items that fit. This is documented in the composer. (Parts 5, 8.)
