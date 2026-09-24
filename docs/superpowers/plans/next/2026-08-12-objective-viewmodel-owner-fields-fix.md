# Objective ViewModel Owner/Achieved Fields Fix Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `GET /api/v1/work/objectives/{id}` and `GET /api/v1/work/objectives/{id}/tree` actually return `ownerName`/`reportingManagerName`/`isOwner` (and, for the subtree endpoint, `isAchieved`/`achievedAt` on every node) — fields the Application layer already computes but the API ViewModel layer currently drops.

**Architecture:** Two `sealed record` ViewModels gain fields; one static mapper class forwards the already-present Application DTO values into them. No Application/Domain/Infrastructure/migration changes.

**Tech Stack:** ASP.NET Core, C# records, xUnit.

## Global Constraints

- Per `docs/superpowers/project_ core/ONEVO_Backend_Architecture_Document.md` §2.1.1.1: this is a wire-format *completion*, not a wire-format *change* — the shape was already documented (Postman docs, 2026-08-10) and already computed; only the forwarding was missing.
- No new migration, no new endpoint, no handler change.

---

### Task 1: Add the missing fields to both ViewModels and the mapper

**Files:**
- Modify: `src/ONEVO.Api/Contracts/WorkManagement/Objectives/ObjectiveDetailViewModel.cs`
- Modify: `src/ONEVO.Api/Contracts/WorkManagement/Objectives/ObjectiveSubtreeViewModel.cs`
- Modify: `src/ONEVO.Api/Contracts/WorkManagement/Objectives/ObjectiveViewModelMapper.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Objectives/ObjectiveViewModelMapperTests.cs` (new)

**Interfaces:**
- Consumes: `ObjectiveDetailResponse`, `ObjectiveSubtreeNodeResponse` (`src/ONEVO.Application/Features/WorkManagement/Objectives/DTOs/Responses/*.cs`) — both already carry `OwnerName`, `ReportingManagerName`, `IsOwner` (subtree node also `IsAchieved`, `AchievedAt`). Unchanged by this task.
- Produces: `ObjectiveDetailViewModel` now has `OwnerName`, `ReportingManagerName`, `IsOwner` as its last 3 positional parameters. `ObjectiveSubtreeNodeViewModel` now has `OwnerName`, `ReportingManagerName`, `IsOwner`, `IsAchieved`, `AchievedAt` inserted before `Children` (still the last parameter). These are consumed by the frontend's `2026-08-12-milestone-tree-mockup-redesign-design.md` plan.

- [ ] **Step 1: Write the failing unit test**

Create `tests/ONEVO.Tests.Unit/Features/WorkManagement/Objectives/ObjectiveViewModelMapperTests.cs`:

```csharp
using ONEVO.Api.Contracts.WorkManagement.Objectives;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Objectives;

public class ObjectiveViewModelMapperTests
{
    [Fact]
    public void ToViewModel_ObjectiveDetailResponse_ForwardsOwnerAndReportingManagerNamesAndIsOwner()
    {
        var response = new ObjectiveDetailResponse(
            Id: Guid.NewGuid(), ProjectId: Guid.NewGuid(), ParentObjectiveId: null, IsDefault: false,
            Title: "Design Phase", Description: null,
            OwnerId: Guid.NewGuid(), ReportingManagerId: Guid.NewGuid(), CreatedById: Guid.NewGuid(),
            StartDate: new DateOnly(2026, 1, 1), EndDate: new DateOnly(2026, 2, 1),
            Progress: 50m, ActualHours: null, AllocatedHours: 20m, CompletedHours: 10m,
            IsActive: true, IsAchieved: false, AchievedAt: null,
            CreatedAt: DateTimeOffset.UtcNow, UpdatedAt: null,
            OwnerName: "Arun Kumar", ReportingManagerName: "Diya Perera", IsOwner: true);

        var viewModel = response.ToViewModel();

        Assert.Equal("Arun Kumar", viewModel.OwnerName);
        Assert.Equal("Diya Perera", viewModel.ReportingManagerName);
        Assert.True(viewModel.IsOwner);
    }

    [Fact]
    public void ToViewModel_ObjectiveSubtreeNodeResponse_ForwardsOwnerNamesIsOwnerAndAchievedState()
    {
        var childResponse = new ObjectiveSubtreeNodeResponse(
            Id: Guid.NewGuid(), ProjectId: Guid.NewGuid(), ParentObjectiveId: Guid.NewGuid(), IsDefault: false,
            Title: "Child", Description: null,
            OwnerId: Guid.NewGuid(), ReportingManagerId: null, CreatedById: Guid.NewGuid(),
            StartDate: new DateOnly(2026, 1, 10), EndDate: new DateOnly(2026, 1, 20),
            Progress: 100m, ActualHours: null, AllocatedHours: 5m, CompletedHours: 5m,
            IsActive: true, CreatedAt: DateTimeOffset.UtcNow, UpdatedAt: null,
            OwnerName: "Thivaharan", ReportingManagerName: null, IsOwner: false,
            IsAchieved: true, AchievedAt: DateTimeOffset.UtcNow,
            Children: []);

        var childViewModel = childResponse.ToViewModel();

        Assert.Equal("Thivaharan", childViewModel.OwnerName);
        Assert.False(childViewModel.IsOwner);
        Assert.True(childViewModel.IsAchieved);
        Assert.NotNull(childViewModel.AchievedAt);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails to compile**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter ObjectiveViewModelMapperTests`
Expected: build error — `ObjectiveDetailViewModel`/`ObjectiveSubtreeNodeViewModel` positional records don't accept `OwnerName`/`ReportingManagerName`/`IsOwner`/`IsAchieved`/`AchievedAt` by name yet, or the mapper doesn't return them (compile failure, not a runtime assertion failure, since the current records are shorter).

- [ ] **Step 3: Add the fields to both ViewModel records**

`src/ONEVO.Api/Contracts/WorkManagement/Objectives/ObjectiveDetailViewModel.cs` — full new content:

```csharp
namespace ONEVO.Api.Contracts.WorkManagement.Objectives;

public sealed record ObjectiveDetailViewModel(
    Guid Id, Guid ProjectId, Guid? ParentObjectiveId, bool IsDefault, string Title, string? Description,
    Guid OwnerId, Guid? ReportingManagerId, Guid CreatedById, DateOnly StartDate, DateOnly EndDate,
    decimal Progress, decimal? ActualHours, decimal AllocatedHours, decimal CompletedHours,
    bool IsActive, bool IsAchieved, DateTimeOffset? AchievedAt, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt,
    string? OwnerName, string? ReportingManagerName, bool IsOwner);
```

`src/ONEVO.Api/Contracts/WorkManagement/Objectives/ObjectiveSubtreeViewModel.cs` — full new content:

```csharp
namespace ONEVO.Api.Contracts.WorkManagement.Objectives;

public sealed record ObjectiveSubtreeViewModel(ObjectiveDetailViewModel? ParentObjective, ObjectiveSubtreeNodeViewModel Objective);

public sealed record ObjectiveSubtreeNodeViewModel(
    Guid Id, Guid ProjectId, Guid? ParentObjectiveId, bool IsDefault, string Title, string? Description,
    Guid OwnerId, Guid? ReportingManagerId, Guid CreatedById, DateOnly StartDate, DateOnly EndDate,
    decimal Progress, decimal? ActualHours, decimal AllocatedHours, decimal CompletedHours,
    bool IsActive, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt,
    string? OwnerName, string? ReportingManagerName, bool IsOwner, bool IsAchieved, DateTimeOffset? AchievedAt,
    IReadOnlyList<ObjectiveSubtreeNodeViewModel> Children);
```

- [ ] **Step 4: Update the mapper to forward the new fields**

In `src/ONEVO.Api/Contracts/WorkManagement/Objectives/ObjectiveViewModelMapper.cs`, replace these two overloads:

```csharp
    public static ObjectiveDetailViewModel ToViewModel(this ObjectiveDetailResponse dto) => new(
        dto.Id, dto.ProjectId, dto.ParentObjectiveId, dto.IsDefault, dto.Title, dto.Description,
        dto.OwnerId, dto.ReportingManagerId, dto.CreatedById, dto.StartDate, dto.EndDate,
        dto.Progress, dto.ActualHours, dto.AllocatedHours, dto.CompletedHours,
        dto.IsActive, dto.IsAchieved, dto.AchievedAt, dto.CreatedAt, dto.UpdatedAt,
        dto.OwnerName, dto.ReportingManagerName, dto.IsOwner);
```

and:

```csharp
    public static ObjectiveSubtreeNodeViewModel ToViewModel(this ObjectiveSubtreeNodeResponse dto) => new(
        dto.Id, dto.ProjectId, dto.ParentObjectiveId, dto.IsDefault, dto.Title, dto.Description,
        dto.OwnerId, dto.ReportingManagerId, dto.CreatedById, dto.StartDate, dto.EndDate,
        dto.Progress, dto.ActualHours, dto.AllocatedHours, dto.CompletedHours,
        dto.IsActive, dto.CreatedAt, dto.UpdatedAt,
        dto.OwnerName, dto.ReportingManagerName, dto.IsOwner, dto.IsAchieved, dto.AchievedAt,
        dto.Children.Select(c => c.ToViewModel()).ToList());
```

Leave every other overload in the file untouched.

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter ObjectiveViewModelMapperTests`
Expected: PASS, both tests green.

- [ ] **Step 6: Run the full unit test suite to check for regressions**

Run: `dotnet test tests/ONEVO.Tests.Unit`
Expected: PASS — no other test constructs these two ViewModels positionally with the old (shorter) arity, since the two other call sites (`GetById`/`GetSubtree` controller actions) call `.ToViewModel()` on the DTO, not the record constructor directly.

- [ ] **Step 7: Run architecture tests**

Run: `dotnet test tests/ONEVO.Tests.Architecture`
Expected: PASS — this task adds fields to existing Contracts records; it does not touch dependency direction, tenant isolation, or RLS.

- [ ] **Step 8: Commit**

```bash
git add src/ONEVO.Api/Contracts/WorkManagement/Objectives/ObjectiveDetailViewModel.cs src/ONEVO.Api/Contracts/WorkManagement/Objectives/ObjectiveSubtreeViewModel.cs src/ONEVO.Api/Contracts/WorkManagement/Objectives/ObjectiveViewModelMapper.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/Objectives/ObjectiveViewModelMapperTests.cs
git commit -m "fix: forward ownerName/reportingManagerName/isOwner/isAchieved through Objective ViewModels"
```

---

## Self-Review Notes

- **Spec coverage:** The design's entire "Fix" section maps to Task 1 — both ViewModel records, the mapper, both directions (single detail + subtree node). Nothing else in the design requires backend code (the flat `GetTree` endpoint is explicitly unaffected).
- **Placeholder scan:** No TBD/TODO; all code blocks are complete and compilable given the existing surrounding types.
- **Type consistency:** Field order and names match the Application DTOs (`ObjectiveDetailResponse`, `ObjectiveSubtreeNodeResponse`) exactly, positionally, since these are C# records constructed positionally in the mapper.
