# Work Management — Task Foundation, Part 2: Task Creation Requests Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a non-owner Objective member submit a task-creation request that the Objective owner reviews, edits-by-approving, or rejects — the second of the two task-creation paths in spec §3.2.

**Architecture:** Same MediatR CQRS pattern as Part 1. New table `task_creation_requests`, modeled directly on the existing `objective_change_requests` shape (see `ObjectiveChangeRequest.cs`/`ObjectiveChangeRequestConfiguration.cs`, already read in Part 1 Task 1) but routed to the Objective **owner**, not the reporting manager.

**Tech Stack:** Same as Part 1 — ASP.NET Core, EF Core, MediatR, FluentValidation, xUnit + Moq.

**Spec:** `docs/superpowers/specs/next/2026-08-16-work-management-task-foundation-design.md` §3.3.

## Global Constraints

- Prerequisite: Part 1 must be fully implemented and merged first — this plan's handlers depend on `WorkTask`, `IWorkTaskRepository`, `IObjectiveAllocationSlackCalculator`, `ITaskStatusRepository` from Part 1.
- Same EmployeeId-only rule as Part 1 (spec §2).
- The slack check on approval must reuse `IObjectiveAllocationSlackCalculator` from Part 1 Task 6 unchanged — do not re-derive the formula here.

---

### Task 1: `TaskCreationRequest` entity, configuration, repository, migration

**Files:**
- Create: `src/ONEVO.Domain/Features/WorkManagement/Tasks/Entities/TaskCreationRequest.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Configurations/WorkManagement/TaskCreationRequestConfiguration.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Tasks/RepositoryInterfaces/ITaskCreationRequestRepository.cs`
- Create: `src/ONEVO.Infrastructure/Repositories/WorkManagement/TaskCreationRequestRepository.cs`
- Create: `src/ONEVO.Infrastructure/Migrations/<timestamp>_AddTaskCreationRequests.cs` (via `dotnet ef migrations add`)
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/TaskCreationRequestConfigurationTests.cs`

**Interfaces:**
- Produces: `TaskCreationRequest : BaseEntity` (ObjectiveId, RequestedByEmployeeId, PayloadJson, Status, DecidedByEmployeeId?, DecisionComment?, CreatedTaskId?), `TaskCreationRequestStatuses` static class (`Pending`, `Approved`, `Rejected`, `Cancelled`), `TaskCreationRequestPayload` record (Title, Description?, TaskType, Priority, DueDate?, EstimatedHours?, StoryPoints?), `ITaskCreationRequestRepository.{AddAsync, GetByIdForTenantAsync, GetTrackedByIdForTenantAsync, GetPendingByObjectiveIdAsync, GetPendingForOwnerAsync, Update}`.

- [ ] **Step 1: Write the failing test**

```csharp
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class TaskCreationRequestConfigurationTests
{
    [Fact]
    public void TaskCreationRequest_DefaultsToPendingStatus()
    {
        var request = new TaskCreationRequest
        {
            Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), ObjectiveId = Guid.NewGuid(),
            RequestedByEmployeeId = Guid.NewGuid(), PayloadJson = "{}", CreatedById = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow
        };

        Assert.Equal(TaskCreationRequestStatuses.Pending, request.Status);
        Assert.Null(request.DecidedByEmployeeId);
        Assert.Null(request.CreatedTaskId);
    }
}
```

- [ ] **Step 2: Run test, verify FAIL.**

- [ ] **Step 3: Write the entity**

```csharp
// src/ONEVO.Domain/Features/WorkManagement/Tasks/Entities/TaskCreationRequest.cs
using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

public static class TaskCreationRequestStatuses
{
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string Cancelled = "cancelled";
}

/// <summary>
/// A non-owner Objective member's request to create a task, decided by the Objective owner.
/// See docs/superpowers/specs/next/2026-08-16-work-management-task-foundation-design.md §3.3.
/// </summary>
public class TaskCreationRequest : BaseEntity
{
    public Guid ObjectiveId { get; set; }
    public Guid RequestedByEmployeeId { get; set; }
    public string PayloadJson { get; set; } = "{}";
    public string Status { get; set; } = TaskCreationRequestStatuses.Pending;
    public Guid? DecidedByEmployeeId { get; set; }
    public string? DecisionComment { get; set; }
    public Guid? CreatedTaskId { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
}
```

- [ ] **Step 4: Write the payload DTO** (lives in Application layer since it's a serialization contract, not a persisted shape):

```csharp
// src/ONEVO.Application/Features/WorkManagement/Tasks/DTOs/TaskCreationRequestPayload.cs
namespace ONEVO.Application.Features.WorkManagement.Tasks.DTOs;

public sealed record TaskCreationRequestPayload(
    string Title, string? Description, string TaskType, string Priority,
    DateOnly? DueDate, decimal? EstimatedHours, int? StoryPoints);
```

- [ ] **Step 5: Write the EF configuration** (mirrors `ObjectiveChangeRequestConfiguration` exactly, indexed by owner instead of reporting manager):

```csharp
// src/ONEVO.Infrastructure/Persistence/Configurations/WorkManagement/TaskCreationRequestConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.WorkManagement;

public class TaskCreationRequestConfiguration : IEntityTypeConfiguration<TaskCreationRequest>
{
    public void Configure(EntityTypeBuilder<TaskCreationRequest> builder)
    {
        builder.ToTable("task_creation_requests");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Status).HasMaxLength(20).IsRequired();
        builder.Property(r => r.PayloadJson).HasColumnType("jsonb");
        builder.Property(r => r.DecisionComment).HasColumnType("text");

        builder.HasIndex(r => new { r.TenantId, r.ObjectiveId, r.Status })
            .HasDatabaseName("ix_task_creation_requests_tenant_id_objective_id_status");

        builder.HasOne<Objective>().WithMany().HasForeignKey(r => r.ObjectiveId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<WorkTask>().WithMany().HasForeignKey(r => r.CreatedTaskId).OnDelete(DeleteBehavior.SetNull);
    }
}
```

- [ ] **Step 6: Repository interface + implementation**

```csharp
// src/ONEVO.Application/Features/WorkManagement/Tasks/RepositoryInterfaces/ITaskCreationRequestRepository.cs
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

public interface ITaskCreationRequestRepository
{
    Task AddAsync(TaskCreationRequest request, CancellationToken ct = default);
    Task<TaskCreationRequest?> GetByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default);
    Task<TaskCreationRequest?> GetTrackedByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default);

    /// <summary>Pending requests routed to a given Objective owner - the owner's own approval queue.
    /// Joins against objectives.owner_id at the repository layer since TaskCreationRequest has no
    /// owner column of its own (the owner is looked up live via the Objective, not snapshotted -
    /// unlike objective_change_requests.reporting_manager_id, because a task creation request's
    /// "who approves" should always reflect the *current* owner, not who owned it at request time).</summary>
    Task<IReadOnlyList<TaskCreationRequest>> GetPendingForOwnerEmployeeIdAsync(Guid tenantId, Guid ownerEmployeeId, CancellationToken ct = default);

    void Update(TaskCreationRequest request);
}
```

```csharp
// src/ONEVO.Infrastructure/Repositories/WorkManagement/TaskCreationRequestRepository.cs
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using ONEVO.Infrastructure.Persistence;

namespace ONEVO.Infrastructure.Repositories.WorkManagement;

public class TaskCreationRequestRepository : ITaskCreationRequestRepository
{
    private readonly ApplicationDbContext _db;

    public TaskCreationRequestRepository(ApplicationDbContext db) => _db = db;

    public async Task AddAsync(TaskCreationRequest request, CancellationToken ct = default)
        => await _db.Set<TaskCreationRequest>().AddAsync(request, ct);

    public async Task<TaskCreationRequest?> GetByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default)
        => await _db.Set<TaskCreationRequest>().AsNoTracking().FirstOrDefaultAsync(r => r.TenantId == tenantId && r.Id == id, ct);

    public async Task<TaskCreationRequest?> GetTrackedByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default)
        => await _db.Set<TaskCreationRequest>().FirstOrDefaultAsync(r => r.TenantId == tenantId && r.Id == id, ct);

    public async Task<IReadOnlyList<TaskCreationRequest>> GetPendingForOwnerEmployeeIdAsync(Guid tenantId, Guid ownerEmployeeId, CancellationToken ct = default)
        => await (
            from r in _db.Set<TaskCreationRequest>().AsNoTracking()
            join o in _db.Set<Objective>().AsNoTracking() on r.ObjectiveId equals o.Id
            where r.TenantId == tenantId && r.Status == TaskCreationRequestStatuses.Pending && o.OwnerId == ownerEmployeeId
            select r
        ).ToListAsync(ct);

    public void Update(TaskCreationRequest request) => _db.Set<TaskCreationRequest>().Update(request);
}
```

- [ ] **Step 7: Register in DI:** `services.AddScoped<ITaskCreationRequestRepository, TaskCreationRequestRepository>();`

- [ ] **Step 8: Generate + write the migration** (`dotnet ef migrations add AddTaskCreationRequests --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api`), then append the RLS SQL block for `task_creation_requests`, copying the exact pattern from Part 1 Task 5 (this table **does** have `tenant_id` via `BaseEntity`, so it gets a real policy, unlike `task_assignments`).

- [ ] **Step 9: Apply migration, verify RLS via `pg_policies`, run `TenantIsolationArchitectureTests`, verify PASS.**

- [ ] **Step 10: Run unit test, verify PASS. Step 11: Commit.**

```bash
git add src/ONEVO.Domain/Features/WorkManagement/Tasks/Entities/TaskCreationRequest.cs src/ONEVO.Application/Features/WorkManagement/Tasks/DTOs/TaskCreationRequestPayload.cs src/ONEVO.Infrastructure/Persistence/Configurations/WorkManagement/TaskCreationRequestConfiguration.cs src/ONEVO.Application/Features/WorkManagement/Tasks/RepositoryInterfaces/ITaskCreationRequestRepository.cs src/ONEVO.Infrastructure/Repositories/WorkManagement/TaskCreationRequestRepository.cs src/ONEVO.Infrastructure/Migrations/ src/ONEVO.Infrastructure/DependencyInjection.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/TaskCreationRequestConfigurationTests.cs
git commit -m "feat(work): TaskCreationRequest entity, configuration, repository, migration"
```

### Task 2: `CreateTaskCreationRequest` command — member submits, no slack check at submission time

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/CreateTaskCreationRequest/{CreateTaskCreationRequestCommand,CreateTaskCreationRequestCommandHandler,CreateTaskCreationRequestCommandValidator}.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/CreateTaskCreationRequestCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `ITaskCreationRequestRepository`, `IMilestoneMembershipCoordinator` (to confirm requester is an active Objective member — same service already used by `AddObjectiveMemberCommandHandler`).
- Produces: `CreateTaskCreationRequestCommand(Guid ObjectiveId, string Title, string? Description, string TaskType, string Priority, DateOnly? DueDate, decimal? EstimatedHours, int? StoryPoints) : IRequest<Result<TaskCreationRequestResponse>>`.

- [ ] **Step 1: Write the failing test**

```csharp
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.CreateTaskCreationRequest;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Lookups;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class CreateTaskCreationRequestCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();
    private static readonly Guid OwnerId = Guid.NewGuid();

    private (CreateTaskCreationRequestCommandHandler Handler, Mock<ITaskCreationRequestRepository> Requests) Build(bool isActiveMember)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync(EmployeeId);

        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Objective { Id = ObjectiveId, TenantId = TenantId, OwnerId = OwnerId, IsActive = true, CreatedAt = DateTimeOffset.UtcNow });

        var membership = new Mock<IMilestoneMembershipCoordinator>();
        membership.Setup(x => x.IsActiveMemberAsync(TenantId, ObjectiveId, EmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(isActiveMember);

        var requests = new Mock<ITaskCreationRequestRepository>();

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result<TaskCreationRequestResponse>>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result<TaskCreationRequestResponse>>> op, CancellationToken ct) => op(ct));

        var handler = new CreateTaskCreationRequestCommandHandler(currentUser.Object, identity.Object, objectives.Object, membership.Object, requests.Object, unitOfWork.Object);
        return (handler, requests);
    }

    [Fact]
    public async Task Handle_ActiveMember_CreatesPendingRequest()
    {
        var (handler, requests) = Build(isActiveMember: true);
        var command = new CreateTaskCreationRequestCommand(ObjectiveId, "New task", null, "task", "medium", null, 5m, null);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        requests.Verify(x => x.AddAsync(It.IsAny<Domain.Features.WorkManagement.Tasks.Entities.TaskCreationRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_NotActiveMember_ReturnsForbidden()
    {
        var (handler, requests) = Build(isActiveMember: false);
        var command = new CreateTaskCreationRequestCommand(ObjectiveId, "New task", null, "task", "medium", null, 5m, null);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        requests.Verify(x => x.AddAsync(It.IsAny<Domain.Features.WorkManagement.Tasks.Entities.TaskCreationRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
```

**Note for implementer:** `IMilestoneMembershipCoordinator.IsActiveMemberAsync` is assumed here as the natural membership-check method. Before writing the handler, read `src/ONEVO.Application/Features/WorkManagement/Objectives/Services/IMilestoneMembershipCoordinator.cs` to confirm the exact existing method name/signature — if no such boolean check exists yet (only `GetActiveAssigneeAsync`), add `Task<bool> IsActiveMemberAsync(Guid tenantId, Guid objectiveId, Guid employeeId, CancellationToken ct = default)` to that interface and its implementation rather than duplicating a query in this handler.

- [ ] **Step 2: Run test, verify FAIL.**

- [ ] **Step 3: Write the response DTO, command, validator**

```csharp
// add to src/ONEVO.Application/Features/WorkManagement/Tasks/DTOs/Responses/WorkTaskResponse.cs (same file as Part 1 Task 6)
public sealed record TaskCreationRequestResponse(
    Guid Id, Guid ObjectiveId, string Status, TaskCreationRequestPayload Payload, DateTimeOffset CreatedAt);
```

```csharp
// src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/CreateTaskCreationRequest/CreateTaskCreationRequestCommand.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.CreateTaskCreationRequest;

public sealed record CreateTaskCreationRequestCommand(
    Guid ObjectiveId, string Title, string? Description, string TaskType, string Priority,
    DateOnly? DueDate, decimal? EstimatedHours, int? StoryPoints
) : IRequest<Result<TaskCreationRequestResponse>>;
```

Validator: identical rule set to `CreateTaskCommandValidator` (Part 1 Task 6) — copy it verbatim with the class renamed.

- [ ] **Step 4: Write the handler**

```csharp
// src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/CreateTaskCreationRequest/CreateTaskCreationRequestCommandHandler.cs
using System.Text.Json;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.CreateTaskCreationRequest;

public class CreateTaskCreationRequestCommandHandler : IRequestHandler<CreateTaskCreationRequestCommand, Result<TaskCreationRequestResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IObjectiveRepository _objectives;
    private readonly IMilestoneMembershipCoordinator _membership;
    private readonly ITaskCreationRequestRepository _requests;
    private readonly IUnitOfWork _unitOfWork;

    public CreateTaskCreationRequestCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IObjectiveRepository objectives,
        IMilestoneMembershipCoordinator membership, ITaskCreationRequestRepository requests, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _identity = identity;
        _objectives = objectives;
        _membership = membership;
        _requests = requests;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<TaskCreationRequestResponse>> Handle(CreateTaskCreationRequestCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<TaskCreationRequestResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, _currentUser.UserId, ct);
        if (callerEmployeeId is null)
            return Result<TaskCreationRequestResponse>.Forbidden("No employee record for the current user.");

        var objective = await _objectives.GetByIdForTenantAsync(tenantId, request.ObjectiveId, ct);
        if (objective is null || !objective.IsActive)
            return Result<TaskCreationRequestResponse>.NotFound("Objective not found.");

        if (objective.OwnerId == callerEmployeeId.Value)
            return Result<TaskCreationRequestResponse>.Failure("The milestone owner creates tasks directly - no request needed.", 400);

        var isMember = await _membership.IsActiveMemberAsync(tenantId, objective.Id, callerEmployeeId.Value, ct);
        if (!isMember)
            return Result<TaskCreationRequestResponse>.Forbidden("Only active milestone members can request tasks.");

        var payload = new TaskCreationRequestPayload(
            request.Title.Trim(), request.Description?.Trim(), request.TaskType, request.Priority,
            request.DueDate, request.EstimatedHours, request.StoryPoints);

        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var now = DateTimeOffset.UtcNow;
            var entity = new TaskCreationRequest
            {
                Id = Guid.NewGuid(), TenantId = tenantId, ObjectiveId = objective.Id,
                RequestedByEmployeeId = callerEmployeeId.Value, PayloadJson = JsonSerializer.Serialize(payload),
                Status = TaskCreationRequestStatuses.Pending, CreatedById = _currentUser.UserId, CreatedAt = now
            };

            await _requests.AddAsync(entity, innerCt);
            await _unitOfWork.SaveChangesAsync(innerCt);

            return Result<TaskCreationRequestResponse>.Success(new TaskCreationRequestResponse(entity.Id, entity.ObjectiveId, entity.Status, payload, entity.CreatedAt));
        }, ct);
    }
}
```

- [ ] **Step 5: Run tests, verify PASS. Step 6: Commit.**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/CreateTaskCreationRequest/ src/ONEVO.Application/Features/WorkManagement/Tasks/DTOs/Responses/WorkTaskResponse.cs src/ONEVO.Application/Features/WorkManagement/Objectives/Services/ tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/CreateTaskCreationRequestCommandHandlerTests.cs
git commit -m "feat(work): CreateTaskCreationRequest command - member submits, owner-routed"
```

### Task 3: `Approve`/`Reject`/`Cancel` for task creation requests

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/ApproveTaskCreationRequest/{ApproveTaskCreationRequestCommand,ApproveTaskCreationRequestCommandHandler}.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/RejectTaskCreationRequest/{RejectTaskCreationRequestCommand,RejectTaskCreationRequestCommandHandler}.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/CancelTaskCreationRequest/{CancelTaskCreationRequestCommand,CancelTaskCreationRequestCommandHandler}.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/ApproveTaskCreationRequestCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `IObjectiveAllocationSlackCalculator` (Part 1 Task 6), `IWorkTaskRepository.AddAsync`, `ITaskStatusRepository` (for default status resolution, same as `CreateTaskCommandHandler`).
- Produces: `ApproveTaskCreationRequestCommand(Guid RequestId) : IRequest<Result<WorkTaskResponse>>`, `RejectTaskCreationRequestCommand(Guid RequestId, string Comment) : IRequest<Result>`, `CancelTaskCreationRequestCommand(Guid RequestId) : IRequest<Result>`.

- [ ] **Step 1: Write the failing tests — happy-path approve, slack-exceeded-at-approval-time (the interesting case per spec §3.3: "re-checks slack at decision time, not creation-request time"), and non-owner-forbidden.**

```csharp
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.ApproveTaskCreationRequest;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class ApproveTaskCreationRequestCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid OwnerEmployeeId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();
    private static readonly Guid RequestId = Guid.NewGuid();
    private static readonly Guid DefaultStatusId = Guid.NewGuid();

    private (ApproveTaskCreationRequestCommandHandler Handler, Mock<IWorkTaskRepository> Tasks, Mock<ITaskCreationRequestRepository> Requests) Build(
        decimal allocatedHours, decimal existingTaskSum, decimal requestedHours)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync(OwnerEmployeeId);

        var payload = new Application.Features.WorkManagement.Tasks.DTOs.TaskCreationRequestPayload("Title", null, "task", "medium", null, requestedHours, null);
        var pendingRequest = new TaskCreationRequest
        {
            Id = RequestId, TenantId = TenantId, ObjectiveId = ObjectiveId, RequestedByEmployeeId = Guid.NewGuid(),
            PayloadJson = System.Text.Json.JsonSerializer.Serialize(payload), Status = TaskCreationRequestStatuses.Pending,
            CreatedById = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow
        };

        var requests = new Mock<ITaskCreationRequestRepository>();
        requests.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, RequestId, It.IsAny<CancellationToken>())).ReturnsAsync(pendingRequest);

        var objective = new Objective { Id = ObjectiveId, TenantId = TenantId, OwnerId = OwnerEmployeeId, AllocatedHours = allocatedHours, IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>())).ReturnsAsync(objective);
        objectives.Setup(x => x.GetTrackedActiveDirectChildrenAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<Objective>());

        var tasks = new Mock<IWorkTaskRepository>();
        tasks.Setup(x => x.GetActiveAllocationSumByObjectiveIdAsync(TenantId, ObjectiveId, null, It.IsAny<CancellationToken>())).ReturnsAsync(existingTaskSum);

        var statuses = new Mock<ITaskStatusRepository>();
        statuses.Setup(x => x.GetByObjectiveIdAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Domain.Features.WorkManagement.Tasks.Entities.TaskStatus>
            {
                new() { Id = DefaultStatusId, TenantId = TenantId, ObjectiveId = ObjectiveId, Name = "To Do", DisplayOrder = 0, CreatedAt = DateTimeOffset.UtcNow }
            });

        var slack = new ObjectiveAllocationSlackCalculator(objectives.Object, tasks.Object);

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result<WorkTaskResponse>>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result<WorkTaskResponse>>> op, CancellationToken ct) => op(ct));

        var handler = new ApproveTaskCreationRequestCommandHandler(currentUser.Object, identity.Object, requests.Object, objectives.Object, tasks.Object, statuses.Object, slack, unitOfWork.Object);
        return (handler, tasks, requests);
    }

    [Fact]
    public async Task Handle_OwnerWithinSlack_ApprovesAndCreatesTask()
    {
        var (handler, tasks, requests) = Build(allocatedHours: 100m, existingTaskSum: 40m, requestedHours: 30m);
        var result = await handler.Handle(new ApproveTaskCreationRequestCommand(RequestId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        tasks.Verify(x => x.AddAsync(It.IsAny<Domain.Features.WorkManagement.Tasks.Entities.WorkTask>(), It.IsAny<CancellationToken>()), Times.Once);
        requests.Verify(x => x.Update(It.Is<TaskCreationRequest>(r => r.Status == TaskCreationRequestStatuses.Approved && r.CreatedTaskId != null)), Times.Once);
    }

    [Fact]
    public async Task Handle_SlackChangedSinceRequestCreated_ReturnsConflict()
    {
        // 60 slack when request was created (allocated 100 - 40 used), but now only 10 slack (allocated 100 - 90 used) - simulates another task consuming allocation in the meantime.
        var (handler, tasks, _) = Build(allocatedHours: 100m, existingTaskSum: 90m, requestedHours: 30m);
        var result = await handler.Handle(new ApproveTaskCreationRequestCommand(RequestId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        tasks.Verify(x => x.AddAsync(It.IsAny<Domain.Features.WorkManagement.Tasks.Entities.WorkTask>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
```

- [ ] **Step 2: Run test, verify FAIL.**

- [ ] **Step 3: Write `ApproveTaskCreationRequestCommand`/`Handler`** — re-checks slack at decision time per spec §3.3, creates the `WorkTask` from `PayloadJson`, resolves default `StatusId` the same way `CreateTaskCommandHandler` does (Part 1 Task 10 Step 4):

```csharp
// src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/ApproveTaskCreationRequest/ApproveTaskCreationRequestCommand.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.ApproveTaskCreationRequest;

public sealed record ApproveTaskCreationRequestCommand(Guid RequestId) : IRequest<Result<WorkTaskResponse>>;
```

```csharp
// src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/ApproveTaskCreationRequest/ApproveTaskCreationRequestCommandHandler.cs
using System.Text.Json;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.ApproveTaskCreationRequest;

public class ApproveTaskCreationRequestCommandHandler : IRequestHandler<ApproveTaskCreationRequestCommand, Result<WorkTaskResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly ITaskCreationRequestRepository _requests;
    private readonly IObjectiveRepository _objectives;
    private readonly IWorkTaskRepository _tasks;
    private readonly ITaskStatusRepository _statuses;
    private readonly IObjectiveAllocationSlackCalculator _slack;
    private readonly IUnitOfWork _unitOfWork;

    public ApproveTaskCreationRequestCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, ITaskCreationRequestRepository requests,
        IObjectiveRepository objectives, IWorkTaskRepository tasks, ITaskStatusRepository statuses,
        IObjectiveAllocationSlackCalculator slack, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _identity = identity;
        _requests = requests;
        _objectives = objectives;
        _tasks = tasks;
        _statuses = statuses;
        _slack = slack;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<WorkTaskResponse>> Handle(ApproveTaskCreationRequestCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<WorkTaskResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, _currentUser.UserId, ct);
        if (callerEmployeeId is null)
            return Result<WorkTaskResponse>.Forbidden("No employee record for the current user.");

        var pending = await _requests.GetTrackedByIdForTenantAsync(tenantId, request.RequestId, ct);
        if (pending is null)
            return Result<WorkTaskResponse>.NotFound("Request not found.");

        if (pending.Status != TaskCreationRequestStatuses.Pending)
            return Result<WorkTaskResponse>.Conflict("This request has already been decided.");

        var objective = await _objectives.GetByIdForTenantAsync(tenantId, pending.ObjectiveId, ct);
        if (objective is null)
            return Result<WorkTaskResponse>.NotFound("Objective not found.");

        if (objective.OwnerId != callerEmployeeId.Value)
            return Result<WorkTaskResponse>.Forbidden("Only this milestone's owner can decide this request.");

        var payload = JsonSerializer.Deserialize<TaskCreationRequestPayload>(pending.PayloadJson)!;

        if (payload.EstimatedHours.HasValue)
        {
            var slack = await _slack.CalculateAsync(tenantId, objective, ct: ct);
            if (payload.EstimatedHours.Value > slack)
                return Result<WorkTaskResponse>.Conflict(
                    JsonSerializer.Serialize(new InsufficientAllocationResponse(slack)));
        }

        var statuses = await _statuses.GetByObjectiveIdAsync(tenantId, objective.Id, ct);
        var defaultStatus = statuses.Where(s => !s.MarksTaskComplete).OrderBy(s => s.DisplayOrder).FirstOrDefault();
        if (defaultStatus is null)
            return Result<WorkTaskResponse>.Failure("No task statuses configured for this milestone yet.", 422);

        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var now = DateTimeOffset.UtcNow;
            var task = new WorkTask
            {
                Id = Guid.NewGuid(), TenantId = tenantId, ProjectId = objective.ProjectId, ObjectiveId = objective.Id,
                ShortId = $"TASK-{Guid.NewGuid():N}".Substring(0, 12), // same follow-up as Part 1 Task 9 - wire real numbering here too
                Title = payload.Title, Description = payload.Description, TaskType = payload.TaskType,
                Priority = payload.Priority, DueDate = payload.DueDate, EstimatedHours = payload.EstimatedHours,
                StoryPoints = payload.StoryPoints, StatusId = defaultStatus.Id, CompletedHours = 0m,
                ProgressPercent = 0, CreatedById = _currentUser.UserId, CreatedAt = now
            };

            await _tasks.AddAsync(task, innerCt);

            pending.Status = TaskCreationRequestStatuses.Approved;
            pending.DecidedByEmployeeId = callerEmployeeId.Value;
            pending.DecidedAt = now;
            pending.CreatedTaskId = task.Id;
            pending.UpdatedAt = now;
            _requests.Update(pending);

            await _unitOfWork.SaveChangesAsync(innerCt);

            return Result<WorkTaskResponse>.Success(new WorkTaskResponse(
                task.Id, task.ObjectiveId, task.ShortId, task.Title, task.Description,
                task.TaskType, task.StatusId, task.Priority, task.StoryPoints,
                task.DueDate, task.EstimatedHours, task.CompletedHours, task.ProgressPercent));
        }, ct);
    }
}
```

**Follow-up note:** apply Part 1 Task 9's real `ShortId` scheme here too once that task lands — same `IProjectRepository.IncrementAndGetNextTaskNumberAsync` call, inserted the same way.

- [ ] **Step 4: Write `RejectTaskCreationRequestCommand`/`Handler`** (owner-only, requires non-empty `Comment`, sets `Status = Rejected`, `DecisionComment`, no task created — mirror the shape of `ApproveTaskCreationRequestCommandHandler`'s auth checks minus the slack/task-creation logic) and `CancelTaskCreationRequestCommand`/`Handler` (requester-only, only while `Pending`, sets `Status = Cancelled`).

- [ ] **Step 5: Write tests for both (happy path + wrong-actor-forbidden for each), run all Task 3 tests, verify PASS.**

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/ApproveTaskCreationRequest/ src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/RejectTaskCreationRequest/ src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/CancelTaskCreationRequest/ tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/ApproveTaskCreationRequestCommandHandlerTests.cs
git commit -m "feat(work): Approve/Reject/Cancel task creation request commands"
```

### Task 4: `GetMyTaskCreationRequests` (owner queue) query + Controller wiring + Postman docs

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/Tasks/Queries/GetMyTaskCreationRequests/{GetMyTaskCreationRequestsQuery,GetMyTaskCreationRequestsQueryHandler}.cs`
- Create: `src/ONEVO.Api/Contracts/WorkManagement/Tasks/TaskCreationRequestContracts.cs` (Request/ViewModel + Mapper, mirroring Part 1 Task 11's Contracts pattern)
- Modify: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/TasksController.cs`
- Create: `docs/postman-request/Work Management/Create Task Creation Request.md`, `Approve Task Creation Request.md`, `Reject Task Creation Request.md`, `Cancel Task Creation Request.md`, `My Task Creation Requests.md`

**Interfaces:**
- Produces: `GetMyTaskCreationRequestsQuery : IRequest<Result<IReadOnlyList<TaskCreationRequestResponse>>>` (uses `ITaskCreationRequestRepository.GetPendingForOwnerEmployeeIdAsync`, Part 2 Task 1).

- [ ] **Step 1: Write the query/handler + a happy-path test**, following the exact structure of Part 1 Task 7's `GetObjectiveTasksQueryHandler`.

- [ ] **Step 2: Add five routes to `TasksController`:**

```csharp
[HttpPost("objectives/{objectiveId:guid}/task-creation-requests")]
public async Task<IActionResult> CreateRequest(Guid objectiveId, [FromBody] CreateTaskCreationRequestRequest request, CancellationToken ct)
{
    var result = await _mediator.Send(new CreateTaskCreationRequestCommand(
        objectiveId, request.Title, request.Description, request.TaskType, request.Priority,
        request.DueDate, request.EstimatedHours, request.StoryPoints), ct);

    return result.IsSuccess
        ? StatusCode(202, result.Value!.ToViewModel())
        : Problem(result.Error, statusCode: result.StatusCode ?? 400);
}

[HttpPost("task-creation-requests/{id:guid}/approve")]
public async Task<IActionResult> ApproveRequest(Guid id, CancellationToken ct)
{
    var result = await _mediator.Send(new ApproveTaskCreationRequestCommand(id), ct);

    return result.IsSuccess
        ? StatusCode(201, result.Value!.ToViewModel())
        : Problem(result.Error, statusCode: result.StatusCode ?? 400);
}

[HttpPost("task-creation-requests/{id:guid}/reject")]
public async Task<IActionResult> RejectRequest(Guid id, [FromBody] RejectTaskCreationRequestRequest request, CancellationToken ct)
{
    var result = await _mediator.Send(new RejectTaskCreationRequestCommand(id, request.Comment), ct);

    return result.IsSuccess ? NoContent() : Problem(result.Error, statusCode: result.StatusCode ?? 400);
}

[HttpPost("task-creation-requests/{id:guid}/cancel")]
public async Task<IActionResult> CancelRequest(Guid id, CancellationToken ct)
{
    var result = await _mediator.Send(new CancelTaskCreationRequestCommand(id), ct);

    return result.IsSuccess ? NoContent() : Problem(result.Error, statusCode: result.StatusCode ?? 400);
}

[HttpGet("task-creation-requests/mine")]
public async Task<IActionResult> MyRequests(CancellationToken ct)
{
    var result = await _mediator.Send(new GetMyTaskCreationRequestsQuery(), ct);

    return result.IsSuccess
        ? Ok(result.Value!.Select(r => r.ToViewModel()).ToList())
        : Problem(result.Error, statusCode: result.StatusCode ?? 400);
}
```

(No `[RequirePermission]` on `CreateRequest`/`ApproveRequest`/etc. beyond the module base gate — same reasoning as `ObjectivesController.AcceptInvitation`: the request lifecycle's own in-handler checks, not a module permission, gate who can act.)

- [ ] **Step 3: Write the 5 Postman docs, update `docs/postman-request/README.md`'s index.**

- [ ] **Step 4: Run the full Work Management test suite, verify PASS. Step 5: Commit.**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Tasks/Queries/GetMyTaskCreationRequests/ src/ONEVO.Api/Contracts/WorkManagement/Tasks/TaskCreationRequestContracts.cs src/ONEVO.Api/Controllers/Tenant/WorkManagement/TasksController.cs docs/postman-request/Work\ Management/ docs/postman-request/README.md tests/
git commit -m "feat(work): task creation request queue endpoint, controller wiring, Postman docs"
```

## Part 2 complete
