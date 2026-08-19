# Work Management Dapi Demo Task Data Seeding — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extend the existing `dapi` Work Management demo seeder so Board, Backlog, and Approvals UIs have realistic Task Foundation rows (statuses, leaf tasks, assignments, pending create + extend-allocation requests) without hand-creating data.

**Architecture:** Keep declarative data in `WorkManagementDapiDemoData.cs`. Add a new partial `WorkManagementDapiDemoSeeder.Tasks.cs` that (1) seeds project-level + per-leaf `TaskStatus` copies, (2) applies a 3-slot leaf task recipe under every leaf, (3) seeds pending `TaskCreationRequest` and `extend_allocation` `ObjectiveChangeRequest` rows from path specs. Call it after `SeedProjectsAndObjectivesAsync`. All Guids via existing `DeterministicGuid`.

**Tech Stack:** EF Core `ApplicationDbContext`, existing dapi demo seeder + SQLite unit harness in `WorkManagementDapiDemoSeederTests`.

**Spec:** `docs/superpowers/specs/next/2026-08-17-work-management-task-data-seeding-design.md`

## Global Constraints

- Dev/Test-only seeder only — no production paths.
- Do not modify `DevSmokeTestTenantSeeder` or its fixed counts.
- Idempotent: check-exists-by-deterministic-Id then skip; second `SeedAsync` must not change counts.
- Tasks only on **leaves**; `sum(EstimatedHours)` per leaf ≤ that leaf’s `AllocatedHours`.
- Seed **both** project template statuses (`ObjectiveId=null`) and per-leaf status copies (Board lazy-copy is bypassed).
- Assignees must already be `ProjectMember`s on that objective; `TaskAssignment` has no `TenantId`.
- Advance `Project.NextTaskNumber` to match highest allocated ShortId number + 1.
- Scope: Work Management seeders only — no Calendar/Core HR/Org changes.
- PowerShell: use `;` not `&&`.

---

## File map

| Path | Action |
| --- | --- |
| `src/ONEVO.Infrastructure/Persistence/Seeders/WorkManagementDapiDemoData.cs` | Add recipe + approval path records |
| `src/ONEVO.Infrastructure/Persistence/Seeders/WorkManagementDapiDemoSeeder.Tasks.cs` | Create — statuses/tasks/assignments/approvals |
| `src/ONEVO.Infrastructure/Persistence/Seeders/WorkManagementDapiDemoSeeder.cs` | Wire call + log message |
| `tests/.../WorkManagementDapiDemoSeederTests.cs` | New facts for task/approvals coverage |

---

### Task 1: Data records + failing tests

**Files:**
- Modify: `WorkManagementDapiDemoData.cs`
- Modify: `WorkManagementDapiDemoSeederTests.cs`

- [ ] **Step 1: Add data records** to `WorkManagementDapiDemoData.cs` (after existing records, before `WorkManagementDapiDemoData` class body end for records; static lists inside the class):

```csharp
public sealed record DemoLeafTaskSlot(
    string TitleSuffix,
    string TaskType,
    string Priority,
    string StatusName,
    decimal EstimatedHoursFraction,
    bool AssignExtraMember,
    bool MarkComplete);

public sealed record DemoTaskCreationRequestSpec(
    string ProjectKey,
    string ObjectivePath,
    string RequesterKey,
    string Title,
    string? Description,
    string TaskType,
    string Priority,
    decimal EstimatedHours);

public sealed record DemoAllocationExtendSpec(
    string ProjectKey,
    string ObjectivePath,
    decimal AdditionalHours,
    string Reason);
```

Inside `WorkManagementDapiDemoData`:

```csharp
public static readonly IReadOnlyList<DemoLeafTaskSlot> LeafTaskSlots =
[
    new("Kickoff", WorkTaskTypes.Task, WorkTaskPriorities.Medium, "To Do", 0.10m, false, false),
    new("Build", WorkTaskTypes.Story, WorkTaskPriorities.High, "In Process", 0.15m, true, false),
    new("Handoff", WorkTaskTypes.Bug, WorkTaskPriorities.Low, "Review", 0.10m, false, false),
];

// Alternate Done on even leaf index in seeder by swapping Handoff → Done + MarkComplete when leafIndex % 2 == 0.

public static readonly IReadOnlyList<DemoTaskCreationRequestSpec> TaskCreationRequests =
[
    // ≥3 per project — use ExtraMember leaves only. Paths use '/' join matching seeder path keys.
    new("epos", "E-pos_System/Testing and deployment", "mathusanth", "Add regression suite task", null, WorkTaskTypes.Task, WorkTaskPriorities.Medium, 8m),
    new("epos", "E-pos_System/Hardware Integration", "kiru", "Device firmware smoke checklist", null, WorkTaskTypes.Task, WorkTaskPriorities.High, 6m),
    new("epos", "E-pos_System/Marketing", "kavisna", "Launch landing page copy", null, WorkTaskTypes.Story, WorkTaskPriorities.Medium, 4m),
    // evtix ×3, onexso ×3, watercraft ×3, hwportal ×3 — same pattern on Testing/Hardware/Marketing (hwportal: Testing + Marketing + Device Pairing with kiru as owner so use another leaf with extras: Testing and deployment / Marketing, and add third on Cross Project Hardware Support Desk only if it has extras — Cross Project has no extras; use Portal Dashboard? no extras. HWPORTAL leaves with extras: Testing (basith+nilaxan), Marketing (kavisna+…). Need third: Device Pairing owner=kiru with no extras. Use Protocol Adapters? not leaf. Third: seed on Testing twice with different titles OR add ExtraMember to a leaf — prefer two on Testing + one on Marketing and one more on Testing = 3 on Testing/Marketing only.
    // (Fill all five projects to ≥3 each in the real file.)
];

public static readonly IReadOnlyList<DemoAllocationExtendSpec> AllocationExtends =
[
    new("epos", "E-pos_System/Pos System", 40m, "Need more hours for architecture depth"),
    new("evtix", "Event management ticketing/Ticketing Platform", 30m, "Booking engine capacity"),
    new("onexso", "Onexso - HR and Work Management System/Core HR And Employee Management", 50m, "Lifecycle module overrun"),
    new("watercraft", "Watercraft/Hull And Vessel Design", 45m, "Structural analysis buffer"),
    new("hwportal", "The Hardware integration portal/Device Connectivity Framework", 35m, "Protocol adapter expansion"),
];
```

Use `using` aliases / fully qualify `WorkTaskTypes` from Domain — or hardcode string literals `"task"`, `"story"`, `"bug"`, `"medium"`, `"high"`, `"low"` in the data file to avoid Domain dependency from pure data (prefer string literals matching constants).

- [ ] **Step 2: Write failing tests** in `WorkManagementDapiDemoSeederTests.cs`:

```csharp
[Fact]
public async Task SeedAsync_CreatesProjectAndLeafTaskStatusesForEveryLeafWithTasks()
{
    // After seed: each of 5 projects has exactly 4 template statuses (ObjectiveId null).
    // Leaf count with tasks = all leaves; each has 4 statuses.
}

[Fact]
public async Task SeedAsync_CreatesTwoToThreeTasksPerLeaf_WithShortIdsAndSlack()
{
    // Every leaf has 2 or 3 tasks; ShortId starts with Identifier + "-";
    // sum(EstimatedHours) <= leaf.AllocatedHours; Project.NextTaskNumber == max number + 1
}

[Fact]
public async Task SeedAsync_CreatesPendingApprovalsQueuePerProject()
{
    // ≥3 pending TaskCreationRequests per project (join via Objective.ProjectId)
    // ≥1 pending extend_allocation ObjectiveChangeRequest per project
}

[Fact]
public async Task SeedAsync_TaskLayer_IsIdempotent()
{
    // Run twice; WorkTasks / TaskStatuses / TaskCreationRequests / extend OCR counts unchanged
}
```

- [ ] **Step 3: Run tests — expect FAIL** (methods not wired yet)

```powershell
dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~WorkManagementDapiDemoSeederTests" --no-restore
```

- [ ] **Step 4: Commit**

```powershell
git add src/ONEVO.Infrastructure/Persistence/Seeders/WorkManagementDapiDemoData.cs tests/ONEVO.Tests.Unit/Features/DevPlatform/Tenancy/WorkManagementDapiDemoSeederTests.cs
git commit -m "test(work): add failing coverage for dapi task demo seeding"
```

---

### Task 2: Seed statuses, tasks, assignments, approvals

**Files:**
- Create: `WorkManagementDapiDemoSeeder.Tasks.cs`
- Modify: `WorkManagementDapiDemoSeeder.cs` (call site)

- [ ] **Step 1: Implement `SeedTasksAndApprovalsAsync`**

Algorithm sketch:

1. Build `employeeIdByPersonKey` / user id map (reuse existing `ResolveUserId` + employee dict from main SeedAsync — pass the same dictionary already built for people).
2. For each `DemoProjectTree`:
   - Resolve `projectId`, load `Project`.
   - Seed 4 template statuses.
   - Walk tree; collect leaves as `(path, node, allocatedHours computed same as SeedObjectiveNodeAsync)`.
   - For each leaf index `i`:
     - Seed 4 objective statuses.
     - For each slot in `LeafTaskSlots` (optionally force Done on even `i` for slot 2):
       - Compute hours; create `WorkTask` with ShortId `$"{Identifier}-{next}"`; bump next.
       - Assign owner; if `AssignExtraMember && ExtraMemberKeys.Length > 0`, second `TaskAssignment`.
   - Set `project.NextTaskNumber = next`.
3. For each `TaskCreationRequests` spec: resolve objective by path Guid; insert pending `TaskCreationRequest` with serialized payload.
4. For each `AllocationExtends` spec: insert pending `ObjectiveChangeRequest` with type `extend_allocation`.

Status names/orders:

| Name | DisplayOrder | MarksTaskComplete |
| --- | --- | --- |
| To Do | 0 | false |
| In Process | 1 | false |
| Review | 2 | false |
| Done | 3 | true |

Guid keys:

- Status template: `dapi-demo:task-status:{projectKey}:template:{name}`
- Status leaf: `dapi-demo:task-status:{projectKey}:{path}:{name}`
- Task: `dapi-demo:task:{projectKey}:{path}:{slotIndex}`
- Assignment: `dapi-demo:task-assignment:{projectKey}:{path}:{slotIndex}:{personKey}`
- TCR: `dapi-demo:task-creation-request:{projectKey}:{path}:{title}`
- Extend: `dapi-demo:allocation-extend:{projectKey}:{path}`

- [ ] **Step 2: Wire call** in `SeedAsync` after `SeedProjectsAndObjectivesAsync(...)`, before return/logging. Update log line to mention tasks.

- [ ] **Step 3: Run tests — expect PASS**

```powershell
dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~WorkManagementDapiDemoSeederTests"
```

- [ ] **Step 4: Commit**

```powershell
git add src/ONEVO.Infrastructure/Persistence/Seeders/WorkManagementDapiDemoSeeder.Tasks.cs src/ONEVO.Infrastructure/Persistence/Seeders/WorkManagementDapiDemoSeeder.cs tests/ONEVO.Tests.Unit/Features/DevPlatform/Tenancy/WorkManagementDapiDemoSeederTests.cs src/ONEVO.Infrastructure/Persistence/Seeders/WorkManagementDapiDemoData.cs
git commit -m "feat(work): seed dapi demo tasks, statuses, and approvals queue"
```

---

### Task 3: Docs touch-up (optional same commit as Task 2 if tiny)

- [ ] Ensure design remains under `docs/superpowers/specs/next/`.
- [ ] Leave finishing-pass move of Task Foundation docs alone (separate gate).

---

## Done when

- All new + existing `WorkManagementDapiDemoSeederTests` pass.
- Second seed run does not inflate task/status/approval counts.
- Each project has template statuses + leaf tasks with valid ShortIds and slack.
- Approvals queue floors met (≥3 TCR + ≥1 extend per project).
