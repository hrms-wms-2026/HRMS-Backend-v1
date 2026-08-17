# Objective ViewModel — Missing Owner/Achieved Fields — Design

**Status:** Approved 2026-08-12 (found while verifying backend APIs ahead of the frontend's unified tree-table redesign — `Hrms--Web-application---front-end---v1/docs/superpowers/specs/next/2026-08-12-milestone-tree-mockup-redesign-design.md`). No plan written yet.

## Problem (confirmed by reading source, not just docs)

`docs/postman-request/Work Management/Get Objective.md` and `Get Objective Subtree.md` both document `ownerName`, `reportingManagerName`, `isOwner` (and, for Subtree, `isAchieved`/`achievedAt` on every node) as "Added 2026-08-10 ... for the Project Detail milestone tree view's detail panel." That never actually happened on the wire:

- `GetObjectiveByIdQueryHandler` / `GetObjectiveSubtreeQueryHandler` correctly **compute** these fields onto their Application-layer DTOs — confirmed: `ObjectiveDetailResponse` (`src/ONEVO.Application/Features/WorkManagement/Objectives/DTOs/Responses/ObjectiveDetailResponse.cs`) and `ObjectiveSubtreeNodeResponse` (`.../ObjectiveSubtreeResponse.cs`) both carry `OwnerName`, `ReportingManagerName`, `IsOwner` (Subtree also `IsAchieved`, `AchievedAt`).
- The API-layer ViewModels that actually get serialized do **not** have these fields: `ObjectiveDetailViewModel` and `ObjectiveSubtreeNodeViewModel` (`src/ONEVO.Api/Contracts/WorkManagement/Objectives/*.cs`) were never updated when the Application DTOs gained them.
- `ObjectiveViewModelMapper.ToViewModel()` (same folder) silently drops the fields in the DTO→ViewModel conversion — there's nowhere for them to go.

Net effect: `GET /api/v1/work/objectives/{id}` and `GET /api/v1/work/objectives/{id}/tree` both return less than the docs promise and less than the Application layer already computes. This is exactly the failure mode `docs/superpowers/project_ core/ONEVO_Backend_Architecture_Document.md` §2.1.1.1's View Model Convention exists to prevent — the retrofit landed on the Application side but never finished on the Contracts side.

## Fix

Add the missing fields to both ViewModel records and pass them through in `ObjectiveViewModelMapper`. No Application, Domain, or Infrastructure change — the data is already correct; only the wire contract is incomplete.

```csharp
// ObjectiveDetailViewModel.cs — add 3 fields
public sealed record ObjectiveDetailViewModel(
    Guid Id, Guid ProjectId, Guid? ParentObjectiveId, bool IsDefault, string Title, string? Description,
    Guid OwnerId, Guid? ReportingManagerId, Guid CreatedById, DateOnly StartDate, DateOnly EndDate,
    decimal Progress, decimal? ActualHours, decimal AllocatedHours, decimal CompletedHours,
    bool IsActive, bool IsAchieved, DateTimeOffset? AchievedAt, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt,
    string? OwnerName, string? ReportingManagerName, bool IsOwner);

// ObjectiveSubtreeViewModel.cs — add 5 fields to the node record
public sealed record ObjectiveSubtreeNodeViewModel(
    Guid Id, Guid ProjectId, Guid? ParentObjectiveId, bool IsDefault, string Title, string? Description,
    Guid OwnerId, Guid? ReportingManagerId, Guid CreatedById, DateOnly StartDate, DateOnly EndDate,
    decimal Progress, decimal? ActualHours, decimal AllocatedHours, decimal CompletedHours,
    bool IsActive, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt,
    string? OwnerName, string? ReportingManagerName, bool IsOwner, bool IsAchieved, DateTimeOffset? AchievedAt,
    IReadOnlyList<ObjectiveSubtreeNodeViewModel> Children);
```

`ObjectiveViewModelMapper.ToViewModel()` overloads for both records: append the corresponding `dto.OwnerName, dto.ReportingManagerName, dto.IsOwner` (and `dto.IsAchieved, dto.AchievedAt` for the subtree node) to the existing argument lists — same values, already present on the Application DTO, just not currently forwarded.

Per the View Model Convention (§2.1.1.1), this is explicitly framed as a wire-format completion, not a wire-format change: the intended shape was already documented and already computed — this closes the gap between what's computed and what's sent, it does not introduce a new shape.

## Scope

- Two files: `ObjectiveDetailViewModel.cs`, `ObjectiveSubtreeViewModel.cs`.
- One file: `ObjectiveViewModelMapper.cs` (both `ToViewModel()` overloads for these two types).
- No migration, no new endpoint, no handler change.
- No other Objectives/Projects/ProjectMembers endpoint affected — `ObjectiveTreeItemViewModel` (the flat, project-wide `GetTree` list) is unaffected; it never claimed to carry these fields (see the frontend redesign spec, which uses the flat endpoint only to locate the tree root, not for display data).

## Testing

- `ONEVO.Tests.Unit`: extend existing `GetObjectiveByIdQueryHandlerTests` / any `GetObjectiveSubtree` handler tests (or add if none assert on the ViewModel mapping) to assert `OwnerName`/`ReportingManagerName`/`IsOwner`/`IsAchieved`/`AchievedAt` survive the `ToViewModel()` call.
- `ONEVO.Tests.Integration`: if an existing HTTP-level test hits `GET /work/objectives/{id}` or `.../tree`, extend its response assertions to include the new fields; otherwise a quick manual check against a seeded objective is sufficient given the mechanical nature of the fix.
