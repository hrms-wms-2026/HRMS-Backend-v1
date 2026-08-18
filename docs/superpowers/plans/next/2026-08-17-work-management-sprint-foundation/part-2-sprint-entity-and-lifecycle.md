# Work Management — Sprint Entity & Lifecycle (Part 2 of 4) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. **Depends on Part 1 being complete** (`part-1-task-status-governance.md`) — this plan uses `TaskStatusVisibilities`, the fixed `MoveTaskStatusCommandHandler`, and the live `Objective.CompletedHours` rollup.

**Goal:** The `Sprint` entity and its 5-state lifecycle (Future/Active/Complete/Incomplete/Achieved),
`WorkTask.SprintId`, the CRUD commands an Objective owner uses to run sprints, the date-driven
background job, and the new guard on Objective-Achieve requiring every Sprint to be Complete or
Achieved first.

**Architecture:** `Sprint` follows the same `BaseEntity` + repository + MediatR-command pattern as
every other Work Management entity. State transitions are either owner-triggered commands
(Create/Edit/Complete/Achieve) or a periodic `BackgroundService` (`SprintLifecycleJob`, mirroring the
existing `AgentCommandExpiryJob`) for the two date-driven transitions. "Freezing" an Achieved sprint's
tasks reuses the Sprint's own `Status` as the single source of truth — no new field on `WorkTask`.

**Tech Stack:** ASP.NET Core, EF Core (PostgreSQL), MediatR CQRS, FluentValidation, xUnit + Moq, `BackgroundService`.

**Spec:** `docs/superpowers/specs/next/2026-08-17-work-management-sprint-foundation-design.md`

## Global Constraints

- Work Management module only.
- `Sprint.ObjectiveId` and `Sprint.ProjectId` are both **required** (`Guid`, not `Guid?`) — do not
  make either nullable (spec explicitly departs from the old tentative nullable-objective design).
- `WorkTask.SprintId` is **nullable at the DB level**; only application-layer validation on new task
  creation requires it (spec, Data model section) — do not make the migration column non-nullable.
- Achieved is a `Status` value, not `IsDeleted = true` — see spec's explicit reasoning. Achieved
  sprints must remain visible to repository queries used by the owner's "all sprints" view and by the
  Objective-achieve gate.
- Every owner-only command follows the exact existing pattern: resolve caller's EmployeeId, compare
  to `objective.OwnerId`, `Result.Forbidden(...)` on mismatch.
- Multiple Sprints may be `Active` on the same Objective simultaneously (confirmed answer) — do not
  add a "only one Active sprint" uniqueness constraint.

---

### Task 1: `Sprint` entity, `SprintStatuses`, EF configuration, migration

**Files:**
- Create: `src/ONEVO.Domain/Features/WorkManagement/Sprints/Entities/Sprint.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Configurations/WorkManagement/SprintConfiguration.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/ApplicationDbContext.cs` (add `DbSet<Sprint> Sprints`)
- Create: migration via `dotnet ef migrations add AddSprints`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Sprints/SprintConfigurationTests.cs`

**Interfaces:**
- Produces: `Sprint : BaseEntity` with `ProjectId, ObjectiveId, Name, StartDate, EndDate, Status,
  CompletedAt, AchievedAt`; `public static class SprintStatuses { Future, Active, Complete, Incomplete, Achieved }`.

- [ ] **Step 1: Write the failing test**

```csharp
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Sprints;

public class SprintConfigurationTests
{
    [Fact]
    public void Sprint_DefaultsStatusToFuture()
    {
        var sprint = new Sprint
        {
            Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), ProjectId = Guid.NewGuid(), ObjectiveId = Guid.NewGuid(),
            Name = "Sprint 1", StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2026, 9, 14),
            CreatedAt = DateTimeOffset.UtcNow
        };

        Assert.Equal(SprintStatuses.Future, sprint.Status);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~SprintConfigurationTests`
Expected: FAIL to compile — `Sprint`/`SprintStatuses` don't exist.

- [ ] **Step 3: Write the entity**

```csharp
using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.WorkManagement.Sprints.Entities;

public static class SprintStatuses
{
    public const string Future = "future";
    public const string Active = "active";
    public const string Complete = "complete";
    public const string Incomplete = "incomplete";
    public const string Achieved = "achieved";
}

/// <summary>
/// A time-boxed iteration owned by one Objective. Achieved is a status value, not a use of
/// BaseEntity.IsDeleted - an Achieved sprint must stay visible to the owner's "all sprints" Backlog
/// view and to the Objective-achieve gate check (see AchieveObjectiveCommandHandler), both of which
/// would silently break under the standard !IsDeleted repository filter convention.
/// </summary>
public class Sprint : BaseEntity
{
    public Guid ProjectId { get; set; }
    public Guid ObjectiveId { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string Status { get; set; } = SprintStatuses.Future;
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? AchievedAt { get; set; }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~SprintConfigurationTests`
Expected: PASS.

- [ ] **Step 5: Write the EF configuration**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.WorkManagement;

public class SprintConfiguration : IEntityTypeConfiguration<Sprint>
{
    public void Configure(EntityTypeBuilder<Sprint> builder)
    {
        builder.ToTable("sprints");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Name).HasMaxLength(100).IsRequired();
        builder.Property(s => s.Status).HasMaxLength(20).IsRequired();

        builder.HasIndex(s => new { s.TenantId, s.ObjectiveId, s.Status })
            .HasDatabaseName("ix_sprints_tenant_id_objective_id_status");
    }
}
```

- [ ] **Step 6: Register the DbSet**

In `ApplicationDbContext.cs`, add near the other WorkManagement DbSets (alongside
`TaskStatuses`/`WorkTasks`): `public DbSet<Sprint> Sprints => Set<Sprint>();`. Add
`using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;` to this file's usings.

- [ ] **Step 7: Generate and apply the migration**

Run: `dotnet ef migrations add AddSprints --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api --output-dir Migrations`
Expected: generates `CreateTable("sprints", ...)` with all `Sprint` columns in snake_case plus the
standard `BaseEntity` columns (`tenant_id`, `created_at`, `updated_at`, `created_by_id`, `is_deleted`)
— confirm this matches how other WorkManagement entity tables were generated (check
`20260807...` or any earlier `CreateTable` migration for a WorkManagement entity as a shape reference).
Rename the migration file to `20260817000002_AddSprints.cs` for chronological ordering after Part 1's
`20260817000001_AddTaskStatusVisibility.cs`.

Run: `dotnet ef database update --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api`
Expected: succeeds.

- [ ] **Step 8: Commit**

```bash
git add src/ONEVO.Domain/Features/WorkManagement/Sprints/ src/ONEVO.Infrastructure/Persistence/Configurations/WorkManagement/SprintConfiguration.cs src/ONEVO.Infrastructure/Persistence/ApplicationDbContext.cs src/ONEVO.Infrastructure/Migrations/ tests/ONEVO.Tests.Unit/Features/WorkManagement/Sprints/
git commit -m "feat(work): add the Sprint entity and its 5-state status model"
```

---

### Task 2: `ISprintRepository` / `EfSprintRepository`

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/Sprints/RepositoryInterfaces/ISprintRepository.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfSprintRepository.cs`
- Modify: `src/ONEVO.Infrastructure/DependencyInjection.cs` (register the repository)

**Interfaces:**
- Produces:
  ```csharp
  Task AddAsync(Sprint sprint, CancellationToken ct = default);
  Task<Sprint?> GetByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default);
  Task<Sprint?> GetTrackedByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default);
  Task<IReadOnlyList<Sprint>> GetByObjectiveIdAsync(Guid tenantId, Guid objectiveId, CancellationToken ct = default);
  Task<IReadOnlyList<Sprint>> GetActiveByObjectiveIdAsync(Guid tenantId, Guid objectiveId, CancellationToken ct = default);
  Task<IReadOnlyList<Sprint>> GetByStatusAsync(string status, CancellationToken ct = default);
  void Update(Sprint sprint);
  ```
  — `GetByStatusAsync` is tenant-unscoped (used only by the background job, which sweeps all tenants).

- [ ] **Step 1: Write the interface**

```csharp
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;

namespace ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;

public interface ISprintRepository
{
    Task AddAsync(Sprint sprint, CancellationToken ct = default);
    Task<Sprint?> GetByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default);
    Task<Sprint?> GetTrackedByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Sprint>> GetByObjectiveIdAsync(Guid tenantId, Guid objectiveId, CancellationToken ct = default);

    /// <summary>Active sprints for one Objective - what non-owner members see in Backlog (spec permissions table).</summary>
    Task<IReadOnlyList<Sprint>> GetActiveByObjectiveIdAsync(Guid tenantId, Guid objectiveId, CancellationToken ct = default);

    /// <summary>Tenant-unscoped, for SprintLifecycleJob's periodic sweep across every tenant.</summary>
    Task<IReadOnlyList<Sprint>> GetByStatusAsync(string status, CancellationToken ct = default);

    void Update(Sprint sprint);
}
```

- [ ] **Step 2: Write the implementation**

```csharp
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;

public class EfSprintRepository : ISprintRepository
{
    private readonly ApplicationDbContext _db;

    public EfSprintRepository(ApplicationDbContext db) => _db = db;

    public async Task AddAsync(Sprint sprint, CancellationToken ct = default)
        => await _db.Sprints.AddAsync(sprint, ct);

    public async Task<Sprint?> GetByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default)
        => await _db.Sprints.AsNoTracking().FirstOrDefaultAsync(s => s.TenantId == tenantId && s.Id == id, ct);

    public async Task<Sprint?> GetTrackedByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default)
        => await _db.Sprints.FirstOrDefaultAsync(s => s.TenantId == tenantId && s.Id == id, ct);

    public async Task<IReadOnlyList<Sprint>> GetByObjectiveIdAsync(Guid tenantId, Guid objectiveId, CancellationToken ct = default)
        => await _db.Sprints.AsNoTracking().Where(s => s.TenantId == tenantId && s.ObjectiveId == objectiveId).ToListAsync(ct);

    public async Task<IReadOnlyList<Sprint>> GetActiveByObjectiveIdAsync(Guid tenantId, Guid objectiveId, CancellationToken ct = default)
        => await _db.Sprints.AsNoTracking()
            .Where(s => s.TenantId == tenantId && s.ObjectiveId == objectiveId && s.Status == SprintStatuses.Active)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Sprint>> GetByStatusAsync(string status, CancellationToken ct = default)
        => await _db.Sprints.Where(s => s.Status == status).ToListAsync(ct);

    public void Update(Sprint sprint) => _db.Sprints.Update(sprint);
}
```

Add `using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;` for the `SprintStatuses` reference.

- [ ] **Step 3: Register in DI**

In `src/ONEVO.Infrastructure/DependencyInjection.cs`, find where sibling WorkManagement repositories
are registered (e.g. `services.AddScoped<IObjectiveRepository, EfObjectiveRepository>();`) and add:

```csharp
        services.AddScoped<ISprintRepository, EfSprintRepository>();
```

- [ ] **Step 4: Build to verify no errors**

Run: `dotnet build src/ONEVO.Api`
Expected: succeeds (no tests yet — this repository is exercised by the commands in the next tasks,
which mock the interface directly, matching this codebase's established convention of not
unit-testing EF repository implementations directly).

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Sprints/RepositoryInterfaces/ src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfSprintRepository.cs src/ONEVO.Infrastructure/DependencyInjection.cs
git commit -m "feat(work): ISprintRepository/EfSprintRepository"
```

---

### Task 3: `CreateSprintCommand`

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/Sprints/DTOs/Responses/SprintResponse.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Sprints/Commands/CreateSprint/CreateSprintCommand.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Sprints/Commands/CreateSprint/CreateSprintCommandHandler.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Sprints/Commands/CreateSprint/CreateSprintCommandValidator.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Sprints/CreateSprintCommandHandlerTests.cs`

**Interfaces:**
- Produces: `SprintResponse(Guid Id, Guid ObjectiveId, string Name, DateOnly StartDate, DateOnly EndDate, string Status, DateTimeOffset? CompletedAt, DateTimeOffset? AchievedAt)`;
  `CreateSprintCommand(Guid ObjectiveId, string Name, DateOnly StartDate, DateOnly EndDate) : IRequest<Result<SprintResponse>>`.

- [ ] **Step 1: Write the failing test**

```csharp
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.Commands.CreateSprint;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Sprints;

public class CreateSprintCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid OwnerEmployeeId = Guid.NewGuid();
    private static readonly Guid OtherEmployeeId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();

    private (CreateSprintCommandHandler Handler, Mock<ISprintRepository> Sprints) Build(Guid callerEmployeeId)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(callerEmployeeId);

        var objective = new Objective { Id = ObjectiveId, TenantId = TenantId, ProjectId = ProjectId, OwnerId = OwnerEmployeeId, IsActive = true, Title = "Obj", CreatedAt = DateTimeOffset.UtcNow };
        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>())).ReturnsAsync(objective);

        var sprints = new Mock<ISprintRepository>();

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result<SprintResponse>>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result<SprintResponse>>> op, CancellationToken ct) => op(ct));
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new CreateSprintCommandHandler(currentUser.Object, identity.Object, objectives.Object, sprints.Object, unitOfWork.Object);
        return (handler, sprints);
    }

    [Fact]
    public async Task Handle_StartDateInFuture_CreatesWithFutureStatus()
    {
        var (handler, sprints) = Build(OwnerEmployeeId);
        var command = new CreateSprintCommand(ObjectiveId, "Sprint 1", DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)), DateOnly.FromDateTime(DateTime.UtcNow.AddDays(21)));

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(SprintStatuses.Future, result.Value!.Status);
        sprints.Verify(x => x.AddAsync(It.Is<Sprint>(s => s.Status == SprintStatuses.Future), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_StartDateTodayOrEarlier_CreatesWithActiveStatus()
    {
        var (handler, sprints) = Build(OwnerEmployeeId);
        var command = new CreateSprintCommand(ObjectiveId, "Sprint 1", DateOnly.FromDateTime(DateTime.UtcNow), DateOnly.FromDateTime(DateTime.UtcNow.AddDays(14)));

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(SprintStatuses.Active, result.Value!.Status);
    }

    [Fact]
    public async Task Handle_EndDateBeforeStartDate_ReturnsFailure()
    {
        var (handler, sprints) = Build(OwnerEmployeeId);
        var command = new CreateSprintCommand(ObjectiveId, "Sprint 1", DateOnly.FromDateTime(DateTime.UtcNow.AddDays(14)), DateOnly.FromDateTime(DateTime.UtcNow));

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        sprints.Verify(x => x.AddAsync(It.IsAny<Sprint>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_NotOwner_ReturnsForbidden()
    {
        var (handler, sprints) = Build(OtherEmployeeId);
        var command = new CreateSprintCommand(ObjectiveId, "Sprint 1", DateOnly.FromDateTime(DateTime.UtcNow), DateOnly.FromDateTime(DateTime.UtcNow.AddDays(14)));

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }
}
```

Note: the "EndDate before StartDate" check happens in the **handler**, not only the validator, because
it needs no cross-field FluentValidation complexity for this simple case — Step 4 below adds it as a
`RuleFor` with `.Must` referencing both fields, so it's actually validator-level; the test above still
passes either way since `Handle` returns the validator-driven failure through the normal MediatR
pipeline in production, but this unit test calls `Handle` directly (bypassing `ValidationBehavior`), so
the handler itself must also defensively reject it — implement the check in **both** places (Step 4 and
Step 5) to keep this direct-handler-call test meaningful.

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~CreateSprintCommandHandlerTests`
Expected: FAIL to compile.

- [ ] **Step 3: Write `SprintResponse`**

```csharp
namespace ONEVO.Application.Features.WorkManagement.Sprints.DTOs.Responses;

public sealed record SprintResponse(
    Guid Id, Guid ObjectiveId, string Name, DateOnly StartDate, DateOnly EndDate, string Status,
    DateTimeOffset? CompletedAt, DateTimeOffset? AchievedAt);
```

- [ ] **Step 4: Write the command and validator**

```csharp
// CreateSprintCommand.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Sprints.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Sprints.Commands.CreateSprint;

public sealed record CreateSprintCommand(Guid ObjectiveId, string Name, DateOnly StartDate, DateOnly EndDate) : IRequest<Result<SprintResponse>>;
```

```csharp
// CreateSprintCommandValidator.cs
using FluentValidation;

namespace ONEVO.Application.Features.WorkManagement.Sprints.Commands.CreateSprint;

public class CreateSprintCommandValidator : AbstractValidator<CreateSprintCommand>
{
    public CreateSprintCommandValidator()
    {
        RuleFor(x => x.ObjectiveId).NotEqual(Guid.Empty).WithMessage("Objective is required.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100).WithMessage("Name is required and must be 100 characters or fewer.");
        RuleFor(x => x).Must(x => x.EndDate >= x.StartDate).WithMessage("End date must not be before start date.");
    }
}
```

- [ ] **Step 5: Write the handler**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;

namespace ONEVO.Application.Features.WorkManagement.Sprints.Commands.CreateSprint;

public class CreateSprintCommandHandler : IRequestHandler<CreateSprintCommand, Result<SprintResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IObjectiveRepository _objectives;
    private readonly ISprintRepository _sprints;
    private readonly IUnitOfWork _unitOfWork;

    public CreateSprintCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IObjectiveRepository objectives,
        ISprintRepository sprints, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _identity = identity;
        _objectives = objectives;
        _sprints = sprints;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<SprintResponse>> Handle(CreateSprintCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<SprintResponse>.Forbidden("Authentication required.");

        if (request.EndDate < request.StartDate)
            return Result<SprintResponse>.Failure("End date must not be before start date.");

        var tenantId = _currentUser.TenantId;
        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, _currentUser.UserId, ct);
        if (callerEmployeeId is null)
            return Result<SprintResponse>.Forbidden("No employee record for the current user.");

        var objective = await _objectives.GetByIdForTenantAsync(tenantId, request.ObjectiveId, ct);
        if (objective is null || !objective.IsActive)
            return Result<SprintResponse>.NotFound("Objective not found.");

        if (objective.OwnerId != callerEmployeeId.Value)
            return Result<SprintResponse>.Forbidden("Only this milestone's owner can create sprints.");

        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var now = DateTimeOffset.UtcNow;
            var today = DateOnly.FromDateTime(now.UtcDateTime);
            var initialStatus = request.StartDate <= today ? SprintStatuses.Active : SprintStatuses.Future;

            var sprint = new Sprint
            {
                Id = Guid.NewGuid(), TenantId = tenantId, ProjectId = objective.ProjectId, ObjectiveId = objective.Id,
                Name = request.Name.Trim(), StartDate = request.StartDate, EndDate = request.EndDate,
                Status = initialStatus, CreatedById = _currentUser.UserId, CreatedAt = now
            };

            await _sprints.AddAsync(sprint, innerCt);
            await _unitOfWork.SaveChangesAsync(innerCt);

            return Result<SprintResponse>.Success(new SprintResponse(
                sprint.Id, sprint.ObjectiveId, sprint.Name, sprint.StartDate, sprint.EndDate, sprint.Status,
                sprint.CompletedAt, sprint.AchievedAt));
        }, ct);
    }
}
```

- [ ] **Step 6: Run to verify all pass**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~CreateSprintCommandHandlerTests`
Expected: PASS (all 4 tests).

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Sprints/DTOs/ src/ONEVO.Application/Features/WorkManagement/Sprints/Commands/CreateSprint/ tests/ONEVO.Tests.Unit/Features/WorkManagement/Sprints/CreateSprintCommandHandlerTests.cs
git commit -m "feat(work): CreateSprintCommand"
```

---

### Task 4: `EditSprintCommand`

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/Sprints/Commands/EditSprint/EditSprintCommand.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Sprints/Commands/EditSprint/EditSprintCommandHandler.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Sprints/Commands/EditSprint/EditSprintCommandValidator.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Sprints/EditSprintCommandHandlerTests.cs`

**Interfaces:**
- Produces: `EditSprintCommand(Guid SprintId, string Name, DateOnly StartDate, DateOnly EndDate) : IRequest<Result<SprintResponse>>`.

- [ ] **Step 1: Write the failing test**

```csharp
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.Commands.EditSprint;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Sprints;

public class EditSprintCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid OwnerEmployeeId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();
    private static readonly Guid SprintId = Guid.NewGuid();

    private (EditSprintCommandHandler Handler, Sprint Sprint) Build(string sprintStatus)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OwnerEmployeeId);

        var sprint = new Sprint { Id = SprintId, TenantId = TenantId, ObjectiveId = ObjectiveId, Name = "Old", StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2026, 9, 14), Status = sprintStatus, CreatedAt = DateTimeOffset.UtcNow };
        var sprints = new Mock<ISprintRepository>();
        sprints.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, SprintId, It.IsAny<CancellationToken>())).ReturnsAsync(sprint);

        var objective = new Objective { Id = ObjectiveId, TenantId = TenantId, OwnerId = OwnerEmployeeId, IsActive = true, Title = "Obj", CreatedAt = DateTimeOffset.UtcNow };
        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>())).ReturnsAsync(objective);

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result<SprintResponse>>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result<SprintResponse>>> op, CancellationToken ct) => op(ct));
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new EditSprintCommandHandler(currentUser.Object, identity.Object, objectives.Object, sprints.Object, unitOfWork.Object);
        return (handler, sprint);
    }

    [Fact]
    public async Task Handle_ActiveSprint_UpdatesFields()
    {
        var (handler, sprint) = Build(SprintStatuses.Active);
        var command = new EditSprintCommand(SprintId, "New Name", new DateOnly(2026, 9, 2), new DateOnly(2026, 9, 16));

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("New Name", sprint.Name);
        Assert.Equal(new DateOnly(2026, 9, 16), sprint.EndDate);
    }

    [Theory]
    [InlineData(SprintStatuses.Complete)]
    [InlineData(SprintStatuses.Achieved)]
    public async Task Handle_TerminalSprint_ReturnsConflict(string status)
    {
        var (handler, sprint) = Build(status);
        var command = new EditSprintCommand(SprintId, "New Name", sprint.StartDate, sprint.EndDate);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        Assert.Equal("Old", sprint.Name);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~EditSprintCommandHandlerTests`
Expected: FAIL to compile.

- [ ] **Step 3: Write the command, validator, handler**

```csharp
// EditSprintCommand.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Sprints.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Sprints.Commands.EditSprint;

public sealed record EditSprintCommand(Guid SprintId, string Name, DateOnly StartDate, DateOnly EndDate) : IRequest<Result<SprintResponse>>;
```

```csharp
// EditSprintCommandValidator.cs
using FluentValidation;

namespace ONEVO.Application.Features.WorkManagement.Sprints.Commands.EditSprint;

public class EditSprintCommandValidator : AbstractValidator<EditSprintCommand>
{
    public EditSprintCommandValidator()
    {
        RuleFor(x => x.SprintId).NotEqual(Guid.Empty);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x).Must(x => x.EndDate >= x.StartDate).WithMessage("End date must not be before start date.");
    }
}
```

```csharp
// EditSprintCommandHandler.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;

namespace ONEVO.Application.Features.WorkManagement.Sprints.Commands.EditSprint;

public class EditSprintCommandHandler : IRequestHandler<EditSprintCommand, Result<SprintResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IObjectiveRepository _objectives;
    private readonly ISprintRepository _sprints;
    private readonly IUnitOfWork _unitOfWork;

    public EditSprintCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IObjectiveRepository objectives,
        ISprintRepository sprints, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _identity = identity;
        _objectives = objectives;
        _sprints = sprints;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<SprintResponse>> Handle(EditSprintCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<SprintResponse>.Forbidden("Authentication required.");

        if (request.EndDate < request.StartDate)
            return Result<SprintResponse>.Failure("End date must not be before start date.");

        var tenantId = _currentUser.TenantId;
        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, _currentUser.UserId, ct);
        if (callerEmployeeId is null)
            return Result<SprintResponse>.Forbidden("No employee record for the current user.");

        var sprint = await _sprints.GetTrackedByIdForTenantAsync(tenantId, request.SprintId, ct);
        if (sprint is null)
            return Result<SprintResponse>.NotFound("Sprint not found.");

        var objective = await _objectives.GetByIdForTenantAsync(tenantId, sprint.ObjectiveId, ct);
        if (objective is null)
            return Result<SprintResponse>.NotFound("Objective not found.");

        if (objective.OwnerId != callerEmployeeId.Value)
            return Result<SprintResponse>.Forbidden("Only this milestone's owner can edit sprints.");

        if (sprint.Status is SprintStatuses.Complete or SprintStatuses.Achieved)
            return Result<SprintResponse>.Conflict("This sprint has already ended and can no longer be edited.");

        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            sprint.Name = request.Name.Trim();
            sprint.StartDate = request.StartDate;
            sprint.EndDate = request.EndDate;
            sprint.UpdatedAt = DateTimeOffset.UtcNow;

            await _unitOfWork.SaveChangesAsync(innerCt);

            return Result<SprintResponse>.Success(new SprintResponse(
                sprint.Id, sprint.ObjectiveId, sprint.Name, sprint.StartDate, sprint.EndDate, sprint.Status,
                sprint.CompletedAt, sprint.AchievedAt));
        }, ct);
    }
}
```

- [ ] **Step 4: Run to verify all pass**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~EditSprintCommandHandlerTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Sprints/Commands/EditSprint/ tests/ONEVO.Tests.Unit/Features/WorkManagement/Sprints/EditSprintCommandHandlerTests.cs
git commit -m "feat(work): EditSprintCommand"
```

---

### Task 5: `WorkTask.SprintId` + required-on-create validation

**Files:**
- Modify: `src/ONEVO.Domain/Features/WorkManagement/Tasks/Entities/WorkTask.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Configurations/WorkManagement/WorkTaskConfiguration.cs`
- Modify: `src/ONEVO.Api/Contracts/WorkManagement/Tasks/TaskContracts.cs` (`CreateTaskRequest`, `WorkTaskViewModel`)
- Modify: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/CreateTask/CreateTaskCommand.cs`,
  `CreateTaskCommandHandler.cs`, `CreateTaskCommandValidator.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Tasks/DTOs/Responses/WorkTaskResponse.cs`
- Modify: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/TasksController.cs` (`Create` action)
- Create: migration via `dotnet ef migrations add AddWorkTaskSprintId`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/CreateTaskCommandHandlerTests.cs`

**Interfaces:**
- Produces: `WorkTask.SprintId` (`Guid?`); `CreateTaskCommand` gains a required `Guid SprintId`
  parameter (not nullable in the command itself — the *column* is nullable for migration safety per
  the spec, but every new task creation must supply one).

- [ ] **Step 1: Write the failing test**

Read `CreateTaskCommandHandlerTests.cs` first to match its existing fixture style (it likely already
mocks `IObjectiveRepository`/`IProjectRepository`/`ITaskStatusRepository`/`IObjectiveAllocationSlackCalculator`
per `CreateTaskCommandHandler`'s real constructor — reuse that exact setup), then add a `SprintId` to
the command construction in every existing passing test (compile will fail otherwise once the command
gains a required parameter), and add:

```csharp
    [Fact]
    public async Task Handle_ValidSprintId_SetsSprintIdOnTask()
    {
        // Arrange via this file's existing Build(...)-style helper and owner/status/slack fixtures.
        var sprintId = Guid.NewGuid();
        var command = new CreateTaskCommand(ObjectiveId, "Title", null, WorkTaskTypes.Task, WorkTaskPriorities.Medium, null, null, null, sprintId);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        tasks.Verify(x => x.AddAsync(It.Is<WorkTask>(t => t.SprintId == sprintId), It.IsAny<CancellationToken>()), Times.Once);
    }
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~CreateTaskCommandHandlerTests`
Expected: FAIL to compile — `CreateTaskCommand` doesn't accept a `SprintId` argument yet.

- [ ] **Step 3: Add the entity field and EF configuration**

In `WorkTask.cs`, add: `public Guid? SprintId { get; set; }`

In `WorkTaskConfiguration.cs`, add (after the existing `HasOne<TaskStatusEntity>` line):
```csharp
        builder.HasOne<Sprint>().WithMany().HasForeignKey(t => t.SprintId).OnDelete(DeleteBehavior.Restrict);
```
Add `using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;` to this file's usings.

- [ ] **Step 4: Generate and apply the migration**

Run: `dotnet ef migrations add AddWorkTaskSprintId --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api --output-dir Migrations`
Expected: `AddColumn<Guid>(name: "sprint_id", table: "tasks", nullable: true)` plus a `CreateIndex` on
`sprint_id` and a foreign key constraint to `sprints`. Rename to `20260817000003_AddWorkTaskSprintId.cs`
for ordering after Task 1's migration in this part.

Run: `dotnet ef database update --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api`

- [ ] **Step 5: Extend `CreateTaskCommand`, its validator, and the handler**

In `CreateTaskCommand.cs`, add `Guid SprintId` as the last parameter:
```csharp
public sealed record CreateTaskCommand(
    Guid ObjectiveId, string Title, string? Description, string TaskType, string Priority,
    DateOnly? DueDate, decimal? EstimatedHours, int? StoryPoints, Guid SprintId
) : IRequest<Result<WorkTaskResponse>>;
```

In `CreateTaskCommandValidator.cs`, add: `RuleFor(x => x.SprintId).NotEqual(Guid.Empty).WithMessage("Sprint is required.");`

In `CreateTaskCommandHandler.cs`, add a `SprintId = request.SprintId,` line to the `WorkTask` object
construction inside the transaction block (alongside the existing `ObjectiveId = objective.Id,` etc.).
Also add a check just before that transaction block that the sprint exists, belongs to this objective,
and is not `Achieved` (mirrors the not-yet-written freeze check from Task 7 — implement it here too,
since a brand-new task should never be creatable directly into an already-Achieved/frozen sprint):

```csharp
        var sprint = await _sprints.GetByIdForTenantAsync(tenantId, request.SprintId, ct);
        if (sprint is null || sprint.ObjectiveId != objective.Id)
            return Result<WorkTaskResponse>.NotFound("Sprint not found.");
        if (sprint.Status == SprintStatuses.Achieved)
            return Result<WorkTaskResponse>.Conflict("This sprint has been achieved and is frozen.");
```

This requires adding an `ISprintRepository _sprints` field + constructor parameter to
`CreateTaskCommandHandler`. Add `using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;`
and `using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;` to this file's usings.

- [ ] **Step 6: Extend `WorkTaskResponse` and the API contract/viewmodel**

In `WorkTaskResponse.cs`'s `WorkTaskResponse` record (not `InsufficientAllocationResponse`), add
`Guid? SprintId` as the last parameter.

**`WorkTaskResponse` is constructed positionally in four places — all four must be updated together
or the build breaks** (confirmed via `grep -rln "new WorkTaskResponse(" src/ONEVO.Application`):
- `CreateTaskCommandHandler.cs` — add `task.SprintId` to its construction (this is the one this task
  is primarily about).
- `EditTaskCommandHandler.cs` — add `task.SprintId` to its construction (the task being edited already
  has a `SprintId`; edit doesn't change it, just needs to pass it through).
- `Commands/ApproveTaskCreationRequest/ApproveTaskCreationRequestCommandHandler.cs` — this handler
  creates a new `WorkTask` when a request is approved; check whether it needs its own `SprintId`
  resolution (the original `TaskCreationRequestPayload` doesn't carry one today — see the note below)
  or can reuse a value already available on the request/objective context, then pass it through the
  same way.
- `Queries/GetObjectiveTasks/GetObjectiveTasksQueryHandler.cs` — add `t.SprintId` (or equivalent) to
  its construction, reading it off the already-fetched `WorkTask` entity.

**Note on `ApproveTaskCreationRequestCommandHandler`:** `TaskCreationRequestPayload` (built by
`CreateTaskCreationRequestCommandHandler`, Part unrelated to this plan) doesn't currently carry a
`SprintId` field, meaning task-creation-*requests* (the non-owner request-to-create flow, distinct
from the owner's direct create) have no way to specify a sprint today. Since Sprint is now required
on every task, add `SprintId` to `TaskCreationRequestPayload` and to
`CreateTaskCreationRequestCommand`/`CreateTaskCreationRequestCommandHandler`/
`CreateTaskCreationRequestCommandValidator`/`CreateTaskCreationRequestRequest` (the API contract) the
same way `CreateTaskCommand` gained it in this task's earlier steps — this is a parallel, same-shaped
change to a sibling command that this task's scope must cover for the feature to be internally
consistent (a non-owner couldn't otherwise ever create a valid task-creation request). Write this as
its own TDD cycle (failing test asserting the payload round-trips `SprintId`, then implement) before
moving to Step 7.

In `TaskContracts.cs`: add `Guid SprintId` to `CreateTaskRequest`, add `Guid SprintId` to
`CreateTaskCreationRequestRequest`, and add `Guid? SprintId` to `WorkTaskViewModel`. Update
`TaskViewModelMapper`'s (or wherever `ToViewModel()` for `WorkTaskResponse` lives — search
`grep -rn "WorkTaskResponse.*ToViewModel\|static.*ToViewModel.*WorkTaskResponse" src/ONEVO.Api`)
mapping to pass `.SprintId` through. Update `TasksController.cs`'s `CreateRequest` action to pass
`request.SprintId` into the `CreateTaskCreationRequestCommand` construction.

In `TasksController.cs`'s `Create` action, update the command construction:
```csharp
        var result = await _mediator.Send(new CreateTaskCommand(
            objectiveId, request.Title, request.Description, request.TaskType, request.Priority,
            request.DueDate, request.EstimatedHours, request.StoryPoints, request.SprintId), ct);
```

- [ ] **Step 7: Run to verify all pass, then full build**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~CreateTaskCommandHandlerTests`
then `dotnet build src/ONEVO.Api` to catch any other construction site needing the new argument.
Expected: PASS, build succeeds.

- [ ] **Step 8: Commit**

```bash
git add src/ONEVO.Domain/Features/WorkManagement/Tasks/Entities/WorkTask.cs src/ONEVO.Infrastructure/Persistence/Configurations/WorkManagement/WorkTaskConfiguration.cs src/ONEVO.Infrastructure/Migrations/ src/ONEVO.Api/Contracts/WorkManagement/Tasks/TaskContracts.cs src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/CreateTask/ src/ONEVO.Application/Features/WorkManagement/Tasks/DTOs/Responses/WorkTaskResponse.cs src/ONEVO.Api/Controllers/Tenant/WorkManagement/TasksController.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/CreateTaskCommandHandlerTests.cs
git commit -m "feat(work): tasks now require a Sprint on creation (WorkTask.SprintId)"
```

---

### Task 6: `CompleteSprintCommand`

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/Sprints/Commands/CompleteSprint/CompleteSprintCommand.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Sprints/Commands/CompleteSprint/CompleteSprintCommandHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Sprints/CompleteSprintCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `IWorkTaskRepository.GetBySprintIdAsync` (new — add alongside `GetByObjectiveIdAsync`),
  `ITaskStatusRepository.GetByIdForTenantAsync` (existing, looped per distinct status id — same
  accepted convention as `ICallerIdentityResolver.ResolveDisplayNamesByEmployeeIdAsync`'s doc comment
  already documents for this codebase).
- Produces: `CompleteSprintCommand(Guid SprintId) : IRequest<Result<SprintResponse>>`.

- [ ] **Step 1: Add `IWorkTaskRepository.GetBySprintIdAsync`**

In `IWorkTaskRepository.cs`, add:
```csharp
    Task<IReadOnlyList<WorkTask>> GetBySprintIdAsync(Guid tenantId, Guid sprintId, CancellationToken ct = default);
```
In `EfWorkTaskRepository.cs` (read it first to match its existing filter style, same note as Part 1
Task 5), add:
```csharp
    public async Task<IReadOnlyList<WorkTask>> GetBySprintIdAsync(Guid tenantId, Guid sprintId, CancellationToken ct = default)
        => await _db.WorkTasks.AsNoTracking().Where(t => t.TenantId == tenantId && t.SprintId == sprintId && !t.IsDeleted).ToListAsync(ct);
```

- [ ] **Step 2: Write the failing test**

```csharp
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.Commands.CompleteSprint;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using TaskStatusEntity = ONEVO.Domain.Features.WorkManagement.Tasks.Entities.TaskStatus;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Sprints;

public class CompleteSprintCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid OwnerEmployeeId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();
    private static readonly Guid SprintId = Guid.NewGuid();
    private static readonly Guid DoneStatusId = Guid.NewGuid();
    private static readonly Guid InProcessStatusId = Guid.NewGuid();

    private (CompleteSprintCommandHandler Handler, Sprint Sprint) Build(IReadOnlyList<WorkTask> tasksInSprint)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync(OwnerEmployeeId);

        var sprint = new Sprint { Id = SprintId, TenantId = TenantId, ObjectiveId = ObjectiveId, Name = "S1", StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2026, 9, 14), Status = SprintStatuses.Active, CreatedAt = DateTimeOffset.UtcNow };
        var sprints = new Mock<ISprintRepository>();
        sprints.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, SprintId, It.IsAny<CancellationToken>())).ReturnsAsync(sprint);

        var objective = new Objective { Id = ObjectiveId, TenantId = TenantId, OwnerId = OwnerEmployeeId, IsActive = true, Title = "Obj", CreatedAt = DateTimeOffset.UtcNow };
        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>())).ReturnsAsync(objective);

        var tasks = new Mock<IWorkTaskRepository>();
        tasks.Setup(x => x.GetBySprintIdAsync(TenantId, SprintId, It.IsAny<CancellationToken>())).ReturnsAsync(tasksInSprint);

        var doneStatus = new TaskStatusEntity { Id = DoneStatusId, TenantId = TenantId, Name = "Done", MarksTaskComplete = true, CreatedAt = DateTimeOffset.UtcNow };
        var inProcessStatus = new TaskStatusEntity { Id = InProcessStatusId, TenantId = TenantId, Name = "In Process", MarksTaskComplete = false, CreatedAt = DateTimeOffset.UtcNow };
        var statuses = new Mock<ITaskStatusRepository>();
        statuses.Setup(x => x.GetByIdForTenantAsync(TenantId, DoneStatusId, It.IsAny<CancellationToken>())).ReturnsAsync(doneStatus);
        statuses.Setup(x => x.GetByIdForTenantAsync(TenantId, InProcessStatusId, It.IsAny<CancellationToken>())).ReturnsAsync(inProcessStatus);

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result<SprintResponse>>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result<SprintResponse>>> op, CancellationToken ct) => op(ct));
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new CompleteSprintCommandHandler(currentUser.Object, identity.Object, objectives.Object, sprints.Object, tasks.Object, statuses.Object, unitOfWork.Object);
        return (handler, sprint);
    }

    [Fact]
    public async Task Handle_AllTasksComplete_MarksSprintComplete()
    {
        var tasksInSprint = new List<WorkTask> { new() { Id = Guid.NewGuid(), TenantId = TenantId, StatusId = DoneStatusId, Title = "A", ShortId = "T-1", CreatedAt = DateTimeOffset.UtcNow } };
        var (handler, sprint) = Build(tasksInSprint);

        var result = await handler.Handle(new CompleteSprintCommand(SprintId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(SprintStatuses.Complete, sprint.Status);
        Assert.NotNull(sprint.CompletedAt);
    }

    [Fact]
    public async Task Handle_SomeTaskNotComplete_ReturnsFailure()
    {
        var tasksInSprint = new List<WorkTask>
        {
            new() { Id = Guid.NewGuid(), TenantId = TenantId, StatusId = DoneStatusId, Title = "A", ShortId = "T-1", CreatedAt = DateTimeOffset.UtcNow },
            new() { Id = Guid.NewGuid(), TenantId = TenantId, StatusId = InProcessStatusId, Title = "B", ShortId = "T-2", CreatedAt = DateTimeOffset.UtcNow }
        };
        var (handler, sprint) = Build(tasksInSprint);

        var result = await handler.Handle(new CompleteSprintCommand(SprintId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(422, result.StatusCode);
        Assert.Equal(SprintStatuses.Active, sprint.Status);
    }
}
```

- [ ] **Step 3: Run to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~CompleteSprintCommandHandlerTests`
Expected: FAIL to compile.

- [ ] **Step 4: Write the command and handler**

```csharp
// CompleteSprintCommand.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Sprints.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Sprints.Commands.CompleteSprint;

public sealed record CompleteSprintCommand(Guid SprintId) : IRequest<Result<SprintResponse>>;
```

```csharp
// CompleteSprintCommandHandler.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;

namespace ONEVO.Application.Features.WorkManagement.Sprints.Commands.CompleteSprint;

public class CompleteSprintCommandHandler : IRequestHandler<CompleteSprintCommand, Result<SprintResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IObjectiveRepository _objectives;
    private readonly ISprintRepository _sprints;
    private readonly IWorkTaskRepository _tasks;
    private readonly ITaskStatusRepository _statuses;
    private readonly IUnitOfWork _unitOfWork;

    public CompleteSprintCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IObjectiveRepository objectives,
        ISprintRepository sprints, IWorkTaskRepository tasks, ITaskStatusRepository statuses, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _identity = identity;
        _objectives = objectives;
        _sprints = sprints;
        _tasks = tasks;
        _statuses = statuses;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<SprintResponse>> Handle(CompleteSprintCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<SprintResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, _currentUser.UserId, ct);
        if (callerEmployeeId is null)
            return Result<SprintResponse>.Forbidden("No employee record for the current user.");

        var sprint = await _sprints.GetTrackedByIdForTenantAsync(tenantId, request.SprintId, ct);
        if (sprint is null)
            return Result<SprintResponse>.NotFound("Sprint not found.");

        var objective = await _objectives.GetByIdForTenantAsync(tenantId, sprint.ObjectiveId, ct);
        if (objective is null)
            return Result<SprintResponse>.NotFound("Objective not found.");

        if (objective.OwnerId != callerEmployeeId.Value)
            return Result<SprintResponse>.Forbidden("Only this milestone's owner can complete sprints.");

        var tasks = await _tasks.GetBySprintIdAsync(tenantId, sprint.Id, ct);
        var distinctStatusIds = tasks.Select(t => t.StatusId).Distinct().ToList();
        foreach (var statusId in distinctStatusIds)
        {
            var status = await _statuses.GetByIdForTenantAsync(tenantId, statusId, ct);
            if (status is null || !status.MarksTaskComplete)
                return Result<SprintResponse>.Failure("Every task in this sprint must be in a complete status before it can be marked Complete.", 422);
        }

        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            sprint.Status = SprintStatuses.Complete;
            sprint.CompletedAt = DateTimeOffset.UtcNow;
            sprint.UpdatedAt = DateTimeOffset.UtcNow;

            await _unitOfWork.SaveChangesAsync(innerCt);

            return Result<SprintResponse>.Success(new SprintResponse(
                sprint.Id, sprint.ObjectiveId, sprint.Name, sprint.StartDate, sprint.EndDate, sprint.Status,
                sprint.CompletedAt, sprint.AchievedAt));
        }, ct);
    }
}
```

- [ ] **Step 5: Run to verify all pass**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~CompleteSprintCommandHandlerTests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Tasks/RepositoryInterfaces/IWorkTaskRepository.cs src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfWorkTaskRepository.cs src/ONEVO.Application/Features/WorkManagement/Sprints/Commands/CompleteSprint/ tests/ONEVO.Tests.Unit/Features/WorkManagement/Sprints/CompleteSprintCommandHandlerTests.cs
git commit -m "feat(work): CompleteSprintCommand - requires every task in the sprint to be in a complete status"
```

---

### Task 7: `AchieveSprintCommand` + freeze check in `MoveTaskStatus`/`EditTask`

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/Sprints/Commands/AchieveSprint/AchieveSprintCommand.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Sprints/Commands/AchieveSprint/AchieveSprintCommandHandler.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/MoveTaskStatus/MoveTaskStatusCommandHandler.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/EditTask/EditTaskCommandHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Sprints/AchieveSprintCommandHandlerTests.cs`,
  extend `MoveTaskStatusCommandHandlerTests.cs` and `EditTaskCommandHandlerTests.cs`

**Interfaces:**
- Produces: `AchieveSprintCommand(Guid SprintId) : IRequest<Result<SprintResponse>>`.

- [ ] **Step 1: Write the failing test for `AchieveSprintCommand`**

```csharp
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.Commands.AchieveSprint;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Sprints;

public class AchieveSprintCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid OwnerEmployeeId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();
    private static readonly Guid SprintId = Guid.NewGuid();

    [Theory]
    [InlineData(SprintStatuses.Future)]
    [InlineData(SprintStatuses.Active)]
    [InlineData(SprintStatuses.Incomplete)]
    public async Task Handle_AnyNonTerminalStatus_MovesToAchieved(string startingStatus)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync(OwnerEmployeeId);

        var sprint = new Sprint { Id = SprintId, TenantId = TenantId, ObjectiveId = ObjectiveId, Name = "S1", StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2026, 9, 14), Status = startingStatus, CreatedAt = DateTimeOffset.UtcNow };
        var sprints = new Mock<ISprintRepository>();
        sprints.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, SprintId, It.IsAny<CancellationToken>())).ReturnsAsync(sprint);

        var objective = new Objective { Id = ObjectiveId, TenantId = TenantId, OwnerId = OwnerEmployeeId, IsActive = true, Title = "Obj", CreatedAt = DateTimeOffset.UtcNow };
        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>())).ReturnsAsync(objective);

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result<SprintResponse>>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result<SprintResponse>>> op, CancellationToken ct) => op(ct));
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new AchieveSprintCommandHandler(currentUser.Object, identity.Object, objectives.Object, sprints.Object, unitOfWork.Object);

        var result = await handler.Handle(new AchieveSprintCommand(SprintId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(SprintStatuses.Achieved, sprint.Status);
        Assert.NotNull(sprint.AchievedAt);
    }
}
```

- [ ] **Step 2: Run to verify it fails, then write the command and handler**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~AchieveSprintCommandHandlerTests`
Expected: FAIL to compile.

```csharp
// AchieveSprintCommand.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Sprints.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Sprints.Commands.AchieveSprint;

public sealed record AchieveSprintCommand(Guid SprintId) : IRequest<Result<SprintResponse>>;
```

```csharp
// AchieveSprintCommandHandler.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;

namespace ONEVO.Application.Features.WorkManagement.Sprints.Commands.AchieveSprint;

public class AchieveSprintCommandHandler : IRequestHandler<AchieveSprintCommand, Result<SprintResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IObjectiveRepository _objectives;
    private readonly ISprintRepository _sprints;
    private readonly IUnitOfWork _unitOfWork;

    public AchieveSprintCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IObjectiveRepository objectives,
        ISprintRepository sprints, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _identity = identity;
        _objectives = objectives;
        _sprints = sprints;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<SprintResponse>> Handle(AchieveSprintCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<SprintResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, _currentUser.UserId, ct);
        if (callerEmployeeId is null)
            return Result<SprintResponse>.Forbidden("No employee record for the current user.");

        var sprint = await _sprints.GetTrackedByIdForTenantAsync(tenantId, request.SprintId, ct);
        if (sprint is null)
            return Result<SprintResponse>.NotFound("Sprint not found.");

        var objective = await _objectives.GetByIdForTenantAsync(tenantId, sprint.ObjectiveId, ct);
        if (objective is null)
            return Result<SprintResponse>.NotFound("Objective not found.");

        if (objective.OwnerId != callerEmployeeId.Value)
            return Result<SprintResponse>.Forbidden("Only this milestone's owner can achieve sprints.");

        if (sprint.Status == SprintStatuses.Achieved)
            return Result<SprintResponse>.Conflict("This sprint has already been achieved.");

        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            sprint.Status = SprintStatuses.Achieved;
            sprint.AchievedAt = DateTimeOffset.UtcNow;
            sprint.UpdatedAt = DateTimeOffset.UtcNow;

            await _unitOfWork.SaveChangesAsync(innerCt);

            return Result<SprintResponse>.Success(new SprintResponse(
                sprint.Id, sprint.ObjectiveId, sprint.Name, sprint.StartDate, sprint.EndDate, sprint.Status,
                sprint.CompletedAt, sprint.AchievedAt));
        }, ct);
    }
}
```

- [ ] **Step 3: Run to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~AchieveSprintCommandHandlerTests`
Expected: PASS (all 3 parameterized cases).

- [ ] **Step 4: Add the freeze check to `MoveTaskStatusCommandHandler`**

This handler already gained `ITaskStatusRepository`/`IObjectiveRepository`/`IMilestoneMembershipCoordinator`
in Part 1 Task 7. Add an `ISprintRepository _sprints` field + constructor parameter, and insert this
check right after the existing membership/visibility authorization block (before the transaction),
in `MoveTaskStatusCommandHandler.cs`:

```csharp
        if (task.SprintId.HasValue)
        {
            var sprint = await _sprints.GetByIdForTenantAsync(tenantId, task.SprintId.Value, ct);
            if (sprint is not null && sprint.Status == SprintStatuses.Achieved)
                return Result.Forbidden("This task's sprint has been achieved and is now frozen.");
        }
```

Add `using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;` and
`using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;` to this file's usings.

Write the failing test first (append to `MoveTaskStatusCommandHandlerTests.cs`, extending its
`Build(...)` helper to accept and wire a `Mock<ISprintRepository>`):

```csharp
    [Fact]
    public async Task Handle_TaskInAchievedSprint_ReturnsForbidden()
    {
        var newStatus = new TaskStatusEntity { Id = NewStatusId, TenantId = TenantId, Name = "In Process", MarksTaskComplete = false, Visibility = TaskStatusVisibilities.Public, CreatedAt = DateTimeOffset.UtcNow };
        var (handler, objective, task) = Build(OwnerEmployeeId, callerIsMember: false, newStatus);
        // Requires Build(...) to accept an optional Sprint (or a sprints-mock override) and set
        // task.SprintId accordingly - extend Build's signature to take a `Sprint? sprint = null`
        // parameter, wiring an Achieved sprint through the new ISprintRepository mock when provided.

        var result = await handler.Handle(new MoveTaskStatusCommand(TaskId, NewStatusId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }
```

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~MoveTaskStatusCommandHandlerTests`
Expected: FAIL first (compile error from the new constructor param), then implement Step 4's handler
change, then PASS.

- [ ] **Step 5: Apply the identical freeze check to `EditTaskCommandHandler`**

In `EditTaskCommandHandler.cs`, add the same `ISprintRepository` dependency and the same check
(task's Sprint is Achieved → `Result<WorkTaskResponse>.Forbidden(...)`), placed right after the
existing `task is null` check and before the `EstimatedHours`/slack-check block. Write the failing
test first in `EditTaskCommandHandlerTests.cs` following the same pattern as Step 4's test, then
implement, then verify it passes.

- [ ] **Step 6: Run full regression**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~WorkManagement`
Expected: all PASS.

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Sprints/Commands/AchieveSprint/ src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/MoveTaskStatus/MoveTaskStatusCommandHandler.cs src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/EditTask/EditTaskCommandHandler.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/Sprints/AchieveSprintCommandHandlerTests.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/MoveTaskStatusCommandHandlerTests.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/EditTaskCommandHandlerTests.cs
git commit -m "feat(work): AchieveSprintCommand, and freeze status-moves/edits on tasks in an Achieved sprint"
```

---

### Task 8: `SprintLifecycleJob` background service

**Files:**
- Create: `src/ONEVO.Infrastructure/Services/WorkManagement/SprintLifecycleJob.cs`
- Modify: `src/ONEVO.Infrastructure/DependencyInjection.cs` (register as hosted service)
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Sprints/SprintLifecycleJobTests.cs`

**Interfaces:**
- Consumes: `ISprintRepository.GetByStatusAsync`, `IWorkTaskRepository.GetBySprintIdAsync`,
  `ITaskStatusRepository.GetByIdForTenantAsync`.

This job is the only place in this plan where "sweep across every tenant" logic is needed — mirrors
`AgentCommandExpiryJob`'s exact shape (`BackgroundService`, `PeriodicTimer`, per-tick DI scope,
catch-and-log so one bad tick never crashes the host).

- [ ] **Step 1: Write the failing test for the pure transition logic**

Rather than testing the `BackgroundService` machinery directly (hard to unit test meaningfully, and
`AgentCommandExpiryJob` itself has no test file — confirmed convention in this codebase is to not unit
test the timer loop itself), extract the per-sprint decision into a small static, directly-testable
method and test *that*:

```csharp
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;
using ONEVO.Infrastructure.Services.WorkManagement;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Sprints;

public class SprintLifecycleJobTests
{
    [Fact]
    public void DetermineNextStatus_FutureSprintStartDateReached_ReturnsActive()
    {
        var today = new DateOnly(2026, 9, 1);
        var next = SprintLifecycleJob.DetermineNextStatus(SprintStatuses.Future, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 14), today, allTasksComplete: false);

        Assert.Equal(SprintStatuses.Active, next);
    }

    [Fact]
    public void DetermineNextStatus_FutureSprintStartDateNotYetReached_StaysFuture()
    {
        var today = new DateOnly(2026, 8, 30);
        var next = SprintLifecycleJob.DetermineNextStatus(SprintStatuses.Future, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 14), today, allTasksComplete: false);

        Assert.Null(next);
    }

    [Fact]
    public void DetermineNextStatus_ActiveSprintPastEndDateWithUnfinishedTasks_ReturnsIncomplete()
    {
        var today = new DateOnly(2026, 9, 15);
        var next = SprintLifecycleJob.DetermineNextStatus(SprintStatuses.Active, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 14), today, allTasksComplete: false);

        Assert.Equal(SprintStatuses.Incomplete, next);
    }

    [Fact]
    public void DetermineNextStatus_ActiveSprintPastEndDateAllTasksComplete_StaysActive()
    {
        // Completion is a manual owner action (CompleteSprintCommand) - the job never auto-completes,
        // it only auto-flags Incomplete. An owner who hasn't clicked Complete yet keeps the sprint Active.
        var today = new DateOnly(2026, 9, 15);
        var next = SprintLifecycleJob.DetermineNextStatus(SprintStatuses.Active, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 14), today, allTasksComplete: true);

        Assert.Null(next);
    }

    [Fact]
    public void DetermineNextStatus_TerminalStatuses_NeverChange()
    {
        Assert.Null(SprintLifecycleJob.DetermineNextStatus(SprintStatuses.Complete, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 14), new DateOnly(2026, 9, 20), allTasksComplete: true));
        Assert.Null(SprintLifecycleJob.DetermineNextStatus(SprintStatuses.Incomplete, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 14), new DateOnly(2026, 9, 20), allTasksComplete: false));
        Assert.Null(SprintLifecycleJob.DetermineNextStatus(SprintStatuses.Achieved, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 14), new DateOnly(2026, 9, 20), allTasksComplete: true));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~SprintLifecycleJobTests`
Expected: FAIL to compile.

- [ ] **Step 3: Implement the job**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;

namespace ONEVO.Infrastructure.Services.WorkManagement;

/// <summary>
/// Advances Sprint.Status for the two date-driven transitions: Future->Active when the start date
/// arrives, and Active->Incomplete when the end date passes with unfinished tasks. Completion is
/// always a manual owner action (CompleteSprintCommand) - this job never sets Complete. Mirrors
/// AgentCommandExpiryJob's shape (PeriodicTimer, per-tick DI scope, catch-and-log).
/// </summary>
public sealed class SprintLifecycleJob : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(5);

    private readonly IServiceProvider _services;
    private readonly ILogger<SprintLifecycleJob> _logger;

    public SprintLifecycleJob(IServiceProvider services, ILogger<SprintLifecycleJob> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(CheckInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = _services.CreateAsyncScope();
                var sprints = scope.ServiceProvider.GetRequiredService<ISprintRepository>();
                var tasks = scope.ServiceProvider.GetRequiredService<IWorkTaskRepository>();
                var statuses = scope.ServiceProvider.GetRequiredService<ITaskStatusRepository>();

                var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);
                var candidates = (await sprints.GetByStatusAsync(SprintStatuses.Future, stoppingToken))
                    .Concat(await sprints.GetByStatusAsync(SprintStatuses.Active, stoppingToken));

                var advancedCount = 0;
                foreach (var sprint in candidates)
                {
                    var allTasksComplete = false;
                    if (sprint.Status == SprintStatuses.Active)
                    {
                        var sprintTasks = await tasks.GetBySprintIdAsync(sprint.TenantId, sprint.Id, stoppingToken);
                        allTasksComplete = sprintTasks.Count > 0;
                        foreach (var task in sprintTasks)
                        {
                            var status = await statuses.GetByIdForTenantAsync(sprint.TenantId, task.StatusId, stoppingToken);
                            if (status is null || !status.MarksTaskComplete)
                            {
                                allTasksComplete = false;
                                break;
                            }
                        }
                    }

                    var next = DetermineNextStatus(sprint.Status, sprint.StartDate, sprint.EndDate, today, allTasksComplete);
                    if (next is null)
                        continue;

                    sprint.Status = next;
                    sprint.UpdatedAt = DateTimeOffset.UtcNow;
                    sprints.Update(sprint);
                    advancedCount++;
                }

                if (advancedCount > 0)
                {
                    var db = scope.ServiceProvider.GetRequiredService<DbContext>();
                    await db.SaveChangesAsync(stoppingToken);
                    _logger.LogInformation("SprintLifecycleJob advanced {Count} sprints.", advancedCount);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SprintLifecycleJob encountered an error.");
            }
        }
    }

    /// <summary>Pure decision function, extracted for direct unit testing without the BackgroundService/DI machinery. Returns null if no transition applies.</summary>
    public static string? DetermineNextStatus(string currentStatus, DateOnly startDate, DateOnly endDate, DateOnly today, bool allTasksComplete)
    {
        if (currentStatus == SprintStatuses.Future && today >= startDate)
            return SprintStatuses.Active;

        if (currentStatus == SprintStatuses.Active && today > endDate && !allTasksComplete)
            return SprintStatuses.Incomplete;

        return null;
    }
}
```

Note: `scope.ServiceProvider.GetRequiredService<DbContext>()` — check whether this codebase's DI
registers `ApplicationDbContext` as `DbContext` or only as its concrete type; if `GetRequiredService<DbContext>()`
fails to resolve, use `GetRequiredService<ApplicationDbContext>()` instead (add
`using ONEVO.Infrastructure.Persistence;` if so) — check how `WorkManagementDapiDemoSeeder.StartAsync`
resolves its `db` variable (`scope.ServiceProvider.GetRequiredService<ApplicationDbContext>()`,
confirmed earlier in this session) and match that exact pattern instead of assuming `DbContext` resolves.

- [ ] **Step 4: Run to verify all pass**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~SprintLifecycleJobTests`
Expected: PASS (all 5 tests — these test the static `DetermineNextStatus` method directly, no DI
needed).

- [ ] **Step 5: Register the hosted service**

In `src/ONEVO.Infrastructure/DependencyInjection.cs`, alongside `AgentCommandExpiryJob`'s registration:
```csharp
        services.AddHostedService<Services.WorkManagement.SprintLifecycleJob>();
```

- [ ] **Step 6: Build to verify**

Run: `dotnet build src/ONEVO.Api`
Expected: succeeds, no DI resolution errors at startup (verify by also doing a quick
`dotnet run --project src/ONEVO.Api` and confirming no crash on boot, then stop it).

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Infrastructure/Services/WorkManagement/SprintLifecycleJob.cs src/ONEVO.Infrastructure/DependencyInjection.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/Sprints/SprintLifecycleJobTests.cs
git commit -m "feat(work): SprintLifecycleJob - date-driven Future->Active and Active->Incomplete transitions"
```

---

### Task 9: Objective-Achieve now requires every Sprint to be Complete or Achieved

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/AchieveObjective/AchieveObjectiveCommandHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Objectives/AchieveObjectiveCommandHandlerTests.cs`
  (find via `find tests/ONEVO.Tests.Unit -iname "AchieveObjectiveCommandHandlerTests.cs"`)

- [ ] **Step 1: Write the failing test**

Read the existing test file first to match its fixture style (it already tests the "all direct
children must be achieved" precondition at the same spot this new check goes — follow that exact
pattern), then add:

```csharp
    [Fact]
    public async Task Handle_SprintNeitherCompleteNorAchieved_ReturnsFailure()
    {
        // Arrange via this file's existing Build(...)-style helper for the immediate-apply path
        // (objective.CreatedById == userId), with a Mock<ISprintRepository> added returning one
        // Sprint whose Status is SprintStatuses.Active for this objective.

        var result = await handler.Handle(new AchieveObjectiveCommand(ObjectiveId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        objectives.Verify(x => x.Update(It.IsAny<Objective>()), Times.Never);
    }

    [Fact]
    public async Task Handle_AllSprintsCompleteOrAchieved_Succeeds()
    {
        // Same arrangement, but the mocked Sprint list has Status = SprintStatuses.Complete (or a mix
        // of Complete/Achieved) for every sprint on this objective.

        var result = await handler.Handle(new AchieveObjectiveCommand(ObjectiveId), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~AchieveObjectiveCommandHandlerTests`
Expected: FAIL — handler constructor doesn't accept `ISprintRepository` yet, and/or the new test's
assertion fails since no check exists.

- [ ] **Step 3: Add the check**

Add an `ISprintRepository _sprints` field + constructor parameter to `AchieveObjectiveCommandHandler`.
Insert this check right after the existing direct-children-achieved check (around line 66-68 in the
file as read during Part 2 research):

```csharp
        var sprints = await _sprints.GetByObjectiveIdAsync(tenantId, objective.Id, ct);
        if (sprints.Any(s => s.Status is not (SprintStatuses.Complete or SprintStatuses.Achieved)))
            return Result<ObjectiveChangeOutcomeResponse>.Failure("All sprints on this milestone must be Complete or Achieved before it can be achieved.");
```

Add `using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;` and
`using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;` to this file's usings.

- [ ] **Step 4: Run to verify all pass**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~AchieveObjectiveCommandHandlerTests`
Expected: PASS, including all pre-existing tests in this file (an objective with zero sprints must
still be achievable — `Any()` on an empty list is `false`, so this is safe by construction; add an
explicit test for that zero-sprints case too if the file doesn't already imply it through its other
fixtures).

- [ ] **Step 5: Full regression check**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~WorkManagement`
Expected: all PASS.

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/AchieveObjective/AchieveObjectiveCommandHandler.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/Objectives/AchieveObjectiveCommandHandlerTests.cs
git commit -m "feat(work): an Objective can only be achieved once every one of its Sprints is Complete or Achieved"
```

---

### Task 10: Controller wiring — `SprintsController`

**Files:**
- Create: `src/ONEVO.Api/Contracts/WorkManagement/Sprints/SprintContracts.cs`
- Create: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/SprintsController.cs`

- [ ] **Step 1: Write the contracts**

```csharp
namespace ONEVO.Api.Contracts.WorkManagement.Sprints;

public sealed record CreateSprintRequest(string Name, DateOnly StartDate, DateOnly EndDate);
public sealed record EditSprintRequest(string Name, DateOnly StartDate, DateOnly EndDate);

public sealed record SprintViewModel(
    Guid Id, Guid ObjectiveId, string Name, DateOnly StartDate, DateOnly EndDate, string Status,
    DateTimeOffset? CompletedAt, DateTimeOffset? AchievedAt);

public static class SprintViewModelMapper
{
    public static SprintViewModel ToViewModel(this Application.Features.WorkManagement.Sprints.DTOs.Responses.SprintResponse dto) =>
        new(dto.Id, dto.ObjectiveId, dto.Name, dto.StartDate, dto.EndDate, dto.Status, dto.CompletedAt, dto.AchievedAt);
}
```

- [ ] **Step 2: Write the controller**

```csharp
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.WorkManagement.Sprints;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.WorkManagement.Sprints.Commands.AchieveSprint;
using ONEVO.Application.Features.WorkManagement.Sprints.Commands.CompleteSprint;
using ONEVO.Application.Features.WorkManagement.Sprints.Commands.CreateSprint;
using ONEVO.Application.Features.WorkManagement.Sprints.Commands.EditSprint;

namespace ONEVO.Api.Controllers.Tenant.WorkManagement;

[ApiController]
[Route("api/v1/work")]
[Authorize(Policy = "TenantPolicy")]
public class SprintsController : ControllerBase
{
    private readonly IMediator _mediator;

    public SprintsController(IMediator mediator) => _mediator = mediator;

    [HttpPost("objectives/{objectiveId:guid}/sprints")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> Create(Guid objectiveId, [FromBody] CreateSprintRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new CreateSprintCommand(objectiveId, request.Name, request.StartDate, request.EndDate), ct);

        return result.IsSuccess
            ? StatusCode(201, result.Value!.ToViewModel())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPatch("sprints/{id:guid}")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> Edit(Guid id, [FromBody] EditSprintRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new EditSprintCommand(id, request.Name, request.StartDate, request.EndDate), ct);

        return result.IsSuccess
            ? Ok(result.Value!.ToViewModel())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("sprints/{id:guid}/complete")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> Complete(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new CompleteSprintCommand(id), ct);

        return result.IsSuccess
            ? Ok(result.Value!.ToViewModel())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("sprints/{id:guid}/achieve")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> Achieve(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new AchieveSprintCommand(id), ct);

        return result.IsSuccess
            ? Ok(result.Value!.ToViewModel())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
```

Note: this plan does not add a `GET objectives/{objectiveId}/sprints` list endpoint — Part 4 (frontend
Backlog UI) needs one. Add it here now rather than leaving a gap for Part 4 to discover:

```csharp
    [HttpGet("objectives/{objectiveId:guid}/sprints")]
    public async Task<IActionResult> GetByObjective(Guid objectiveId, [FromQuery] bool activeOnly, CancellationToken ct)
    {
        // activeOnly=true is what non-owner members' Backlog view calls (spec permissions table);
        // the owner's "all sprints" view calls with activeOnly=false or omitted.
    }
```

This requires a new query (`GetObjectiveSprintsQuery`) — not yet written. Add it now as this task's
final step rather than deferring:

- [ ] **Step 3: Add `GetObjectiveSprintsQuery`**

Create `src/ONEVO.Application/Features/WorkManagement/Sprints/Queries/GetObjectiveSprints/GetObjectiveSprintsQuery.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Sprints.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Sprints.Queries.GetObjectiveSprints;

public sealed record GetObjectiveSprintsQuery(Guid ObjectiveId, bool ActiveOnly) : IRequest<Result<IReadOnlyList<SprintResponse>>>;
```

And `GetObjectiveSprintsQueryHandler.cs` in the same folder — no owner check needed for reading (any
active Objective member can call this; the `ActiveOnly` flag is what the frontend uses to implement
the owner-sees-all-vs-member-sees-active-only rule, not a server-side identity check, since the spec's
permission table frames this as a *view* distinction the frontend requests explicitly, not a hidden
per-user filter):

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Sprints.Queries.GetObjectiveSprints;

public class GetObjectiveSprintsQueryHandler : IRequestHandler<GetObjectiveSprintsQuery, Result<IReadOnlyList<SprintResponse>>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ISprintRepository _sprints;

    public GetObjectiveSprintsQueryHandler(ICurrentUser currentUser, ISprintRepository sprints)
    {
        _currentUser = currentUser;
        _sprints = sprints;
    }

    public async Task<Result<IReadOnlyList<SprintResponse>>> Handle(GetObjectiveSprintsQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<IReadOnlyList<SprintResponse>>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var sprints = request.ActiveOnly
            ? await _sprints.GetActiveByObjectiveIdAsync(tenantId, request.ObjectiveId, ct)
            : await _sprints.GetByObjectiveIdAsync(tenantId, request.ObjectiveId, ct);

        return Result<IReadOnlyList<SprintResponse>>.Success(
            sprints.Select(s => new SprintResponse(s.Id, s.ObjectiveId, s.Name, s.StartDate, s.EndDate, s.Status, s.CompletedAt, s.AchievedAt)).ToList());
    }
}
```

Now fill in the `GetByObjective` controller action body from Step 2:
```csharp
    [HttpGet("objectives/{objectiveId:guid}/sprints")]
    public async Task<IActionResult> GetByObjective(Guid objectiveId, [FromQuery] bool activeOnly, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetObjectiveSprintsQuery(objectiveId, activeOnly), ct);

        return result.IsSuccess
            ? Ok(result.Value!.Select(s => s.ToViewModel()).ToList())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
```

Add `using ONEVO.Application.Features.WorkManagement.Sprints.Queries.GetObjectiveSprints;` to the
controller's usings.

- [ ] **Step 4: Manual verification**

Run: `dotnet build src/ONEVO.Api`
Expected: succeeds.
Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~WorkManagement`
Expected: all PASS — this is the full Part 2 regression gate.

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Api/Contracts/WorkManagement/Sprints/ src/ONEVO.Api/Controllers/Tenant/WorkManagement/SprintsController.cs src/ONEVO.Application/Features/WorkManagement/Sprints/Queries/
git commit -m "feat(work): wire the Sprint CRUD + lifecycle + list endpoints"
```

---

### Task 11: Sprint lifecycle notifications (in-app)

**Files:**
- Modify: `src/ONEVO.Infrastructure/Persistence/Seeders/NotificationTemplateSeeder.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Sprints/Commands/CompleteSprint/CompleteSprintCommandHandler.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Sprints/Commands/AchieveSprint/AchieveSprintCommandHandler.cs`
- Modify: `src/ONEVO.Infrastructure/Services/WorkManagement/SprintLifecycleJob.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Sprints/CompleteSprintCommandHandlerTests.cs`,
  `AchieveSprintCommandHandlerTests.cs` (extend)

**This closes a spec-coverage gap found during self-review** — the spec's Notifications section
(`sprint_completed`/`sprint_incomplete`/`sprint_achieved`, in-app, sent to the sprint's Objective
members) had no implementing task until now.

**Also fixes a real bug found while reading the existing seeder, unrelated to but blocking this
task:** `NotificationTemplateSeeder.SeedAsync` gates its *entire* seed behind
`if (await notifications.AnyTemplatesExistAsync(ct)) { return; }` — an all-or-nothing check. Any
environment that already ran this seeder once (which is every environment that's shipped the Task
Foundation feature, i.e. now) would see templates already exist and **skip seeding the new ones
entirely**, silently. Fix: change to a per-template existence check before this task's new templates
are added, so both old and new environments end up with all 7 templates correctly.

- [ ] **Step 1: Write the failing test for the seeder's per-template idempotency**

Find or create `tests/ONEVO.Tests.Unit/Features/Auth/NotificationTemplateSeederTests.cs` (search
`find tests/ONEVO.Tests.Unit -iname "NotificationTemplateSeederTests.cs"` first — if it exists, read
it to match its style; if not, use the same `SqliteTestApplicationDbContext` pattern as this plan's
other seeder tests):

```csharp
    [Fact]
    public async Task SeedAsync_SomeTemplatesAlreadyExist_StillAddsTheMissingOnes()
    {
        // Arrange: pre-insert one of the original 4 templates (e.g. "work_task_creation_request_created")
        // directly into the test DbContext, simulating an environment that already ran this seeder
        // before the new sprint templates were added. Then run NotificationTemplateSeeder.SeedAsync
        // (or StartAsync, whichever this file's existing tests already call).

        // Assert: all 7 expected template codes now exist in the DbContext, including the 3 new
        // "sprint_completed"/"sprint_incomplete"/"sprint_achieved" codes - not skipped.
    }
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~NotificationTemplateSeederTests`
Expected: FAIL — current all-or-nothing gate skips seeding when the pre-inserted template makes
`AnyTemplatesExistAsync` return true.

- [ ] **Step 3: Fix the seeder**

Replace `SeedAsync`'s all-or-nothing gate and bulk `AddTemplateRangeAsync` call with a per-template
check-then-add loop, and add the 3 new templates to the list:

```csharp
    private async Task SeedAsync(INotificationRepository notifications, ApplicationDbContext db, CancellationToken ct)
    {
        var templates = new List<NotificationTemplate>
        {
            new()
            {
                Id = Guid.NewGuid(), Code = "work_task_creation_request_created",
                InAppTitleTemplate = "New task request",
                InAppBodyTemplate = "{{requesterName}} requested a new task \"{{taskTitle}}\" on {{objectiveName}}."
            },
            new()
            {
                Id = Guid.NewGuid(), Code = "work_task_creation_request_decided",
                InAppTitleTemplate = "Task request {{decision}}",
                InAppBodyTemplate = "Your task request \"{{taskTitle}}\" on {{objectiveName}} was {{decision}}."
            },
            new()
            {
                Id = Guid.NewGuid(), Code = "work_allocation_extend_request_created",
                InAppTitleTemplate = "Allocation extension requested",
                InAppBodyTemplate = "{{requesterName}} requested {{requestedHours}} more hours for {{objectiveName}}."
            },
            new()
            {
                Id = Guid.NewGuid(), Code = "work_allocation_extend_request_decided",
                InAppTitleTemplate = "Allocation request {{decision}}",
                InAppBodyTemplate = "Your allocation extension request for {{objectiveName}} was {{decision}}."
            },
            new()
            {
                Id = Guid.NewGuid(), Code = "work_sprint_completed",
                InAppTitleTemplate = "Sprint completed",
                InAppBodyTemplate = "\"{{sprintName}}\" on {{objectiveName}} was marked Complete."
            },
            new()
            {
                Id = Guid.NewGuid(), Code = "work_sprint_incomplete",
                InAppTitleTemplate = "Sprint ended incomplete",
                InAppBodyTemplate = "\"{{sprintName}}\" on {{objectiveName}} ended with unfinished tasks and is now Incomplete."
            },
            new()
            {
                Id = Guid.NewGuid(), Code = "work_sprint_achieved",
                InAppTitleTemplate = "Sprint achieved",
                InAppBodyTemplate = "\"{{sprintName}}\" on {{objectiveName}} was achieved and its tasks are now frozen."
            }
        };

        var addedCount = 0;
        foreach (var template in templates)
        {
            var existing = await notifications.GetTemplateByCodeAsync(template.Code, ct);
            if (existing is not null)
                continue;

            await notifications.AddTemplateRangeAsync(new[] { template }, ct);
            addedCount++;
        }

        if (addedCount > 0)
        {
            await db.SaveChangesAsync(ct);
            _logger.LogInformation("Seeded {Count} new notification templates.", addedCount);
        }
        else
        {
            _logger.LogInformation("Notification templates already up to date — nothing to seed.");
        }
    }
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~NotificationTemplateSeederTests`
Expected: PASS.

- [ ] **Step 5: Add a shared member-notification helper and wire it into `CompleteSprintCommandHandler`**

Both `CompleteSprintCommandHandler` and `AchieveSprintCommandHandler` need to notify every active
member of the sprint's Objective — add this as a small private helper in each (not a new shared
service; the notification logic is 4 lines and doesn't warrant a new abstraction per this codebase's
YAGNI convention seen elsewhere). Add `IProjectMemberRepository`, `IMilestoneMembershipCoordinator`,
and `INotificationDispatcher` to `CompleteSprintCommandHandler`'s constructor, then in the transaction
block, after setting `sprint.Status = SprintStatuses.Complete;` etc., before `SaveChangesAsync`:

```csharp
            var members = await _members.ListActiveForObjectiveAsync(tenantId, objective.Id, innerCt);
            foreach (var member in members)
            {
                var assignee = await _membership.GetActiveAssigneeAsync(tenantId, member.EmployeeId, innerCt);
                if (assignee is null) continue;

                await _notifications.SendTemplatedAsync(
                    tenantId, assignee.UserId, "work_sprint_completed",
                    new Dictionary<string, string> { ["sprintName"] = sprint.Name, ["objectiveName"] = objective.Title },
                    "sprint", sprint.Id, innerCt);
            }
```

Write the failing test first (extend `CompleteSprintCommandHandlerTests.cs`'s `Build(...)` helper to
mock `IProjectMemberRepository.ListActiveForObjectiveAsync` returning one member, mock
`IMilestoneMembershipCoordinator.GetActiveAssigneeAsync` returning an `Employee`, mock
`INotificationDispatcher`, then assert `SendTemplatedAsync` was called once with `"work_sprint_completed"`),
run it to see it fail, implement, run again to see it pass — same TDD cycle as every other task in
this plan.

- [ ] **Step 6: Wire the identical notification into `AchieveSprintCommandHandler`**

Same pattern, same three new constructor dependencies, template code `"work_sprint_achieved"`, same
placeholder shape. Write the failing test first in `AchieveSprintCommandHandlerTests.cs`, following
Step 5's exact process.

- [ ] **Step 7: Wire the Incomplete notification into `SprintLifecycleJob`**

In `SprintLifecycleJob.cs`'s `ExecuteAsync`, inside the `foreach (var sprint in candidates)` loop,
right after `sprints.Update(sprint);`, when `next == SprintStatuses.Incomplete` specifically (not for
the Future→Active transition — only Incomplete warrants a notification, matching the spec's exact
event list):

```csharp
                    if (next == SprintStatuses.Incomplete)
                    {
                        var members = scope.ServiceProvider.GetRequiredService<IProjectMemberRepository>();
                        var membership = scope.ServiceProvider.GetRequiredService<IMilestoneMembershipCoordinator>();
                        var notifications = scope.ServiceProvider.GetRequiredService<INotificationDispatcher>();
                        var objectives = scope.ServiceProvider.GetRequiredService<IObjectiveRepository>();

                        var objective = await objectives.GetByIdForTenantAsync(sprint.TenantId, sprint.ObjectiveId, stoppingToken);
                        if (objective is not null)
                        {
                            var activeMembers = await members.ListActiveForObjectiveAsync(sprint.TenantId, sprint.ObjectiveId, stoppingToken);
                            foreach (var member in activeMembers)
                            {
                                var assignee = await membership.GetActiveAssigneeAsync(sprint.TenantId, member.EmployeeId, stoppingToken);
                                if (assignee is null) continue;

                                await notifications.SendTemplatedAsync(
                                    sprint.TenantId, assignee.UserId, "work_sprint_incomplete",
                                    new Dictionary<string, string> { ["sprintName"] = sprint.Name, ["objectiveName"] = objective.Title },
                                    "sprint", sprint.Id, stoppingToken);
                            }
                        }
                    }
```

Add the corresponding `using` statements for `IProjectMemberRepository`, `IMilestoneMembershipCoordinator`,
`INotificationDispatcher`, `IObjectiveRepository` to this file. This job has no existing unit test
harness for its DI-driven tick (per Task 8's own note that `AgentCommandExpiryJob` has none either) —
this notification wiring is intentionally left uncovered by an automated test for the same reason;
verify it manually in Task 5 of Part 4's end-to-end check instead (create a sprint with a past end
date and an incomplete task, wait for the job's tick or trigger it manually in a debugger, confirm the
notification appears).

- [ ] **Step 8: Run full regression**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~WorkManagement`
Expected: all PASS.

- [ ] **Step 9: Commit**

```bash
git add src/ONEVO.Infrastructure/Persistence/Seeders/NotificationTemplateSeeder.cs src/ONEVO.Application/Features/WorkManagement/Sprints/Commands/CompleteSprint/CompleteSprintCommandHandler.cs src/ONEVO.Application/Features/WorkManagement/Sprints/Commands/AchieveSprint/AchieveSprintCommandHandler.cs src/ONEVO.Infrastructure/Services/WorkManagement/SprintLifecycleJob.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/Sprints/
git commit -m "feat(work): sprint completed/incomplete/achieved in-app notifications, and fix the notification-template seeder's all-or-nothing idempotency gate"
```

---

## End of Part 2

Part 2 is complete when all 11 tasks are committed and the full `dotnet test tests/ONEVO.Tests.Unit
--filter FullyQualifiedName~WorkManagement` suite is green, with the dev server booting cleanly
(confirming `SprintLifecycleJob` resolves its DI dependencies). This unblocks Part 3 (frontend
Objective Settings + task-detail popup + assignee UX) and Part 4 (frontend Backlog Sprint UI + Board
scoping), both of which consume the endpoints this part exposed.
