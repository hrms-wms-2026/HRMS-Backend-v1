# Work Management Dapi Demo — Task / Status / Approvals Seeding Design

**Date:** 2026-08-17  
**Status:** Approved for implementation (scope A/B + density C locked with product)  
**Extends:** `WorkManagementDapiDemoSeeder` (projects + objectives already seeded)

## Goal

Give the `dapi` tenant enough Task Foundation data that Board, Backlog, Approvals, and notification deep-links are usable in local demo without hand-creating rows: project + objective `task_statuses`, leaf `tasks` + `task_assignments`, and a small pending Approvals queue (`task_creation_requests` + `extend_allocation` change requests).

## Locked decisions

| Topic | Choice |
| --- | --- |
| Approvals scope | **B** — seed Tasks **and** a small Approvals queue |
| Density | **C** — **2–3 tasks per leaf**; **≥3** pending task-creation requests per project; **≥1** pending `extend_allocation` per project |
| Status columns | Match CreateProject: To Do / In Process / Review / Done (`MarksTaskComplete` only on Done) |
| Where tasks live | **Leaves only** (objectives with no children in the demo tree) |
| ShortIds | `{Project.Identifier}-{n}` with `Project.NextTaskNumber` advanced after seed |
| Assignees | Only people already on that objective’s `ProjectMember` rows; primary = objective owner; optional second assignee when `ExtraMemberKeys` is non-empty |
| Idempotency | Deterministic Guids (`DeterministicGuid("dapi-demo:…")`); check-exists-then-skip |

## Data shape

### 1. Task statuses (critical)

Board lazy-copies project templates into an objective on first open. Direct EF inserts **bypass** that path, so the seeder must insert:

1. **Project template** — 4 rows per project with `ObjectiveId = null`
2. **Per-leaf copies** — the same 4 names/orders for **every leaf** that receives tasks (`ObjectiveId = leafId`)

Keys:

- `dapi-demo:task-status:{projectKey}:template:{name}`
- `dapi-demo:task-status:{projectKey}:{objectivePath}:{name}`

### 2. Tasks (recipe, not hundreds of hand titles)

Declarative **leaf task recipe** of three slots applied to every leaf:

| Slot | Status | Est. hours fraction of leaf `AllocatedHours` | Notes |
| --- | --- | --- | --- |
| 0 | To Do | 0.10 | Title `{LeafTitle} — Kickoff` |
| 1 | In Process | 0.15 | Title `{LeafTitle} — Build`; `StartedAt` set; progress ~40 |
| 2 | Review or Done (alternating by leaf index) | 0.10 | Done rows: `ProgressPercent=100`, `CompletedAt`, `CompletedHours=EstimatedHours` |

`EstimatedHours = max(2, Round(leafAllocated * fraction))`. Sum of fractions = **0.35** so slack remains for Approvals demos and live creates.

Types/priorities cycle: `task`/`medium`, `story`/`high`, `bug`/`low` across slots.

ShortId: allocate `n = Project.NextTaskNumber` then increment; persist final `NextTaskNumber` on the project.

Assignment keys: `dapi-demo:task-assignment:{taskKey}:{personKey}`.  
`TaskAssignment` has **no** `TenantId` — only `TaskId`, `UserId`, `EmployeeId`, `AssignedById`, `AssignedAt`.

### 3. Approvals queue

**Task creation requests** (≥3 pending per project): target leaves that have at least one `ExtraMemberKeys` entry (e.g. Testing and deployment, Hardware Integration, Marketing). Requester = first extra member (not owner). Payload = `TaskCreationRequestPayload` JSON. Status = `pending`. No `CreatedTaskId`.

**Extend allocation** (≥1 pending per project): one non-root objective (prefer a mid-tree node with headroom narrative). `RequestType = extend_allocation`, `RequestedById` = objective owner employee id, `ReportingManagerId` = objective’s `ReportingManagerId` (seeded as Dabi), payload = `ExtendAllocationRequestPayload(RequestedAdditionalHours, Reason)`. Status = `pending`.

## File layout

| File | Role |
| --- | --- |
| `WorkManagementDapiDemoData.cs` | Add recipe records + approval path specs |
| `WorkManagementDapiDemoSeeder.Tasks.cs` | New partial: statuses, tasks, assignments, approvals |
| `WorkManagementDapiDemoSeeder.cs` | Call `SeedTasksAndApprovalsAsync` after objectives |
| `WorkManagementDapiDemoSeederTests.cs` | Assertions for counts, ShortIds, slack, idempotency, approvals |

## Out of scope

- Notifications rows (templates already seeded; live create path covers inbox)
- Sprint / backlog version linkage
- Changing existing objective trees or people roster
- Production seeders

## Verification

- Unit: extend `WorkManagementDapiDemoSeederTests` (idempotent second run; per-project task/status/approval floors; no leaf over-allocation; ShortId prefix = Identifier; Done statuses mark complete)
- Manual (optional after boot): Board shows 4 columns with cards; Approvals tab shows pending create + extend rows for EPOS
