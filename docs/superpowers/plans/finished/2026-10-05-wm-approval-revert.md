# WM Approval Revert Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task, inline in the current session. Do not spawn subagents. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let the person who decided a Work Management approval (Approve or Reject) undo that decision within 30 minutes — including undoing whatever the approved action actually changed — and reopen the request as Pending so it can be decided again.

**Architecture:** Mirrors the existing decide path. `WorkApprovalRequest` gains `UndoStateJson`/`RevertedAt`/`RevertedByEmployeeId`. Every `IApprovalActionApplier` that mutates fields now also returns an `UndoJson` snapshot captured at apply time (before the mutation), stored on the request. A new parallel `IApprovalActionReverter`/`IApprovalActionReverterRegistry` (one reverter per action type, 18 total) reads that snapshot back and undoes it — reusing the existing `IModuleWriteService`/`ISprintWriteService`/`ITaskWriteService` methods wherever one is already symmetric (e.g. Achieve/Unachieve), and adding small `Restore`/`ApplyUncompleteAsync`/`ApplyUnachieveAsync`(sprint) methods to those services where no symmetric method exists yet. A new `RevertWorkApprovalRequestCommand` (same transactional shape as `DecideWorkApprovalRequestCommandHandler`) enforces: caller must be `DecidedByEmployeeId`, `DecidedAt + 30min >= now`, target not already re-decided. Three action types (Sprint Complete, Module Member Add, Project Status Template Change) can refuse with a `Conflict` reason instead of corrupting data if reality has moved on since the original apply — everything else is a clean restore.

**Tech Stack:** .NET 9 / EF Core (Npgsql, snake_case naming) / MediatR / xUnit + Moq + FluentAssertions (backend, `HRMS-Backend-v1`); Angular 21 standalone components / Vitest (frontend, `Hrms--Web-application---front-end---v1`).

**Spec:** No separate spec doc — this plan follows the in-chat design approved in session `19607734-9424-479c-a87c-2e573d5b3e3e`, 2026-10-05 (summarized in the Architecture line above; full exploration notes: deletes are soft via `SoftDeleteInterceptor` for `WorkTask`/`Sprint`, but `Objective` "delete" is its own `IsActive` flag and never goes through that interceptor; `task.status_change`/`task.comment` never create approval requests so are out of scope; only the 18 action types with a registered `IApprovalActionApplier` can ever appear on the Approvals page).

## Global Constraints

- **Work Management only.** Never edit CoreHr, Leave, TimeAttendance, People, Calendar, Auth features, or shared notification/outbox code.
- **Stay on `feature/wm-milestones-page-redesign` in both repos — do not create a new branch.** (User's explicit instruction this session.)
- Engines/appliers/reverters never call `SaveChangesAsync` directly — the command handler wraps everything in `IUnitOfWork.ExecuteInTransactionAsync`, same convention as `DecideWorkApprovalRequestCommandHandler`.
- Every migration that adds a tenant-owned table includes the `TenantTables = [...]` RLS block (this plan only adds columns to an existing RLS-covered table, so no new RLS policy is needed — verify `wm_approval_requests` already has one; if Task 1's migration script shows no RLS statements, that's correct, not a bug).
- Test command: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~<Name>"`.
- Gate at the end of the backend tasks: `dotnet build src/ONEVO.Api -c Release`, `dotnet test tests/ONEVO.Tests.Unit -c Release`, `dotnet test tests/ONEVO.Tests.Architecture -c Release` all green.
- Gate at the end of the frontend tasks: `npx ng build`, `npx ng test --watch=false --browsers=ChromeHeadless`.
- **Do NOT run `dotnet ef database update`.** Do not push, do not open a PR.
- Commit messages end with `Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>`.
- `.ts` files are CRLF in the frontend repo — prefer the Edit tool over Write there.
- **"Build fails" false alarm:** `dotnet build` failing with NuGet.targets "path1 null" means a stale MSBuild server. Run `dotnet build-server shutdown` and retry.
- **Never write the word "SQLite"** anywhere under `src/`, not even in a comment — an architecture test fails on it.

## Review Focus

1. **Approve → revert → approve again must not spuriously go Stale.** `ModuleApplierBase`/`SprintApplierBase` return `Stale` when `target.UpdatedAt > request.TargetUpdatedAtSnapshot`. Apply and revert both bump `UpdatedAt` via `AuditableEntityInterceptor` (which stamps its own `now` during `SaveChangesAsync`, overwriting anything the reverter sets in memory). Every reverter that leaves `TargetUpdatedAtSnapshot` meaningful must re-baseline it to the **post-revert, post-save** `target.UpdatedAt` — read back from the same tracked entity reference after the first `SaveChangesAsync()` call, then persist that in a second `SaveChangesAsync()` in the same transaction. Task 4's handler and Task 6's `TaskEdit` reverter both pin this.
2. **TaskCreate's revert must null `request.TargetId`.** `TaskCreateApplier.ApplyAsync` overwrites `request.TargetId` with the new task's id (`src/ONEVO.Application/Features/WorkManagement/Tasks/Appliers/TaskCreateApplier.cs:53`). A reopened Pending request still pointing at a now-soft-deleted task would break the feed's "Open task" link and the one-pending-per-target-and-action unique index. Task 5 pins this.
3. **Revert must read `AppliedPayloadJson ?? PayloadJson`, never `PayloadJson` alone.** The approver may have edited the payload before approving (`DecideWorkApprovalRequestCommandHandler.cs:79-96` sets `AppliedPayloadJson` only when it differs from `PayloadJson`). `ModuleAllocationExtend`'s revert in particular must undo the hours that were *actually* applied, not the hours that were originally requested. Task 6 pins this.
4. **Pending-uniqueness race on revert.** `HasPendingAsync` (checked by `WorkApprovalEngine.SubmitAsync`) is a read then a later write — a resubmission racing a revert can both pass the check and then collide on `ux_wm_approval_requests_one_pending_per_target_action` when the revert flips the row back to Pending. Catch the unique-constraint violation and map it to 409, don't let it bubble as a 500. Task 4 pins this.
5. **Viewer-aware `canRevert`.** `WorkApprovalRequestMapper.ToResponse` has no caller context today. `canRevert` is true only for the viewer who is `request.DecidedByEmployeeId` — a different viewer must see `canRevert: false` even for the same row within the window (the Approvals page is shared: requester and decider both see the same request). Task 4 pins this with a test that asserts the requester's own view never shows `canRevert: true`.

---

## File map

| File | Responsibility |
|---|---|
| `src/ONEVO.Domain/Features/WorkManagement/Approvals/Entities/WorkApprovalRequest.cs` | add `UndoStateJson`, `RevertedAt`, `RevertedByEmployeeId` |
| `src/ONEVO.Infrastructure/Persistence/Configurations/WorkManagement/WorkApprovalRequestConfiguration.cs` | column type for `UndoStateJson` (`jsonb`) |
| `src/ONEVO.Infrastructure/Migrations/<ts>_AddApprovalRevertColumns.cs` | the three columns |
| `src/ONEVO.Application/Features/WorkManagement/Approvals/Services/IApprovalActionApplier.cs` | extend `ApplyOutcome` with `UndoJson` |
| `src/ONEVO.Application/Features/WorkManagement/Approvals/Services/IApprovalActionReverter.cs` | new: `ApprovalRevertContext`, `RevertOutcome`, `IApprovalActionReverter`, `IApprovalActionReverterRegistry` |
| `src/ONEVO.Application/Features/WorkManagement/Approvals/Services/ApprovalActionReverterRegistry.cs` | new registry impl, mirrors `ApprovalActionApplierRegistry` |
| `src/ONEVO.Application/Features/WorkManagement/Approvals/Commands/RevertWorkApprovalRequest/RevertWorkApprovalRequestCommand.cs` + `...CommandHandler.cs` | new command |
| `src/ONEVO.Application/Features/WorkManagement/Approvals/DTOs/WorkApprovalRequestResponse.cs` | add `CanRevert`, `RevertableUntil` |
| `src/ONEVO.Application/Features/WorkManagement/Approvals/DTOs/ApprovalDetailResponse.cs` | same two fields |
| `src/ONEVO.Application/Features/WorkManagement/Approvals/Mappers/WorkApprovalRequestMapper.cs` | thread `viewerEmployeeId`, compute the two fields |
| `src/ONEVO.Application/Features/WorkManagement/Approvals/Queries/ListProjectWorkApprovals/ListProjectWorkApprovalsQueryHandler.cs` | pass `caller` into the mapper call |
| `src/ONEVO.Application/Features/WorkManagement/Approvals/Queries/GetApprovalDetail/GetApprovalDetailQueryHandler.cs` | set the two fields on `ApprovalDetailResponse` |
| `src/ONEVO.API/Controllers/Tenant/WorkManagement/WorkApprovalsController.cs` | `POST approvals/{id}/revert` |
| `src/ONEVO.Application/Features/WorkManagement/{Objectives,Sprints,Tasks}/Services/I*WriteService.cs` + impls | add `Restore`, `ApplyUncompleteAsync`, `ApplyUnachieveAsync` (sprint) |
| `src/ONEVO.Application/Features/WorkManagement/{Objectives,Sprints,Tasks}/Reverters/*.cs` | 18 new reverter classes (new `Reverters` folder per feature area, sibling to each existing `Appliers` folder) |
| `src/ONEVO.Infrastructure/DependencyInjection.cs` | register the 18 reverters + registry |
| `src/app/modules/work/data-access/work-approvals-api.service.ts` | `revertWorkApproval(id)` |
| `src/app/modules/work/models/dto/work-approval.dto.ts` | `canRevert`, `revertableUntil` |
| `src/app/modules/work/state/work-approvals.store.ts` | revert action + optimistic row update |
| `src/app/modules/work/feature/work-approvals/work-approvals.component.ts` (+ `.html`) | Revert button, countdown, confirm dialog |

---

### Task 1: Schema — `UndoStateJson`, `RevertedAt`, `RevertedByEmployeeId`

**Files:**
- Modify:
  - `src/ONEVO.Domain/Features/WorkManagement/Approvals/Entities/WorkApprovalRequest.cs`
  - `src/ONEVO.Infrastructure/Persistence/Configurations/WorkManagement/WorkApprovalRequestConfiguration.cs`
- Create: `src/ONEVO.Infrastructure/Migrations/<ts>_AddApprovalRevertColumns.cs` (generated, then hand-checked)
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Approvals/EfWorkApprovalRequestRepositoryTests.cs` (extend)

**Interfaces:**
- Produces: `WorkApprovalRequest.UndoStateJson` (`string?`), `.RevertedAt` (`DateTimeOffset?`), `.RevertedByEmployeeId` (`Guid?`)

- [ ] **Step 1: Add the three columns to the entity**

In `src/ONEVO.Domain/Features/WorkManagement/Approvals/Entities/WorkApprovalRequest.cs`, after `DecidedAt`:

```csharp
    /// <summary>Snapshot captured by the applier at approve-time (before it mutated anything), used to
    /// undo the change on revert. Null for request types whose apply has nothing to undo (Reject never
    /// applies, and some appliers reuse PayloadJson/AppliedPayloadJson instead of a separate snapshot).</summary>
    public string? UndoStateJson { get; set; }
    public DateTimeOffset? RevertedAt { get; set; }
    public Guid? RevertedByEmployeeId { get; set; }
```

- [ ] **Step 2: Configure the new column type**

In `WorkApprovalRequestConfiguration.cs`, next to the existing `PayloadJson` line:

```csharp
        builder.Property(r => r.UndoStateJson).HasColumnType("jsonb");
```

- [ ] **Step 3: Write the failing repository test**

Add to `EfWorkApprovalRequestRepositoryTests.cs`:

```csharp
    [Fact]
    public async Task RevertColumns_RoundTripThroughSaveAndReload()
    {
        var request = NewRequest(Guid.NewGuid(), WorkApprovalRequestStatuses.Pending);
        await using (var db = CreateContext())
        {
            db.WorkApprovalRequests.Add(request);
            await db.SaveChangesAsync();
        }

        var reverter = Guid.NewGuid();
        var revertedAt = DateTimeOffset.UtcNow;
        await using (var db = CreateContext())
        {
            var repo = new EfWorkApprovalRequestRepository(db);
            var tracked = await repo.GetTrackedByIdForTenantAsync(TenantId, request.Id);
            tracked!.UndoStateJson = "{\"title\":\"Old title\"}";
            tracked.RevertedAt = revertedAt;
            tracked.RevertedByEmployeeId = reverter;
            repo.Update(tracked);
            await db.SaveChangesAsync();
        }

        await using var read = CreateContext();
        var reloaded = await new EfWorkApprovalRequestRepository(read).GetTrackedByIdForTenantAsync(TenantId, request.Id);
        reloaded!.UndoStateJson.Should().Be("{\"title\":\"Old title\"}");
        reloaded.RevertedAt.Should().BeCloseTo(revertedAt, TimeSpan.FromSeconds(1));
        reloaded.RevertedByEmployeeId.Should().Be(reverter);
    }
```

- [ ] **Step 4: Run it to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~RevertColumns_RoundTripThroughSaveAndReload"`
Expected: build FAIL, `UndoStateJson` not found on `WorkApprovalRequest`.

- [ ] **Step 5: Re-run after Steps 1-2 land**

Expected: 1 passed.

- [ ] **Step 6: Generate the migration**

```bash
dotnet build src/ONEVO.Application
dotnet ef migrations add AddApprovalRevertColumns --project src/ONEVO.Infrastructure/ONEVO.Infrastructure.csproj --startup-project src/ONEVO.API/ONEVO.Api.csproj
```

If it fails with NuGet.targets "path1 null", run `dotnet build-server shutdown` and retry (stale MSBuild server, not the code).

Expected, in the generated `Up`: three `AddColumn` calls (`undo_state_json` jsonb, `reverted_at` timestamptz, `reverted_by_employee_id` uuid) on `wm_approval_requests`. No `CreateTable`, no RLS statements needed — this is an existing, already-RLS-covered table.

**Check the diff of `ApplicationDbContextModelSnapshot.cs`: it must only add these three columns.** If unrelated modules show up as changed, the snapshot is stale from another checkout (see memory `feedback_hrms_migration_snapshot_corruption`) — stop and diagnose before continuing.

- [ ] **Step 7: Verify the migration script and the architecture suite**

```bash
dotnet ef migrations script <previous-migration-name> AddApprovalRevertColumns --project src/ONEVO.Infrastructure/ONEVO.Infrastructure.csproj --startup-project src/ONEVO.API/ONEVO.Api.csproj -o <scratchpad>/approval_revert_columns.sql
dotnet test tests/ONEVO.Tests.Architecture
```

`<previous-migration-name>` is whatever migration currently sorts last in `src/ONEVO.Infrastructure/Migrations`. **Do not run `database update`.**

- [ ] **Step 8: Commit**

```bash
git add src/ONEVO.Domain/Features/WorkManagement/Approvals src/ONEVO.Infrastructure/Persistence/Configurations/WorkManagement/WorkApprovalRequestConfiguration.cs src/ONEVO.Infrastructure/Migrations tests/ONEVO.Tests.Unit/Features/WorkManagement/Approvals/EfWorkApprovalRequestRepositoryTests.cs
git commit -m "feat(work-management): add approval revert audit columns

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 2: Reverter interfaces and registry

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/Approvals/Services/IApprovalActionApplier.cs`
- Create:
  - `src/ONEVO.Application/Features/WorkManagement/Approvals/Services/IApprovalActionReverter.cs`
  - `src/ONEVO.Application/Features/WorkManagement/Approvals/Services/ApprovalActionReverterRegistry.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Approvals/ApprovalActionReverterRegistryTests.cs`

**Interfaces:**
- Produces:
  - `ApplyOutcome.UndoJson` (`string?`, new optional positional param, default `null` — every existing `ApplyOutcome.Stale`/`Invalid(...)` call site keeps compiling unchanged; `Applied` changes shape from a static property to a static method, a deliberate breaking rename Step 7 fixes at all 18 call sites)
  - `ApprovalRevertContext(WorkApprovalRequest Request, Guid DeciderEmployeeId)`
  - `enum RevertOutcomeKind { Reverted, Stale, Conflict, NotRevertable }`
  - `RevertOutcome(RevertOutcomeKind Kind, string? Reason, Func<DateTimeOffset?>? ReadTargetUpdatedAt)` with factory methods `Reverted(Func<DateTimeOffset?>)`, `Stale`, `Conflict(string)`, `NotRevertable(string)`
  - `IApprovalActionReverter { string ActionType; Task<RevertOutcome> RevertAsync(ApprovalRevertContext, CancellationToken); }`
  - `IApprovalActionReverterRegistry { IApprovalActionReverter? Find(string actionType); }`

- [ ] **Step 1: Extend `ApplyOutcome` with `UndoJson`**

In `IApprovalActionApplier.cs`, change:

```csharp
public sealed record ApplyOutcome(ApplyOutcomeKind Kind, string? Error = null)
{
    public static ApplyOutcome Applied { get; } = new(ApplyOutcomeKind.Applied);
    /// <summary>Target deleted, or changed since Request.TargetUpdatedAtSnapshot - nothing applied.</summary>
    public static ApplyOutcome Stale { get; } = new(ApplyOutcomeKind.Stale);
    /// <summary>The (possibly approver-edited) payload fails validation - nothing applied, the request stays pending.</summary>
    public static ApplyOutcome Invalid(string error) => new(ApplyOutcomeKind.Invalid, error);
}
```

to:

```csharp
public sealed record ApplyOutcome(ApplyOutcomeKind Kind, string? Error = null, string? UndoJson = null)
{
    /// <summary>undoJson is the pre-mutation snapshot a reverter needs to undo this apply. Appliers that
    /// have nothing to snapshot (nothing mutated beyond what AppliedPayloadJson/PayloadJson already
    /// records, or a type with no reverter) pass null.</summary>
    public static ApplyOutcome Applied(string? undoJson = null) => new(ApplyOutcomeKind.Applied, null, undoJson);
    /// <summary>Target deleted, or changed since Request.TargetUpdatedAtSnapshot - nothing applied.</summary>
    public static ApplyOutcome Stale { get; } = new(ApplyOutcomeKind.Stale);
    /// <summary>The (possibly approver-edited) payload fails validation - nothing applied, the request stays pending.</summary>
    public static ApplyOutcome Invalid(string error) => new(ApplyOutcomeKind.Invalid, error);
}
```

**This renames the `Applied` property to the `Applied(...)` method.** Every existing applier that currently writes `return ApplyOutcome.Applied;` must become `return ApplyOutcome.Applied();` — Task 5/6/7/8 update each one as they add that type's `UndoJson`. Grep `ApplyOutcome.Applied\b` (word boundary, not followed by `(`) across `src/ONEVO.Application/Features/WorkManagement` right after this step to get the full list; there are 18, one per existing applier.

- [ ] **Step 2: Create the reverter interfaces**

`src/ONEVO.Application/Features/WorkManagement/Approvals/Services/IApprovalActionReverter.cs`:

```csharp
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;

namespace ONEVO.Application.Features.WorkManagement.Approvals.Services;

public sealed record ApprovalRevertContext(WorkApprovalRequest Request, Guid DeciderEmployeeId);

public enum RevertOutcomeKind { Reverted, Stale, Conflict, NotRevertable }

public sealed record RevertOutcome(RevertOutcomeKind Kind, string? Reason = null, Func<DateTimeOffset?>? ReadTargetUpdatedAt = null)
{
    /// <summary>readTargetUpdatedAt lets the caller re-baseline Request.TargetUpdatedAtSnapshot to the
    /// true post-revert, post-save value (the reverter holds the tracked target reference; the
    /// AuditableEntityInterceptor only stamps the real UpdatedAt during SaveChangesAsync, so the caller
    /// must read it back AFTER that save, not compute its own "now"). Null when the reverter touched no
    /// snapshot-checked target (e.g. task.create, which nulls TargetId instead).</summary>
    public static RevertOutcome Reverted(Func<DateTimeOffset?>? readTargetUpdatedAt = null) => new(RevertOutcomeKind.Reverted, null, readTargetUpdatedAt);
    /// <summary>The target was deleted or changed since the request was decided - nothing to undo.</summary>
    public static RevertOutcome Stale { get; } = new(RevertOutcomeKind.Stale);
    /// <summary>Reality moved on since apply in a way that makes undoing it unsafe (e.g. a member who was
    /// added has since been assigned work). The request stays Approved/Rejected; nothing is undone.</summary>
    public static RevertOutcome Conflict(string reason) => new(RevertOutcomeKind.Conflict, reason);
    /// <summary>This action type has no reverter, or this specific request has nothing to undo (e.g. it
    /// was Rejected, so nothing was ever applied).</summary>
    public static RevertOutcome NotRevertable(string reason) => new(RevertOutcomeKind.NotRevertable, reason);
}

/// <summary>
/// Undoes one approved Work Management action type - the mirror of IApprovalActionApplier. One
/// implementation per WorkActionTypes value that has something to undo, registered in DI as
/// IApprovalActionReverter. Runs inside the revert command's transaction and must not call
/// SaveChangesAsync.
/// </summary>
public interface IApprovalActionReverter
{
    string ActionType { get; }
    Task<RevertOutcome> RevertAsync(ApprovalRevertContext context, CancellationToken ct);
}

public interface IApprovalActionReverterRegistry
{
    IApprovalActionReverter? Find(string actionType);
}
```

- [ ] **Step 3: Write the failing registry test**

`tests/ONEVO.Tests.Unit/Features/WorkManagement/Approvals/ApprovalActionReverterRegistryTests.cs`:

```csharp
using FluentAssertions;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Approvals;

public class ApprovalActionReverterRegistryTests
{
    private sealed class FakeReverter : IApprovalActionReverter
    {
        public string ActionType { get; init; } = WorkActionTypes.ModuleEdit;
        public Task<RevertOutcome> RevertAsync(ApprovalRevertContext context, CancellationToken ct)
            => Task.FromResult(RevertOutcome.Reverted());
    }

    [Fact]
    public void Find_ReturnsRegisteredReverter_ByActionType()
    {
        var registry = new ApprovalActionReverterRegistry(new IApprovalActionReverter[] { new FakeReverter() });
        registry.Find(WorkActionTypes.ModuleEdit).Should().NotBeNull();
        registry.Find(WorkActionTypes.TaskDelete).Should().BeNull();
    }

    [Fact]
    public void Constructor_TwoRevertersForSameActionType_Throws()
    {
        var act = () => new ApprovalActionReverterRegistry(new IApprovalActionReverter[]
        {
            new FakeReverter(), new FakeReverter()
        });
        act.Should().Throw<InvalidOperationException>();
    }
}
```

- [ ] **Step 4: Run it to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~ApprovalActionReverterRegistryTests"`
Expected: build FAIL, `ApprovalActionReverterRegistry` not found.

- [ ] **Step 5: Implement the registry**

`src/ONEVO.Application/Features/WorkManagement/Approvals/Services/ApprovalActionReverterRegistry.cs`:

```csharp
namespace ONEVO.Application.Features.WorkManagement.Approvals.Services;

public sealed class ApprovalActionReverterRegistry : IApprovalActionReverterRegistry
{
    private readonly IReadOnlyDictionary<string, IApprovalActionReverter> _byActionType;

    public ApprovalActionReverterRegistry(IEnumerable<IApprovalActionReverter> reverters)
    {
        var map = new Dictionary<string, IApprovalActionReverter>();
        foreach (var reverter in reverters)
        {
            if (!map.TryAdd(reverter.ActionType, reverter))
                throw new InvalidOperationException($"Two approval reverters are registered for '{reverter.ActionType}'.");
        }
        _byActionType = map;
    }

    public IApprovalActionReverter? Find(string actionType) => _byActionType.GetValueOrDefault(actionType);
}
```

- [ ] **Step 6: Run the test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~ApprovalActionReverterRegistryTests"`
Expected: 2 passed.

- [ ] **Step 7: Fix every existing `ApplyOutcome.Applied` call site**

Grep `ApplyOutcome\.Applied;` and `ApplyOutcome\.Applied$` across `src/ONEVO.Application/Features/WorkManagement`. For each of the 18 existing appliers, change `return ApplyOutcome.Applied;` to `return ApplyOutcome.Applied();` (no snapshot yet — Tasks 5-8 fill in the real `UndoJson` for each as they add that type's reverter). This step alone must compile and all existing unit tests must still pass before moving on.

- [ ] **Step 8: Run the full existing Approvals test suite**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~Features.WorkManagement.Approvals|FullyQualifiedName~Appliers"`
Expected: all green, same count as before this task (behavior unchanged, only the `Applied` call sites changed shape).

- [ ] **Step 9: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement
git commit -m "feat(work-management): add IApprovalActionReverter and UndoJson on ApplyOutcome

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 3: Write-service additions — `Restore` and the sprint unwind methods

**Files:**
- Modify:
  - `src/ONEVO.Application/Features/WorkManagement/Tasks/Services/ITaskWriteService.cs` + `TaskWriteService.cs`
  - `src/ONEVO.Application/Features/WorkManagement/Objectives/Services/IModuleWriteService.cs` + `ModuleWriteService.cs`
  - `src/ONEVO.Application/Features/WorkManagement/Sprints/Services/ISprintWriteService.cs` + `SprintWriteService.cs`
  - `src/ONEVO.Domain/Features/WorkManagement/Sprints/Entities/SprintActivityLog.cs` (add `SprintActivityActions.Reverted`)
- Test:
  - `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/TaskWriteServiceTests.cs` (extend)
  - `tests/ONEVO.Tests.Unit/Features/WorkManagement/Objectives/ModuleWriteServiceTests.cs` (extend)
  - `tests/ONEVO.Tests.Unit/Features/WorkManagement/Sprints/SprintWriteServiceTests.cs` (extend)

**Interfaces:**
- Produces:
  - `ITaskWriteService.Restore(WorkTask trackedTask)` — `void`
  - `IModuleWriteService.Restore(Objective trackedModule)` — `void`
  - `ISprintWriteService.Restore(Guid tenantId, Sprint trackedSprint, IReadOnlyCollection<Guid> taskIdsToReattach, CancellationToken ct)` — `Task<Result>`
  - `ISprintWriteService.ApplyUncompleteAsync(Guid tenantId, Guid actorEmployeeId, Sprint trackedSprint, IReadOnlyCollection<Guid> movedTaskIds, Guid? targetSprintId, CancellationToken ct)` — `Task<Result>`
  - `ISprintWriteService.ApplyUnachieveAsync(Guid tenantId, Guid actorEmployeeId, Sprint trackedSprint, string previousStatus, CancellationToken ct)` — `Task<Result>`
  - `SprintActivityActions.Reverted` (`"reverted"`)

> Module delete ("`Objective.IsActive = false`") and Task/Sprint delete (soft-delete via `SoftDeleteInterceptor`, `BaseEntity.IsDeleted`/`DeletedAt`) are **different mechanisms** — confirmed by reading `ModuleWriteService.ApplyDeleteAsync` (sets `IsActive` directly, never calls `_objectives.Remove(...)`) versus `TaskWriteService.Delete`/`SprintWriteService.ApplyDeleteAsync` (both call `.Remove(...)`, which `SoftDeleteInterceptor` converts to `IsDeleted = true`). `Restore` must flip whichever flag that type actually uses.

- [ ] **Step 1: Write the failing `Restore` tests**

Add to `TaskWriteServiceTests.cs` (follow that file's existing fixture/mock setup for the constructor):

```csharp
    [Fact]
    public void Restore_ClearsIsDeletedAndDeletedAt()
    {
        var task = new WorkTask { Id = Guid.NewGuid(), TenantId = TenantId, IsDeleted = true, DeletedAt = DateTimeOffset.UtcNow };
        var service = Build();
        service.Restore(task);
        task.IsDeleted.Should().BeFalse();
        task.DeletedAt.Should().BeNull();
    }
```

Add to `ModuleWriteServiceTests.cs`:

```csharp
    [Fact]
    public void Restore_SetsIsActiveTrue()
    {
        var module = new Objective { Id = Guid.NewGuid(), TenantId = TenantId, IsActive = false };
        var service = Build();
        service.Restore(module);
        module.IsActive.Should().BeTrue();
    }
```

Add to `SprintWriteServiceTests.cs` (adjust the mock setup to this file's existing `Build()` helper and mocked repositories):

```csharp
    [Fact]
    public async Task Restore_UndeletesSprintAndReattachesGivenTasks()
    {
        var sprint = new Sprint { Id = Guid.NewGuid(), TenantId = TenantId, IsDeleted = true, DeletedAt = DateTimeOffset.UtcNow, Status = SprintStatuses.Complete };
        var task = new WorkTask { Id = Guid.NewGuid(), TenantId = TenantId, SprintId = null };
        _tasks.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, task.Id, It.IsAny<CancellationToken>())).ReturnsAsync(task);

        var result = await Build().Restore(TenantId, sprint, new[] { task.Id }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        sprint.IsDeleted.Should().BeFalse();
        sprint.DeletedAt.Should().BeNull();
        task.SprintId.Should().Be(sprint.Id);
    }

    [Fact]
    public async Task Restore_TaskAlreadyInAnotherSprint_ReturnsConflict()
    {
        var sprint = new Sprint { Id = Guid.NewGuid(), TenantId = TenantId, IsDeleted = true, Status = SprintStatuses.Complete };
        var elsewhere = Guid.NewGuid();
        var task = new WorkTask { Id = Guid.NewGuid(), TenantId = TenantId, SprintId = elsewhere };
        _tasks.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, task.Id, It.IsAny<CancellationToken>())).ReturnsAsync(task);

        var result = await Build().Restore(TenantId, sprint, new[] { task.Id }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        sprint.IsDeleted.Should().BeTrue(); // nothing applied
    }
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~Restore_"`
Expected: build FAIL, `Restore` not found on any of the three services.

- [ ] **Step 3: Add `Restore` to `ITaskWriteService`/`TaskWriteService`**

In `ITaskWriteService.cs`, next to `void Delete(WorkTask trackedTask);`:

```csharp
    /// <summary>Undoes Delete: clears the soft-delete flags set by SoftDeleteInterceptor.</summary>
    void Restore(WorkTask trackedTask);
```

In `TaskWriteService.cs`, next to `public void Delete(WorkTask trackedTask) => _tasks.Remove(trackedTask);`:

```csharp
    public void Restore(WorkTask trackedTask)
    {
        trackedTask.IsDeleted = false;
        trackedTask.DeletedAt = null;
        _tasks.Update(trackedTask);
    }
```

- [ ] **Step 4: Add `Restore` to `IModuleWriteService`/`ModuleWriteService`**

In `IModuleWriteService.cs`, next to the delete methods:

```csharp
    /// <summary>Undoes ApplyDeleteAsync: Objective "delete" is the IsActive flag, not BaseEntity.IsDeleted.</summary>
    void Restore(Objective trackedModule);
```

In `ModuleWriteService.cs`, in the `// ---- Delete ----` section, after `ApplyDeleteAsync`:

```csharp
    public void Restore(Objective trackedModule)
    {
        trackedModule.IsActive = true;
        trackedModule.UpdatedAt = DateTimeOffset.UtcNow;
        _objectives.Update(trackedModule);
    }
```

- [ ] **Step 5: Add `Restore` to `ISprintWriteService`/`SprintWriteService`**

In `ISprintWriteService.cs`, next to `ApplyDeleteAsync`:

```csharp
    /// <summary>Undoes ApplyDeleteAsync: clears the soft-delete flags and reattaches the given tasks to
    /// this sprint (the ones ApplyDeleteAsync detached). Conflict (nothing applied) if any of those
    /// tasks has since been attached to a different sprint.</summary>
    Task<Result> Restore(Guid tenantId, Sprint trackedSprint, IReadOnlyCollection<Guid> taskIdsToReattach, CancellationToken ct = default);
```

In `SprintWriteService.cs`, in the `// ---- Delete ----` section, after `ApplyDeleteAsync`:

```csharp
    public async Task<Result> Restore(Guid tenantId, Sprint trackedSprint, IReadOnlyCollection<Guid> taskIdsToReattach, CancellationToken ct = default)
    {
        var tracked = new List<Domain.Features.WorkManagement.Tasks.Entities.WorkTask>();
        foreach (var taskId in taskIdsToReattach)
        {
            var task = await _tasks.GetTrackedByIdForTenantAsync(tenantId, taskId, ct);
            // A task this sprint's delete moved out must still be unassigned - if it's since joined a
            // different sprint, re-grabbing it here would silently steal it from that other sprint.
            if (task is null || task.SprintId is not null)
                return Result.Conflict("A task that was in this sprint has since moved elsewhere - restore it manually first.");
            tracked.Add(task);
        }

        foreach (var task in tracked)
        {
            task.SprintId = trackedSprint.Id;
            task.UpdatedAt = DateTimeOffset.UtcNow;
            _tasks.Update(task);
        }

        trackedSprint.IsDeleted = false;
        trackedSprint.DeletedAt = null;
        trackedSprint.UpdatedAt = DateTimeOffset.UtcNow;
        return Result.Success();
    }
```

- [ ] **Step 6: Run the `Restore` tests again**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~Restore_"`
Expected: 4 passed.

- [ ] **Step 7: Write the failing `ApplyUncompleteAsync` test**

Add to `SprintWriteServiceTests.cs`:

```csharp
    [Fact]
    public async Task ApplyUncompleteAsync_RestoresActiveStatusAndMovedTasks()
    {
        var sprint = new Sprint { Id = Guid.NewGuid(), TenantId = TenantId, Status = SprintStatuses.Complete, CompletedAt = DateTimeOffset.UtcNow };
        var movedTask = new WorkTask { Id = Guid.NewGuid(), TenantId = TenantId, SprintId = null };
        _tasks.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, movedTask.Id, It.IsAny<CancellationToken>())).ReturnsAsync(movedTask);

        var result = await Build().ApplyUncompleteAsync(TenantId, ActorId, sprint, new[] { movedTask.Id }, null, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        sprint.Status.Should().Be(SprintStatuses.Active);
        sprint.CompletedAt.Should().BeNull();
        movedTask.SprintId.Should().Be(sprint.Id);
    }

    [Fact]
    public async Task ApplyUncompleteAsync_MovedTaskNoLongerWhereCompleteLeftIt_ReturnsConflict()
    {
        var sprint = new Sprint { Id = Guid.NewGuid(), TenantId = TenantId, Status = SprintStatuses.Complete };
        var elsewhere = Guid.NewGuid();
        var movedTask = new WorkTask { Id = Guid.NewGuid(), TenantId = TenantId, SprintId = elsewhere };
        _tasks.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, movedTask.Id, It.IsAny<CancellationToken>())).ReturnsAsync(movedTask);

        var result = await Build().ApplyUncompleteAsync(TenantId, ActorId, sprint, new[] { movedTask.Id }, null, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        sprint.Status.Should().Be(SprintStatuses.Complete);
    }
```

- [ ] **Step 8: Implement `ApplyUncompleteAsync`**

In `ISprintWriteService.cs`:

```csharp
    /// <summary>Undoes ApplyCompleteAsync: Active status, clears CompletedAt, and moves movedTaskIds back
    /// onto this sprint. Conflict (nothing applied) if a moved task is no longer where Complete left it
    /// (targetSprintId if Disposition was "sprint", else unassigned) - something else touched it since.</summary>
    Task<Result> ApplyUncompleteAsync(Guid tenantId, Guid actorEmployeeId, Sprint trackedSprint, IReadOnlyCollection<Guid> movedTaskIds, Guid? targetSprintId, CancellationToken ct = default);
```

In `SprintWriteService.cs`, after `ApplyCompleteAsync`:

```csharp
    public async Task<Result> ApplyUncompleteAsync(Guid tenantId, Guid actorEmployeeId, Sprint trackedSprint, IReadOnlyCollection<Guid> movedTaskIds, Guid? targetSprintId, CancellationToken ct = default)
    {
        var tracked = new List<Domain.Features.WorkManagement.Tasks.Entities.WorkTask>();
        foreach (var taskId in movedTaskIds)
        {
            var task = await _tasks.GetTrackedByIdForTenantAsync(tenantId, taskId, ct);
            if (task is null || task.SprintId != targetSprintId)
                return Result.Conflict("A task this sprint's completion moved has since moved again - complete the sprint again instead of reverting.");
            tracked.Add(task);
        }

        foreach (var task in tracked)
        {
            task.SprintId = trackedSprint.Id;
            task.UpdatedAt = DateTimeOffset.UtcNow;
            _tasks.Update(task);
        }

        var fromStatus = trackedSprint.Status;
        trackedSprint.Status = SprintStatuses.Active;
        trackedSprint.CompletedAt = null;
        trackedSprint.UpdatedAt = DateTimeOffset.UtcNow;

        await _logs.AddAsync(SprintActivityLogFactory.Create(
            tenantId, trackedSprint.Id, actorEmployeeId, SprintActivityActions.Reverted, fromStatus, SprintStatuses.Active,
            new { movedBackTaskIds = movedTaskIds }), ct);
        return Result.Success();
    }
```

- [ ] **Step 9: Write the failing `ApplyUnachieveAsync` (sprint) test**

```csharp
    [Fact]
    public async Task ApplyUnachieveAsync_RestoresPreviousStatusAndClearsAchievedAt()
    {
        var sprint = new Sprint { Id = Guid.NewGuid(), TenantId = TenantId, Status = SprintStatuses.Achieved, AchievedAt = DateTimeOffset.UtcNow };
        var result = await Build().ApplyUnachieveAsync(TenantId, ActorId, sprint, SprintStatuses.Active, CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
        sprint.Status.Should().Be(SprintStatuses.Active);
        sprint.AchievedAt.Should().BeNull();
    }
```

- [ ] **Step 10: Implement `ApplyUnachieveAsync` (sprint)**

In `ISprintWriteService.cs`:

```csharp
    /// <summary>Undoes ApplyAchieveAsync: restores the status the sprint had right before it was
    /// achieved (Draft, Active or Complete - Achieve is allowed from any of those) and clears AchievedAt.</summary>
    Task<Result> ApplyUnachieveAsync(Guid tenantId, Guid actorEmployeeId, Sprint trackedSprint, string previousStatus, CancellationToken ct = default);
```

In `SprintWriteService.cs`, after `ApplyAchieveAsync`:

```csharp
    public Task<Result> ApplyUnachieveAsync(Guid tenantId, Guid actorEmployeeId, Sprint trackedSprint, string previousStatus, CancellationToken ct = default)
    {
        if (trackedSprint.Status != SprintStatuses.Achieved)
            return Task.FromResult(Result.Conflict("This sprint is not achieved."));

        trackedSprint.Status = previousStatus;
        trackedSprint.AchievedAt = null;
        trackedSprint.UpdatedAt = DateTimeOffset.UtcNow;
        return Task.FromResult(Result.Success());
    }
```

(No activity log call here deliberately — `ApplyUnachieveAsync` has no `ct`-requiring I/O once the guard clause passes, so it stays synchronous like `ValidateAchieveAsync`; the caller, `SprintAchieveReverter` in Task 6, logs via the generic revert notification path instead.)

- [ ] **Step 11: Add the `Reverted` activity action**

In `src/ONEVO.Domain/Features/WorkManagement/Sprints/Entities/SprintActivityLog.cs`, in `SprintActivityActions`:

```csharp
    public const string Reverted = "reverted";
```

- [ ] **Step 12: Run every new write-service test**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~Restore_|FullyQualifiedName~ApplyUncompleteAsync|FullyQualifiedName~ApplyUnachieveAsync"`
Expected: 8 passed.

- [ ] **Step 13: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Tasks/Services src/ONEVO.Application/Features/WorkManagement/Objectives/Services src/ONEVO.Application/Features/WorkManagement/Sprints/Services src/ONEVO.Domain/Features/WorkManagement/Sprints/Entities/SprintActivityLog.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/TaskWriteServiceTests.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/Objectives/ModuleWriteServiceTests.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/Sprints/SprintWriteServiceTests.cs
git commit -m "feat(work-management): add Restore/ApplyUncomplete/ApplyUnachieve write-service methods

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 4: `RevertWorkApprovalRequestCommand`, endpoint, and viewer-aware `canRevert`

**Files:**
- Modify:
  - `src/ONEVO.Domain/Features/WorkManagement/Notifications/Entities/WorkNotificationLog.cs` (add `WorkNotificationKinds.Reverted`)
  - `src/ONEVO.Application/Features/WorkManagement/Notifications/Services/WorkNotificationEngine.cs` (`DecisionWord`)
  - `src/ONEVO.Application/Features/WorkManagement/Approvals/DTOs/WorkApprovalRequestResponse.cs`
  - `src/ONEVO.Application/Features/WorkManagement/Approvals/DTOs/ApprovalDetailResponse.cs`
  - `src/ONEVO.Application/Features/WorkManagement/Approvals/Mappers/WorkApprovalRequestMapper.cs`
  - `src/ONEVO.Application/Features/WorkManagement/Approvals/Queries/ListProjectWorkApprovals/ListProjectWorkApprovalsQueryHandler.cs`
  - `src/ONEVO.Application/Features/WorkManagement/Approvals/Queries/GetApprovalDetail/GetApprovalDetailQueryHandler.cs`
  - `src/ONEVO.API/Controllers/Tenant/WorkManagement/WorkApprovalsController.cs`
- Create:
  - `src/ONEVO.Application/Features/WorkManagement/Approvals/Commands/RevertWorkApprovalRequest/RevertWorkApprovalRequestCommand.cs`
  - `src/ONEVO.Application/Features/WorkManagement/Approvals/Commands/RevertWorkApprovalRequest/RevertWorkApprovalRequestCommandHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Approvals/RevertWorkApprovalRequestCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `IApprovalActionReverterRegistry.Find`, `IWorkApprovalRequestRepository.GetTrackedByIdForTenantAsync`/`Update`, `IUnitOfWork.ExecuteInTransactionAsync`/`SaveChangesAsync`
- Produces:
  - `RevertWorkApprovalRequestCommand(Guid RequestId) : IRequest<Result<WorkApprovalRequestResponse>>`
  - `WorkApprovalRequestResponse` gains `bool CanRevert`, `DateTimeOffset? RevertableUntil` (both appended, so positional construction at the two existing call sites must add two arguments — not optional defaults, so the compiler catches every missed call site)
  - `WorkApprovalRequestMapper.ToResponse(WorkApprovalRequest r, IReadOnlyDictionary<Guid,string> names, Guid viewerEmployeeId, ProjectModuleTree? tree = null)`

> **30-minute window constant:** `private const int RevertWindowMinutes = 30;` on the handler, reused by the mapper via a small shared static (`ApprovalRevertWindow.Minutes`) so the two places that compute "is this revertable" never drift.

- [ ] **Step 1: Add the shared revert-window constant**

`src/ONEVO.Application/Features/WorkManagement/Approvals/Services/ApprovalRevertWindow.cs`:

```csharp
namespace ONEVO.Application.Features.WorkManagement.Approvals.Services;

/// <summary>The single source of truth for "how long after a decision can it be reverted" - both the
/// revert command's authorization check and the response mapper's canRevert/revertableUntil fields
/// read this, so the two can never disagree.</summary>
public static class ApprovalRevertWindow
{
    public const int Minutes = 30;
}
```

- [ ] **Step 2: Add `WorkNotificationKinds.Reverted`**

In `WorkNotificationLog.cs`:

```csharp
    public const string Reverted = "reverted";
```

In `WorkNotificationEngine.cs`'s `DecisionWord`:

```csharp
        WorkNotificationKinds.Reverted => "reopened for another decision",
```

(No template or seeder change: `NotifyAsync`'s `template` switch already falls through any kind it doesn't special-case — Direct/Alert/Requested/Commented — to `DecidedTemplate`, which is exactly where Approved/Rejected/Cancelled/Stale already land. `Reverted` lands there too for free.)

- [ ] **Step 3: Add `CanRevert`/`RevertableUntil` to both response DTOs**

In `WorkApprovalRequestResponse.cs`, append two required positional params after `CurrentAllocatedHours`:

```csharp
public sealed record WorkApprovalRequestResponse(
    Guid Id,
    Guid ProjectId,
    string ActionType,
    string TargetType,
    Guid? TargetId,
    string TargetTitle,
    string Status,
    Guid RequestedByEmployeeId,
    string RequestedByName,
    Guid ApproverEmployeeId,
    string ApproverName,
    string PayloadJson,
    string? DecisionComment,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DecidedAt,
    decimal? CurrentAllocatedHours,
    /// <summary>True only for the viewer who decided this request, only while Approved/Rejected, only
    /// within ApprovalRevertWindow.Minutes of DecidedAt.</summary>
    bool CanRevert,
    DateTimeOffset? RevertableUntil);
```

Open `ApprovalDetailResponse.cs`, find its record declaration, and append the same two fields at the end in the same style as its existing fields (match its exact naming/doc-comment conventions - read the file first).

- [ ] **Step 4: Thread `viewerEmployeeId` through the mapper**

`WorkApprovalRequestMapper.cs`:

```csharp
using ONEVO.Application.Features.WorkManagement.Approvals.DTOs;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Hierarchy;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;

namespace ONEVO.Application.Features.WorkManagement.Approvals.Mappers;

public static class WorkApprovalRequestMapper
{
    public static WorkApprovalRequestResponse ToResponse(
        WorkApprovalRequest r, IReadOnlyDictionary<Guid, string> names, Guid viewerEmployeeId, ProjectModuleTree? tree = null)
    {
        var revertableUntil = r.DecidedAt?.AddMinutes(ApprovalRevertWindow.Minutes);
        var canRevert = r.DecidedByEmployeeId == viewerEmployeeId
            && (r.Status == WorkApprovalRequestStatuses.Approved || r.Status == WorkApprovalRequestStatuses.Rejected)
            && revertableUntil is { } until && until >= DateTimeOffset.UtcNow;

        return new(
            r.Id, r.ProjectId, r.ActionType, r.TargetType, r.TargetId, r.TargetTitle, r.Status,
            r.RequestedByEmployeeId, names.GetValueOrDefault(r.RequestedByEmployeeId) ?? "Unknown",
            r.ApproverEmployeeId, names.GetValueOrDefault(r.ApproverEmployeeId) ?? "Unknown",
            r.PayloadJson, r.DecisionComment, r.CreatedAt, r.DecidedAt,
            r.ActionType == WorkActionTypes.ModuleAllocationExtend && r.TargetId is { } moduleId
                ? tree?.Get(moduleId)?.AllocatedHours
                : null,
            canRevert, r.Status == WorkApprovalRequestStatuses.Approved || r.Status == WorkApprovalRequestStatuses.Rejected ? revertableUntil : null);
    }
}
```

- [ ] **Step 5: Fix the two existing mapper call sites**

`ListProjectWorkApprovalsQueryHandler.cs:61`: change `WorkApprovalRequestMapper.ToResponse(r, names, tree)` to `WorkApprovalRequestMapper.ToResponse(r, names, caller, tree)` (`caller` is the `callerEmployeeId.Value` already resolved earlier in that handler, line 56).

`DecideWorkApprovalRequestCommandHandler.cs:126`: change `WorkApprovalRequestMapper.ToResponse(request, names)` to `WorkApprovalRequestMapper.ToResponse(request, names, caller)` (`caller` already resolved at line 51).

In `GetApprovalDetailQueryHandler.cs`: read how `ApprovalDetailResponse` is currently constructed (it does not go through `WorkApprovalRequestMapper` today), find the already-resolved caller employee id in that handler, and set the same `CanRevert`/`RevertableUntil` logic as Step 4 inline (or extract a tiny shared helper `ApprovalRevertWindow.ComputeCanRevert(request, viewerEmployeeId)` returning `(bool CanRevert, DateTimeOffset? RevertableUntil)` and call it from both the mapper and this handler, to avoid duplicating the boolean expression - prefer the helper).

- [ ] **Step 6: Write the failing command-handler tests**

`tests/ONEVO.Tests.Unit/Features/WorkManagement/Approvals/RevertWorkApprovalRequestCommandHandlerTests.cs`. Follow the mock/fixture setup style of `DecideWorkApprovalRequestCommandHandlerTests.cs` (same folder) for `ICurrentUser`, `ICallerIdentityResolver`, `IWorkApprovalRequestRepository`, `IUnitOfWork` (its `ExecuteInTransactionAsync` mock just invokes the passed delegate), `IWorkNotificationEngine`:

```csharp
    [Fact]
    public async Task Handle_CallerIsNotTheDecider_ReturnsForbidden()
    {
        var request = ApprovedRequest(decidedBy: Guid.NewGuid());
        _requests.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, request.Id, It.IsAny<CancellationToken>())).ReturnsAsync(request);

        var result = await Handler().Handle(new RevertWorkApprovalRequestCommand(request.Id), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Handle_WindowExpired_ReturnsConflict()
    {
        var request = ApprovedRequest(decidedBy: Caller, decidedAt: DateTimeOffset.UtcNow.AddMinutes(-31));
        _requests.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, request.Id, It.IsAny<CancellationToken>())).ReturnsAsync(request);

        var result = await Handler().Handle(new RevertWorkApprovalRequestCommand(request.Id), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Handle_RejectedRequest_FlipsToPendingWithNoReverterNeeded()
    {
        var request = RejectedRequest(decidedBy: Caller, decidedAt: DateTimeOffset.UtcNow.AddMinutes(-5));
        _requests.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, request.Id, It.IsAny<CancellationToken>())).ReturnsAsync(request);

        var result = await Handler().Handle(new RevertWorkApprovalRequestCommand(request.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        request.Status.Should().Be(WorkApprovalRequestStatuses.Pending);
        request.RevertedAt.Should().NotBeNull();
        request.RevertedByEmployeeId.Should().Be(Caller);
        request.DecidedByEmployeeId.Should().BeNull();
    }

    [Fact]
    public async Task Handle_ApprovedRequest_NoReverterRegistered_ReturnsUnprocessable()
    {
        var request = ApprovedRequest(decidedBy: Caller, decidedAt: DateTimeOffset.UtcNow.AddMinutes(-5));
        _requests.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, request.Id, It.IsAny<CancellationToken>())).ReturnsAsync(request);
        _reverters.Setup(x => x.Find(request.ActionType)).Returns((IApprovalActionReverter?)null);

        var result = await Handler().Handle(new RevertWorkApprovalRequestCommand(request.Id), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(422);
    }

    [Fact]
    public async Task Handle_ApprovedRequest_ReverterReturnsConflict_LeavesRequestApproved()
    {
        var request = ApprovedRequest(decidedBy: Caller, decidedAt: DateTimeOffset.UtcNow.AddMinutes(-5));
        _requests.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, request.Id, It.IsAny<CancellationToken>())).ReturnsAsync(request);
        var reverter = new Mock<IApprovalActionReverter>();
        reverter.Setup(x => x.RevertAsync(It.IsAny<ApprovalRevertContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RevertOutcome.Conflict("already moved on"));
        _reverters.Setup(x => x.Find(request.ActionType)).Returns(reverter.Object);

        var result = await Handler().Handle(new RevertWorkApprovalRequestCommand(request.Id), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
        request.Status.Should().Be(WorkApprovalRequestStatuses.Approved);
    }

    [Fact]
    public async Task Handle_ApprovedRequest_ReverterSucceeds_RebaselinesTargetUpdatedAtSnapshot()
    {
        var request = ApprovedRequest(decidedBy: Caller, decidedAt: DateTimeOffset.UtcNow.AddMinutes(-5));
        _requests.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, request.Id, It.IsAny<CancellationToken>())).ReturnsAsync(request);
        var freshUpdatedAt = DateTimeOffset.UtcNow;
        var reverter = new Mock<IApprovalActionReverter>();
        reverter.Setup(x => x.RevertAsync(It.IsAny<ApprovalRevertContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RevertOutcome.Reverted(() => freshUpdatedAt));
        _reverters.Setup(x => x.Find(request.ActionType)).Returns(reverter.Object);

        var result = await Handler().Handle(new RevertWorkApprovalRequestCommand(request.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        request.Status.Should().Be(WorkApprovalRequestStatuses.Pending);
        request.TargetUpdatedAtSnapshot.Should().Be(freshUpdatedAt);
        request.UndoStateJson.Should().BeNull();
        request.AppliedPayloadJson.Should().BeNull();
        _unitOfWork.Invocations.Count(i => i.Method.Name == nameof(IUnitOfWork.SaveChangesAsync)).Should().Be(2);
    }
```

Add `ApprovedRequest`/`RejectedRequest` helpers near the top of the class, building a `WorkApprovalRequest` with `Id`, `TenantId = TenantId`, `ActionType = WorkActionTypes.ModuleEdit`, `Status`, `DecidedByEmployeeId = decidedBy`, `DecidedAt = decidedAt ?? DateTimeOffset.UtcNow`. Add a `_reverters = new Mock<IApprovalActionReverterRegistry>()` field and pass `_reverters.Object` into the handler constructor alongside the existing mocks `DecideWorkApprovalRequestCommandHandlerTests.cs` already sets up (`_currentUser`, `_identity`, `_requests`, `_notifications`, `_unitOfWork`) — `Caller` is a fixed `Guid` the `_currentUser`/`_identity` mocks resolve to, matching that file's existing pattern.

- [ ] **Step 7: Run them to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~RevertWorkApprovalRequestCommandHandlerTests"`
Expected: build FAIL, `RevertWorkApprovalRequestCommand` not found.

- [ ] **Step 8: Implement the command**

`RevertWorkApprovalRequestCommand.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Approvals.DTOs;

namespace ONEVO.Application.Features.WorkManagement.Approvals.Commands.RevertWorkApprovalRequest;

public sealed record RevertWorkApprovalRequestCommand(Guid RequestId) : IRequest<Result<WorkApprovalRequestResponse>>;
```

- [ ] **Step 9: Implement the handler**

`RevertWorkApprovalRequestCommandHandler.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Exceptions;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Approvals.DTOs;
using ONEVO.Application.Features.WorkManagement.Approvals.Mappers;
using ONEVO.Application.Features.WorkManagement.Approvals.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.Notifications.Entities;

namespace ONEVO.Application.Features.WorkManagement.Approvals.Commands.RevertWorkApprovalRequest;

public sealed class RevertWorkApprovalRequestCommandHandler
    : IRequestHandler<RevertWorkApprovalRequestCommand, Result<WorkApprovalRequestResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IWorkApprovalRequestRepository _requests;
    private readonly IApprovalActionReverterRegistry _reverters;
    private readonly IWorkNotificationEngine _notifications;
    private readonly IUnitOfWork _unitOfWork;

    public RevertWorkApprovalRequestCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IWorkApprovalRequestRepository requests,
        IApprovalActionReverterRegistry reverters, IWorkNotificationEngine notifications, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _identity = identity;
        _requests = requests;
        _reverters = reverters;
        _notifications = notifications;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<WorkApprovalRequestResponse>> Handle(RevertWorkApprovalRequestCommand command, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<WorkApprovalRequestResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, _currentUser.UserId, ct);
        if (callerEmployeeId is null)
            return Result<WorkApprovalRequestResponse>.Forbidden("No employee record for the current user.");
        var caller = callerEmployeeId.Value;

        var request = await _requests.GetTrackedByIdForTenantAsync(tenantId, command.RequestId, ct);
        if (request is null)
            return Result<WorkApprovalRequestResponse>.NotFound("Approval request not found.");

        if (request.DecidedByEmployeeId != caller)
            return Result<WorkApprovalRequestResponse>.Forbidden("Only the person who decided this request can revert it.");
        if (request.Status is not (WorkApprovalRequestStatuses.Approved or WorkApprovalRequestStatuses.Rejected))
            return Result<WorkApprovalRequestResponse>.Conflict("This request is not in a state that can be reverted.");
        var revertableUntil = request.DecidedAt?.AddMinutes(ApprovalRevertWindow.Minutes);
        if (revertableUntil is null || revertableUntil < DateTimeOffset.UtcNow)
            return Result<WorkApprovalRequestResponse>.Conflict("The 30-minute window to revert this decision has passed.");

        Func<DateTimeOffset?>? readTargetUpdatedAt = null;
        if (request.Status == WorkApprovalRequestStatuses.Approved)
        {
            var reverter = _reverters.Find(request.ActionType);
            if (reverter is null)
                return Result<WorkApprovalRequestResponse>.UnprocessableEntity("This request type cannot be reverted.");

            var outcome = await reverter.RevertAsync(new ApprovalRevertContext(request, caller), ct);
            switch (outcome.Kind)
            {
                case RevertOutcomeKind.Stale:
                    return Result<WorkApprovalRequestResponse>.Conflict("What this request changed no longer exists or has moved on.");
                case RevertOutcomeKind.Conflict:
                case RevertOutcomeKind.NotRevertable:
                    return Result<WorkApprovalRequestResponse>.Conflict(outcome.Reason ?? "This request can no longer be reverted.");
            }
            readTargetUpdatedAt = outcome.ReadTargetUpdatedAt;
        }

        try
        {
            var result = await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
            {
                var now = DateTimeOffset.UtcNow;
                request.RevertedAt = now;
                request.RevertedByEmployeeId = caller;
                request.Status = WorkApprovalRequestStatuses.Pending;
                request.DecidedByEmployeeId = null;
                request.DecisionComment = null;
                request.DecidedAt = null;
                request.AppliedPayloadJson = null;
                request.UndoStateJson = null;
                _requests.Update(request);
                await _unitOfWork.SaveChangesAsync(innerCt);

                // Must happen AFTER the save above: AuditableEntityInterceptor only stamps the real
                // UpdatedAt during SaveChangesAsync, so reading it any earlier would race the interceptor
                // (see Review Focus #1). A second small save persists just this follow-up field.
                if (readTargetUpdatedAt?.Invoke() is { } freshUpdatedAt)
                {
                    request.TargetUpdatedAtSnapshot = freshUpdatedAt;
                    _requests.Update(request);
                    await _unitOfWork.SaveChangesAsync(innerCt);
                }

                await _notifications.NotifyAsync(new WorkNotificationEvent(
                    tenantId, request.ProjectId, caller, WorkNotificationKinds.Reverted, request.ActionType, request.TargetType,
                    request.TargetId, request.TargetTitle, request.Id, [request.RequestedByEmployeeId]), innerCt);

                return Result<WorkApprovalRequestResponse>.Success(null!);
            }, ct);
            if (!result.IsSuccess)
                return result;
        }
        catch (UniqueConstraintConflictException)
        {
            return Result<WorkApprovalRequestResponse>.Conflict(
                "Another request for this same change is already pending - resolve that one first.");
        }

        var names = await _identity.ResolveDisplayNamesByEmployeeIdAsync(
            tenantId, [request.RequestedByEmployeeId, request.ApproverEmployeeId], ct);
        return Result<WorkApprovalRequestResponse>.Success(WorkApprovalRequestMapper.ToResponse(request, names, caller));
    }
}
```

- [ ] **Step 10: Run the handler tests**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~RevertWorkApprovalRequestCommandHandlerTests"`
Expected: 6 passed.

- [ ] **Step 11: Add the controller endpoint**

In `WorkApprovalsController.cs`, next to the `cancel` endpoint:

```csharp
    [HttpPost("approvals/{id:guid}/revert")]
    public async Task<IActionResult> Revert(Guid id, CancellationToken ct)
        => ToResult(await _mediator.Send(new RevertWorkApprovalRequestCommand(id), ct));
```

Add `using ONEVO.Application.Features.WorkManagement.Approvals.Commands.RevertWorkApprovalRequest;` at the top.

- [ ] **Step 12: Run the full Approvals suite + architecture suite**

```bash
dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~Features.WorkManagement.Approvals"
dotnet test tests/ONEVO.Tests.Architecture
```

- [ ] **Step 13: Commit**

```bash
git add src/ONEVO.Domain/Features/WorkManagement/Notifications src/ONEVO.Application/Features/WorkManagement/Approvals src/ONEVO.Application/Features/WorkManagement/Notifications/Services/WorkNotificationEngine.cs src/ONEVO.API/Controllers/Tenant/WorkManagement/WorkApprovalsController.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/Approvals/RevertWorkApprovalRequestCommandHandlerTests.cs
git commit -m "feat(work-management): add revert endpoint and viewer-aware canRevert

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 5: Simple reverters — flag-flip types (no snapshot needed)

Covers `task.create`, `task.delete`, `module.delete`, `module.achieve`, `module.unachieve` (5 types; `sprint.delete` needs a task-id snapshot and moves to Task 6 alongside the other snapshot-based types). Each of these can be fully derived from the target's current state plus (for `task.create`) the request itself — no `UndoJson` needed.

**Files:**
- Modify (add `UndoJson`, or leave null, per the applier's `return ApplyOutcome.Applied();` from Task 2 Step 7):
  - `src/ONEVO.Application/Features/WorkManagement/Tasks/Appliers/TaskCreateApplier.cs`
  - `src/ONEVO.Application/Features/WorkManagement/Tasks/Appliers/TaskDeleteApplier.cs`
  - `src/ONEVO.Application/Features/WorkManagement/Objectives/Appliers/ModuleDeleteApplier.cs`
  - `src/ONEVO.Application/Features/WorkManagement/Objectives/Appliers/ModuleAchieveApplier.cs`
  - `src/ONEVO.Application/Features/WorkManagement/Objectives/Appliers/ModuleUnachieveApplier.cs`
- Create:
  - `src/ONEVO.Application/Features/WorkManagement/Tasks/Reverters/TaskCreateReverter.cs`
  - `src/ONEVO.Application/Features/WorkManagement/Tasks/Reverters/TaskDeleteReverter.cs`
  - `src/ONEVO.Application/Features/WorkManagement/Objectives/Reverters/ModuleDeleteReverter.cs`
  - `src/ONEVO.Application/Features/WorkManagement/Objectives/Reverters/ModuleAchieveReverter.cs`
  - `src/ONEVO.Application/Features/WorkManagement/Objectives/Reverters/ModuleUnachieveReverter.cs`
- Test: one test class per reverter under `tests/ONEVO.Tests.Unit/Features/WorkManagement/{Tasks,Objectives}/Reverters/`

**Interfaces:**
- Consumes: `ITaskWriteService.Restore`, `IModuleWriteService.Restore`/`ApplyAchieveAsync`/`ApplyUnachieveAsync`, `IWorkTaskRepository.GetTrackedByIdForTenantAsync`, `IObjectiveRepository.GetTrackedByIdForTenantAsync`

- [ ] **Step 1: `TaskCreateApplier` nulls `TargetId` is already the apply-time behavior — no applier change needed here.** (Reminder from Review Focus #2: `TaskCreateReverter` below must null it back.)

- [ ] **Step 2: Write the failing `TaskCreateReverter` test**

`tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/Reverters/TaskCreateReverterTests.cs`:

```csharp
using FluentAssertions;
using Moq;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Reverters;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks.Reverters;

public class TaskCreateReverterTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private readonly Mock<ITaskWriteService> _writes = new();
    private readonly Mock<IWorkTaskRepository> _tasks = new();

    private TaskCreateReverter Build() => new(_writes.Object, _tasks.Object);

    [Fact]
    public async Task RevertAsync_SoftDeletesTheCreatedTaskAndNullsTargetId()
    {
        var taskId = Guid.NewGuid();
        var task = new WorkTask { Id = taskId, TenantId = TenantId };
        var request = new WorkApprovalRequest { Id = Guid.NewGuid(), TenantId = TenantId, TargetId = taskId, ActionType = WorkActionTypes.TaskCreate };
        _tasks.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, taskId, It.IsAny<CancellationToken>())).ReturnsAsync(task);

        var outcome = await Build().RevertAsync(new ApprovalRevertContext(request, Guid.NewGuid()), CancellationToken.None);

        outcome.Kind.Should().Be(RevertOutcomeKind.Reverted);
        _writes.Verify(x => x.Restore(It.IsAny<WorkTask>()), Times.Never);
        _writes.Verify(x => x.Delete(task), Times.Once);
        request.TargetId.Should().BeNull();
    }

    [Fact]
    public async Task RevertAsync_TaskAlreadyGone_ReturnsStale()
    {
        var request = new WorkApprovalRequest { Id = Guid.NewGuid(), TenantId = TenantId, TargetId = Guid.NewGuid(), ActionType = WorkActionTypes.TaskCreate };
        _tasks.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, request.TargetId!.Value, It.IsAny<CancellationToken>())).ReturnsAsync((WorkTask?)null);

        var outcome = await Build().RevertAsync(new ApprovalRevertContext(request, Guid.NewGuid()), CancellationToken.None);

        outcome.Kind.Should().Be(RevertOutcomeKind.Stale);
    }
}
```

- [ ] **Step 3: Run it to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~TaskCreateReverterTests"`
Expected: build FAIL, `TaskCreateReverter` not found.

- [ ] **Step 4: Implement `TaskCreateReverter`**

`src/ONEVO.Application/Features/WorkManagement/Tasks/Reverters/TaskCreateReverter.cs`:

```csharp
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Reverters;

/// <summary>Undoes an approved task.create by soft-deleting the task it created, and nulls
/// Request.TargetId back to null (TaskCreateApplier stamped it with the new task's id - see Review
/// Focus #2) so the reopened Pending request doesn't keep pointing at a now-deleted task.</summary>
public sealed class TaskCreateReverter : IApprovalActionReverter
{
    private readonly ITaskWriteService _writes;
    private readonly IWorkTaskRepository _tasks;

    public TaskCreateReverter(ITaskWriteService writes, IWorkTaskRepository tasks)
    {
        _writes = writes;
        _tasks = tasks;
    }

    public string ActionType => WorkActionTypes.TaskCreate;

    public async Task<RevertOutcome> RevertAsync(ApprovalRevertContext context, CancellationToken ct)
    {
        var request = context.Request;
        if (request.TargetId is null)
            return RevertOutcome.Stale;

        var task = await _tasks.GetTrackedByIdForTenantAsync(request.TenantId, request.TargetId.Value, ct);
        if (task is null)
            return RevertOutcome.Stale;

        _writes.Delete(task);
        request.TargetId = null;
        // No TargetUpdatedAtSnapshot to re-baseline: TaskCreateApplier never checked one (it has no
        // existing target to be stale against), and the request now has no TargetId either.
        return RevertOutcome.Reverted();
    }
}
```

- [ ] **Step 5: Run the test**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~TaskCreateReverterTests"`
Expected: 2 passed.

- [ ] **Step 6: Write, implement and test `TaskDeleteReverter`** (same TDD cycle as Steps 2-5)

Test file `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/Reverters/TaskDeleteReverterTests.cs` — same shape as `TaskCreateReverterTests`, asserting `_writes.Restore(task)` is called once and the outcome is `Reverted`, plus a `RevertAsync_TaskNotFound_ReturnsStale` case.

`src/ONEVO.Application/Features/WorkManagement/Tasks/Reverters/TaskDeleteReverter.cs`:

```csharp
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Reverters;

/// <summary>Undoes an approved task.delete by clearing the soft-delete flags SoftDeleteInterceptor set.</summary>
public sealed class TaskDeleteReverter : IApprovalActionReverter
{
    private readonly ITaskWriteService _writes;
    private readonly IWorkTaskRepository _tasks;

    public TaskDeleteReverter(ITaskWriteService writes, IWorkTaskRepository tasks)
    {
        _writes = writes;
        _tasks = tasks;
    }

    public string ActionType => WorkActionTypes.TaskDelete;

    public async Task<RevertOutcome> RevertAsync(ApprovalRevertContext context, CancellationToken ct)
    {
        var request = context.Request;
        if (request.TargetId is null)
            return RevertOutcome.Stale;

        // IWorkTaskRepository's tracked lookup goes through the same IsDeleted query filter as every
        // other read, so a soft-deleted task is invisible to GetTrackedByIdForTenantAsync - fall back to
        // an ignore-filter lookup. Add this method to IWorkTaskRepository if it doesn't already exist
        // (check first - a deleted-task lookup may already exist for an audit/history screen).
        var task = await _tasks.GetTrackedByIdForTenantIncludingDeletedAsync(request.TenantId, request.TargetId.Value, ct);
        if (task is null || !task.IsDeleted)
            return RevertOutcome.Stale;

        _writes.Restore(task);
        return RevertOutcome.Reverted(() => task.UpdatedAt);
    }
}
```

> **Verify before writing this one:** check whether `IWorkTaskRepository` already exposes an ignore-filter / "including deleted" lookup (several repositories in this codebase have one for history/audit screens - grep `IgnoreQueryFilters` under `src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement`). If one exists under a different name, use that name instead of adding a duplicate; if none exists, add `Task<WorkTask?> GetTrackedByIdForTenantIncludingDeletedAsync(Guid tenantId, Guid id, CancellationToken ct = default)` to `IWorkTaskRepository`/`EfWorkTaskRepository` first (implementation: `_db.WorkTasks.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.TenantId == tenantId && t.Id == id, ct)`), with its own small repository test. This same lookup is reused by `ModuleDeleteReverter`'s Objective equivalent below — check `IObjectiveRepository` for the same gap.

- [ ] **Step 7: Write, implement and test `ModuleDeleteReverter`**

Objective's "delete" is the `IsActive` flag, not the generic `IsDeleted` soft-delete, so **no `IgnoreQueryFilters` lookup is needed here** — a plain `GetTrackedByIdForTenantAsync` already returns it (confirm: check whether `IObjectiveRepository`'s tracked lookup filters on `IsActive` itself; if it does, it needs the same ignore-filter treatment as Task's, if not, the plain lookup is fine - verify by reading `EfObjectiveRepository.GetTrackedByIdForTenantAsync` before writing this reverter).

`src/ONEVO.Application/Features/WorkManagement/Objectives/Reverters/ModuleDeleteReverter.cs`:

```csharp
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Reverters;

/// <summary>Undoes an approved module.delete by clearing the IsActive flag ApplyDeleteAsync set.</summary>
public sealed class ModuleDeleteReverter : IApprovalActionReverter
{
    private readonly IModuleWriteService _writes;
    private readonly IObjectiveRepository _objectives;

    public ModuleDeleteReverter(IModuleWriteService writes, IObjectiveRepository objectives)
    {
        _writes = writes;
        _objectives = objectives;
    }

    public string ActionType => WorkActionTypes.ModuleDelete;

    public async Task<RevertOutcome> RevertAsync(ApprovalRevertContext context, CancellationToken ct)
    {
        var request = context.Request;
        if (request.TargetId is null)
            return RevertOutcome.Stale;

        var module = await _objectives.GetTrackedByIdForTenantAsync(request.TenantId, request.TargetId.Value, ct);
        if (module is null || module.IsActive)
            return RevertOutcome.Stale;

        _writes.Restore(module);
        return RevertOutcome.Reverted(() => module.UpdatedAt);
    }
}
```

- [ ] **Step 8: Write, implement and test `ModuleAchieveReverter` and `ModuleUnachieveReverter`**

Both reuse `ModuleApplierBase`'s loading shape, but as reverters (not appliers) they're written directly against `IObjectiveRepository` the same way `ModuleDeleteReverter` is above.

`src/ONEVO.Application/Features/WorkManagement/Objectives/Reverters/ModuleAchieveReverter.cs`:

```csharp
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Reverters;

/// <summary>Undoes an approved module.achieve by calling ApplyUnachieveAsync - the exact inverse
/// operation, with its own validation (current head must be active) reused as-is.</summary>
public sealed class ModuleAchieveReverter : IApprovalActionReverter
{
    private readonly IModuleWriteService _writes;
    private readonly IObjectiveRepository _objectives;

    public ModuleAchieveReverter(IModuleWriteService writes, IObjectiveRepository objectives)
    {
        _writes = writes;
        _objectives = objectives;
    }

    public string ActionType => WorkActionTypes.ModuleAchieve;

    public async Task<RevertOutcome> RevertAsync(ApprovalRevertContext context, CancellationToken ct)
    {
        var request = context.Request;
        if (request.TargetId is null)
            return RevertOutcome.Stale;

        var module = await _objectives.GetTrackedByIdForTenantAsync(request.TenantId, request.TargetId.Value, ct);
        if (module is null || !module.IsAchieved)
            return RevertOutcome.Stale;

        var result = await _writes.ApplyUnachieveAsync(request.TenantId, module, ct);
        if (!result.IsSuccess)
            return RevertOutcome.Conflict(result.Error ?? "The milestone's current head is no longer active, so this can't be auto-reverted.");

        return RevertOutcome.Reverted(() => module.UpdatedAt);
    }
}
```

`src/ONEVO.Application/Features/WorkManagement/Objectives/Reverters/ModuleUnachieveReverter.cs` is the mirror image (calls `ApplyAchieveAsync`, Stale guard is `module.IsAchieved` instead of `!module.IsAchieved`, Conflict message is about sub-milestones/active sprints instead of an inactive head):

```csharp
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Reverters;

/// <summary>Undoes an approved module.unachieve by calling ApplyAchieveAsync - reusing its validation
/// (every direct child already achieved, no tasks in an Active sprint) as the Conflict guard.</summary>
public sealed class ModuleUnachieveReverter : IApprovalActionReverter
{
    private readonly IModuleWriteService _writes;
    private readonly IObjectiveRepository _objectives;

    public ModuleUnachieveReverter(IModuleWriteService writes, IObjectiveRepository objectives)
    {
        _writes = writes;
        _objectives = objectives;
    }

    public string ActionType => WorkActionTypes.ModuleUnachieve;

    public async Task<RevertOutcome> RevertAsync(ApprovalRevertContext context, CancellationToken ct)
    {
        var request = context.Request;
        if (request.TargetId is null)
            return RevertOutcome.Stale;

        var module = await _objectives.GetTrackedByIdForTenantAsync(request.TenantId, request.TargetId.Value, ct);
        if (module is null || module.IsAchieved)
            return RevertOutcome.Stale;

        var result = await _writes.ApplyAchieveAsync(request.TenantId, module, ct);
        if (!result.IsSuccess)
            return RevertOutcome.Conflict(result.Error ?? "This milestone can no longer be re-achieved automatically.");

        return RevertOutcome.Reverted(() => module.UpdatedAt);
    }
}
```

Write each one's test first (mirroring `TaskCreateReverterTests`'s shape: a happy path asserting the opposite write-service method was called, a Stale case, and a Conflict case where the mocked write-service call returns `Result.Failure(...)`), run red, implement, run green, for both files.

- [ ] **Step 9: Run every Task 5 reverter test**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~Reverters"`
Expected: all green (10 tests: 2 each × 5 reverters, plus any extra Conflict-case tests added in Step 8).

- [ ] **Step 10: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Tasks/Reverters src/ONEVO.Application/Features/WorkManagement/Objectives/Reverters src/ONEVO.Application/Features/WorkManagement/Tasks/RepositoryInterfaces src/ONEVO.Application/Features/WorkManagement/Objectives/RepositoryInterfaces src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/Reverters tests/ONEVO.Tests.Unit/Features/WorkManagement/Objectives/Reverters
git commit -m "feat(work-management): add flag-flip reverters (task.create/delete, module.delete/achieve/unachieve)

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 6: Snapshot-based reverters — field edits, transfer, allocation, sprint lifecycle

Covers `task.edit`, `module.edit`, `sprint.edit`, `module.transfer`, `module.allocation_extend`, `sprint.start`, `sprint.achieve`, `sprint.delete` (8 types). Each applier is modified to capture a pre-mutation snapshot as `UndoJson`; each reverter reads it back.

**Files:**
- Modify (capture `UndoJson` before mutating):
  - `src/ONEVO.Application/Features/WorkManagement/Tasks/Appliers/TaskEditApplier.cs`
  - `src/ONEVO.Application/Features/WorkManagement/Objectives/Appliers/ModuleEditApplier.cs`
  - `src/ONEVO.Application/Features/WorkManagement/Sprints/Appliers/SprintEditApplier.cs`
  - `src/ONEVO.Application/Features/WorkManagement/Objectives/Appliers/ModuleTransferApplier.cs`
  - `src/ONEVO.Application/Features/WorkManagement/Objectives/Appliers/ModuleAllocationExtendApplier.cs` (uses `AppliedPayloadJson ?? PayloadJson` directly instead — see Review Focus #3, no `UndoJson` needed)
  - `src/ONEVO.Application/Features/WorkManagement/Sprints/Appliers/SprintStartApplier.cs`
  - `src/ONEVO.Application/Features/WorkManagement/Sprints/Appliers/SprintAchieveApplier.cs`
  - `src/ONEVO.Application/Features/WorkManagement/Sprints/Appliers/SprintDeleteApplier.cs`
  - `src/ONEVO.Domain/Features/WorkManagement/Tasks/Entities/TaskEditLog.cs` (add `TaskEditLogSources.Reverted`)
- Create: one reverter per type under `.../Tasks/Reverters/`, `.../Objectives/Reverters/`, `.../Sprints/Reverters/`
- Test: one test class per reverter, same folders as Task 5

**Interfaces:**
- Consumes: `ITaskWriteService.ApplyEditAsync`, `IModuleWriteService.ApplyEditAsync`/`ApplyTransferAsync`, `ISprintWriteService.ApplyEditAsync`/`Restore`, `IObjectiveRepository`/`ISprintRepository`/`IWorkTaskRepository` tracked lookups

- [ ] **Step 1: `module.edit` — write the failing reverter test**

`tests/ONEVO.Tests.Unit/Features/WorkManagement/Objectives/Reverters/ModuleEditReverterTests.cs`:

```csharp
using System.Text.Json;
using FluentAssertions;
using Moq;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Reverters;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Objectives.Reverters;

public class ModuleEditReverterTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private readonly Mock<IModuleWriteService> _writes = new();
    private readonly Mock<IObjectiveRepository> _objectives = new();

    private ModuleEditReverter Build() => new(_writes.Object, _objectives.Object);

    [Fact]
    public async Task RevertAsync_RestoresTheSnapshottedFields()
    {
        var moduleId = Guid.NewGuid();
        var module = new Objective { Id = moduleId, TenantId = TenantId, Title = "New title" };
        var undo = new ModuleEditInput("Old title", "Old description", null, null, 40m);
        var request = new WorkApprovalRequest
        {
            Id = Guid.NewGuid(), TenantId = TenantId, TargetId = moduleId, ActionType = WorkActionTypes.ModuleEdit,
            UndoStateJson = JsonSerializer.Serialize(undo, ModulePayloadJson.Options)
        };
        _objectives.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, moduleId, It.IsAny<CancellationToken>())).ReturnsAsync(module);
        _writes.Setup(x => x.ApplyEditAsync(TenantId, module, undo, It.IsAny<CancellationToken>()))
            .Callback<Guid, Objective, ModuleEditInput, CancellationToken>((_, m, i, _) => m.Title = i.Title)
            .ReturnsAsync(ONEVO.Application.Common.Models.Result.Success());

        var outcome = await Build().RevertAsync(new ApprovalRevertContext(request, Guid.NewGuid()), CancellationToken.None);

        outcome.Kind.Should().Be(RevertOutcomeKind.Reverted);
        module.Title.Should().Be("Old title");
    }

    [Fact]
    public async Task RevertAsync_NoUndoSnapshot_ReturnsNotRevertable()
    {
        var request = new WorkApprovalRequest { Id = Guid.NewGuid(), TenantId = TenantId, TargetId = Guid.NewGuid(), ActionType = WorkActionTypes.ModuleEdit, UndoStateJson = null };
        var outcome = await Build().RevertAsync(new ApprovalRevertContext(request, Guid.NewGuid()), CancellationToken.None);
        outcome.Kind.Should().Be(RevertOutcomeKind.NotRevertable);
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~ModuleEditReverterTests"`
Expected: build FAIL, `ModuleEditReverter` not found.

- [ ] **Step 3: Capture the snapshot in `ModuleEditApplier`**

In `ModuleEditApplier.cs`, change `ApplyAsync` to snapshot before mutating and pass `UndoJson` through. `ModuleApplierBase.ApplyAsync` currently does `return result.IsSuccess ? ApplyOutcome.Applied : ApplyOutcome.Invalid(...)` — it needs a way for a subclass to attach `UndoJson` to that `Applied` result. Add a `protected virtual string? CaptureUndoJson(Objective module) => null;` hook to `ModuleApplierBase`, called **before** `ApplyAsync(tenantId, module, payloadJson, ct)` runs:

```csharp
    public async Task<ApplyOutcome> ApplyAsync(ApprovalApplyContext context, CancellationToken ct)
    {
        var request = context.Request;
        if (request.TargetId is null)
            return ApplyOutcome.Stale;

        var module = await Objectives.GetTrackedByIdForTenantAsync(request.TenantId, request.TargetId.Value, ct);
        if (module is null || !module.IsActive || IsStale(module))
            return ApplyOutcome.Stale;
        if (ChecksSnapshot && request.TargetUpdatedAtSnapshot is { } snap && (module.UpdatedAt ?? module.CreatedAt) > snap)
            return ApplyOutcome.Stale;

        var undoJson = CaptureUndoJson(module);
        var result = await ApplyAsync(request.TenantId, module, context.PayloadJson, ct);
        return result.IsSuccess ? ApplyOutcome.Applied(undoJson) : ApplyOutcome.Invalid(result.Error ?? "The change could not be applied.");
    }

    protected virtual string? CaptureUndoJson(Objective module) => null;
```

In `ModuleEditApplier.cs`, override it:

```csharp
    protected override string? CaptureUndoJson(Objective module)
        => System.Text.Json.JsonSerializer.Serialize(
            new ModuleEditInput(module.Title, module.Description, module.StartDate, module.EndDate, module.AllocatedHours),
            ModulePayloadJson.Options);
```

- [ ] **Step 4: Implement `ModuleEditReverter`**

`src/ONEVO.Application/Features/WorkManagement/Objectives/Reverters/ModuleEditReverter.cs`:

```csharp
using System.Text.Json;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Reverters;

/// <summary>Undoes an approved module.edit by replaying ApplyEditAsync with the pre-edit field values
/// ModuleEditApplier snapshotted into UndoStateJson.</summary>
public sealed class ModuleEditReverter : IApprovalActionReverter
{
    private readonly IModuleWriteService _writes;
    private readonly IObjectiveRepository _objectives;

    public ModuleEditReverter(IModuleWriteService writes, IObjectiveRepository objectives)
    {
        _writes = writes;
        _objectives = objectives;
    }

    public string ActionType => WorkActionTypes.ModuleEdit;

    public async Task<RevertOutcome> RevertAsync(ApprovalRevertContext context, CancellationToken ct)
    {
        var request = context.Request;
        if (request.UndoStateJson is null)
            return RevertOutcome.NotRevertable("Nothing to undo for this request.");
        if (request.TargetId is null)
            return RevertOutcome.Stale;

        var module = await _objectives.GetTrackedByIdForTenantAsync(request.TenantId, request.TargetId.Value, ct);
        if (module is null || !module.IsActive)
            return RevertOutcome.Stale;

        var undo = JsonSerializer.Deserialize<ModuleEditInput>(request.UndoStateJson, ModulePayloadJson.Options);
        if (undo is null)
            return RevertOutcome.NotRevertable("The pre-edit snapshot is corrupt.");

        var result = await _writes.ApplyEditAsync(request.TenantId, module, undo, ct);
        if (!result.IsSuccess)
            return RevertOutcome.Conflict(result.Error ?? "The milestone has changed in a way that blocks reverting this edit.");

        return RevertOutcome.Reverted(() => module.UpdatedAt);
    }
}
```

- [ ] **Step 5: Run the `module.edit` tests**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~ModuleEditReverterTests"`
Expected: 2 passed.

- [ ] **Step 6: `sprint.edit` — same pattern**

Add `protected virtual string? CaptureUndoJson(Sprint sprint) => null;` to `SprintApplierBase` the same way as Step 3 (called before `ApplyAsync(request.TenantId, request.RequestedByEmployeeId, sprint, context.PayloadJson, ct)`, result becomes `ApplyOutcome.Applied(undoJson)`).

In `SprintEditApplier.cs`:

```csharp
    protected override string? CaptureUndoJson(Sprint sprint)
        => System.Text.Json.JsonSerializer.Serialize(
            new SprintEditInput(sprint.Name, sprint.Goal, sprint.StartDate, sprint.EndDate),
            SprintPayloadJson.Options);
```

`src/ONEVO.Application/Features/WorkManagement/Sprints/Reverters/SprintEditReverter.cs` — same shape as `ModuleEditReverter`, built on `ISprintRepository`/`ISprintWriteService.ApplyEditAsync`, passing `context.DeciderEmployeeId` as the `actorEmployeeId` argument:

```csharp
using System.Text.Json;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Sprints.DTOs;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;

namespace ONEVO.Application.Features.WorkManagement.Sprints.Reverters;

/// <summary>Undoes an approved sprint.edit by replaying ApplyEditAsync with the pre-edit values
/// SprintEditApplier snapshotted.</summary>
public sealed class SprintEditReverter : IApprovalActionReverter
{
    private readonly ISprintWriteService _writes;
    private readonly ISprintRepository _sprints;

    public SprintEditReverter(ISprintWriteService writes, ISprintRepository sprints)
    {
        _writes = writes;
        _sprints = sprints;
    }

    public string ActionType => WorkActionTypes.SprintEdit;

    public async Task<RevertOutcome> RevertAsync(ApprovalRevertContext context, CancellationToken ct)
    {
        var request = context.Request;
        if (request.UndoStateJson is null)
            return RevertOutcome.NotRevertable("Nothing to undo for this request.");
        if (request.TargetId is null)
            return RevertOutcome.Stale;

        var sprint = await _sprints.GetTrackedByIdForTenantAsync(request.TenantId, request.TargetId.Value, ct);
        if (sprint is null)
            return RevertOutcome.Stale;

        var undo = JsonSerializer.Deserialize<SprintEditInput>(request.UndoStateJson, SprintPayloadJson.Options);
        if (undo is null)
            return RevertOutcome.NotRevertable("The pre-edit snapshot is corrupt.");

        var result = await _writes.ApplyEditAsync(request.TenantId, context.DeciderEmployeeId, sprint, undo, ct);
        if (!result.IsSuccess)
            return RevertOutcome.Conflict(result.Error ?? "This sprint has changed in a way that blocks reverting this edit.");

        return RevertOutcome.Reverted(() => sprint.UpdatedAt);
    }
}
```

Write `SprintEditReverterTests.cs` first (same two-case shape as `ModuleEditReverterTests`), run red, then implement Step 6, run green.

- [ ] **Step 7: `task.edit`**

In `TaskEditApplier.cs`, capture the snapshot right before calling `_writes.ApplyEditAsync` (there is no shared base class for Task appliers to add a hook to, so this one captures inline):

```csharp
        var objective = await _objectives.GetByIdForTenantAsync(request.TenantId, task.ObjectiveId, ct);
        if (objective is null)
            return ApplyOutcome.Stale;

        var undo = new TaskEditInput(
            task.Title, task.Description, task.Priority, task.DueDate, task.EstimatedHours,
            task.StoryPoints, task.ProgressPercent, null, task.SprintId);
        var undoJson = System.Text.Json.JsonSerializer.Serialize(undo, TaskPayload.Options);

        var applied = await _writes.ApplyEditAsync(request.TenantId, request.RequestedByEmployeeId, task, objective, input,
            TaskEditLogSources.ApprovedRequest, request.Id, ct);
        return applied.IsSuccess
            ? ApplyOutcome.Applied(undoJson)
            : ApplyOutcome.Invalid(applied.Error ?? "The task edit is not valid.");
```

(Replaces the existing final three lines of `TaskEditApplier.ApplyAsync`; everything above that in the method is unchanged.)

Add `TaskEditLogSources.Reverted = "reverted";` to `TaskEditLog.cs` next to `ApprovedRequest`.

`src/ONEVO.Application/Features/WorkManagement/Tasks/Reverters/TaskEditReverter.cs`:

```csharp
using System.Text.Json;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Appliers;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Reverters;

/// <summary>Undoes an approved task.edit by replaying ApplyEditAsync with the pre-edit field values
/// TaskEditApplier snapshotted. A SprintId in the snapshot that differs from the task's current one
/// moves it back, logged the same way the original edit logged the forward move.</summary>
public sealed class TaskEditReverter : IApprovalActionReverter
{
    private readonly ITaskWriteService _writes;
    private readonly IObjectiveRepository _objectives;
    private readonly IWorkTaskRepository _tasks;

    public TaskEditReverter(ITaskWriteService writes, IObjectiveRepository objectives, IWorkTaskRepository tasks)
    {
        _writes = writes;
        _objectives = objectives;
        _tasks = tasks;
    }

    public string ActionType => WorkActionTypes.TaskEdit;

    public async Task<RevertOutcome> RevertAsync(ApprovalRevertContext context, CancellationToken ct)
    {
        var request = context.Request;
        if (request.UndoStateJson is null)
            return RevertOutcome.NotRevertable("Nothing to undo for this request.");
        if (request.TargetId is null)
            return RevertOutcome.Stale;

        var task = await _tasks.GetTrackedByIdForTenantAsync(request.TenantId, request.TargetId.Value, ct);
        if (task is null)
            return RevertOutcome.Stale;

        var objective = await _objectives.GetByIdForTenantAsync(request.TenantId, task.ObjectiveId, ct);
        if (objective is null)
            return RevertOutcome.Stale;

        var undo = JsonSerializer.Deserialize<TaskEditInput>(request.UndoStateJson, TaskPayload.Options);
        if (undo is null)
            return RevertOutcome.NotRevertable("The pre-edit snapshot is corrupt.");

        var result = await _writes.ApplyEditAsync(request.TenantId, context.DeciderEmployeeId, task, objective, undo,
            TaskEditLogSources.Reverted, request.Id, ct);
        if (!result.IsSuccess)
            return RevertOutcome.Conflict(result.Error ?? "This task has changed in a way that blocks reverting this edit.");

        return RevertOutcome.Reverted(() => task.UpdatedAt);
    }
}
```

> `TaskPayload` is `internal` to the `Tasks.Appliers` namespace today (`TaskCreateApplier.cs:11`) — either widen it to `public` (it's a pure options holder, safe to share) or duplicate the one-line `JsonSerializerOptions` in `Tasks.Reverters`. Prefer widening it; check for a better-named existing public equivalent first (e.g. `TaskEditApplier`'s own usings already reference a payload options holder for task DTOs — grep before adding a second one).

Write `TaskEditReverterTests.cs` first, covering: happy path (asserts `_writes.ApplyEditAsync` called with the deserialized `TaskEditInput` matching the snapshot), `UndoStateJson is null` → `NotRevertable`, task not found → `Stale`.

- [ ] **Step 8: `module.transfer`**

No applier change needed — reuse `request.PayloadJson`'s already-known shape is not enough (we need the *old* head, not the new one), so this one does need a capture. In `ModuleTransferApplier.cs`, override the same `CaptureUndoJson` hook added in Step 3:

```csharp
    protected override string? CaptureUndoJson(Objective module)
        => System.Text.Json.JsonSerializer.Serialize(new ModuleTransferInput(module.OwnerId), ModulePayloadJson.Options);
```

`src/ONEVO.Application/Features/WorkManagement/Objectives/Reverters/ModuleTransferReverter.cs`:

```csharp
using System.Text.Json;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Reverters;

/// <summary>Undoes an approved module.transfer by transferring the head back - reusing ApplyTransferAsync
/// itself (its membership upsert/deactivate pair runs the same way in either direction).</summary>
public sealed class ModuleTransferReverter : IApprovalActionReverter
{
    private readonly IModuleWriteService _writes;
    private readonly IObjectiveRepository _objectives;

    public ModuleTransferReverter(IModuleWriteService writes, IObjectiveRepository objectives)
    {
        _writes = writes;
        _objectives = objectives;
    }

    public string ActionType => WorkActionTypes.ModuleTransfer;

    public async Task<RevertOutcome> RevertAsync(ApprovalRevertContext context, CancellationToken ct)
    {
        var request = context.Request;
        if (request.UndoStateJson is null)
            return RevertOutcome.NotRevertable("Nothing to undo for this request.");
        if (request.TargetId is null)
            return RevertOutcome.Stale;

        var module = await _objectives.GetTrackedByIdForTenantAsync(request.TenantId, request.TargetId.Value, ct);
        if (module is null || !module.IsActive)
            return RevertOutcome.Stale;

        var undo = JsonSerializer.Deserialize<ModuleTransferInput>(request.UndoStateJson, ModulePayloadJson.Options);
        if (undo is null)
            return RevertOutcome.NotRevertable("The pre-transfer snapshot is corrupt.");
        if (module.OwnerId == undo.NewHeadEmployeeId)
            return RevertOutcome.Stale; // already transferred again since

        var result = await _writes.ApplyTransferAsync(request.TenantId, module, undo, ct);
        if (!result.IsSuccess)
            return RevertOutcome.Conflict(result.Error ?? "The previous head is no longer an active employee, so this can't be auto-reverted.");

        return RevertOutcome.Reverted(() => module.UpdatedAt);
    }
}
```

- [ ] **Step 9: `module.allocation_extend`**

No applier change: no `UndoJson`, no `CaptureUndoJson` override needed. `request.AppliedPayloadJson ?? request.PayloadJson` already carries the exact hours that were added (Review Focus #3).

`src/ONEVO.Application/Features/WorkManagement/Objectives/Reverters/ModuleAllocationExtendReverter.cs`:

```csharp
using System.Text.Json;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Reverters;

/// <summary>Undoes an approved module.allocation_extend by subtracting back the hours that were
/// actually applied (AppliedPayloadJson if the approver lowered them, else PayloadJson - Review
/// Focus #3). No UndoJson needed: the applied amount already lives on the request.</summary>
public sealed class ModuleAllocationExtendReverter : IApprovalActionReverter
{
    private readonly IObjectiveRepository _objectives;

    public ModuleAllocationExtendReverter(IObjectiveRepository objectives) => _objectives = objectives;

    public string ActionType => WorkActionTypes.ModuleAllocationExtend;

    public async Task<RevertOutcome> RevertAsync(ApprovalRevertContext context, CancellationToken ct)
    {
        var request = context.Request;
        if (request.TargetId is null)
            return RevertOutcome.Stale;

        var module = await _objectives.GetTrackedByIdForTenantAsync(request.TenantId, request.TargetId.Value, ct);
        if (module is null || !module.IsActive)
            return RevertOutcome.Stale;

        var appliedJson = request.AppliedPayloadJson ?? request.PayloadJson;
        var applied = JsonSerializer.Deserialize<ModuleAllocationExtendInput>(appliedJson, ModulePayloadJson.Options);
        if (applied is null)
            return RevertOutcome.NotRevertable("The applied payload is corrupt.");
        if (module.AllocatedHours < applied.RequestedAdditionalHours)
            return RevertOutcome.Conflict("The milestone's allocated hours have since dropped below what this request added - reconcile manually.");

        module.AllocatedHours -= applied.RequestedAdditionalHours;
        module.UpdatedAt = DateTimeOffset.UtcNow;
        _objectives.Update(module);

        return RevertOutcome.Reverted(() => module.UpdatedAt);
    }
}
```

- [ ] **Step 10: `sprint.start`**

In `SprintStartApplier.cs`, override `CaptureUndoJson(Sprint sprint)` (added in Step 6) to snapshot only `Goal` (the one field `ApplyStartAsync` conditionally overwrites; `StartDate`/`EndDate` go from null to set, so "undo" is always "null them out", no snapshot needed for those):

```csharp
    protected override string? CaptureUndoJson(Sprint sprint)
        => sprint.Goal is { } goal ? System.Text.Json.JsonSerializer.Serialize(new SprintStartUndoSnapshot(goal), SprintPayloadJson.Options) : null;
```

Add the small snapshot record to `src/ONEVO.Application/Features/WorkManagement/Sprints/DTOs/SprintActionPayloads.cs` (or wherever `SprintStartInput` is declared - put it right next to it):

```csharp
/// <summary>UndoStateJson for sprint.start - only the pre-start Goal; StartDate/EndDate always revert
/// to null (a Draft sprint has no dates by definition).</summary>
public sealed record SprintStartUndoSnapshot(string? PreviousGoal);
```

`src/ONEVO.Application/Features/WorkManagement/Sprints/Reverters/SprintStartReverter.cs`:

```csharp
using System.Text.Json;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Sprints.DTOs;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;

namespace ONEVO.Application.Features.WorkManagement.Sprints.Reverters;

/// <summary>Undoes an approved sprint.start: back to Draft, dates cleared, Goal restored to whatever it
/// was (if anything) right before Start set it.</summary>
public sealed class SprintStartReverter : IApprovalActionReverter
{
    private readonly ISprintRepository _sprints;

    public SprintStartReverter(ISprintRepository sprints) => _sprints = sprints;

    public string ActionType => WorkActionTypes.SprintStart;

    public async Task<RevertOutcome> RevertAsync(ApprovalRevertContext context, CancellationToken ct)
    {
        var request = context.Request;
        if (request.TargetId is null)
            return RevertOutcome.Stale;

        var sprint = await _sprints.GetTrackedByIdForTenantAsync(request.TenantId, request.TargetId.Value, ct);
        if (sprint is null || sprint.Status != SprintStatuses.Active)
            return RevertOutcome.Stale;

        var undo = request.UndoStateJson is null
            ? null
            : JsonSerializer.Deserialize<SprintStartUndoSnapshot>(request.UndoStateJson, SprintPayloadJson.Options);

        sprint.Status = SprintStatuses.Draft;
        sprint.StartDate = null;
        sprint.EndDate = null;
        if (undo is not null)
            sprint.Goal = undo.PreviousGoal;
        sprint.UpdatedAt = DateTimeOffset.UtcNow;

        return RevertOutcome.Reverted(() => sprint.UpdatedAt);
    }
}
```

> This reverter does not call `_sprints.Update(sprint)` explicitly because the field mutations are on an already-tracked entity returned by `GetTrackedByIdForTenantAsync` - EF's change tracker picks them up on the next `SaveChangesAsync` without an explicit `Update` call, exactly like every `Apply*Async` method in `SprintWriteService` already relies on. No activity-log entry here either, matching how `ApplyUnachieveAsync` (Task 3) stayed log-free - the revert notification (sent by the command handler to the requester) is the audit trail for this action.

- [ ] **Step 11: `sprint.achieve`**

In `SprintAchieveApplier.cs`, override `CaptureUndoJson(Sprint sprint)`:

```csharp
    protected override string? CaptureUndoJson(Sprint sprint)
        => System.Text.Json.JsonSerializer.Serialize(new SprintAchieveUndoSnapshot(sprint.Status), SprintPayloadJson.Options);
```

```csharp
public sealed record SprintAchieveUndoSnapshot(string PreviousStatus);
```

`src/ONEVO.Application/Features/WorkManagement/Sprints/Reverters/SprintAchieveReverter.cs`:

```csharp
using System.Text.Json;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Sprints.DTOs;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;

namespace ONEVO.Application.Features.WorkManagement.Sprints.Reverters;

/// <summary>Undoes an approved sprint.achieve via ISprintWriteService.ApplyUnachieveAsync (Task 3),
/// restoring the exact status the sprint had right before it was achieved.</summary>
public sealed class SprintAchieveReverter : IApprovalActionReverter
{
    private readonly ISprintWriteService _writes;
    private readonly ISprintRepository _sprints;

    public SprintAchieveReverter(ISprintWriteService writes, ISprintRepository sprints)
    {
        _writes = writes;
        _sprints = sprints;
    }

    public string ActionType => WorkActionTypes.SprintAchieve;

    public async Task<RevertOutcome> RevertAsync(ApprovalRevertContext context, CancellationToken ct)
    {
        var request = context.Request;
        if (request.UndoStateJson is null)
            return RevertOutcome.NotRevertable("Nothing to undo for this request.");
        if (request.TargetId is null)
            return RevertOutcome.Stale;

        var sprint = await _sprints.GetTrackedByIdForTenantAsync(request.TenantId, request.TargetId.Value, ct);
        if (sprint is null)
            return RevertOutcome.Stale;

        var undo = JsonSerializer.Deserialize<SprintAchieveUndoSnapshot>(request.UndoStateJson, SprintPayloadJson.Options);
        if (undo is null)
            return RevertOutcome.NotRevertable("The pre-achieve snapshot is corrupt.");

        var result = await _writes.ApplyUnachieveAsync(request.TenantId, context.DeciderEmployeeId, sprint, undo.PreviousStatus, ct);
        if (!result.IsSuccess)
            return RevertOutcome.Conflict(result.Error ?? "This sprint is no longer achieved.");

        return RevertOutcome.Reverted(() => sprint.UpdatedAt);
    }
}
```

- [ ] **Step 12: `sprint.delete`**

In `SprintDeleteApplier.cs`, capture the task-id list before deleting (this one has no shared base-class hook to reuse since its `ApplyAsync` override returns a plain `Result`, not going through the base's "snapshot-then-call" flow the same way — add the capture inline before `Writes.ApplyDeleteAsync` runs):

```csharp
    protected override async Task<Result> ApplyAsync(Guid tenantId, Guid actorEmployeeId, Sprint sprint, string payloadJson, CancellationToken ct)
    {
        var validation = Writes.ValidateDelete(sprint);
        if (!validation.IsSuccess)
            return validation;
        await Writes.ApplyDeleteAsync(tenantId, sprint, ct);
        return Result.Success();
    }
```

This base-class `ApplyAsync(Guid, Guid, Sprint, string, CancellationToken)` override returns `Result`, not `ApplyOutcome` — `SprintApplierBase.ApplyAsync` (the outer one) is what wraps it into `ApplyOutcome.Applied()`/`Invalid(...)`. Reuse the same `CaptureUndoJson(Sprint sprint)` hook from Step 6, but this one needs the **task ids**, which aren't on the `Sprint` itself — override it to return `null` here and instead capture inside this method, passing the snapshot a different way: add a `protected string? PendingUndoJson;` field pattern is fragile across calls, so instead change this applier to not use the shared hook and set `PendingUndoJson` is rejected — **do it the direct way**: change `SprintDeleteApplier` to implement `ApplyAsync(ApprovalApplyContext, CancellationToken)` directly (bypassing `SprintApplierBase` for this one type only, same as `TaskDeleteApplier` already bypasses `ModuleApplierBase`'s task equivalent by implementing `IApprovalActionApplier` directly):

```csharp
using System.Text.Json;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Sprints.DTOs;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;

namespace ONEVO.Application.Features.WorkManagement.Sprints.Appliers;

/// <summary>Applies an approved sprint.delete (still Complete/Achieved only); its tasks go back to the
/// backlog. Captures the moved task ids as UndoJson (sprint.delete has no symmetric write-service undo
/// to reuse, unlike achieve/unachieve, so the reverter needs this list explicitly).</summary>
public sealed class SprintDeleteApplier : IApprovalActionApplier
{
    private readonly ISprintRepository _sprints;
    private readonly ISprintWriteService _writes;
    private readonly IWorkTaskRepository _tasks;

    public SprintDeleteApplier(ISprintRepository sprints, ISprintWriteService writes, IWorkTaskRepository tasks)
    {
        _sprints = sprints;
        _writes = writes;
        _tasks = tasks;
    }

    public string ActionType => WorkActionTypes.SprintDelete;

    public async Task<ApplyOutcome> ApplyAsync(ApprovalApplyContext context, CancellationToken ct)
    {
        var request = context.Request;
        if (request.TargetId is null)
            return ApplyOutcome.Stale;

        var sprint = await _sprints.GetTrackedByIdForTenantAsync(request.TenantId, request.TargetId.Value, ct);
        if (sprint is null)
            return ApplyOutcome.Stale;
        if (request.TargetUpdatedAtSnapshot is { } snap && (sprint.UpdatedAt ?? sprint.CreatedAt) > snap)
            return ApplyOutcome.Stale;

        var validation = _writes.ValidateDelete(sprint);
        if (!validation.IsSuccess)
            return ApplyOutcome.Invalid(validation.Error ?? "The sprint change could not be applied.");

        var taskIds = (await _tasks.GetBySprintIdAsync(request.TenantId, sprint.Id, ct)).Select(t => t.Id).ToList();
        var undoJson = JsonSerializer.Serialize(new SprintDeleteUndoSnapshot(taskIds), SprintPayloadJson.Options);

        await _writes.ApplyDeleteAsync(request.TenantId, sprint, ct);
        return ApplyOutcome.Applied(undoJson);
    }
}
```

```csharp
public sealed record SprintDeleteUndoSnapshot(IReadOnlyList<Guid> TaskIds);
```

(This drops `SprintApplierBase` inheritance for this one applier. Its existing unit tests that mock `ISprintWriteService`/`ISprintRepository` through the base class's shape will need their constructor call updated to pass an `IWorkTaskRepository` mock too — update `SprintDeleteApplierTests.cs` accordingly and re-run it before moving on.)

`src/ONEVO.Application/Features/WorkManagement/Sprints/Reverters/SprintDeleteReverter.cs`:

```csharp
using System.Text.Json;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Sprints.DTOs;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;

namespace ONEVO.Application.Features.WorkManagement.Sprints.Reverters;

/// <summary>Undoes an approved sprint.delete via ISprintWriteService.Restore (Task 3): clears the
/// soft-delete flags and reattaches the tasks SprintDeleteApplier detached, Conflict if any of them has
/// since joined a different sprint.</summary>
public sealed class SprintDeleteReverter : IApprovalActionReverter
{
    private readonly ISprintWriteService _writes;
    private readonly ISprintRepository _sprints;

    public SprintDeleteReverter(ISprintWriteService writes, ISprintRepository sprints)
    {
        _writes = writes;
        _sprints = sprints;
    }

    public string ActionType => WorkActionTypes.SprintDelete;

    public async Task<RevertOutcome> RevertAsync(ApprovalRevertContext context, CancellationToken ct)
    {
        var request = context.Request;
        if (request.UndoStateJson is null)
            return RevertOutcome.NotRevertable("Nothing to undo for this request.");
        if (request.TargetId is null)
            return RevertOutcome.Stale;

        // Deleted sprints are invisible to the normal tracked lookup (IsDeleted query filter) - this
        // needs the same ignore-filter lookup flagged for Task in Task 5 Step 6. Add
        // ISprintRepository.GetTrackedByIdForTenantIncludingDeletedAsync if it doesn't already exist.
        var sprint = await _sprints.GetTrackedByIdForTenantIncludingDeletedAsync(request.TenantId, request.TargetId.Value, ct);
        if (sprint is null || !sprint.IsDeleted)
            return RevertOutcome.Stale;

        var undo = JsonSerializer.Deserialize<SprintDeleteUndoSnapshot>(request.UndoStateJson, SprintPayloadJson.Options);
        if (undo is null)
            return RevertOutcome.NotRevertable("The deleted-tasks snapshot is corrupt.");

        var result = await _writes.Restore(request.TenantId, sprint, undo.TaskIds, ct);
        if (!result.IsSuccess)
            return RevertOutcome.Conflict(result.Error ?? "A task from this sprint has since moved elsewhere.");

        return RevertOutcome.Reverted(() => sprint.UpdatedAt);
    }
}
```

- [ ] **Step 13: Run every Task 6 reverter test**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~Reverters"`
Expected: all Task 5 + Task 6 reverter tests green (roughly 24-28 total — exact count depends on how many Conflict-case tests were added per Step 8/Task 5's instruction).

- [ ] **Step 14: Run the full Appliers suite (snapshot capture must not change apply behavior)**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~Appliers"`
Expected: same pass count as before this task.

- [ ] **Step 15: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement src/ONEVO.Domain/Features/WorkManagement/Tasks/Entities/TaskEditLog.cs tests/ONEVO.Tests.Unit/Features/WorkManagement
git commit -m "feat(work-management): add snapshot-based reverters (edits, transfer, allocation, sprint lifecycle)

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 7: Conflict-check reverters — `module.member_add`, `module.member_remove`, `sprint.complete`

These are the three the user explicitly chose to still include despite the risk (see the design conversation this plan follows). Each refuses with `RevertOutcome.Conflict(...)` — leaving the request Approved, nothing undone — the moment reality has moved on since the original apply, instead of silently corrupting data.

**Files:**
- Modify:
  - `src/ONEVO.Application/Features/WorkManagement/Sprints/Services/ISprintWriteService.cs` + `SprintWriteService.cs` (`ApplyCompleteAsync` now returns `Result<IReadOnlyList<Guid>>`)
  - `src/ONEVO.Application/Features/WorkManagement/Sprints/Commands/CompleteSprint/CompleteSprintCommandHandler.cs` (adjust to the new return type)
  - `src/ONEVO.Application/Features/WorkManagement/Sprints/Appliers/SprintCompleteApplier.cs`
  - `src/ONEVO.Application/Features/WorkManagement/Tasks/RepositoryInterfaces/ITaskAssignmentRepository.cs` + its EF implementation (add the membership-conflict check if no equivalent exists)
- Create:
  - `src/ONEVO.Application/Features/WorkManagement/Objectives/Reverters/ModuleMemberAddReverter.cs`
  - `src/ONEVO.Application/Features/WorkManagement/Objectives/Reverters/ModuleMemberRemoveReverter.cs`
  - `src/ONEVO.Application/Features/WorkManagement/Sprints/Reverters/SprintCompleteReverter.cs`
- Test: one test class per reverter, plus `SprintWriteServiceTests.cs` extended for the `ApplyCompleteAsync` signature change

**Interfaces:**
- Consumes: `IMilestoneMembershipCoordinator.ApplyMemberAddAsync`/`ApplyMemberRemoveAsync`, `ISprintWriteService.ApplyUncompleteAsync` (Task 3)
- Produces: `ISprintWriteService.ApplyCompleteAsync(...)` now returns `Task<Result<IReadOnlyList<Guid>>>` (was `Task<Result>`) — the `Value` is `movedTaskIds`

- [ ] **Step 1: `module.member_remove` first (simplest of the three) — write the failing reverter test**

`tests/ONEVO.Tests.Unit/Features/WorkManagement/Objectives/Reverters/ModuleMemberRemoveReverterTests.cs`:

```csharp
using System.Text.Json;
using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Reverters;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Objectives.Reverters;

public class ModuleMemberRemoveReverterTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private readonly Mock<IObjectiveRepository> _objectives = new();
    private readonly Mock<IMilestoneMembershipCoordinator> _membership = new();

    private ModuleMemberRemoveReverter Build() => new(_objectives.Object, _membership.Object);

    [Fact]
    public async Task RevertAsync_ReAddsTheRemovedMember()
    {
        var moduleId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var module = new Objective { Id = moduleId, TenantId = TenantId, IsActive = true };
        var request = new WorkApprovalRequest
        {
            Id = Guid.NewGuid(), TenantId = TenantId, TargetId = moduleId, ActionType = WorkActionTypes.ModuleMemberRemove,
            PayloadJson = JsonSerializer.Serialize(new ModuleMemberRemoveInput(employeeId), ModulePayloadJson.Options)
        };
        _objectives.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, moduleId, It.IsAny<CancellationToken>())).ReturnsAsync(module);
        _membership.Setup(x => x.ApplyMemberAddAsync(TenantId, module, It.IsAny<Guid>(), employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ONEVO.Domain.Features.WorkManagement.ProjectInvitations.Entities.ProjectMemberInvitation>.Success(null!));

        var outcome = await Build().RevertAsync(new ApprovalRevertContext(request, Guid.NewGuid()), CancellationToken.None);

        outcome.Kind.Should().Be(RevertOutcomeKind.Reverted);
        _membership.Verify(x => x.ApplyMemberAddAsync(TenantId, module, It.IsAny<Guid>(), employeeId, It.IsAny<CancellationToken>()), Times.Once);
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~ModuleMemberRemoveReverterTests"`
Expected: build FAIL, `ModuleMemberRemoveReverter` not found.

- [ ] **Step 3: Implement `ModuleMemberRemoveReverter`**

```csharp
using System.Text.Json;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Reverters;

/// <summary>Undoes an approved module.member_remove by re-inviting the employee, reusing
/// IMilestoneMembershipCoordinator.ApplyMemberAddAsync exactly as a normal member_add would.</summary>
public sealed class ModuleMemberRemoveReverter : IApprovalActionReverter
{
    private readonly IObjectiveRepository _objectives;
    private readonly IMilestoneMembershipCoordinator _membership;

    public ModuleMemberRemoveReverter(IObjectiveRepository objectives, IMilestoneMembershipCoordinator membership)
    {
        _objectives = objectives;
        _membership = membership;
    }

    public string ActionType => WorkActionTypes.ModuleMemberRemove;

    public async Task<RevertOutcome> RevertAsync(ApprovalRevertContext context, CancellationToken ct)
    {
        var request = context.Request;
        if (request.TargetId is null)
            return RevertOutcome.Stale;

        var module = await _objectives.GetTrackedByIdForTenantAsync(request.TenantId, request.TargetId.Value, ct);
        if (module is null || !module.IsActive)
            return RevertOutcome.Stale;

        var input = JsonSerializer.Deserialize<ModuleMemberRemoveInput>(request.PayloadJson, ModulePayloadJson.Options);
        if (input is null || input.EmployeeId == Guid.Empty)
            return RevertOutcome.NotRevertable("The original request has no employee to restore.");

        var result = await _membership.ApplyMemberAddAsync(request.TenantId, module, context.DeciderEmployeeId, input.EmployeeId, ct);
        if (!result.IsSuccess)
            return RevertOutcome.Conflict(result.Error ?? "This member could not be re-added - they may already be a member again, or no longer an active employee.");

        return RevertOutcome.Reverted(() => module.UpdatedAt);
    }
}
```

- [ ] **Step 4: Run the test**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~ModuleMemberRemoveReverterTests"`
Expected: 1 passed.

- [ ] **Step 5: `module.member_add` — add the "already working" conflict check**

First check whether `ITaskAssignmentRepository` (or `IWorkTaskRepository`) already exposes a way to ask "is this employee assigned to any task under this Module" — grep `AssignedTo|GetByObjectiveIdAsync|ByEmployeeId` across `src/ONEVO.Application/Features/WorkManagement/Tasks/RepositoryInterfaces`. If nothing fits, add:

```csharp
    /// <summary>True if this employee is assigned to any (non-deleted) task whose Module is this one.
    /// Used by ModuleMemberAddReverter to refuse reverting a membership grant once the member has
    /// already started real work under it.</summary>
    Task<bool> AnyForEmployeeInObjectiveAsync(Guid tenantId, Guid objectiveId, Guid employeeId, CancellationToken ct = default);
```

to `ITaskAssignmentRepository`, implemented in its EF repository as (adjust the exact join to match how `TaskAssignment`/`WorkTask` actually relate in this codebase - verify `TaskAssignment.TaskId` → `WorkTask.Id` → `WorkTask.ObjectiveId` before writing the query):

```csharp
    public async Task<bool> AnyForEmployeeInObjectiveAsync(Guid tenantId, Guid objectiveId, Guid employeeId, CancellationToken ct = default)
        => await _db.TaskAssignments.AsNoTracking()
            .Join(_db.WorkTasks.AsNoTracking(), a => a.TaskId, t => t.Id, (a, t) => new { a, t })
            .AnyAsync(x => x.a.TenantId == tenantId && x.a.EmployeeId == employeeId && x.t.ObjectiveId == objectiveId, ct);
```

Write its own small repository test first (`EfTaskAssignmentRepositoryTests.cs`, extend if it exists), red then green, before using it below.

- [ ] **Step 6: Write the failing `ModuleMemberAddReverter` tests**

```csharp
    [Fact]
    public async Task RevertAsync_MemberNotYetAssignedAnyTask_RemovesThem()
    {
        // _assignments.AnyForEmployeeInObjectiveAsync(...) returns false (default Moq bool) => removal proceeds
    }

    [Fact]
    public async Task RevertAsync_MemberAlreadyAssignedATask_ReturnsConflict()
    {
        // _assignments.Setup(...).ReturnsAsync(true) => outcome.Kind == RevertOutcomeKind.Conflict, membership.ApplyMemberRemoveAsync never called
    }
```

Write these as full test methods (same constructor/mock shape as `ModuleMemberRemoveReverterTests`, plus a `Mock<ITaskAssignmentRepository> _assignments`), run red.

- [ ] **Step 7: Implement `ModuleMemberAddReverter`**

```csharp
using System.Text.Json;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Reverters;

/// <summary>Undoes an approved module.member_add by removing the member - but refuses (Conflict) if
/// they've since been assigned to any task in the Module, so a revert can never silently strand an
/// assignee with no membership behind it.</summary>
public sealed class ModuleMemberAddReverter : IApprovalActionReverter
{
    private readonly IObjectiveRepository _objectives;
    private readonly IMilestoneMembershipCoordinator _membership;
    private readonly ITaskAssignmentRepository _assignments;

    public ModuleMemberAddReverter(IObjectiveRepository objectives, IMilestoneMembershipCoordinator membership, ITaskAssignmentRepository assignments)
    {
        _objectives = objectives;
        _membership = membership;
        _assignments = assignments;
    }

    public string ActionType => WorkActionTypes.ModuleMemberAdd;

    public async Task<RevertOutcome> RevertAsync(ApprovalRevertContext context, CancellationToken ct)
    {
        var request = context.Request;
        if (request.TargetId is null)
            return RevertOutcome.Stale;

        var module = await _objectives.GetTrackedByIdForTenantAsync(request.TenantId, request.TargetId.Value, ct);
        if (module is null || !module.IsActive)
            return RevertOutcome.Stale;

        var input = JsonSerializer.Deserialize<ModuleMemberAddInput>(request.PayloadJson, ModulePayloadJson.Options);
        if (input is null || input.EmployeeId == Guid.Empty)
            return RevertOutcome.NotRevertable("The original request has no employee to remove.");

        if (await _assignments.AnyForEmployeeInObjectiveAsync(request.TenantId, module.Id, input.EmployeeId, ct))
            return RevertOutcome.Conflict("This member has already been assigned work in this milestone - remove them manually instead.");

        var result = await _membership.ApplyMemberRemoveAsync(request.TenantId, module, input.EmployeeId, ct);
        if (!result.IsSuccess)
            return RevertOutcome.Conflict(result.Error ?? "This member could not be removed.");

        return RevertOutcome.Reverted(() => module.UpdatedAt);
    }
}
```

- [ ] **Step 8: Run both member reverter test classes**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~ModuleMemberAddReverterTests|FullyQualifiedName~ModuleMemberRemoveReverterTests"`
Expected: all green.

- [ ] **Step 9: `sprint.complete` — change `ApplyCompleteAsync` to return the moved task ids**

In `ISprintWriteService.cs`:

```csharp
    Task<Result<IReadOnlyList<Guid>>> ApplyCompleteAsync(Guid tenantId, Guid actorEmployeeId, Sprint trackedSprint, SprintCompleteInput input, CancellationToken ct = default);
```

In `SprintWriteService.cs`, change the signature and both `return` statements:

```csharp
    public async Task<Result<IReadOnlyList<Guid>>> ApplyCompleteAsync(Guid tenantId, Guid actorEmployeeId, Sprint trackedSprint, SprintCompleteInput input, CancellationToken ct = default)
    {
        var validation = await ValidateCompleteAsync(tenantId, trackedSprint, input, ct);
        if (!validation.IsSuccess)
            return Result<IReadOnlyList<Guid>>.Failure(validation.Error!, validation.StatusCode ?? 400);

        // ... unchanged body down through the NotifyAudienceAsync call ...

        var completionTemplateCode = movedTaskIds.Count > 0 ? "work_sprint_incomplete" : "work_sprint_completed";
        await NotifyAudienceAsync(tenantId, trackedSprint, audience, completionTemplateCode, ct);
        return Result<IReadOnlyList<Guid>>.Success(movedTaskIds);
    }
```

(Every line between `var fromStatus = trackedSprint.Status;` and the `completionTemplateCode` line is unchanged - only the two `return` statements and the method signature change.)

In `SprintCompleteApplier.cs`:

```csharp
    protected override async Task<Result> ApplyAsync(Guid tenantId, Guid actorEmployeeId, Sprint sprint, string payloadJson, CancellationToken ct)
    {
        var input = Read<SprintCompleteInput>(payloadJson);
        if (input is null)
            return Result.Failure("The sprint complete request has no disposition.");
        var applied = await Writes.ApplyCompleteAsync(tenantId, actorEmployeeId, sprint, input, ct);
        return applied.IsSuccess ? Result.Success() : Result.Failure(applied.Error!, applied.StatusCode ?? 400);
    }
```

`SprintApplierBase.ApplyAsync(Guid, Guid, Sprint, string, CancellationToken)` is declared `Task<Result>`, so `SprintCompleteApplier`'s override can't directly surface `movedTaskIds` through it the same way `ModuleEditApplier`'s `CaptureUndoJson` hook does (that hook runs *before* `ApplyAsync`, when there's nothing to capture yet for Complete - the ids only exist *after* `ApplyCompleteAsync` runs). Give `SprintApplierBase` a second hook, `protected virtual string? CaptureUndoJsonAfterApply(Sprint sprint) => null;`, called in the outer `ApplyAsync` right after the inner one succeeds:

```csharp
        var result = await ApplyAsync(request.TenantId, request.RequestedByEmployeeId, sprint, context.PayloadJson, ct);
        return result.IsSuccess ? ApplyOutcome.Applied(CaptureUndoJsonAfterApply(sprint)) : ApplyOutcome.Invalid(result.Error ?? "The sprint change could not be applied.");
    }

    protected virtual string? CaptureUndoJsonAfterApply(Sprint sprint) => null;
```

Store `movedTaskIds` on a field the applier sets during its own `ApplyAsync` override, read by the hook:

```csharp
public sealed class SprintCompleteApplier : SprintApplierBase
{
    private IReadOnlyList<Guid>? _movedTaskIds;

    public SprintCompleteApplier(ISprintRepository sprints, ISprintWriteService writes) : base(sprints, writes) { }

    public override string ActionType => WorkActionTypes.SprintComplete;

    protected override async Task<Result> ApplyAsync(Guid tenantId, Guid actorEmployeeId, Sprint sprint, string payloadJson, CancellationToken ct)
    {
        var input = Read<SprintCompleteInput>(payloadJson);
        if (input is null)
            return Result.Failure("The sprint complete request has no disposition.");
        var applied = await Writes.ApplyCompleteAsync(tenantId, actorEmployeeId, sprint, input, ct);
        if (applied.IsSuccess)
            _movedTaskIds = applied.Value;
        return applied.IsSuccess ? Result.Success() : Result.Failure(applied.Error!, applied.StatusCode ?? 400);
    }

    protected override string? CaptureUndoJsonAfterApply(Sprint sprint)
        => _movedTaskIds is null ? null : System.Text.Json.JsonSerializer.Serialize(new SprintCompleteUndoSnapshot(_movedTaskIds), SprintPayloadJson.Options);
}
```

```csharp
public sealed record SprintCompleteUndoSnapshot(IReadOnlyList<Guid> MovedTaskIds);
```

(`SprintApplierBase` is a shared base — adding this second hook as `virtual` with a no-op default keeps `SprintEditApplier`/`SprintStartApplier`/`SprintAchieveApplier` from Task 6 compiling unchanged, since none of them override it.)

Update `CompleteSprintCommandHandler.cs:63` (`var applied = await _writes.ApplyCompleteAsync(...)`) for the new return type - it almost certainly only checks `applied.IsSuccess`/`applied.Error` today and never used a `Result` value, so this is a one-line type change, not a behavior change; read that handler's surrounding lines first to confirm nothing else needs updating.

- [ ] **Step 10: Write the failing `SprintCompleteReverter` tests**

Cover: happy path (moved tasks reattached, status back to Active), `UndoStateJson` null → `NotRevertable`, a moved task that's since left its post-complete location → the `ApplyUncompleteAsync`-surfaced `Conflict` propagates through unchanged. Same test-file shape as `SprintAchieveReverterTests`.

- [ ] **Step 11: Implement `SprintCompleteReverter`**

```csharp
using System.Text.Json;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Sprints.DTOs;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;

namespace ONEVO.Application.Features.WorkManagement.Sprints.Reverters;

/// <summary>Undoes an approved sprint.complete via ISprintWriteService.ApplyUncompleteAsync (Task 3),
/// using the moved-task-id list SprintCompleteApplier snapshotted and the disposition still on the
/// request's applied payload (Review Focus #3).</summary>
public sealed class SprintCompleteReverter : IApprovalActionReverter
{
    private readonly ISprintWriteService _writes;
    private readonly ISprintRepository _sprints;

    public SprintCompleteReverter(ISprintWriteService writes, ISprintRepository sprints)
    {
        _writes = writes;
        _sprints = sprints;
    }

    public string ActionType => WorkActionTypes.SprintComplete;

    public async Task<RevertOutcome> RevertAsync(ApprovalRevertContext context, CancellationToken ct)
    {
        var request = context.Request;
        if (request.UndoStateJson is null)
            return RevertOutcome.NotRevertable("Nothing to undo for this request.");
        if (request.TargetId is null)
            return RevertOutcome.Stale;

        var sprint = await _sprints.GetTrackedByIdForTenantAsync(request.TenantId, request.TargetId.Value, ct);
        if (sprint is null)
            return RevertOutcome.Stale;

        var undo = JsonSerializer.Deserialize<SprintCompleteUndoSnapshot>(request.UndoStateJson, SprintPayloadJson.Options);
        var appliedInput = JsonSerializer.Deserialize<SprintCompleteInput>(request.AppliedPayloadJson ?? request.PayloadJson, SprintPayloadJson.Options);
        if (undo is null || appliedInput is null)
            return RevertOutcome.NotRevertable("The completion snapshot is corrupt.");

        var targetSprintId = appliedInput.Disposition == "sprint" ? appliedInput.TargetSprintId : null;
        var result = await _writes.ApplyUncompleteAsync(request.TenantId, context.DeciderEmployeeId, sprint, undo.MovedTaskIds, targetSprintId, ct);
        if (!result.IsSuccess)
            return RevertOutcome.Conflict(result.Error ?? "This sprint's tasks have moved since it was completed.");

        return RevertOutcome.Reverted(() => sprint.UpdatedAt);
    }
}
```

- [ ] **Step 12: Run every Task 7 test**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~ModuleMemberAddReverterTests|FullyQualifiedName~ModuleMemberRemoveReverterTests|FullyQualifiedName~SprintCompleteReverterTests|FullyQualifiedName~SprintWriteServiceTests|FullyQualifiedName~SprintCompleteApplier|FullyQualifiedName~CompleteSprintCommandHandler"`
Expected: all green, including the pre-existing `SprintCompleteApplierTests`/`CompleteSprintCommandHandlerTests` (confirms the `ApplyCompleteAsync` signature change didn't break either caller).

- [ ] **Step 13: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement tests/ONEVO.Tests.Unit/Features/WorkManagement
git commit -m "feat(work-management): add conflict-checked reverters (member_add/remove, sprint.complete)

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 8: `project.status_template_change` reverter — the hard one

This is the last of the 18 and the only one that can't reuse a diff-shaped input (`TaskStatusChangeSetApplier.Apply` consumes a `TaskStatusChangeSet`, which describes *changes*, not "restore to this exact prior list"). The reverter instead snapshots the **whole template** before the original apply and reconciles the live template back to it directly, refusing (Conflict) if a status that must be deleted to restore the snapshot still has active tasks on it.

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/Tasks/Appliers/TaskStatusTemplateChangeApplier.cs`
- Create:
  - `src/ONEVO.Application/Features/WorkManagement/Tasks/DTOs/ProjectStatusTemplateUndoSnapshot.cs`
  - `src/ONEVO.Application/Features/WorkManagement/Tasks/Reverters/ProjectStatusTemplateChangeReverter.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/Reverters/ProjectStatusTemplateChangeReverterTests.cs`

**Interfaces:**
- Consumes: `ITaskStatusRepository.GetProjectTemplateAsync`/`AddAsync`/`Update`/`Remove`, `IWorkTaskRepository.AnyActiveByStatusIdAsync`
- Produces: `ProjectStatusTemplateSnapshotEntry(Guid Id, string Name, int DisplayOrder, string Category, string Color, string Visibility, bool MarksTaskComplete)`, `ProjectStatusTemplateUndoSnapshot(IReadOnlyList<ProjectStatusTemplateSnapshotEntry> Statuses)`

- [ ] **Step 1: Capture the whole-template snapshot in `TaskStatusTemplateChangeApplier`**

Change its `ApplyAsync` (the `var current = await _statuses.GetProjectTemplateAsync(...)` line already exists — capture right after it, before `TaskStatusChangeSetApplier.Apply` mutates anything):

```csharp
        var current = await _statuses.GetProjectTemplateAsync(request.TenantId, request.ProjectId, ct);
        var undoJson = JsonSerializer.Serialize(
            new ProjectStatusTemplateUndoSnapshot(current.Select(s => new ProjectStatusTemplateSnapshotEntry(
                s.Id, s.Name, s.DisplayOrder, s.Category, s.Color, s.Visibility, s.MarksTaskComplete)).ToList()),
            TaskPayload.Options);
        var applied = TaskStatusChangeSetApplier.Apply(
            current, payload.Changes, request.TenantId, request.ProjectId, _currentUser.UserId, DateTimeOffset.UtcNow);

        if (applied.Outcome == TaskStatusChangeApplyOutcome.Invalid)
            return ApplyOutcome.Invalid(applied.Message!);
        if (applied.Outcome == TaskStatusChangeApplyOutcome.Stale)
            return ApplyOutcome.Stale;

        foreach (var status in applied.Added)
            await _statuses.AddAsync(status, ct);
        foreach (var status in applied.Modified)
            _statuses.Update(status);
        foreach (var status in applied.Deleted)
            _statuses.Remove(status);

        await _sweeper.MarkConflictingStaleAsync(
            request.TenantId, request.ProjectId, context.DeciderEmployeeId, payload.Changes.Footprint(), request.Id, ct);

        return ApplyOutcome.Applied(undoJson);
```

(Only the snapshot capture, the `current` → `undoJson` lines, and the final `return` are new; everything else in the method is unchanged.)

- [ ] **Step 2: Create the snapshot DTO**

`src/ONEVO.Application/Features/WorkManagement/Tasks/DTOs/ProjectStatusTemplateUndoSnapshot.cs`:

```csharp
namespace ONEVO.Application.Features.WorkManagement.Tasks.DTOs;

/// <summary>One status row exactly as it existed right before a project.status_template_change was
/// applied - enough to re-create it (if the apply deleted it) or restore its fields (if the apply
/// modified it) on revert.</summary>
public sealed record ProjectStatusTemplateSnapshotEntry(
    Guid Id, string Name, int DisplayOrder, string Category, string Color, string Visibility, bool MarksTaskComplete);

public sealed record ProjectStatusTemplateUndoSnapshot(IReadOnlyList<ProjectStatusTemplateSnapshotEntry> Statuses);
```

- [ ] **Step 3: Write the failing reverter tests**

`tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/Reverters/ProjectStatusTemplateChangeReverterTests.cs`:

```csharp
using System.Text.Json;
using FluentAssertions;
using Moq;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Reverters;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;
using TaskStatusEntity = ONEVO.Domain.Features.WorkManagement.Tasks.Entities.TaskStatus;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks.Reverters;

public class ProjectStatusTemplateChangeReverterTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private readonly Mock<ITaskStatusRepository> _statuses = new();
    private readonly Mock<IWorkTaskRepository> _tasks = new();

    private ProjectStatusTemplateChangeReverter Build() => new(_statuses.Object, _tasks.Object);

    private static WorkApprovalRequest Request(ProjectStatusTemplateUndoSnapshot snapshot) => new()
    {
        Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, ActionType = WorkActionTypes.ProjectStatusTemplateChange,
        UndoStateJson = JsonSerializer.Serialize(snapshot)
    };

    [Fact]
    public async Task RevertAsync_RestoresAModifiedStatussFields()
    {
        var statusId = Guid.NewGuid();
        var live = new TaskStatusEntity { Id = statusId, TenantId = TenantId, Name = "Renamed", DisplayOrder = 0, Category = TaskStatusCategories.Active, Color = "#000000", Visibility = TaskStatusVisibilities.Public };
        var snapshot = new ProjectStatusTemplateUndoSnapshot([new ProjectStatusTemplateSnapshotEntry(statusId, "Original", 0, TaskStatusCategories.Active, "#FFFFFF", TaskStatusVisibilities.Public, false)]);
        _statuses.Setup(x => x.GetProjectTemplateAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<TaskStatusEntity> { live });

        var outcome = await Build().RevertAsync(new ApprovalRevertContext(Request(snapshot), Guid.NewGuid()), CancellationToken.None);

        outcome.Kind.Should().Be(RevertOutcomeKind.Reverted);
        live.Name.Should().Be("Original");
        live.Color.Should().Be("#FFFFFF");
    }

    [Fact]
    public async Task RevertAsync_StatusAddedByTheOriginalApply_IsDeletedOnRevert()
    {
        var addedId = Guid.NewGuid();
        var added = new TaskStatusEntity { Id = addedId, TenantId = TenantId, Name = "New status" };
        var snapshot = new ProjectStatusTemplateUndoSnapshot([]); // added status wasn't in the pre-apply template
        _statuses.Setup(x => x.GetProjectTemplateAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<TaskStatusEntity> { added });
        _tasks.Setup(x => x.AnyActiveByStatusIdAsync(TenantId, addedId, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var outcome = await Build().RevertAsync(new ApprovalRevertContext(Request(snapshot), Guid.NewGuid()), CancellationToken.None);

        outcome.Kind.Should().Be(RevertOutcomeKind.Reverted);
        _statuses.Verify(x => x.Remove(added), Times.Once);
    }

    [Fact]
    public async Task RevertAsync_StatusAddedByApplyNowHasActiveTasks_ReturnsConflict()
    {
        var addedId = Guid.NewGuid();
        var added = new TaskStatusEntity { Id = addedId, TenantId = TenantId, Name = "New status" };
        var snapshot = new ProjectStatusTemplateUndoSnapshot([]);
        _statuses.Setup(x => x.GetProjectTemplateAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<TaskStatusEntity> { added });
        _tasks.Setup(x => x.AnyActiveByStatusIdAsync(TenantId, addedId, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var outcome = await Build().RevertAsync(new ApprovalRevertContext(Request(snapshot), Guid.NewGuid()), CancellationToken.None);

        outcome.Kind.Should().Be(RevertOutcomeKind.Conflict);
        _statuses.Verify(x => x.Remove(It.IsAny<TaskStatusEntity>()), Times.Never);
    }

    [Fact]
    public async Task RevertAsync_StatusDeletedByTheOriginalApply_IsReCreatedOnRevert()
    {
        var deletedId = Guid.NewGuid();
        var snapshot = new ProjectStatusTemplateUndoSnapshot([new ProjectStatusTemplateSnapshotEntry(deletedId, "Blocked", 2, TaskStatusCategories.Active, "#111111", TaskStatusVisibilities.Public, false)]);
        _statuses.Setup(x => x.GetProjectTemplateAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<TaskStatusEntity>());

        var outcome = await Build().RevertAsync(new ApprovalRevertContext(Request(snapshot), Guid.NewGuid()), CancellationToken.None);

        outcome.Kind.Should().Be(RevertOutcomeKind.Reverted);
        _statuses.Verify(x => x.AddAsync(It.Is<TaskStatusEntity>(s => s.Id == deletedId && s.Name == "Blocked"), It.IsAny<CancellationToken>()), Times.Once);
    }
}
```

- [ ] **Step 4: Run them to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~ProjectStatusTemplateChangeReverterTests"`
Expected: build FAIL, `ProjectStatusTemplateChangeReverter` not found.

- [ ] **Step 5: Implement the reverter**

`src/ONEVO.Application/Features/WorkManagement/Tasks/Reverters/ProjectStatusTemplateChangeReverter.cs`:

```csharp
using System.Text.Json;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using TaskStatusEntity = ONEVO.Domain.Features.WorkManagement.Tasks.Entities.TaskStatus;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Reverters;

/// <summary>
/// Undoes an approved project.status_template_change by reconciling the live template back to the
/// whole-template snapshot TaskStatusTemplateChangeApplier captured before it ran - not a diff replay
/// like TaskStatusChangeSetApplier (that type describes *changes*, this restores an exact prior state).
/// Refuses (Conflict) if a status the original apply added now has active tasks on it, since deleting
/// it to restore the snapshot would orphan them - the same rule ApplyAsync itself uses for an ordinary
/// delete.
/// </summary>
public sealed class ProjectStatusTemplateChangeReverter : IApprovalActionReverter
{
    private readonly ITaskStatusRepository _statuses;
    private readonly IWorkTaskRepository _tasks;

    public ProjectStatusTemplateChangeReverter(ITaskStatusRepository statuses, IWorkTaskRepository tasks)
    {
        _statuses = statuses;
        _tasks = tasks;
    }

    public string ActionType => WorkActionTypes.ProjectStatusTemplateChange;

    public async Task<RevertOutcome> RevertAsync(ApprovalRevertContext context, CancellationToken ct)
    {
        var request = context.Request;
        if (request.UndoStateJson is null)
            return RevertOutcome.NotRevertable("Nothing to undo for this request.");

        var snapshot = JsonSerializer.Deserialize<ProjectStatusTemplateUndoSnapshot>(request.UndoStateJson);
        if (snapshot is null)
            return RevertOutcome.NotRevertable("The pre-change template snapshot is corrupt.");

        var current = await _statuses.GetProjectTemplateAsync(request.TenantId, request.ProjectId, ct);
        var currentById = current.ToDictionary(s => s.Id);
        var snapshotById = snapshot.Statuses.ToDictionary(s => s.Id);

        // Statuses in the live template but not the snapshot were added by the original apply - undo
        // deletes them, but only if nothing has since started using one.
        var toDelete = current.Where(s => !snapshotById.ContainsKey(s.Id)).ToList();
        foreach (var status in toDelete)
        {
            if (await _tasks.AnyActiveByStatusIdAsync(request.TenantId, status.Id, ct))
                return RevertOutcome.Conflict($"\"{status.Name}\" was added by this change and now has tasks on it - move them out before reverting.");
        }

        foreach (var status in toDelete)
            _statuses.Remove(status);

        // Statuses in the snapshot but not the live template were deleted by the original apply -
        // undo re-creates them with their original id (nothing still references it, by the same
        // guarantee the original delete's own active-task check gave).
        foreach (var entry in snapshot.Statuses.Where(s => !currentById.ContainsKey(s.Id)))
        {
            await _statuses.AddAsync(new TaskStatusEntity
            {
                Id = entry.Id, TenantId = request.TenantId, ProjectId = request.ProjectId, ObjectiveId = null,
                Name = entry.Name, DisplayOrder = entry.DisplayOrder, Category = entry.Category,
                Color = entry.Color, Visibility = entry.Visibility, MarksTaskComplete = entry.MarksTaskComplete
            }, ct);
        }

        // Statuses present in both were (possibly) modified by the original apply - restore their
        // fields to the snapshot's values.
        foreach (var status in current)
        {
            if (!snapshotById.TryGetValue(status.Id, out var entry))
                continue;
            status.Name = entry.Name;
            status.DisplayOrder = entry.DisplayOrder;
            status.Category = entry.Category;
            status.Color = entry.Color;
            status.Visibility = entry.Visibility;
            status.MarksTaskComplete = entry.MarksTaskComplete;
            status.UpdatedAt = DateTimeOffset.UtcNow;
            _statuses.Update(status);
        }

        // No TargetUpdatedAtSnapshot to re-baseline: this action type has no single TargetId (it's
        // project-wide), so ModuleApplierBase/SprintApplierBase's staleness check never applied to it.
        return RevertOutcome.Reverted();
    }
}
```

- [ ] **Step 6: Run the reverter tests**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~ProjectStatusTemplateChangeReverterTests"`
Expected: 4 passed.

- [ ] **Step 7: Run the existing `TaskStatusTemplateChangeApplier`/`TaskStatusChangeSetApplier` suites**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~TaskStatusTemplateChangeApplier|FullyQualifiedName~TaskStatusChangeSetApplier"`
Expected: same pass count as before this task (the snapshot capture in Step 1 doesn't change apply behavior).

- [ ] **Step 8: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Tasks tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/Reverters
git commit -m "feat(work-management): add project.status_template_change reverter

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 9: DI registration and the full backend gate

**Files:**
- Modify: `src/ONEVO.Infrastructure/DependencyInjection.cs`

**Interfaces:**
- Produces: all 18 `IApprovalActionReverter` implementations resolvable from the container, `IApprovalActionReverterRegistry` resolvable.

- [ ] **Step 1: Register the registry and all 18 reverters**

In `DependencyInjection.cs`, directly under the existing `services.AddScoped<IApprovalActionApplier, ...>` block (the 18 lines ending with `SprintCreateApplier`):

```csharp
        services.AddScoped<IApprovalActionReverterRegistry, ApprovalActionReverterRegistry>();
        services.AddScoped<IApprovalActionReverter, ONEVO.Application.Features.WorkManagement.Tasks.Reverters.TaskCreateReverter>();
        services.AddScoped<IApprovalActionReverter, ONEVO.Application.Features.WorkManagement.Tasks.Reverters.TaskEditReverter>();
        services.AddScoped<IApprovalActionReverter, ONEVO.Application.Features.WorkManagement.Tasks.Reverters.TaskDeleteReverter>();
        services.AddScoped<IApprovalActionReverter, ONEVO.Application.Features.WorkManagement.Tasks.Reverters.ProjectStatusTemplateChangeReverter>();
        services.AddScoped<IApprovalActionReverter, ONEVO.Application.Features.WorkManagement.Objectives.Reverters.ModuleAllocationExtendReverter>();
        services.AddScoped<IApprovalActionReverter, ONEVO.Application.Features.WorkManagement.Objectives.Reverters.ModuleMemberAddReverter>();
        services.AddScoped<IApprovalActionReverter, ONEVO.Application.Features.WorkManagement.Objectives.Reverters.ModuleMemberRemoveReverter>();
        services.AddScoped<IApprovalActionReverter, ONEVO.Application.Features.WorkManagement.Objectives.Reverters.ModuleUnachieveReverter>();
        services.AddScoped<IApprovalActionReverter, ONEVO.Application.Features.WorkManagement.Objectives.Reverters.ModuleAchieveReverter>();
        services.AddScoped<IApprovalActionReverter, ONEVO.Application.Features.WorkManagement.Objectives.Reverters.ModuleTransferReverter>();
        services.AddScoped<IApprovalActionReverter, ONEVO.Application.Features.WorkManagement.Objectives.Reverters.ModuleDeleteReverter>();
        services.AddScoped<IApprovalActionReverter, ONEVO.Application.Features.WorkManagement.Objectives.Reverters.ModuleEditReverter>();
        services.AddScoped<IApprovalActionReverter, ONEVO.Application.Features.WorkManagement.Sprints.Reverters.SprintDeleteReverter>();
        services.AddScoped<IApprovalActionReverter, ONEVO.Application.Features.WorkManagement.Sprints.Reverters.SprintAchieveReverter>();
        services.AddScoped<IApprovalActionReverter, ONEVO.Application.Features.WorkManagement.Sprints.Reverters.SprintCompleteReverter>();
        services.AddScoped<IApprovalActionReverter, ONEVO.Application.Features.WorkManagement.Sprints.Reverters.SprintStartReverter>();
        services.AddScoped<IApprovalActionReverter, ONEVO.Application.Features.WorkManagement.Sprints.Reverters.SprintEditReverter>();
```

(17 reverters, not 18 — `sprint.create` has no reverter: creating a sprint from an approval has nothing a revert would meaningfully undo beyond what `sprint.delete`'s own revert already covers if someone deletes it afterward, and the user's approved scope was "undo what Accept/Reject changed," not "add a brand-new undo-create for every create type." `TaskCreateReverter` is the one exception already covered in Task 5 — the reason it needs its own is Review Focus #2 (nulling `TargetId`), which `sprint.create`/`module.create`... module.create doesn't exist as an action type, and sprint.create has no equivalent `TargetId`-stamping behavior to undo, since `SprintCreateApplier` sets `request.TargetId` the same stamping way — **check this before Step 1**: grep `request.TargetId =` in `SprintCreateApplier.cs`; if it stamps `TargetId` the same way `TaskCreateApplier` does, add a `SprintCreateReverter` mirroring `TaskCreateReverter` exactly (soft-delete the created sprint via `Writes.Restore` with an empty task-id list, null `TargetId`) and register it here as the 18th line. Add `WorkActionTypes.SprintCreate` test coverage either way.)

- [ ] **Step 2: Add the missing `using`**

```csharp
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
```

(likely already present from the existing applier registrations — only add if the build complains).

- [ ] **Step 3: Run the registry's own test plus a container-resolves-everything smoke check**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~ApprovalActionReverterRegistryTests"`

Then write one more test (in the same file) that asserts there's a reverter registered for every `WorkActionTypes` constant that has an `IApprovalActionApplier` (catches a forgotten registration at compile+test time instead of production):

```csharp
    [Fact]
    public void EveryActionTypeWithAnApplier_AlsoHasAReverter_ExceptSprintCreateIfItHasNoTargetIdStamping()
    {
        // Build the real ApprovalActionReverterRegistry from DI (ONEVO.API's composition root) in a
        // thin integration-style test, or hand-list the 18 WorkActionTypes constants here and assert
        // ApprovalActionReverterRegistry.Find returns non-null for each except the ones this plan
        // deliberately excluded. Prefer the DI-resolution version if an existing test in this codebase
        // already spins up the full container (grep "ServiceCollection" under tests/ONEVO.Tests.Unit
        // for a precedent) - it catches a missing DI line, which a hand-listed assertion cannot.
    }
```

- [ ] **Step 4: Run the full backend gate**

```bash
dotnet build-server shutdown
dotnet build src/ONEVO.Api -c Release
dotnet test tests/ONEVO.Tests.Unit -c Release
dotnet test tests/ONEVO.Tests.Architecture -c Release
```

Expected: all green. If the architecture suite fails on something naming- or layering-related (e.g. a `Reverters` folder convention check), read the specific failing test and fix the reverter file(s) it flags rather than suppressing it.

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Infrastructure/DependencyInjection.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/Approvals/ApprovalActionReverterRegistryTests.cs
git commit -m "feat(work-management): register all approval reverters in DI

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

## Part B — Frontend

### Task 10: API service and model

**Files:**
- Modify:
  - `src/app/modules/work/data-access/work-approvals-api.service.ts`
  - `src/app/modules/work/models/dto/work-approval.dto.ts`
- Test: `src/app/modules/work/data-access/work-approvals-api.service.spec.ts`

**Interfaces:**
- Produces: `WorkApprovalsApiService.revertWorkApproval(id: string): Observable<WorkApprovalRequestDto>`
- Modifies: `WorkApprovalRequestDto` gains `canRevert: boolean`, `revertableUntil: string | null` (matching the backend's camelCase JSON serialization of `CanRevert`/`RevertableUntil`)

- [ ] **Step 1: Read the existing service and DTO first**

Open `work-approvals-api.service.ts` and find `approveWorkApproval`/`rejectWorkApproval`/`cancelWorkApproval` (or whatever the existing decide methods are actually named — match their exact signature shape, HTTP client call pattern, and return type). Open `work-approval.dto.ts` and find the interface those methods return.

- [ ] **Step 2: Write the failing service test**

In `work-approvals-api.service.spec.ts`, copy the existing `approveWorkApproval`/`rejectWorkApproval` test(s) verbatim and adapt for revert:

```typescript
it('reverts an approval request', () => {
  const dto = { id: 'req-1', canRevert: false } as WorkApprovalRequestDto;
  let result: WorkApprovalRequestDto | undefined;
  service.revertWorkApproval('req-1').subscribe(r => (result = r));

  const req = httpMock.expectOne(r => r.method === 'POST' && r.url.endsWith('/approvals/req-1/revert'));
  req.flush(dto);

  expect(result).toEqual(dto);
});
```

(Adjust the exact base-URL/`expectOne` matcher to this file's existing convention — copy it from the `approveWorkApproval` test rather than guessing the URL-building helper's name.)

- [ ] **Step 3: Run it to verify it fails**

Run: `npx ng test --watch=false --browsers=ChromeHeadless --include='**/work-approvals-api.service.spec.ts'`
Expected: FAIL, `revertWorkApproval` not a function.

- [ ] **Step 4: Add the DTO fields**

In `work-approval.dto.ts`, add to the `WorkApprovalRequestDto` interface (next to `decidedAt` or wherever the other decision-related fields sit):

```typescript
  canRevert: boolean;
  revertableUntil: string | null;
```

- [ ] **Step 5: Implement `revertWorkApproval`**

In `work-approvals-api.service.ts`, next to `cancelWorkApproval` (or the equivalent existing decide method), follow its exact pattern (same `http.post<WorkApprovalRequestDto>` shape, same base URL constant):

```typescript
  revertWorkApproval(id: string): Observable<WorkApprovalRequestDto> {
    return this.http.post<WorkApprovalRequestDto>(`${this.baseUrl}/approvals/${id}/revert`, {});
  }
```

(Match `baseUrl`/the exact base-path construction to whatever the neighboring methods already use — do not invent a new constant.)

- [ ] **Step 6: Run the test**

Run: `npx ng test --watch=false --browsers=ChromeHeadless --include='**/work-approvals-api.service.spec.ts'`
Expected: passing.

- [ ] **Step 7: Commit**

```bash
git add src/app/modules/work/data-access/work-approvals-api.service.ts src/app/modules/work/data-access/work-approvals-api.service.spec.ts src/app/modules/work/models/dto/work-approval.dto.ts
git commit -m "feat(wm-approvals): add revertWorkApproval API call and canRevert/revertableUntil fields

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 11: Revert button, countdown, and confirmation

**Files:**
- Modify:
  - `src/app/modules/work/state/work-approvals.store.ts`
  - `src/app/modules/work/feature/work-approvals/work-approvals.component.ts`
  - `src/app/modules/work/feature/work-approvals/work-approvals.component.html` (if the component has a separate template file — check; some of this codebase's newer components inline the template)
- Test:
  - `src/app/modules/work/state/work-approvals.store.spec.ts`
  - `src/app/modules/work/feature/work-approvals/work-approvals.component.spec.ts`

**Interfaces:**
- Consumes: `WorkApprovalsApiService.revertWorkApproval` (Task 10)
- Produces: a store action (match the existing naming convention — e.g. if `approve(id)`/`reject(id)` exist on the store, add `revert(id)` the same shape) that calls the API and patches the reverted row (status → `'pending'`, `canRevert` → `false`, `decidedAt`/`decidedByName` cleared) into whatever signal/array the store already holds

- [ ] **Step 1: Read the existing store and component first**

Open `work-approvals.store.ts` and find the `approve`/`reject` (or `decide`) action(s) — match their exact shape: do they call the API then refetch the list, or patch the row in place? Follow the same approach for `revert`. Open `work-approvals.component.ts`/`.html` and find where Approve/Reject buttons render in the explanation card (per memory `project_hrms_approvals_page_redesign`, there's a shared `app-data-table` plus a detail/explanation panel — the Revert button belongs in the detail panel, next to or below the existing decision actions, not in the 3-dot quick-action menu, since the design conversation this plan follows said "whoever accepted it... within the last 30 minutes").

- [ ] **Step 2: Write the failing store test**

In `work-approvals.store.spec.ts`, copy the existing `approve`/`reject` action's test and adapt:

```typescript
it('revert patches the row back to pending and clears canRevert', fakeAsync(() => {
  // seed the store with one Approved row with canRevert: true (copy the existing seeding helper this
  // spec file already uses for approve/reject tests)
  api.revertWorkApproval.and.returnValue(of({ ...seededRow, status: 'pending', canRevert: false, decidedAt: null }));

  store.revert(seededRow.id);
  tick();

  const row = store.requests().find(r => r.id === seededRow.id);
  expect(row?.status).toBe('pending');
  expect(row?.canRevert).toBe(false);
}));
```

(Match the exact signal/method names - `requests()` is a placeholder for whatever the store's actual list accessor is called; read the file first.)

- [ ] **Step 3: Run it to verify it fails**

Run: `npx ng test --watch=false --browsers=ChromeHeadless --include='**/work-approvals.store.spec.ts'`
Expected: FAIL, `revert` not a function.

- [ ] **Step 4: Implement the store action**

Add `revert(id: string)` to `work-approvals.store.ts`, mirroring the existing `approve`/`reject` action's error handling, loading-state signal, and row-patch logic exactly (copy its structure, swap the API call and the patched fields).

- [ ] **Step 5: Run the store test**

Expected: passing.

- [ ] **Step 6: Write the failing component test for the Revert button**

In `work-approvals.component.spec.ts`:

```typescript
it('shows a Revert button only when the selected request has canRevert true', () => {
  // select a row with canRevert: true, detectChanges, query for the revert button -> present
  // select a row with canRevert: false, detectChanges, query again -> absent
});

it('clicking Revert asks for confirmation before calling the store', () => {
  // click revert button, assert store.revert was NOT yet called, confirm the dialog, assert it was
});
```

(Follow this spec file's existing query/harness pattern - e.g. `fixture.debugElement.query(By.css(...))` or a Testing Library helper, whichever this file already uses for the Approve/Reject buttons.)

- [ ] **Step 7: Run it to verify it fails**

Expected: FAIL, no element found / no confirmation flow.

- [ ] **Step 8: Implement the Revert button**

In the explanation-card section of `work-approvals.component` (template + component class), next to the existing decision actions:

- Render a "Revert" button when `selected().canRevert` is true (use the same `selected()`/equivalent signal the existing Approve/Reject buttons already read from).
- A small live countdown ("Revertable for 18:42" counting down to `revertableUntil`, ticking every second via the existing pattern this codebase uses for any other live countdown — check `milestone-tree-tab`/`time-tracking` components for a precedent `setInterval`/`toSignal(interval(...))` pattern before inventing a new one) that hides the button once `revertableUntil` passes, without requiring a page refresh.
- Clicking Revert opens a confirmation dialog (reuse whatever confirm-dialog component/service this codebase already uses elsewhere in this same file or `shared/ui` — check `bulk-action-result-modal` or a `ConfirmDialogService` for the established pattern) with copy along the lines of "Revert this decision? [Approved/Rejected action label] will be undone and the request reopened for a new decision." On confirm, call `store.revert(selected().id)`.
- On success, keep the row selected (it now shows Pending) and show a toast ("Reverted — reopened for another decision") via whatever toast/notification service `milestone-tree-tab.component.ts` already uses (seen committed in this session's earlier notification-completion work — `this.notificationService.info(...)`).
- On a 409 (Conflict) response, show the server's error message in the same toast/error-banner pattern the Approve/Reject flow already uses for its own failure case — do not write new error-handling plumbing.

- [ ] **Step 9: Run the component tests**

Run: `npx ng test --watch=false --browsers=ChromeHeadless --include='**/work-approvals.component.spec.ts'`
Expected: passing.

- [ ] **Step 10: Run the full frontend gate**

```bash
npx ng build
npx ng test --watch=false --browsers=ChromeHeadless
```

Expected: build clean; test suite green (a few pre-existing flaky specs in `milestone-tree-tab`/`work-approvals` may need a solo re-run before trusting a red result — see Global Constraints).

- [ ] **Step 11: Commit**

```bash
git add src/app/modules/work/state/work-approvals.store.ts src/app/modules/work/state/work-approvals.store.spec.ts src/app/modules/work/feature/work-approvals
git commit -m "feat(wm-approvals): add Revert button with 30-minute countdown and confirmation

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 12: Manual browser checklist

Not a coding task — report this checklist to the user at the end instead of checking it yourself (no running backend/DB in this session):

1. Apply the `AddApprovalRevertColumns` migration.
2. As the decider, Approve a `module.edit` request → see the module's new title. Open the Approvals page, select that request, see a "Revert" button with a counting-down timer. Click it, confirm → module title reverts to the old value, request shows Pending again, and the original requester gets a notification.
3. Same flow for a `module.member_add` request where the invited member has **not** yet been assigned any task → Revert succeeds, member is removed from the module.
4. Approve a `module.member_add`, then assign that member to a task, then try Revert → see a clear "already assigned work" error, member stays a module member.
5. Reject a request as the decider → Revert button appears (same 30-minute rule) → clicking it reopens the request as Pending with no module/task changes required (nothing was ever applied).
6. Log in as a *different* user than the decider and open the same decided request → no Revert button, even within the 30-minute window.
7. Wait (or manipulate a test row's `decided_at`) past 30 minutes → Revert button disappears / a direct API call returns 409.
8. Approve a `sprint.complete` request, then manually move one of the moved-out tasks into a different sprint, then try Revert → see the "tasks have moved since" Conflict error, sprint stays Complete.
9. Full regression pass on the existing Approve/Reject/Cancel flows (Task 2's `ApplyOutcome.Applied()` rename and Task 9's registrations touch every applier's call site) — spot-check at least one approval of each of the three families (Task/Module/Sprint) still applies correctly.

Report back: commit hashes for both repos, final test counts (backend unit + architecture, frontend build + test), the migration name to apply, and this checklist for the user to run through in the browser.

