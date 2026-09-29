# My Team Dashboard (`/dashboard/team`) — Design Spec

**Status:** Architecture approved 2026-09-29 (chat). This document is pending the user's review; no
implementation plan exists yet.

**Goal:** Give any user who manages people, approves requests, or leads Work Management modules a
"My Team" view of the dashboard. My Team sits beside the existing personal "My Day" dashboard and
answers four questions: who is in and who is out today, what is waiting for my decision, how the
work I lead is progressing, and what I should act on first.

**Architecture:** `/dashboard` (My Day, unchanged) + a lazy child route `/dashboard/team` (My
Team), switched by a dashboard-level `My Day | My Team` control. My Team is not one population.
Each section is computed by the backend domain that owns the data and is authorized by that
domain's existing rules:
- **People I Manage** follows the People/Attendance authority model.
- **Work I Lead** follows Work Management project/module ownership.

A single lightweight capability endpoint decides whether My Team is offered at all.

**Tech stack:** .NET 10 / EF Core / PostgreSQL (RLS), MediatR CQRS, cookie-session `TenantPolicy`.
Angular standalone components + signals, NgRx Signal Store.

**Repos:**
- Backend: `hrms-wms-2026/HRMS-Backend-v1`. This document lives here.
- Frontend: `hrms-wms-2026/Hrms--Web-application---front-end---v1`. Its work is fully specified
  in §15–§17 and §20.

**Evidence base:** two read-only investigations of `origin/development` `e9bb9b5a` (backend) and
`origin/Development` `8b50f1a` (frontend) on 2026-09-29. Every repository claim below cites the
file it came from.

## Global Constraints

- **No new permission codes.** Every gate reuses an existing permission or an existing
  Work Management relationship (§6). No migration is required by this spec.
- **No role-name checks.** Nothing may branch on "Manager", "HR Manager", or any other role name.
- **Never build a shared `teamEmployeeIds` list** and reuse it across sections.
  - People I Manage populations are resolved per section, inside the owning domain.
  - Work I Lead never reads `ManagementCoverageRecord`, `EmployeeAuthorityResolver`,
    `EmployeeVisibilityScopeResolver` or `EmployeeHierarchyClosure`.
- **Backend authorization is the boundary.** The frontend capability check only decides what is
  offered. Every endpoint re-authorizes, and every deep-link destination re-authorizes.
- **Single legal entity.** V1 is scoped to the caller's active legal entity: the session
  `legal_entity_id` claim, set in `TenantDatabaseTicketStore`, which is the `LegalEntityId` of the
  caller's default employee record.
- **Monitoring stays off in V1.**
  - No live monitoring data (Active / Idle / Meeting / Offline) is returned by any My Team
    endpoint until the separate monitoring remediation (§12) has shipped.
  - `CanViewLiveActivity` is hard-wired to `false` in V1.
- **No new tables and no new caching infrastructure.**
  - No `PriorityAction` table and no other new table.
  - No Redis, no distributed cache, and no cross-request cache of authorization scope.
- **My Day is untouched.** No Team Snapshot card is added to My Day in V1.

---

## 1. Locked product decisions

| # | Decision |
|---|---|
| D1 | Routes are `/dashboard` (My Day) and `/dashboard/team` (My Team). There is no top-level `/team`. |
| D2 | My Team has two populations with separate authorization: **People I Manage** and **Work I Lead**. |
| D3 | A Work Management lead with no organizational coverage may use My Team. Tab availability is capability-based, not role-based and not a flat frontend permission any-of. |
| D4 | Team Progress = work inside modules the caller **effectively owns** (owner of the module or of any ancestor module), including sub-modules. Plain membership never qualifies. There is no "Blocked" metric. |
| D5 | Live activity is shown only when the caller has monitoring capability **and** the employee is inside the caller's authorized coverage. The monitoring remediation is a hard prerequisite, so V1 is attendance-only. |
| D6 | Attendance visibility shows **Absent**. "On leave" and leave details are returned only when the caller is Leave-authorized for that employee. The backend omits the data; the frontend does not merely hide it. |
| D7 | Priority Actions is a presentation layer over existing actionable entities. It keeps source domain, authorization, routing and a deep link. It has no table. The user's personal overdue tasks are excluded. Ordering is deterministic and grouped, with no scoring. |
| D8 | V1 covers the active legal entity only. Multi-legal-entity support is a future consideration. |
| D9 | Visibility-resolver cost is reduced by batching plus request-scoped memoization only. |

## 2. Out of scope (V1)

- Live Active / Idle / Meeting / Offline, until §12 ships. The Phase 2 contract is in §8.1.4.
- Onboarding access-grant requests (`GET /onboarding/access-grant-requests/pending-for-me`, gated
  by `roles:manage`). This is administrative access provisioning, not team work. It is a V2
  candidate.
- Objective invitations (`/objectives/invitations/mine`). These are invitations *to* the caller,
  not decisions *about* someone else.
- Aggregating multiple legal entities.
- Changing any existing module page, except the leave approvals deep-link parameter (§15.3).
- The permission cleanup items from the investigation (§19).

---

## 3. Repository facts this design depends on

| Fact | Evidence |
|---|---|
| The dashboard is a hardcoded personal page; `/dashboard/*` already nests sub-routes (`insights`, `activity-timeline`) | FE `app.routes.ts`; `modules/dashboard/feature/dashboard-home/dashboard-home.component.ts` |
| ONEXSO's own-vs-team pattern is a module-local `'my' \| 'team'` mode | FE `leave/feature/my-balances` (`BalanceViewMode`); `attendance/feature/time-tracking` (`AttendanceViewMode`) |
| The canonical org visibility service is `IEmployeeAuthorityResolver.ResolveVisibilityAsync`: coverage records of the actor's primary position, expanded to position holders + reporting subtree, department trees and company-wide, in one legal entity | `Application/Features/CoreHr/EmployeeAuthority/Services/EmployeeAuthorityResolver.cs` |
| The resolver checks the permission through raw role assignments, so it fails closed alongside `[RequirePermission]` | `Infrastructure/.../Auth/Login/EfPermissionRepository.UserHasPermissionCodeAsync` |
| The resolver is registered scoped, has no memoization, and loops per covered position and per covered department | `Application/DependencyInjection.cs` L100; resolver L105–169 |
| Batch holder lookup already exists | `IPositionAssignmentRepository.GetActiveHoldersByPositionIdsAsync` |
| Work Management visibility is relationship-based; other people's projects are forbidden | `WorkManagement/Projects/Queries/ListProjects/ListProjectsQueryHandler.cs` |
| Work Management code comments state coverage is unrelated to Work Management | `Tasks/Queries/GetProjectTasks/...Handler.cs`; `Objectives/Queries/GetObjectiveMembers/...Handler.cs` |
| Effective owner = owner of the module or of any ancestor module; effective manager also counts plain members | `Objectives/Services/MilestoneMembershipCoordinator.cs` (`IsEffectiveOwnerAsync`, `IsEffectiveManagerAsync`) |
| Sprint management uses effective **owner** | `Sprints/Services/SprintAccessService.cs` |
| The root/default module is owned by the project creator | `Projects/Commands/CreateProject/CreateProjectCommandHandler.cs` |
| Work approvals routing | Task creation/edit → module owner (`GetPendingForOwnerEmployeeIdAsync`). Objective change + allocation → `Objective.ReportingManagerId` (`ApproveObjectiveChangeRequestCommandHandler`). Status-template change → root effective manager (`TaskStatusChangeAccessService`). |
| Task completion rule: `MarksTaskComplete \|\| ProgressPercent >= 100`; overdue = `DueDate < today` | `Tasks/Queries/GetMyTaskProgress/GetMyTaskProgressQueryHandler.cs` |
| Task status categories are only `not_started`, `active`, `done` | `Domain/.../Tasks/Entities/TaskStatus.cs` |
| Exceptions have `Status` (Open / Acknowledged / Resolved / Escalated), `DetectedAt`, `EscalatedAt`, and **no severity** | `Domain/Features/Monitoring/Exceptions/Entities/Exception.cs` |
| Exception scope: HR (`employees:write` / `exceptions:manage`) is unrestricted; otherwise coverage + approver-of, minus self | `Monitoring/Exceptions/Services/ExceptionScopeResolver.cs` |
| Attendance team view: `attendance:read` + resolver (`IncludeSelf:false`), self stripped; returns only employees with a record | `TimeAttendance/Queries/AttendanceReadHandlers.cs` L150–170 |
| Attendance status is leave-aware (`on_time_off`, `worked_during_time_off`) | `TimeAttendance/Services/AttendanceDayStatusResolver.cs`; `AttendanceReadHandlers.BuildRowsAsync` |
| The schedule is legal-entity level and pure | `TimeAttendance/Services/AttendanceScheduleResolver.Resolve(legalEntity, utcNow)` |
| Leave visibility = `leave:read` / `leave:manage` unrestricted; `leave:read-team` uses raw coverage (position / department / company); `leave:read-own` = self | `Leave/Calendar/Queries/GetLeaveCalendarQuery.ResolveScopeAsync`; `EfLeaveCalendarRepository` L44–59 |
| Leave pending approvals come from stored approver rows | `Leave/Approval/Queries/LeaveApprovalQueries.cs` (`ListPendingForApproverAsync`) |
| `/auth/me` is the Auth session contract, loaded once at bootstrap and covered by architecture tests | `Auth/Login/Queries/GetCurrentSession`; FE `core/auth/state/auth.store.ts`; `tests/ONEVO.Tests.Architecture/AuthContractArchitectureTests.cs` |
| Deep-link targets exist | FE `/attendance/time-tracking?view=team&type=…&requestId=…` (`legacy-approval-redirect`); `/attendance/alerts?alertId=`; `/time-off/team-approvals`; BE `GET /work/notification-navigation` |

---

## 4. Architecture

```text
Frontend                                        Backend (owning domain)
────────────────────────────────────────────    ─────────────────────────────────────────────
MyTeamCapabilitiesStore (root) ───────────────▶ GET /api/v1/dashboard/team/capabilities
                                                    (Dashboard composition → CoreHr probe + WM probe)
/dashboard        DashboardHome (My Day, unchanged; + mode switch)
/dashboard/team   TeamDashboard
   ├─ TeamStatusCard ──────────────────────────▶ GET /api/v1/attendance/time-tracking/team/today
   │                                                (TimeAttendance · People I Manage)
   ├─ ApprovalsExceptionsCard ─┐
   ├─ PriorityActionsCard ─────┼──────────────▶ GET /api/v1/dashboard/team/action-items
   │                           │                    (Dashboard composition over domain-owned sources:
   │                           │                     Leave, TimeAttendance, Monitoring.Exceptions, WM)
   └─ TeamProgressCard ────────┴──────────────▶ GET /api/v1/work/led-progress
                                                    (WorkManagement · Work I Lead)
```

- **Domain-owned endpoints.** Team Status and Team Progress are ordinary queries in their own
  domains, under their own controllers and module gates, like every existing Home card.
- **A thin composition feature.** `Application/Features/Dashboard/Team/` owns no entity and no
  repository. It contains two queries, *capabilities* and *action-items*, and calls only
  interfaces declared by the owning domains (§9.4).
  - It exists because approvals span five domains.
  - Serving them from one request is what lets the request-scoped coverage memo (§13) collapse
    five visibility resolutions into one.
- **Priority Actions makes no backend call of its own.** The frontend composes it from the
  `action-items` and `led-progress` responses it already holds (§8.4).

## 5. Routes, navigation, and the mode switch

| Path | Component | Guard |
|---|---|---|
| `/dashboard` | `DashboardHomeComponent` (existing, unchanged except that it renders the switch) | `authGuard` (existing) |
| `/dashboard/team` | `TeamDashboardComponent` (new, lazy `loadComponent`) | `authGuard` + new `myTeamGuard` |
| `/dashboard/insights`, `/dashboard/activity-timeline` | unchanged | unchanged |

- **The switch.** `DashboardModeSwitchComponent` is two links, `My Day` → `/dashboard` and
  `My Team` → `/dashboard/team`, marked up as `<nav aria-label="Dashboard view">` with
  `aria-current="page"`.
  - It is rendered at the top of both pages.
  - It renders nothing when `capabilities.isAvailable !== true`, which includes while the
    capabilities call is still loading on a cold cache. My Day never waits on it.
- **`myTeamGuard`.**
  - It awaits `MyTeamCapabilitiesStore.ensureLoaded()`.
  - If `isAvailable` is false, it redirects to `/dashboard`.
  - If the capabilities call fails, it lets navigation through; the page then shows the error
    state (§16). A capability outage must not strand a user who deep-linked in.
- **The sidebar is unchanged.** "Home" (`path: '/dashboard'`, no `exact`) stays highlighted on
  `/dashboard/team` (`nav-items.config.ts` L4).
- **No remembered mode in V1.** `/dashboard` always opens My Day.
- **Copy.**
  - Tab: "My Team". Never "Manager Dashboard".
  - Section headings name their population: "People I manage", "Work I lead".
  - There is no single team headcount anywhere on the page.

---

## 6. Widget Authorization Matrix

| Widget | Capability | Population | Backend Authorization | Data Source |
|---|---|---|---|---|
| Team Status | `canViewPeopleStatus` | **People I Manage**: `ResolveVisibilityAsync(attendance:read, IncludeSelf:false)` in the active legal entity, self removed | `[RequirePermission("attendance:read")]` + resolver scope in the handler | `AttendanceRecord` (today), approved leave overlap, `AttendanceScheduleResolver`, clock-in policy, breaks → `AttendanceDayStatusResolver` |
| Team Status — leave detail | none of its own (per-subject check) | Subjects inside the Leave visibility scope | Leave scope provider (§9.3): `leave:read` / `leave:manage` unrestricted; `leave:read-team` raw coverage; otherwise none | `LeaveRequest` (approved, covering today) |
| Team Status — live activity (Phase 2, **blocked**) | `canViewLiveActivity` (false in V1) | People I Manage ∩ monitoring scope | `monitoring:read` + `ResolveVisibilityAsync(monitoring:read)`, after §12 | `ActivitySnapshot`, `MeetingSignal`, `TrayDeviceRegistration.LastSeenAt` |
| Approvals & Exceptions — people sources | `canReviewPeopleApprovals`, `canReviewExceptions` | Each source's **existing** routing (not one shared list) | Leave: `leave:approve` + stored approver rows. Attendance corrections / work-area / location / device: `attendance:approve` + the same resolver inbox predicate as the list endpoints. Exceptions: `ExceptionScopeResolver.ResolveAsync(forAction:false)` | Existing inbox repositories (count + top N, §9.4) |
| Approvals & Exceptions — work sources | `hasWorkApprovals` | **Work I Lead** routing | Work module gate + relationship: module owner (task creation/edit), `ReportingManagerId` (objective change / allocation), `TaskStatusChangeAccess.CanEditDirectly` (status-template change) | Existing Work request repositories |
| Team Progress | `leadsWork` | **Work I Lead**: modules the caller effectively owns + all their sub-modules, in active projects of the active legal entity | Work module gate (`RequireAnyModule`, same list as `TasksController`); relationship computed in the handler; **no coverage** | `Objective`, `WorkTask`, `TaskStatus`, `TaskAssignment` |
| Priority Actions | shown when at least one of its inputs is shown | Union of the two items above, each item keeping its own source | Inherited from the source responses; nothing new is exposed | `action-items` top items + `led-progress` overdue tasks (frontend composition) |

**Capability → section rule:**
- A section renders **only** when its capability is true.
- A section whose capability is false is not rendered at all. It is not shown as a "locked"
  placeholder.

---

## 7. Capability discovery

### 7.1 Why a dedicated endpoint

The user asked to extend an existing Home/bootstrap response if a suitable one exists. None does:
- **`/auth/me`** is the Auth session contract.
  - It is loaded once on bootstrap and sits on the sign-in critical path.
  - It is locked by `AuthContractArchitectureTests`.
  - Adding CoreHr and Work Management probes would couple Auth to both domains. It would also
    run these queries for every app boot, including users who never open Home.
- **`AttendanceTodayResponse`** (already loaded on Home) belongs to TimeAttendance. It cannot
  carry Work leadership, and it is absent when the time-attendance module is inactive.

So My Team uses `GET /api/v1/dashboard/team/capabilities`.
- It is called **only when `/dashboard` or `/dashboard/team` renders**, which is one extra
  lightweight call per Home visit.
- The frontend keeps the last result in the root store for the session and renders from it
  immediately (stale-while-revalidate). Revisiting Home does not flash or block.

### 7.2 Contract

```text
GET /api/v1/dashboard/team/capabilities        [Authorize(TenantPolicy)], no RequirePermission
200 MyTeamCapabilitiesResponse
{
  isAvailable: bool,                  // OR of every flag below except canViewLiveActivity
  canViewPeopleStatus: bool,          // Team Status section
  canReviewPeopleApprovals: bool,     // leave / attendance approval sources
  canReviewExceptions: bool,          // exception source
  hasWorkApprovals: bool,             // work approval sources
  leadsWork: bool,                    // Team Progress section
  canViewLiveActivity: false,         // hard false in V1 (§12)
  legalEntityId: guid | null          // the active legal entity every section uses
}
```

### 7.3 Computation (all cheap: existence probes, no scope expansion)

| Flag | Rule |
|---|---|
| `canViewPeopleStatus` | `HasPermission("attendance:read")` **and** `IEmployeeAuthorityResolver.HasAnyManagedCoverageAsync(request with RequiredPermission "attendance:read")`. This is a new resolver method: actor employee in the legal entity → raw permission check → active primary assignment → any **active** coverage row. No expansion. |
| `canReviewPeopleApprovals` | `HasPermission("leave:approve") \|\| HasPermission("attendance:approve")` |
| `canReviewExceptions` | The existing `ExceptionScopeResolver` entry gate: `employees:write \|\| exceptions:manage \|\| attendance:approve \|\| exceptions:view` (constants in `ExceptionPermissions`) |
| `leadsWork` | A Work module is active **and** `IWorkLeadershipService.LeadsAnyWorkAsync`: one `EXISTS` for an active objective with `OwnerId == caller`, `!IsAchieved`, in an active project whose `OwningLegalEntityId` = active legal entity. Owning any module implies effectively owning it, so no ancestor walk is needed. |
| `hasWorkApprovals` | A Work module is active **and** `IWorkLeadershipService.HasPendingWorkApprovalsAsync`: `EXISTS` over the four Work routing predicates of §8.2 |

- **Query budget:** at most 10 DB round trips.
- **Target latency:** p95 < 150 ms on the dev dataset.
- **Architecture constraint:** `HasAnyManagedCoverageAsync` receives the permission code as a
  parameter, per `EmployeeAuthorityResolverArchitectureTests.EmployeeAuthorityResolver_NeverHardcodesAPermissionCode`.

---

## 8. Widgets

### 8.1 Team Status (People I Manage)

**Business question:** Who in the people I manage is working, on break, done, late, or absent
today, and who needs attention?

#### 8.1.1 Endpoint

`GET /api/v1/attendance/time-tracking/team/today?limit=50`
- Lives on the existing `TimeTrackingController`.
- `[RequirePermission("attendance:read")]`.
- Query: `GetCoveredTeamTodayQuery(int Limit)`; `limit` 1..200, default 50.

#### 8.1.2 Handler rules

1. **Actor and legal entity.**
   - Resolve the actor the same way `AttendanceReadHandler` does.
   - The legal entity is `actor.LegalEntityId`. If it is missing → 404, reusing the message
     "Current employee record was not found.".
2. **Population.**
   - `ResolveVisibilityAsync(actorUserId, legalEntityId, "attendance:read", IncludeSelf:false, TimeTrackingRead)`.
   - Then remove the actor's own id, exactly as in `GetCoveredAttendanceHistory`.
3. **Load everything in batches** (no per-employee queries):
   - today's `AttendanceRecord`s for the population;
   - breaks for those employees;
   - approved leave covering today for the population (`ILeaveRequestReadRepository.ListApprovedCoveringAsync`);
   - the legal entity (timezone, schedule via `AttendanceScheduleResolver.Resolve`);
   - clock-in policy via the policy list the today-state service already reads (`ListByLegalEntityAsync`), resolved per employee in memory;
   - employee identities (name, avatar file id, position title) via `ListEmployeeIdentitiesAsync`.
4. **Status.** Every employee in the population, **including those with no record**, gets a
   status from `AttendanceDayStatusResolver.Resolve`. Without a record, the schedule comes from
   the legal-entity schedule.
5. **Leave masking (D6).** Evaluated per subject against the Leave scope (§9.3):

| Resolver output | Leave-authorized for subject | Otherwise |
|---|---|---|
| status `on_time_off` | `on_leave` + `leave` block | `absent` (label "Absent"), `leave: null` |
| attention `worked_during_time_off` | kept | attention removed; the working status stays |
| any other | unchanged | unchanged |

6. **Team status vocabulary** (mapped from existing constants; no new domain states):

| Team status | Mapped from |
|---|---|
| `working` | `active` |
| `on_break` | `on_break` |
| `clocked_out` | `clocked_out` |
| `late` | record arrival status `late` while working or on break. It is an overlay flag `isLate`, not a replacement status. |
| `absent` | `not_clocked_in` with `ShouldHaveClockedIn == true`, **or** masked `on_time_off` |
| `not_started` | `not_clocked_in` before the scheduled start |
| `on_leave` | `on_time_off`, leave-authorized only |
| `not_scheduled` | `non_working_day`, `no_schedule`, `policy_not_configured` |

7. **Response.**
   - `summary` counts cover the **whole** population.
   - `members` is capped at `limit`, ordered as follows:
     1. attention severity critical → warning → none;
     2. then `absent`;
     3. then `late`;
     4. then `display name` ascending;
     5. then `employeeId` as a deterministic tiebreak.

#### 8.1.3 DTO

```text
TeamTodayResponse {
  workDate: date, timezone: string, legalEntityId: guid,
  summary: { total, working, onBreak, clockedOut, late, absent, notStarted, onLeave, notScheduled, needsAttention },
  members: TeamTodayMember[], totalMembers: int
}
TeamTodayMember {
  employeeId, displayName, avatarFileId?, positionTitle?,
  status, statusLabel, isLate: bool, clockInAt?, clockOutAt?,
  attentionType?, attentionLabel?, attentionSeverity?,
  leave?: { leaveTypeName: string, endsOn: date }     // null unless Leave-authorized for this subject
}
```

- `summary.onLeave` counts only subjects the caller is Leave-authorized for. Everyone else is
  counted in `absent`, so the totals themselves cannot leak leave.
- No monitoring field exists in V1. The Phase 2 fields are added only by §8.1.4.

#### 8.1.4 Phase 2: live activity (BLOCKED on §12, not built in V1)

When §12 has shipped, a new endpoint `GET /api/v1/monitoring/team/presence` (Monitoring domain)
returns `{ employeeId, presence: active|idle|meeting|offline, since }` under these rules:
- It requires `monitoring:read`.
- Its population is `ResolveVisibilityAsync(monitoring:read, IncludeSelf:false)` in the active
  legal entity.
- `offline` means the latest `TrayDeviceRegistration.LastSeenAt` is older than the same
  `GracePeriodSeconds` `GetTrayPresenceQueryHandler` uses.
- The capability becomes `monitoring:read` **and** a coverage probe.

It is a separate call and not part of `team/today`, so attendance data never depends on
monitoring.

#### 8.1.5 States and privacy

- **Empty.** "No one in your coverage is scheduled today". This also covers coverage rows that
  exist but resolve to nobody.
- **Privacy.**
  - No leave data for unauthorized subjects (above).
  - No monitoring data in V1.
  - The self row is never included.
  - Subjects outside the active legal entity never appear, because the resolver works on one
    legal entity.

### 8.2 Approvals & Exceptions

**Business question:** What is waiting for my decision, in the people I manage and the work I
lead?

**Data:** `GET /api/v1/dashboard/team/action-items` (§9.4). The card shows one row per returned
source: label, `pendingCount`, "oldest waiting X", and a link to the source's list. Sources are
grouped under two headings, "People I manage" and "Work I lead".

| Source key | Gate (existing) | Routing / predicate (existing) | Oldest-first by |
|---|---|---|---|
| `leave.approval` | `leave:approve` | `ListPendingForApproverAsync(approver = caller employee)` | request submitted `CreatedAt` |
| `attendance.correction` | `attendance:approve` | `AttendanceCorrectionWorkflow` inbox: `ResolveVisibilityAsync(attendance:approve, IncludeSelf:false)` + `ListApprovalInboxAsync` | `CreatedAt` |
| `attendance.work_area` | `attendance:approve` | `WorkAreaChangeRequestWorkflow` inbox predicate | `CreatedAt` |
| `attendance.location` | `attendance:approve` | `LocationChangeRequestWorkflow` inbox predicate | `CreatedAt` |
| `attendance.device_change` | `attendance:approve` | `DeviceChangeRequestWorkflow` inbox predicate | `CreatedAt` |
| `monitoring.exception` | `ExceptionScopeResolver` gate | `ResolveAsync(forAction:false, candidates)`; actionable = `Open` ∪ `Escalated`; `Acknowledged` reported as `inProgressCount` | `EscalatedAt ?? DetectedAt` |
| `work.task_creation` | Work module + relationship | `TaskCreationRequest` pending, `GetPendingForOwnerEmployeeIdAsync(caller)` | `CreatedAt` |
| `work.task_edit` | Work module + relationship | `TaskEditRequest` pending, `GetPendingForOwnerEmployeeIdAsync(caller)` | `CreatedAt` |
| `work.objective_change` | Work module + relationship | `ObjectiveChangeRequest` pending with `ReportingManagerId == caller`, including allocation extension (the same set `ListMyObjectiveChangeRequestsQuery` returns) | `CreatedAt` |
| `work.status_template_change` | Work module + relationship | `TaskStatusChangeRequest` pending in projects where `TaskStatusChangeAccessService.ResolveAsync(...).CanEditDirectly` | `CreatedAt` |

**Rules:**
- **Omitted, not zero.** A source the caller is not gated into is **omitted** from the
  response, not returned as zero.
- **Counts match lists.** Each source's `pendingCount` must be produced by **the same query
  predicate** as its existing list endpoint (§9.4, parity tests in §18).
- **Legal entity.**
  - People sources count only subjects whose `LegalEntityId` = the active legal entity.
  - Work sources count only requests whose project `OwningLegalEntityId` = the active legal
    entity.
  - Because some destination lists (for example HR-unrestricted exceptions) are not filtered by
    legal entity, the card subtitle reads "in {legal entity name}", and the acceptance criteria
    record this as expected (§17, AC-11).

**Empty state:** "Nothing is waiting for you".

### 8.3 Team Progress (Work I Lead)

**Business question:** How is the work in the modules I head progressing, and what is overdue?

#### 8.3.1 Endpoint

`GET /api/v1/work/led-progress?overdueLimit=10`
- A new `WorkLeadershipController`, route `api/v1/work`.
- `[RequireAnyModule("worksync_foundation","projects","objectives_milestones","tasks","boards","planning_sprints")]`,
  the same list as `TasksController`.
- No `RequirePermission`, consistent with the rest of Work Management.

#### 8.3.2 Scope algorithm (`IWorkLeadershipService.ResolveLedScopeAsync`)

1. **Caller.** `callerEmployeeId` via `ICallerIdentityResolver`, exactly like every Work handler.
2. **Owned modules.** One query: active, not-achieved objectives with `OwnerId == caller`, in
   active projects with `OwningLegalEntityId` = active legal entity.
3. **Trees.** For the distinct project ids, one query loads every active objective
   `(Id, ParentObjectiveId, ProjectId, Title, IsDefault, EndDate)`.
4. **Expand.** Expand each owned objective to its descendants in memory. This reuses the
   breadth-first walk in `EfProjectMemberRepository.GetActiveObjectiveIdsForEmployeeInProjectAsync`,
   extracted into a shared pure helper `ObjectiveTreeExpander`.
5. **Head modules.** A head module is an owned objective none of whose ancestors is also owned by
   the caller. Counts roll up from each head module over its whole subtree, so nothing is
   counted twice.
6. **Tasks.** Tasks in scope are **top-level only** (`ParentTaskId == null`), matching the board.
   A subtask's progress is represented by its parent.
7. **No-leak intersection.**
   - Intersect the led objective set with the objectives the caller can **open** under
     `TaskAccessResolver`'s rule: active membership on the objective or an ancestor, via the same
     expander, in one query per request.
   - Why: owners normally get a membership row when a module is created or transferred
     (`CreateProject`, `CreateObjective`, `TransferObjectiveHead`), but that is not guaranteed.
     `TaskStatusChangeAccessService` says so ("A module owner is not guaranteed a ProjectMember
     row"), and demo seeders set `OwnerId` directly.
   - Dropped objectives are logged at Information with project and objective ids, so the data
     gap is visible.
   - The dashboard must never list a task the caller would get NotFound for.

**Semantics:**
- Step 5 is exactly "effective owner of the module or any ancestor" (`IsEffectiveOwnerAsync`),
  evaluated in bulk.
- Membership is used **only** to restrict (step 7), never to qualify. So D4 holds: plain
  membership alone never makes someone a lead.

#### 8.3.3 Task classification

This reuses the existing rule by extracting `TaskProgressClassifier.Classify(marksTaskComplete, progressPercent, dueDate, today)`
from `GetMyTaskProgressQueryHandler`. The handler then calls the helper, with no behavior change.
- `completed` = `MarksTaskComplete || ProgressPercent >= 100`
- otherwise `overdue` = `DueDate < today`
- otherwise `inProgress` = `ProgressPercent > 0`
- otherwise `notStarted`

`today` is the UTC date, as in the existing handler, so My Day's task-progress card and Team
Progress agree.

#### 8.3.4 Queries

- The counts come from one grouped query: tasks joined to statuses, grouped by `ObjectiveId`,
  with a `CASE` classification.
- Overdue top N comes from one query: order by `DueDate` ascending, then `ShortId`.
- Assignee names come from one batch `ResolveIdentitiesByEmployeeIdAsync`.
- **Budget:** at most 9 round trips (including the step-7 membership query), regardless of how many projects or modules are in scope.

#### 8.3.5 DTO

```text
LedWorkProgressResponse {
  totals: { total, completed, inProgress, notStarted, overdue },
  projects: [{
    projectId, projectName, identifier,
    totals,
    modules: [{ objectiveId, title, isRootModule, endDate, totals }]   // head modules only, rolled up
  }],
  overdueTasks: [{ taskId, shortId, title, projectId, objectiveId, dueDate, daysOverdue,
                   assignees: [{ employeeId, displayName }] }],
  overdueTotal: int
}
```

#### 8.3.6 States and privacy

- **Empty.** "You don't lead any active modules" shows only when the capability went stale.
  "No open tasks in the modules you lead" shows when there are no tasks.
- **Privacy.** Every returned task is one the caller can already open through Work Management;
  step 7 guarantees this, and §18 tests it against `TaskAccessResolver`.

### 8.4 Priority Actions (presentation layer only)

**Business question:** Of everything above, what should I act on first?

**Inputs** (no extra call):
- `action-items.sources[*].topItems` (up to 5 per source, oldest first);
- `led-progress.overdueTasks`.

**Deterministic V1 ordering.** Items are grouped, not scored. Groups render in this order, up to
5 items each:
1. **Escalated exceptions**, oldest `EscalatedAt` first.
2. **Open exceptions**, oldest `DetectedAt` first.
3. **Pending approvals.** The items of every approval source are merged, oldest `createdAt`
   first. Ties break by `sourceKey`, then `entityId`.
4. **Overdue work I lead**, most days overdue first, then `shortId`.

**Why this is exact.** Each source already returns its oldest 5, so a group's merged top 5 is
exact. No item is derived or invented.

**Other rules:**
- **Each item carries:** `sourceKey`, `domain` (`people`|`work`), `entityId`, title, subject
  name, age or overdue days, and `link` (§15).
- **Excluded:** the caller's own unrelated overdue tasks (D7). Self-submitted requests are
  already excluded by every source's routing.
- **Empty state:** "You're all caught up".

---

## 9. Backend design

### 9.1 New and changed endpoints

| Method + route | Domain / controller | Gate | Query |
|---|---|---|---|
| `GET /api/v1/dashboard/team/capabilities` | Dashboard / `TeamDashboardController` (new, `Controllers/Tenant/Dashboard/`) | `TenantPolicy` | `GetMyTeamCapabilitiesQuery` |
| `GET /api/v1/dashboard/team/action-items` | Dashboard / `TeamDashboardController` | `TenantPolicy`; each source gates itself | `GetTeamActionItemsQuery(int TopPerSource = 5)` |
| `GET /api/v1/attendance/time-tracking/team/today` | TimeAttendance / `TimeTrackingController` | `attendance:read` + resolver | `GetCoveredTeamTodayQuery` |
| `GET /api/v1/work/led-progress` | WorkManagement / `WorkLeadershipController` (new) | Work module gate + relationship | `GetLedWorkProgressQuery` |

No existing endpoint changes its contract.

### 9.2 New and extended application services

| Service | Owner | Purpose |
|---|---|---|
| `IEmployeeAuthorityResolver.HasAnyManagedCoverageAsync(EmployeeAuthorityVisibilityRequest)` | CoreHr | Existence probe for capabilities. No expansion. |
| `IWorkLeadershipService` { `LeadsAnyWorkAsync`, `HasPendingWorkApprovalsAsync`, `ResolveLedScopeAsync` } | WorkManagement | The single home of "Work I Lead" |
| `ObjectiveTreeExpander` (pure) | WorkManagement | Extracted breadth-first walk. `EfProjectMemberRepository` calls it, with no behavior change. |
| `TaskProgressClassifier` (pure) | WorkManagement | Extracted from `GetMyTaskProgressQueryHandler`, with no behavior change |
| `ILeaveVisibilityScopeProvider.ResolveForCurrentUserAsync()` | Leave | Extracted verbatim from `GetLeaveCalendarQuery.ResolveScopeAsync`. The calendar then calls it, with no behavior change. |
| `EmployeeVisibilityScopeMatcher.Includes(scope, employeeId, positionId, departmentId, legalEntityId)` (pure) | CoreHr | In-memory mirror of the `EfLeaveCalendarRepository` scope filter. Kept honest by a parity test. |
| `ITeamActionSource` (interface, declared in `Features/Dashboard/Team/Abstractions`), one implementation per source key | Each owning domain | `Key`, `Domain`, `IsGatedAsync()`, `GetSummaryAsync(legalEntityId, top)` |

### 9.3 Leave authorization for Team Status (D6)

- **Resolve the scope once per request.** `ILeaveVisibilityScopeProvider` returns:
  - `Unrestricted` for `leave:read` / `leave:manage`;
  - raw coverage (`EmployeeVisibilityScopeResolver`) for `leave:read-team`;
  - self for `leave:read-own`;
  - a *none* result otherwise, replacing the calendar's Forbidden. The calendar keeps mapping
    *none* to 403.
- **Check each subject.** A subject is leave-authorized iff `EmployeeVisibilityScopeMatcher.Includes`
  is true. This deliberately follows the **Leave** domain's rule (raw coverage, no department
  descendants), not the attendance resolver's expansion.
- **Documented consequence.** A `leave:read-team` manager can see someone in a sub-department as
  "Absent" but not "On leave". This is correct, because they cannot see that person in the leave
  calendar either.

### 9.4 Action-items composition

- **Discovery.** `GetTeamActionItemsQueryHandler` receives `IEnumerable<ITeamActionSource>` from
  DI.
- **Execution.** It iterates sources **sequentially**. The scoped `DbContext` is not thread-safe,
  so there is no `Task.WhenAll`.
- **Error isolation.** Each source runs in its own try/catch:
  - a gated-out source is skipped, and omitted from the response;
  - a thrown exception is logged with the source key and returns `{ status: "unavailable" }`;
    the other sources still return.
- **Count/list parity.** Each source implementation adds `Count…` and `ListOldest…(top)` methods
  beside its existing inbox list method. All three are built from **one shared `IQueryable`
  predicate builder** in the owning repository, so count, top N and the existing list cannot
  drift.

```text
TeamActionItemsResponse { legalEntityId, sources: ActionSourceSummary[] }
ActionSourceSummary {
  sourceKey, domain: "people"|"work", status: "ok"|"unavailable",
  pendingCount, inProgressCount?, oldestPendingAt?,
  topItems: ActionItem[]            // ≤ TopPerSource, oldest first
}
ActionItem {
  sourceKey, entityId, title, subjectEmployeeId?, subjectName?, createdAt,
  exceptionStatus?,                 // monitoring.exception only
  link: { kind: string, params: { [k: string]: string } }   // §15; data only, no URLs
}
```

---

## 10. People I Manage vs Work I Lead — enforcement rules

1. `Features/Dashboard/Team/**` must not reference `EmployeeHierarchyClosure`,
   `ManagementCoverageRecord`, or any repository directly. It may reference only domain-declared
   interfaces (§9.2). Enforced by a new architecture test.
2. `Features/WorkManagement/**` must not reference `IEmployeeAuthorityResolver`,
   `IEmployeeVisibilityScopeResolver`, `IEmployeeHierarchyClosureRepository` or
   `ManagementCoverageRecord`. This is true today, and a new architecture test keeps it true.
3. No response carries a merged or combined team list. The people population and the work scope
   never cross a section boundary.
4. The UI labels every section with its population (§5).

## 11. Tenant and legal-entity boundaries

- **Tenant.** Every query runs under `TenantPolicy`, the tenant-scoped query filters, and RLS.
  No `IgnoreQueryFilters()` is introduced.
- **Legal entity.** The active legal entity is the session `legal_entity_id` claim, equal to the
  default employee record's `LegalEntityId`.
  - People sources filter subjects to it.
  - Work sources filter projects by `OwningLegalEntityId`.
  - Capabilities report it, so the UI can label "in {name}".
- **No active legal entity.** A caller with no default employee record gets
  `canViewPeopleStatus = false`, `leadsWork = false` and `hasWorkApprovals = false`. My Team is
  then offered only through permission-based approval flags.
- **Multi-legal-entity** aggregation is future work (§20).

## 12. Monitoring security prerequisite (separate work item)

**Work item SEC-MON-SCOPE, high priority.** It enforces coverage on every per-employee monitoring
endpoint. Today these endpoints check only `monitoring:read` (or
`monitoring:screenshots:request`) plus the tenant:

1. `GET /monitoring/activity/snapshots`
2. `GET /monitoring/activity/daily-summary`
3. `GET /monitoring/activity/daily-range`
4. `GET /monitoring/app-usage/snapshots`
5. `GET /monitoring/device-state/snapshots`
6. `GET /monitoring/meetings/signals`
7. `GET /monitoring/daily-report`
8. `GET /monitoring/daily-report/export`
9. `GET /monitoring/screenshots` — `employeeId` is optional, so it returns tenant-wide results
10. `GET /monitoring/screenshots/{id}/url`
11. `POST /monitoring/screenshots/request`
12. `GET /monitoring/reports/productivity` — employee / department / tenant scope

- **Target rule:** the subject must be inside `ResolveVisibilityAsync(RequiredPermission:
  "monitoring:read", LegalEntityId: subject's, IncludeSelf: true)`. This is the pattern
  `GetAttendanceDayDetail` already uses.
- **This spec does not implement it.**
- **This spec requires:**
  - `canViewLiveActivity` stays `false`;
  - no My Team endpoint returns activity, idle, meeting, presence or screenshot data;
  - §8.1.4 is not started until SEC-MON-SCOPE is merged to `development`.

## 13. Performance requirements

### 13.1 Resolver changes (no behavior change)

Implemented in `EmployeeAuthorityResolver`:
1. **Batch holders.** Replace the per-covered-position `GetActiveHoldersAsync` loop with
   `GetActiveHoldersByPositionIdsAsync`.
2. **Batch departments.** Add `IDepartmentRepository.GetDescendantDepartmentIdsAsync(tenantId,
   legalEntityId, IReadOnlyCollection<Guid> rootIds)`: the existing recursive CTE with multiple
   roots. Replace the per-department loop with it.
3. **Memoize expansion within the request.**
   - A private field on the scoped resolver: `Dictionary<(Guid ActorUserId, Guid LegalEntityId), CoverageExpansion>`.
   - `CoverageExpansion` = actor employee id, primary position id, and expanded candidate ids.
   - **The permission check is never memoized.** It still runs on every call with the caller's
     `RequiredPermission`.
   - **Lifetime.** The memo lives exactly as long as the DI scope: one HTTP request, or one job
     scope.
   - **Safety.** No command resolves visibility after mutating coverage in the same scope. The
     plan must verify this with a code search and document it. If such a command exists, it must
     call a new `InvalidateCoverageMemo()` after the write.

**Resulting cost per `ResolveVisibilityAsync`:**
- First call in a request: at most 7 round trips, independent of how many positions or
  departments are covered.
- Later calls with the same `(actor, legal entity)`: 1–2 round trips (the permission check and the
  final active filter).

**Architecture tests stay green:**
- no permission code is hardcoded;
- the request model has no `TenantId`.

### 13.2 Budgets (enforced by integration tests with a DbCommand-counting interceptor)

The plan adds a test-only interceptor if none exists.

| Request | Max DB round trips | Must not grow with |
|---|---|---|
| `capabilities` | 10 | coverage size, project count |
| `team/today` | 15 | population size |
| `action-items` | 45 | covered population. The resolver expansion runs **once** per request. |
| `led-progress` | 9 | projects, modules, tasks |

**Frontend call budget:**

| Scenario | Extra calls |
|---|---|
| Normal employee on `/dashboard` | +1 (`capabilities`) |
| Team-capable user on `/dashboard` | +1 (`capabilities`) |
| `/dashboard/team` | ≤ 3 parallel calls (`team/today`, `action-items`, `led-progress`), each only if its capability is true |

## 14. Loading, empty, partial-permission, and error states

- **Independent section state.** `TeamDashboardStore` (component-provided) holds one state per
  section: `idle | loading | loaded | error`. Each section fetches and fails independently.
- **Loading.** Per-card skeletons shaped like the final content.
- **Initial render.** The page shell and the mode switch render immediately. There is no
  page-level spinner.
- **Partial permission.** Only sections whose capability is true are requested and rendered.
  - Layout: a two-column grid on `xl`, one column below; Priority Actions spans the full width at
    the top when present.
  - **People only:** Team Status, Approvals (people) and Priority Actions.
  - **Work only:** Team Progress, Approvals (work) and Priority Actions.
- **403 from a section.** The capability is stale. Hide the section and call
  `capabilitiesStore.refresh()`. If `isAvailable` becomes false, navigate to `/dashboard`.
- **Other errors** (5xx / network). Show an inline error in that card with **Retry**. Other cards
  are unaffected.
- **Source unavailable.** Within Approvals & Exceptions, a source with `status: "unavailable"`
  renders as "Couldn't load {source}" with a retry that re-requests `action-items`.
- **Refresh.**
  - Team Status refetches every 60 s while `document.visibilityState === 'visible'`. It pauses
    when the tab is hidden and refetches immediately when the tab becomes visible, reusing the
    work-pattern card's visibility pattern.
  - Other cards load on entry and have a manual refresh control.

## 15. Deep links

The destination always re-authorizes. The dashboard never performs an action inline in V1.

| Item | `link.kind` | Frontend route |
|---|---|---|
| Leave approval | `leave.approval` | `/time-off/team-approvals?requestId={id}` |
| Attendance correction / work-area / location / device change | `attendance.approval` | `/attendance/time-tracking?view=team&type={corrections\|work-area\|location\|device-change}&requestId={id}` (existing, `legacy-approval-redirect`) |
| Exception | `monitoring.exception` | `/attendance/alerts?alertId={id}` (existing) |
| Work request (any `work.*` source) | `work.request` | The frontend calls the existing `GET /work/notification-navigation?relatedEntityType={task_creation_request\|task_edit_request\|objective_change_request\|task_status_change_request}&relatedEntityId={id}`, then routes to `/work/{projectId}/milestones/{objectiveId}/{targetTab}` |
| Overdue task | `work.task` | `/work/{projectId}/milestones/{objectiveId}/board` (task focus by the existing board behavior) |
| Team Status member | `attendance.member` | `/attendance/time-tracking?view=team&employeeId={id}` |
| Team Progress module | `work.module` | `/work/{projectId}/milestones/{objectiveId}/tree` |
| "View all" per section | — | Each source's list route |

**One frontend change outside the dashboard:** `pending-approvals.component.ts` honors the
`requestId` query parameter by calling the existing `store.loadDetail(requestId)` on init.

## 16. Privacy rules (summary)

1. No leave type, dates or "on leave" status for subjects outside the caller's Leave scope. The
   backend masks it (§8.1).
2. No monitoring or presence data in V1 (§12).
3. No Work task is exposed through organizational coverage (§10).
4. The self row is excluded from People sections; self requests are excluded by routing.
5. Action item titles carry only what the existing list endpoint already shows to that approver.

---

## 17. Acceptance criteria

- **AC-1.** A user with none of the §7.3 flags sees no switch. `/dashboard/team` redirects them
  to `/dashboard`. My Day is unchanged, apart from one `capabilities` call.
- **AC-2.** A module owner with **no** permissions and no coverage sees the switch.
  - My Team shows Team Progress, and Work approvals if any.
  - It shows no Team Status.
- **AC-3.** A plain module member who is not an owner anywhere gets `leadsWork = false`, and no
  Team Progress for that project.
- **AC-4.** A user with `attendance:read` and coverage sees Team Status for exactly the
  `GetCoveredAttendanceHistory` population. Employees without a record today appear as
  `absent` / `not_started` / `not_scheduled`.
- **AC-5.** An employee on approved leave appears as:
  - `on_leave` with type and end date to a `leave:read` holder;
  - `absent` with `leave: null` to an attendance-only viewer.

  Summary counts obey the same rule.
- **AC-6.** Team Progress counts match task-by-task classification with `TaskProgressClassifier`,
  over head-module subtrees, with no double counting when nested owned modules exist.
- **AC-7.** For every approval source, `pendingCount` equals the length of the existing list
  endpoint for the same user, legal entity and data.
- **AC-8.** A failing source returns `unavailable` without failing the response. A failing
  section endpoint does not affect other cards.
- **AC-9.** Priority Actions order is identical across reloads for identical data, and follows
  §8.4.
- **AC-10.** No My Team response contains activity, idle, meeting, presence or screenshot fields.
- **AC-11.** All People data is limited to the active legal entity, and cards are labelled
  "in {legal entity}".
- **AC-12.** The performance budgets in §13.2 are met. Resolver output is identical before and
  after §13.1 on the existing resolver test suite.

## 18. Tests

### 18.1 Backend unit tests (`ONEVO.Tests.Unit`)

- `TaskProgressClassifier`: each branch, boundary `ProgressPercent == 100`, and `DueDate == today`
  (not overdue).
- `ObjectiveTreeExpander`:
  - deep trees and multiple roots;
  - inactive objectives excluded;
  - cycles are impossible, but the walk is guarded by its visited set;
  - parity with the existing `EfProjectMemberRepository` behavior.
- Head-module selection: nested owned modules roll up once; sibling owned modules are separate.
- `EmployeeVisibilityScopeMatcher`: each branch (unrestricted, own, position, department,
  company legal entity, none).
- Team status mapping table (§8.1.2), including every masking row.
- Priority Actions ordering: pure function tests live in the frontend (§18.4).
- `GetTeamActionItemsQueryHandler`:
  - gated-out source omitted;
  - throwing source → `unavailable`;
  - sources executed sequentially.
- Resolver memo:
  - a second call with a different permission re-checks the permission;
  - an expansion query count of 1 is verified with fakes;
  - a different legal entity is not shared.

### 18.2 Backend integration tests (`ONEVO.Tests.Integration`, real PostgreSQL + RLS)

- Every endpoint: 401 without a session; tenant B's data never appears for tenant A.
- `team/today`:
  - population equals the resolver result;
  - self excluded;
  - other-legal-entity employees excluded;
  - absent detection with the legal-entity schedule;
  - leave masking with `leave:read`, with `leave:read-team` (in and out of raw coverage), and
    with attendance only.
- `led-progress`:
  - owner vs plain member;
  - owner **without** a membership row → that module is excluded (step 7);
  - root owner sees the whole project;
  - achieved modules and inactive projects excluded;
  - other legal entity excluded;
  - every returned task id resolves via `TaskAccessResolver.ResolveViewableTaskAsync` for the
    same caller (no-leak check).
- `action-items`: count/list parity per source against the existing list endpoints; exception
  statuses (`Open` / `Escalated` actionable, `Acknowledged` in progress, `Resolved` excluded).
- `capabilities`: each flag true and false; module-inactive tenant → Work flags false.
- Query budgets (§13.2) with the counting interceptor, using a fixture of 3 covered positions,
  2 departments with sub-departments, company coverage, and 5 projects × 20 modules.
- Resolver regression: the existing resolver tests pass unchanged. Batched and unbatched
  results are identical on a randomized fixture.

### 18.3 Authorization and security tests

- `team/today` without `attendance:read` → 403. With the permission but no coverage → 200 with an
  empty population.
- A user-level permission override grant without a role grant yields an empty scope (fail
  closed), documenting the raw-role resolver behavior.
- `led-progress` for an employee with only coverage over a project's members, and no Work
  relationship → empty.
- `action-items` never includes a source the caller is not gated into, even when rows are routed
  to them (for example `leave.approval` rows but no `leave:approve`).
- Architecture tests (new):
  - the §10 rules 1–2;
  - `TeamDashboardController` lives under `Controllers.Tenant` with `TenantPolicy`;
  - no `IgnoreQueryFilters` in the new code;
  - no string literal matching a monitoring DTO field name in `Features/Dashboard/Team`, which
    guards AC-10.

### 18.4 Frontend tests (Vitest/Jest spec files beside components)

- `myTeamGuard`: redirects when unavailable; lets navigation through on capability error; awaits
  `ensureLoaded`.
- `DashboardModeSwitchComponent`: hidden while loading or unavailable; `aria-current` on the
  active link; keyboard navigable.
- `TeamDashboardComponent`:
  - requests only the sections whose capability is true;
  - one section's error leaves the others rendered;
  - a 403 hides the section and triggers a capabilities refresh.
- Priority Actions composer (pure): group order, tie-breaks, 5-per-group cap, personal tasks never
  present, stable output for identical input.
- Deep-link resolver: every `link.kind` → the route in §15; the `work.request` path calls
  `notification-navigation`.
- Team Status card: `leave: null` renders "Absent" with no leave text; the 60 s visibility-gated
  refresh starts and stops.
- `pending-approvals.component`: opens detail when `?requestId=` is present.
- Route config test: `/dashboard/team` is lazy and guarded; `/dashboard` is unchanged.

## 19. Implementation sequence

Each step is independently mergeable and ends green (backend `dotnet test`, frontend unit
tests + build).

0. **SEC-MON-SCOPE.** Separate spec and PR, run in parallel. It blocks only Phase 2 (§8.1.4).
1. **Resolver performance (§13.1).** Batching + memo + regression and budget tests. No behavior
   change.
2. **Pure extractions.**
   - `TaskProgressClassifier`, `ObjectiveTreeExpander`, `ILeaveVisibilityScopeProvider`,
     `EmployeeVisibilityScopeMatcher`.
   - Existing callers switch to them, with no behavior change and existing tests green.
3. **`IWorkLeadershipService` + `GET /work/led-progress`** + tests.
4. **`GET /attendance/time-tracking/team/today`** + tests.
5. **`ITeamActionSource` implementations** (with count/list parity builders) + **`GET
   /dashboard/team/action-items`** + tests.
6. **`HasAnyManagedCoverageAsync` + `GET /dashboard/team/capabilities`** + architecture tests.
7. **Frontend foundation.**
   - `MyTeamCapabilitiesStore`, `DashboardModeSwitchComponent`, the `/dashboard/team` route and
     `myTeamGuard`.
   - Switch added to My Day.
8. **Frontend sections.**
   - Team Status, Approvals & Exceptions, Team Progress, Priority Actions composer, deep-link
     resolver.
   - The `requestId` parameter on leave approvals.
9. **End-to-end verification** in the browser with seeded users:
   - people-only manager;
   - work-only lead;
   - approver-only;
   - HR (company coverage);
   - plain employee.

   Screenshot each, then run a final full test run in both repos.

## 20. Existing behavior noted, not changed by this spec

- The Team attendance history (`AttendanceReadHandlers.BuildRowsAsync`) already shows
  `on_time_off` to `attendance:read` viewers, which is inconsistent with D6 outside My Team.
  **Recommend a follow-up product decision** on whether to apply the same masking there.
- `GetSprintTasksQueryHandler` lets any project member see every task in a sprint, while
  `GetProjectTasksQueryHandler` filters by module membership. This is a Work Management
  inconsistency and out of scope.
- `AttendanceTodayStateService` runs a full `ResolveVisibilityAsync` on every today load to set
  `CanViewCoveredEmployees`. §13.1 makes that cheaper; switching it to the probe is a possible
  follow-up (small semantic difference: probe = coverage exists, versus resolver = at least one
  active covered employee).
- Permission catalog findings, to be addressed separately:
  - `attendance:read-team`, `employees:read-team`: legacy / dead;
  - `workforce:view`, `workforce:manage`: unused;
  - `workforce:dashboard`, `monitoring:alerts:read`: missing seed;
  - `inbox:read`: derived but never consumed;
  - `leave:read-team`: active, with a misleading description.
- Demo seed: the Dapi "Manager" role (`employees:read-team`, `leave:approve`, `calendar:read`,
  `projects:create`) gets only the approval capability, so no Team Status.

## 21. Future considerations

- Phase 2 live presence (§8.1.4), after SEC-MON-SCOPE.
- Multi-legal-entity My Team, with a legal-entity selector and per-entity resolution.
- A Team Snapshot on My Day (rejected for V1).
- Onboarding access-grant requests as a people source.
- Remembering the last dashboard mode.
- Inline approve/reject from the dashboard, which would require each domain's command to be
  invoked from the card with the same authorization.
