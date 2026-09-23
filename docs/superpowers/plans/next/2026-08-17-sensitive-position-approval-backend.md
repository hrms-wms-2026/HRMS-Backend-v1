# Sensitive Position Approval — Backend Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Route "Change Position" through the existing `AccessGrantRequest` approval machinery when the target position is sensitive, gate approval on `roles:manage` (not per-record occupancy), block an employee from ever changing their own position, and notify approvers by email.

**Architecture:** `ChangeEmployeePositionCommandHandler` gains a branch: non-sensitive stays exactly as shipped (transaction: end old assignment, create new active one); sensitive instead reserves the seat as `Planned` and creates an `AccessGrantRequest` (`ActionType = position_change_access`), leaving the old assignment untouched until approved. `ApproveAccessGrantRequestCommandHandler`/`RejectAccessGrantRequestCommandHandler` branch on `ActionType` to handle both the pre-existing onboarding case and the new position-change case, both now gated by `roles:manage` plus an in-handler self-approval check.

**Tech Stack:** .NET (C#), EF Core, MediatR, FluentValidation, xUnit + Moq, Testcontainers.

## Global Constraints

- Built on top of the finished multi-legal-entity-employment-foundation and employee-detail-screen plans — `TryReservePositionAssignmentAsync`, `ActivatePlannedAsync`, `CancelPlannedAsync`, `TryCreateActiveAssignmentAsync`, `EndActiveAsync`, `GetActivePrimaryAsync` all already exist on `IPositionAssignmentRepository` exactly as used below (confirmed against current code, not guessed).
- `ChangeEmployeePositionCommandHandler`'s existing non-sensitive path uses `IUnitOfWork.ExecuteInTransactionAsync` with an end-then-create-active sequence and a `PositionAtCapacityException`/`UniqueConstraintConflictException` catch pattern — do not restructure this path, only add a branch before it.
- `roles:manage` (`PermissionSeeder.cs` line 172) is the approval permission — no new permission is seeded by this plan.
- Snake_case DB column naming (EF Core convention already configured project-wide).

---

### Task 1: `IPermissionRepository.ListUserIdsWithPermissionCodeAsync`

**Files:**
- Modify: `src/ONEVO.Application/Features/Auth/Permission/RepositoryInterfaces/IPermissionRepository.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/Auth/Login/EfAuthRepository.cs`
- Test: `tests/ONEVO.Tests.Integration/Auth/Permission/ListUserIdsWithPermissionCodeAsyncTests.cs` (create)

**Interfaces:**
- Produces: `Task<IReadOnlyList<Guid>> ListUserIdsWithPermissionCodeAsync(Guid tenantId, string permissionCode, DateTimeOffset now, CancellationToken ct = default)`.

- [ ] **Step 1: Write the failing integration test**

Follow the existing pattern from `ListRolePermissionCodesWithModulesEntityFilterTests.cs` (same folder, from the multi-legal-entity-foundation plan) for base class/seeding helpers:

```csharp
using ONEVO.Infrastructure.Persistence.Repositories.Auth.Login;
using Xunit;

namespace ONEVO.Tests.Integration.Auth.Permission;

public class ListUserIdsWithPermissionCodeAsyncTests : IntegrationTestBase
{
    [Fact]
    public async Task ReturnsEveryUserHoldingThePermission_WithinTheTenant()
    {
        var tenantId = await SeedTenantAsync();
        var otherTenantId = await SeedTenantAsync();
        var roleId = await SeedRoleWithPermissionAsync(tenantId, "roles:manage");
        var userA = await SeedUserWithRoleAsync(tenantId, roleId);
        var userB = await SeedUserWithRoleAsync(tenantId, roleId);
        var userWithoutRole = await SeedUserAsync(tenantId);
        var otherTenantRoleId = await SeedRoleWithPermissionAsync(otherTenantId, "roles:manage");
        await SeedUserWithRoleAsync(otherTenantId, otherTenantRoleId);

        var repo = new EfAuthRepository(Db);
        var result = await repo.ListUserIdsWithPermissionCodeAsync(tenantId, "roles:manage", DateTimeOffset.UtcNow);

        Assert.Contains(userA, result);
        Assert.Contains(userB, result);
        Assert.DoesNotContain(userWithoutRole, result);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task ExcludesExpiredUserRoles()
    {
        var tenantId = await SeedTenantAsync();
        var roleId = await SeedRoleWithPermissionAsync(tenantId, "roles:manage");
        var expiredUser = await SeedUserWithRoleAsync(tenantId, roleId, expiresAt: DateTimeOffset.UtcNow.AddDays(-1));

        var repo = new EfAuthRepository(Db);
        var result = await repo.ListUserIdsWithPermissionCodeAsync(tenantId, "roles:manage", DateTimeOffset.UtcNow);

        Assert.DoesNotContain(expiredUser, result);
    }
}
```

Adjust `SeedTenantAsync`/`SeedRoleWithPermissionAsync`/`SeedUserWithRoleAsync`/`SeedUserAsync` to this repo's real integration-test helper names — read an existing file in `tests/ONEVO.Tests.Integration/Auth/Permission/` first (created in the multi-legal-entity-foundation plan) to match exactly.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Integration/ONEVO.Tests.Integration.csproj --filter "FullyQualifiedName~ListUserIdsWithPermissionCodeAsync"`
Expected: FAIL (build error — method doesn't exist)

- [ ] **Step 3: Add the interface method**

In `IPermissionRepository.cs`, add:

```csharp
    /// <summary>Every user in the tenant currently holding permissionCode via an
    /// unexpired UserRole, regardless of which role grants it. Used to resolve who can
    /// approve a sensitive AccessGrantRequest (roles:manage) - the inverse of
    /// ListRolePermissionCodesWithModulesAsync, which goes user -> permissions.</summary>
    Task<IReadOnlyList<Guid>> ListUserIdsWithPermissionCodeAsync(
        Guid tenantId, string permissionCode, DateTimeOffset now, CancellationToken ct = default);
```

- [ ] **Step 4: Implement it**

In `EfAuthRepository.cs`, add near `ListUserIdsByRoleAsync` (mirror its shape — `.AsNoTracking()`, `.Distinct()`, join through `Users` to scope by tenant since `UserRoles` itself has no `TenantId` column visible from the earlier research):

```csharp
    public async Task<IReadOnlyList<Guid>> ListUserIdsWithPermissionCodeAsync(
        Guid tenantId, string permissionCode, DateTimeOffset now, CancellationToken ct = default)
    {
        var query = _db.UserRoles
            .AsNoTracking()
            .Where(ur => (ur.ExpiresAt == null || ur.ExpiresAt > now))
            .Join(_db.RolePermissions, ur => ur.RoleId, rp => rp.RoleId, (ur, rp) => new { ur, rp })
            .Join(_db.Permissions, x => x.rp.PermissionId, p => p.Id, (x, p) => new { x.ur, p })
            .Where(x => x.p.Code == permissionCode)
            .Join(_db.Users, x => x.ur.UserId, u => u.Id, (x, u) => new { u.Id, u.TenantId })
            .Where(x => x.TenantId == tenantId)
            .Select(x => x.Id)
            .Distinct();

        return await query.ToListAsync(ct);
    }
```

(If `UserRoles` already carries its own `TenantId` column — check the entity before writing this — filter on `ur.TenantId == tenantId` directly instead of joining through `Users`, it's cheaper. Use whichever is actually true of the schema.)

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Integration/ONEVO.Tests.Integration.csproj --filter "FullyQualifiedName~ListUserIdsWithPermissionCodeAsync"`
Expected: PASS (both tests)

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Application/Features/Auth/Permission/RepositoryInterfaces/IPermissionRepository.cs src/ONEVO.Infrastructure/Persistence/Repositories/Auth/Login/EfAuthRepository.cs tests/ONEVO.Tests.Integration/Auth/Permission/ListUserIdsWithPermissionCodeAsyncTests.cs
git commit -m "feat: add IPermissionRepository.ListUserIdsWithPermissionCodeAsync"
```

---

### Task 2: Schema — `AccessGrantRequest` and `PositionAssignment` new columns, `ActionType` constant

**Files:**
- Modify: `src/ONEVO.Domain/Features/CoreHr/Entities/AccessGrantRequest.cs`
- Modify: `src/ONEVO.Domain/Features/CoreHr/Entities/PositionAssignment.cs` (confirmed path from prior plans: `Domain/Features/CoreHr/PositionAssignment/Entities/PositionAssignment.cs` — verify exact path before editing, prior plans disagree slightly on it, use whichever the file search turns up)
- Create: EF Core migration

- [ ] **Step 1: Add the `ActionType` constant and two new properties to `AccessGrantRequest`**

```csharp
public static class AccessGrantActionType
{
    // ActionType is character varying(30); this is 26 characters.
    public const string EmployeeOnboarding = "onboarding_position_access";
    // 22 characters.
    public const string PositionChange = "position_change_access";
}
```

Add to the `AccessGrantRequest` class, directly under `EffectiveTo`:

```csharp
    public Guid? ReservedPositionAssignmentId { get; set; }
    public string? ChangeReason { get; set; }
```

- [ ] **Step 2: Add `ChangeReason` to `PositionAssignment`**

```csharp
    public string? ChangeReason { get; set; }
```

- [ ] **Step 3: Generate the migration**

```bash
dotnet ef migrations add AddPositionChangeApprovalFields --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api
```

- [ ] **Step 4: Inspect the generated migration**

Confirm it contains exactly: `AddColumn<Guid>("reserved_position_assignment_id", "access_grant_requests", nullable: true)`, `AddColumn<string>("change_reason", "access_grant_requests", type: "character varying(20)", maxLength: 20, nullable: true)`, `AddColumn<string>("change_reason", "position_assignments", type: "character varying(20)", maxLength: 20, nullable: true)` (or the EF Core default `text` type if no `[MaxLength]`/Fluent config is added — either is fine, this plan doesn't require a length constraint, just be consistent with how this repo already handles similar string columns elsewhere in `AccessGrantRequest`, e.g. `ApprovalStatus`). Stop and investigate if anything else appears.

- [ ] **Step 5: Apply and verify**

```bash
dotnet ef database update --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api
```
Expected: succeeds.

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Domain/Features/CoreHr/Entities/AccessGrantRequest.cs src/ONEVO.Domain/Features/CoreHr/PositionAssignment/Entities/PositionAssignment.cs src/ONEVO.Infrastructure/Migrations/
git commit -m "feat: add AccessGrantRequest.ReservedPositionAssignmentId/ChangeReason and PositionAssignment.ChangeReason"
```

---

### Task 3: `ChangeEmployeePositionCommandHandler` — self-block + sensitive branch

**Files:**
- Modify: `src/ONEVO.Application/Features/CoreHr/Employee/Commands/ChangeEmployeePosition/ChangeEmployeePositionCommandHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/ChangeEmployeePositionCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `IPermissionRepository.ListUserIdsWithPermissionCodeAsync` (Task 1), `IAccessGrantRequestRepository.AddAsync`, `IPositionAssignmentRepository.TryReservePositionAssignmentAsync` (already exists), `IPositionRepository.GetAccessTemplateByPositionAsync` (already exists, used elsewhere).
- Produces: the handler now returns a distinguishable "pending approval" outcome — since `Result<Unit>.Success(Unit.Value)` is the same shape either way, add a new response type so the controller/frontend can tell the two apart (see Step 3 below).

- [ ] **Step 1: Write the failing unit tests**

Add to `ChangeEmployeePositionCommandHandlerTests.cs`, matching the existing file's mock/constructor setup exactly (read it first — it currently mocks `IEmployeeRepository, IPositionRepository, IPositionAssignmentRepository, IUnitOfWork, ICurrentUser`; add `IPermissionRepository` and `IAccessGrantRequestRepository` mocks alongside):

```csharp
    [Fact]
    public async Task Handle_TargetIsCallersOwnEmployee_ReturnsForbidden()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        _currentUser.Setup(c => c.TenantId).Returns(tenantId);
        _currentUser.Setup(c => c.UserId).Returns(userId);
        _employeeRepository
            .Setup(e => e.GetTrackedByIdAsync(tenantId, employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = employeeId, TenantId = tenantId, UserId = userId, LegalEntityId = Guid.NewGuid() });

        var handler = CreateHandler();
        var result = await handler.Handle(
            new ChangeEmployeePositionCommand(employeeId, Guid.NewGuid(), DateOnly.FromDateTime(DateTime.UtcNow)),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        Assert.Equal("You cannot change your own position.", result.Error);
        _positionRepository.Verify(p => p.GetByIdForLegalEntityAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_SensitivePosition_NoApprovers_ReturnsUnprocessable()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var positionId = Guid.NewGuid();
        SetupNonSelfCaller(tenantId, employeeId);
        _positionRepository.Setup(p => p.GetByIdForLegalEntityAsync(tenantId, It.IsAny<Guid>(), positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Position { Id = positionId, TenantId = tenantId, IsActive = true, DepartmentId = Guid.NewGuid() });
        _positionRepository.Setup(p => p.GetAccessTemplateByPositionAsync(tenantId, positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PositionAccessTemplate { Id = Guid.NewGuid(), RequiresApproval = true, RoleId = Guid.NewGuid() });
        _permissionRepository.Setup(p => p.ListUserIdsWithPermissionCodeAsync(tenantId, "roles:manage", It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid>());

        var handler = CreateHandler();
        var result = await handler.Handle(
            new ChangeEmployeePositionCommand(employeeId, positionId, DateOnly.FromDateTime(DateTime.UtcNow)),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(422, result.StatusCode);
    }

    [Fact]
    public async Task Handle_SensitivePosition_CreatesAccessGrantRequest_DoesNotTouchCurrentAssignment()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var positionId = Guid.NewGuid();
        var approverUserId = Guid.NewGuid();
        SetupNonSelfCaller(tenantId, employeeId);
        var accessTemplate = new PositionAccessTemplate { Id = Guid.NewGuid(), RequiresApproval = true, RoleId = Guid.NewGuid() };
        _positionRepository.Setup(p => p.GetByIdForLegalEntityAsync(tenantId, It.IsAny<Guid>(), positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Position { Id = positionId, TenantId = tenantId, IsActive = true, DepartmentId = Guid.NewGuid() });
        _positionRepository.Setup(p => p.GetAccessTemplateByPositionAsync(tenantId, positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(accessTemplate);
        _permissionRepository.Setup(p => p.ListUserIdsWithPermissionCodeAsync(tenantId, "roles:manage", It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { approverUserId });
        _positionAssignmentRepository
            .Setup(a => a.TryReservePositionAssignmentAsync(tenantId, employeeId, positionId, It.IsAny<DateOnly>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        var handler = CreateHandler();
        var result = await handler.Handle(
            new ChangeEmployeePositionCommand(employeeId, positionId, DateOnly.FromDateTime(DateTime.UtcNow)),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        _accessGrantRequestRepository.Verify(r => r.AddAsync(
            It.Is<AccessGrantRequest>(g => g.ActionType == AccessGrantActionType.PositionChange && g.EmployeeId == employeeId),
            It.IsAny<CancellationToken>()), Times.Once);
        _positionAssignmentRepository.Verify(a => a.EndActiveAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Never);
    }
```

Add a `SetupNonSelfCaller(Guid tenantId, Guid employeeId)` private helper if the file doesn't already have one, that sets `_currentUser.UserId` to a *different* guid than the target employee's `UserId`, and stubs `_employeeRepository.GetTrackedByIdAsync` to return an `Employee` whose `UserId` differs from the caller.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~ChangeEmployeePositionCommandHandlerTests"`
Expected: FAIL (build errors — new mocks/types don't exist on the handler yet)

- [ ] **Step 3: Add the response type**

New file, `src/ONEVO.Application/Features/CoreHr/Employee/Commands/ChangeEmployeePosition/ChangeEmployeePositionResponse.cs`:

```csharp
namespace ONEVO.Application.Features.CoreHr.Employee.Commands.ChangeEmployeePosition;

public sealed record ChangeEmployeePositionResponse(bool PendingApproval);
```

Change the command's `IRequest<Result<Unit>>` to `IRequest<Result<ChangeEmployeePositionResponse>>` (and every `Result<Unit>.X(...)` call site in the handler to `Result<ChangeEmployeePositionResponse>.X(...)`, with the two success returns becoming `Result<ChangeEmployeePositionResponse>.Success(new ChangeEmployeePositionResponse(PendingApproval: false))` for the existing immediate path and `... PendingApproval: true)` for the new sensitive path). Update the controller action in `EmployeesController.cs`'s `ChangePosition` method to `Ok(result.Value)` instead of `NoContent()` on success, since the frontend now needs to read `PendingApproval` from the body.

- [ ] **Step 4: Add the constructor dependencies and the two new checks/branch**

Add `IPermissionRepository _permissionRepository` and `IAccessGrantRequestRepository _accessGrantRequestRepository` (plus `IDateTimeProvider _clock` and `IOutboxWriter _outboxWriter` for Task 5's notification, add now to avoid a second constructor-signature change) to the constructor, matching the existing parameter-then-field-assignment style.

At the very top of `Handle`, immediately after loading `employee` and confirming it's not null:

```csharp
        if (employee.UserId == _currentUser.UserId)
            return Result<ChangeEmployeePositionResponse>.Forbidden("You cannot change your own position.");
```

After loading `position` and confirming it's active, insert the sensitive branch before the existing `currentAssignment`/transaction block:

```csharp
        var accessTemplate = await _positionRepository.GetAccessTemplateByPositionAsync(tenantId, position.Id, ct);
        if (accessTemplate is { RequiresApproval: true })
        {
            var approverUserIds = await _permissionRepository.ListUserIdsWithPermissionCodeAsync(
                tenantId, "roles:manage", _clock.UtcNow, ct);
            if (approverUserIds.Count == 0)
                return Result<ChangeEmployeePositionResponse>.UnprocessableEntity(
                    "No one currently holds the permission required to approve this request.");

            var reservedAssignmentId = await _positionAssignmentRepository.TryReservePositionAssignmentAsync(
                tenantId, employee.Id, position.Id, request.EffectiveFrom, _currentUser.UserId, ct);
            if (reservedAssignmentId is null)
                return Result<ChangeEmployeePositionResponse>.Conflict("This position has reached its capacity.");

            var grantRequest = new AccessGrantRequest
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                EmployeeId = employee.Id,
                ActionType = AccessGrantActionType.PositionChange,
                TargetPositionId = position.Id,
                TargetDepartmentId = position.DepartmentId!.Value,
                PositionAccessTemplateId = accessTemplate.Id,
                RequestedRoleId = accessTemplate.RoleId,
                ApprovalStatus = "Pending",
                RequestedByUserId = _currentUser.UserId,
                RequestedAt = _clock.UtcNow,
                EffectiveFrom = request.EffectiveFrom.ToDateTime(TimeOnly.MinValue),
                ReservedPositionAssignmentId = reservedAssignmentId,
                ChangeReason = request.ChangeReason,
            };
            await _accessGrantRequestRepository.AddAsync(grantRequest, ct);
            await _unitOfWork.SaveChangesAsync(ct);

            foreach (var approverUserId in approverUserIds)
                await EnqueuePositionChangeApprovalEmailAsync(tenantId, approverUserId, grantRequest, employee, position, ct);

            return Result<ChangeEmployeePositionResponse>.Success(new ChangeEmployeePositionResponse(PendingApproval: true));
        }
```

`EnqueuePositionChangeApprovalEmailAsync` is defined in Task 5 — leave it as a private method stub returning `Task.CompletedTask` for now if Task 5 hasn't landed yet in a strict task-by-task execution, or implement both together; either is fine since they're in the same file.

`request.ChangeReason` requires adding `string ChangeReason` to `ChangeEmployeePositionCommand` — add it now: `public sealed record ChangeEmployeePositionCommand(Guid EmployeeId, Guid PositionId, DateOnly EffectiveFrom, string ChangeReason) : IRequest<Result<ChangeEmployeePositionResponse>>;`, and add `RuleFor(x => x.ChangeReason).Must(r => r is "Promotion" or "Transfer" or "LateralMove")` to the validator. Update `ChangePositionRequest` (`src/ONEVO.Api/Contracts/CoreHr/Employees/ChangePositionRequest.cs`) to `public sealed record ChangePositionRequest(Guid PositionId, DateOnly EffectiveFrom, string ChangeReason);` and thread it through `EmployeesController.ChangePosition`.

Change every remaining `Result<Unit>` in the existing (non-sensitive) path to `Result<ChangeEmployeePositionResponse>`, and its final success line to `Result<ChangeEmployeePositionResponse>.Success(new ChangeEmployeePositionResponse(PendingApproval: false))`.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~ChangeEmployeePositionCommandHandlerTests"`
Expected: PASS (all tests — update every pre-existing test in this file for the new `ChangeReason` command parameter and the `Result<ChangeEmployeePositionResponse>` return type)

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Application/Features/CoreHr/Employee/Commands/ChangeEmployeePosition/ src/ONEVO.Api/Contracts/CoreHr/Employees/ChangePositionRequest.cs src/ONEVO.Api/Controllers/Tenant/CoreHr/EmployeesController.cs tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/ChangeEmployeePositionCommandHandlerTests.cs
git commit -m "feat: block self-position-change, route sensitive positions through approval"
```

---

### Task 4: `ApproveAccessGrantRequestCommandHandler` / `RejectAccessGrantRequestCommandHandler` — self-approval check + `PositionChange` branch

**Files:**
- Modify: `src/ONEVO.Application/Features/CoreHr/Onboarding/Commands/ApproveAccessGrantRequest/ApproveAccessGrantRequestCommandHandler.cs`
- Modify: `src/ONEVO.Application/Features/CoreHr/Onboarding/Commands/RejectAccessGrantRequest/RejectAccessGrantRequestCommandHandler.cs`
- Modify: `src/ONEVO.Api/Controllers/Tenant/CoreHr/AccessGrantRequestsController.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/CoreHr/Onboarding/ApproveAccessGrantRequestCommandHandlerTests.cs`, `tests/ONEVO.Tests.Unit/Features/CoreHr/Onboarding/RejectAccessGrantRequestCommandHandlerTests.cs` (find or create the latter)

**Interfaces:**
- Consumes: `IPositionAssignmentRepository.ActivatePlannedAsync`/`CancelPlannedAsync`/`GetActivePrimaryAsync`/`EndActiveAsync` (all already exist).

- [ ] **Step 1: Write the failing unit tests**

Add to `ApproveAccessGrantRequestCommandHandlerTests.cs` (match the file's existing mock/constructor pattern — it has 19 dependencies already; add `IPositionAssignmentRepository` if not already present, it should already be there per the existing capacity check):

```csharp
    [Fact]
    public async Task Handle_CallerIsTheRequester_ReturnsForbidden()
    {
        var callerId = Guid.NewGuid();
        var grantRequest = BuildPendingGrantRequest(requestedByUserId: callerId); // adjust to existing helper naming
        _currentUser.Setup(c => c.UserId).Returns(callerId);
        _accessGrantRequestRepository.Setup(r => r.GetTrackedByIdAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(grantRequest);

        var handler = CreateHandler();
        var result = await handler.Handle(new ApproveAccessGrantRequestCommand(grantRequest.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_PositionChangeActionType_ActivatesReservationAndEndsPreviousAssignment()
    {
        var callerId = Guid.NewGuid();
        var requesterId = Guid.NewGuid();
        var reservedAssignmentId = Guid.NewGuid();
        var previousAssignmentId = Guid.NewGuid();
        var grantRequest = BuildPendingGrantRequest(
            requestedByUserId: requesterId, actionType: AccessGrantActionType.PositionChange,
            reservedPositionAssignmentId: reservedAssignmentId, changeReason: "Promotion");
        _currentUser.Setup(c => c.UserId).Returns(callerId);
        _accessGrantRequestRepository.Setup(r => r.GetTrackedByIdAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(grantRequest);
        _positionAssignmentRepository.Setup(a => a.GetActivePrimaryAsync(It.IsAny<Guid>(), grantRequest.EmployeeId!.Value, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PositionAssignment { Id = previousAssignmentId, EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-1)) });
        _positionAssignmentRepository.Setup(a => a.ActivatePlannedAsync(It.IsAny<Guid>(), reservedAssignmentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var handler = CreateHandler();
        var result = await handler.Handle(new ApproveAccessGrantRequestCommand(grantRequest.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _positionAssignmentRepository.Verify(a => a.ActivatePlannedAsync(It.IsAny<Guid>(), reservedAssignmentId, It.IsAny<CancellationToken>()), Times.Once);
        _positionAssignmentRepository.Verify(a => a.EndActiveAsync(It.IsAny<Guid>(), previousAssignmentId, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Once);
    }
```

Add the equivalent two tests (self-approval-forbidden, `PositionChange` cancels the reservation) to `RejectAccessGrantRequestCommandHandlerTests.cs` — if that test file doesn't exist yet, create it matching `ApproveAccessGrantRequestCommandHandlerTests.cs`'s established shape, since `RejectAccessGrantRequestCommandHandler` already exists in production code with no test file found during this plan's research (verify by checking `tests/ONEVO.Tests.Unit/Features/CoreHr/Onboarding/` before assuming — if it exists, extend it instead of creating a duplicate).

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~ApproveAccessGrantRequestCommandHandlerTests|FullyQualifiedName~RejectAccessGrantRequestCommandHandlerTests"`
Expected: FAIL

- [ ] **Step 3: Add the self-approval + already-decided checks to both handlers**

In both `ApproveAccessGrantRequestCommandHandler.Handle` and `RejectAccessGrantRequestCommandHandler.Handle`, immediately after loading `grantRequest` and confirming it's not null, add:

```csharp
        if (grantRequest.ApprovalStatus != "Pending")
            return Result<T>.Conflict("This request has already been decided.");
        if (grantRequest.RequestedByUserId == _currentUser.UserId)
            return Result<T>.Forbidden("You cannot approve or reject a request you submitted yourself.");
```

(`Result<T>` — substitute the actual response type each handler returns; `ApproveAccessGrantRequestCommandHandler` already has its own revalidation checks for onboarding's draft-status field, which serves a similar "already decided" purpose for that `ActionType` — verify whether an equivalent check already exists there before adding a duplicate; if `RejectAccessGrantRequestCommandHandler` already has an analogous check per its own draft-status-conditional-reset logic noted in this plan's research, adapt rather than duplicate.)

- [ ] **Step 4: Add the `PositionChange` branch to `ApproveAccessGrantRequestCommandHandler`**

After the self-approval/already-decided checks, branch on `ActionType`:

```csharp
        if (grantRequest.ActionType == AccessGrantActionType.PositionChange)
        {
            var currentAssignment = await _positionAssignmentRepository.GetActivePrimaryAsync(
                tenantId, grantRequest.EmployeeId!.Value, ct);
            if (currentAssignment is not null)
            {
                var effectiveTo = DateOnly.FromDateTime(grantRequest.EffectiveFrom).AddDays(-1);
                if (effectiveTo < currentAssignment.EffectiveFrom)
                    effectiveTo = currentAssignment.EffectiveFrom;
                await _positionAssignmentRepository.EndActiveAsync(tenantId, currentAssignment.Id, effectiveTo, ct);
            }

            var activated = await _positionAssignmentRepository.ActivatePlannedAsync(
                tenantId, grantRequest.ReservedPositionAssignmentId!.Value, ct);
            if (!activated)
                return Result<ApproveAccessGrantRequestResponse>.Conflict(
                    "The reserved seat for this request is no longer available.");

            grantRequest.ApprovalStatus = "Approved";
            grantRequest.DecidedByUserId = _currentUser.UserId;
            grantRequest.DecidedAt = _clock.UtcNow;

            await _accessGrantRequestRepository.SaveChangesAsync(ct);

            return Result<ApproveAccessGrantRequestResponse>.Success(
                new ApproveAccessGrantRequestResponse(grantRequest.Id, null, grantRequest.EmployeeId!.Value, "Approved",
                    InvitationQueued: false, ChecklistTaskCount: 0, PositionApprovalStatus: "Approved",
                    MessageKey: "onboarding.access_grant.position_change_approved"));
        }

        // existing onboarding-specific logic (draft revalidation, employee/user/invitation creation) follows unchanged
```

Check `ApproveAccessGrantRequestResponse`'s actual constructor shape against the real file (it was shown in full earlier in this plan's own research phase) before finalizing this — the record above is written from that research; if any field name differs, match the real one, not this draft.

- [ ] **Step 5: Add the `PositionChange` branch to `RejectAccessGrantRequestCommandHandler`**

```csharp
        if (grantRequest.ActionType == AccessGrantActionType.PositionChange)
        {
            await _positionAssignmentRepository.CancelPlannedAsync(
                tenantId, grantRequest.ReservedPositionAssignmentId!.Value, ct);

            grantRequest.ApprovalStatus = "Rejected";
            grantRequest.DecidedByUserId = _currentUser.UserId;
            grantRequest.DecidedAt = _clock.UtcNow;
            grantRequest.DecisionNote = request.DecisionNote;

            await _accessGrantRequestRepository.SaveChangesAsync(ct);

            return Result<RejectAccessGrantRequestResponse>.Success(
                new RejectAccessGrantRequestResponse(grantRequest.Id, "Rejected"));
        }

        // existing onboarding-specific draft-reset logic follows unchanged
```

Match `RejectAccessGrantRequestResponse`'s real constructor shape from the file, same caveat as Step 4.

- [ ] **Step 6: Move both endpoints off `employees:write`, onto `roles:manage`**

In `AccessGrantRequestsController.cs`, change the `[RequirePermission("employees:write")]` attribute on both the `approve-and-send-invite` and `reject` actions to `[RequirePermission("roles:manage")]`. Leave the `GET` list action's permission unchanged (out of scope for this plan — Task 5 adds a separate, new read endpoint).

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~ApproveAccessGrantRequestCommandHandlerTests|FullyQualifiedName~RejectAccessGrantRequestCommandHandlerTests"`
Expected: PASS (all tests — every pre-existing onboarding-path test in both files must still pass unchanged, since that logic wasn't restructured, only gained a precondition and a sibling branch)

- [ ] **Step 8: Run the full unit suite**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj`
Expected: PASS

- [ ] **Step 9: Commit**

```bash
git add src/ONEVO.Application/Features/CoreHr/Onboarding/Commands/ApproveAccessGrantRequest/ src/ONEVO.Application/Features/CoreHr/Onboarding/Commands/RejectAccessGrantRequest/ src/ONEVO.Api/Controllers/Tenant/CoreHr/AccessGrantRequestsController.cs tests/ONEVO.Tests.Unit/Features/CoreHr/Onboarding/ApproveAccessGrantRequestCommandHandlerTests.cs tests/ONEVO.Tests.Unit/Features/CoreHr/Onboarding/RejectAccessGrantRequestCommandHandlerTests.cs
git commit -m "feat: gate access-grant approval on roles:manage, add PositionChange branch, block self-approval"
```

---

### Task 5: Notification (outbox) for both `EmployeeOnboarding` and `PositionChange` requests

**Files:**
- Modify: `src/ONEVO.Application/Common/ServiceInterfaces/IOutboxMessageHandler.cs` (add `OutboxMessageTypes.PositionChangeApprovalRequestEmail`)
- Create: `src/ONEVO.Application/Features/CoreHr/Onboarding/OutboxHandlers/PositionChangeApprovalRequestEmailPayload.cs` + handler
- Modify: `src/ONEVO.Application/Common/ServiceInterfaces/IEmailService.cs` (add `SendPositionChangeApprovalRequestAsync`)
- Modify: `src/ONEVO.Infrastructure/ExternalServices/Email/EmailTemplateRenderer.cs` (add `"position_change_approval_request"` case)
- Modify: `src/ONEVO.Application/Features/CoreHr/Employee/Commands/ChangeEmployeePosition/ChangeEmployeePositionCommandHandler.cs` (implement `EnqueuePositionChangeApprovalEmailAsync` from Task 3)
- Modify: `src/ONEVO.Application/Features/CoreHr/OnboardingDraft/Commands/FinalizeOnboardingDraft/FinalizeOnboardingDraftCommandHandler.cs` (`FinalizeWithPendingApprovalAsync`)
- Test: unit test for the new outbox handler, following `EmployeeOnboardingInviteEmailOutboxHandler`'s existing test as the template

- [ ] **Step 1: Add the outbox message type**

```csharp
    public const string PositionChangeApprovalRequestEmail = "position_change_approval_request_email";
```

- [ ] **Step 2: Add the payload record and outbox handler**

`PositionChangeApprovalRequestEmailPayload.cs`:

```csharp
namespace ONEVO.Application.Features.CoreHr.Onboarding.OutboxHandlers;

public sealed record PositionChangeApprovalRequestEmailPayload(
    Guid TenantId, Guid ApproverUserId, Guid AccessGrantRequestId,
    string ApproverEmail, string EmployeeName, string PositionName, string? ChangeReason);
```

Handler, mirroring `EmployeeOnboardingInviteEmailOutboxHandler`'s existing structure (constructor takes `IEmailService`, `Type => OutboxMessageTypes.PositionChangeApprovalRequestEmail`, `HandleAsync` deserializes the payload and calls the email service):

```csharp
public sealed class PositionChangeApprovalRequestEmailOutboxHandler : IOutboxMessageHandler
{
    private readonly IEmailService _emailService;

    public PositionChangeApprovalRequestEmailOutboxHandler(IEmailService emailService) => _emailService = emailService;

    public string Type => OutboxMessageTypes.PositionChangeApprovalRequestEmail;

    public async Task HandleAsync(string payloadJson, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<PositionChangeApprovalRequestEmailPayload>(payloadJson)
            ?? throw new InvalidOperationException("Invalid position-change approval request email payload.");

        await _emailService.SendPositionChangeApprovalRequestAsync(
            payload.ApproverEmail, payload.EmployeeName, payload.PositionName, payload.ChangeReason, ct);
    }
}
```

Register it in `Infrastructure/DependencyInjection.cs` alongside the other `IOutboxMessageHandler` registrations (find the existing list, add this one the same way).

- [ ] **Step 3: Add the `IEmailService` method and renderer case**

`IEmailService.cs`: `Task SendPositionChangeApprovalRequestAsync(string to, string employeeName, string positionName, string? changeReason, CancellationToken ct = default);`

`TransactionalEmailService.cs` (and any other `IEmailService` implementer, e.g. `CapturingEmailService` in tests): implement it calling `SendTemplateAsync(to, "position_change_approval_request", new { employeeName, positionName, changeReason }, ct)`, matching the existing pattern every other `Send*Async` method in that file already follows.

`EmailTemplateRenderer.cs`: add `"position_change_approval_request" => RenderPositionChangeApprovalRequest(fields),` to the `Render` switch, and a `RenderPositionChangeApprovalRequest` method following the same structure as `RenderPlatformManagerInvite` (subject + html + text, no invite link needed since this isn't a token-based flow — link to the frontend's `/people/approvals` route via `_options.AppBaseUrl` instead, same `ApplyTenantSlug` pattern `RenderEmployeeOnboardingInvite` already uses):

```csharp
    private RenderedEmail RenderPositionChangeApprovalRequest(IReadOnlyDictionary<string, object?> f)
    {
        var employeeName = Get(f, "employeeName");
        var positionName = Get(f, "positionName");
        var changeReason = Get(f, "changeReason", fallback: "position change");
        var appBaseUrl = string.IsNullOrWhiteSpace(_options.AppBaseUrl) ? string.Empty : _options.AppBaseUrl;
        var approvalsUrl = string.IsNullOrWhiteSpace(appBaseUrl)
            ? "[approvals_url placeholder - set Email:AppBaseUrl]"
            : $"{appBaseUrl.TrimEnd('/')}/people/approvals";

        var subject = $"Approval needed: {Escape(employeeName)}'s {Escape(changeReason)} to {Escape(positionName)}";
        var html = $"""
            <!doctype html><html><body>
              <p>{Escape(employeeName)} has been proposed for a {Escape(changeReason)} into <strong>{Escape(positionName)}</strong>, a sensitive position that requires your approval.</p>
              <p><a href="{Escape(approvalsUrl)}">Review this request</a></p>
            </body></html>
            """;
        var text = $"{employeeName} has been proposed for a {changeReason} into {positionName}, which requires your approval.\nReview: {approvalsUrl}";
        return new RenderedEmail(subject, html, text);
    }
```

- [ ] **Step 4: Implement `EnqueuePositionChangeApprovalEmailAsync` in `ChangeEmployeePositionCommandHandler`**

```csharp
    private async Task EnqueuePositionChangeApprovalEmailAsync(
        Guid tenantId, Guid approverUserId, AccessGrantRequest grantRequest, Employee employee, Position position, CancellationToken ct)
    {
        var approver = await _userRepository.GetByIdAsync(approverUserId, ct);
        if (approver is null) return;

        await _outboxWriter.EnqueueAsync(
            OutboxMessageTypes.PositionChangeApprovalRequestEmail,
            new PositionChangeApprovalRequestEmailPayload(
                tenantId, approverUserId, grantRequest.Id, approver.Email,
                $"{employee.FirstName} {employee.LastName}".Trim(), position.Name, grantRequest.ChangeReason),
            tenantId, ct);
    }
```

Add `IUserRepository _userRepository` to the constructor if not already present (it likely isn't — this handler didn't need user lookups before).

- [ ] **Step 5: Wire notification into onboarding's existing sensitive-position path**

In `FinalizeOnboardingDraftCommandHandler.FinalizeWithPendingApprovalAsync`, after `await _accessGrantRequestRepository.AddAsync(grantRequest, ct);` (inside the `if (existingPending is null)` block, so it only fires once, not on every re-finalize of an already-pending draft), add the same enqueue-per-approver loop, resolving approvers via `_permissionRepository.ListUserIdsWithPermissionCodeAsync(draft.TenantId, "roles:manage", _clock.UtcNow, ct)` (add `IPermissionRepository` to this handler's constructor). Use `draft.FirstName`/`draft.LastName`/`position.Name` for the payload instead of an `Employee` (none exists yet for a deferred-onboarding request) and `grantRequest.ChangeReason` will be `null` here (onboarding has no change reason) — the template already handles a null `changeReason` via its `fallback: "position change"`.

- [ ] **Step 6: Write the outbox handler test**

Mirror an existing outbox handler test (e.g. for `EmployeeOnboardingInviteEmailOutboxHandler`) — deserialize a sample payload JSON, verify `IEmailService.SendPositionChangeApprovalRequestAsync` is called with the right arguments.

- [ ] **Step 7: Run the full unit suite**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj`
Expected: PASS

- [ ] **Step 8: Commit**

```bash
git add src/ONEVO.Application/Common/ServiceInterfaces/IOutboxMessageHandler.cs src/ONEVO.Application/Common/ServiceInterfaces/IEmailService.cs src/ONEVO.Application/Features/CoreHr/Onboarding/OutboxHandlers/ src/ONEVO.Infrastructure/ExternalServices/Email/EmailTemplateRenderer.cs src/ONEVO.Infrastructure/ExternalServices/Email/TransactionalEmailService.cs src/ONEVO.Infrastructure/DependencyInjection.cs src/ONEVO.Application/Features/CoreHr/Employee/Commands/ChangeEmployeePosition/ChangeEmployeePositionCommandHandler.cs src/ONEVO.Application/Features/CoreHr/OnboardingDraft/Commands/FinalizeOnboardingDraft/FinalizeOnboardingDraftCommandHandler.cs tests/
git commit -m "feat: notify roles:manage holders by email when a sensitive-position request is created"
```

---

### Task 6: `GET /api/v1/onboarding/access-grant-requests/pending-for-me`

**Files:**
- Create: `src/ONEVO.Application/Features/CoreHr/Onboarding/Queries/ListPendingAccessGrantRequestsForMe/` (Query + Handler + Response)
- Modify: `src/ONEVO.Api/Controllers/Tenant/CoreHr/AccessGrantRequestsController.cs`
- Test: unit test for the handler

- [ ] **Step 1: Write the failing unit test**

```csharp
[Fact]
public async Task Handle_ReturnsOnlyPendingRequests_WithResolvedNames()
{
    // Arrange a mix of Pending/Approved/Rejected AccessGrantRequests via a mocked
    // IAccessGrantRequestRepository.ListPendingAsync(tenantId, ct) (new repository method,
    // add it the same way other List* methods already exist on this repository - read
    // IAccessGrantRequestRepository.cs first to match its existing shape), assert only
    // Pending ones appear in the result and employee/position/requester names resolve.
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~ListPendingAccessGrantRequestsForMe"`
Expected: FAIL

- [ ] **Step 3: Add `IAccessGrantRequestRepository.ListPendingAsync`**

Read `IAccessGrantRequestRepository.cs` first to match its existing method-naming convention exactly, then add a method returning every `Pending`-status row for the tenant, joined to `Position`/`Employee`/`OnboardingDraft`(for name) as needed — or keep it a plain entity list and resolve names in the handler via existing repositories (`IEmployeeRepository`, `IPositionRepository`, `IOnboardingDraftRepository`, `IUserRepository`), whichever this repo's existing query handlers already prefer (check `ListOnboardingAccessGrantRequestsQueryHandler`, the handler behind the existing `GET` list endpoint, and mirror its exact resolution approach rather than inventing a new one).

- [ ] **Step 4: Create the query, response, and handler**

```csharp
public sealed record ListPendingAccessGrantRequestsForMeQuery : IRequest<Result<IReadOnlyList<PendingAccessGrantRequestResponse>>>;

public sealed record PendingAccessGrantRequestResponse(
    Guid Id, string ActionType, string? EmployeeName, string TargetPositionName,
    string? ChangeReason, string RequestedByName, DateTimeOffset RequestedAt);
```

Handler resolves via `ListOnboardingAccessGrantRequestsQueryHandler`'s established name-resolution pattern (Step 3), filters to `Pending`, maps `EmployeeName` to `null` when `EmployeeId` is null (falling back to nothing extra needed — the frontend already knows to render "New hire: {InvitedFullName}" using the draft's own name field if this comes back null, per the frontend companion plan).

- [ ] **Step 5: Wire the endpoint**

```csharp
    [HttpGet("pending-for-me")]
    [RequirePermission("roles:manage")]
    public async Task<IActionResult> PendingForMe(CancellationToken ct = default)
    {
        var result = await _mediator.Send(new ListPendingAccessGrantRequestsForMeQuery(), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
```

- [ ] **Step 6: Run the test to verify it passes, then the full suite**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj`
Expected: PASS

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Application/Features/CoreHr/Onboarding/Queries/ListPendingAccessGrantRequestsForMe/ src/ONEVO.Api/Controllers/Tenant/CoreHr/AccessGrantRequestsController.cs tests/
git commit -m "feat: add GET /api/v1/onboarding/access-grant-requests/pending-for-me"
```

---

### Task 7: Position picker exposes `RequiresApproval`

**Files:**
- Modify: `src/ONEVO.Application/Features/OrgStructure/DTOs/Responses/PositionListItemResponse.cs`
- Modify: `src/ONEVO.Application/Features/OrgStructure/Position/Queries/ListPositions/ListPositionsQueryHandler.cs`
- Modify: whatever mapper builds `PositionListItemResponse` (`PositionMapper.ToListItemResponse` per this plan's research)
- Test: extend the existing `ListPositionsQueryHandlerTests.cs`

- [ ] **Step 1: Write the failing test**

Add a test asserting a position with an active `PositionAccessTemplate.RequiresApproval = true` comes back with `RequiresApproval: true`, and a position with no template (or `RequiresApproval = false`) comes back `false` — mirror this file's existing mock-repository setup exactly.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~ListPositionsQueryHandlerTests"`
Expected: FAIL

- [ ] **Step 3: Add the field and a batched lookup**

Add `bool RequiresApproval` to `PositionListItemResponse`. Add a new repository method (or reuse an existing batch pattern like `GetOccupancyPreviewsAsync` already does for occupancy) to fetch every active `PositionAccessTemplate` for the page's position ids in one query:

```csharp
Task<IReadOnlyDictionary<Guid, bool>> GetRequiresApprovalByPositionIdsAsync(
    Guid tenantId, IReadOnlyCollection<Guid> positionIds, CancellationToken ct = default);
```

Implement on whichever repository already owns `GetAccessTemplateByPositionAsync` (the position repository), querying `PositionAccessTemplate.Where(t => positionIds.Contains(t.PositionId) && t.IsActive).ToDictionary(t => t.PositionId, t => t.RequiresApproval)`. Call it in `ListPositionsQueryHandler` alongside the existing `GetOccupancyPreviewsAsync` batch call, and pass the result into `PositionMapper.ToListItemResponse(p, occupancyByPositionId, requiresApprovalByPositionId)` (new third parameter, defaulting `false` when the position id isn't in the dictionary).

- [ ] **Step 4: Run the test to verify it passes, then the full suite**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Features/OrgStructure/ tests/
git commit -m "feat: expose RequiresApproval on the position list response"
```

---

### Task 8: Expose `employeeId` on `GET /api/v1/auth/me`

**Files:**
- Modify: `src/ONEVO.Application/Features/Auth/Login/DTOs/Responses/AuthSessionResponseDto.cs` (wherever `CurrentUserDto` is declared — same file per this plan's research)
- Modify: `src/ONEVO.Application/Features/Auth/Login/Queries/GetCurrentSession/GetCurrentSessionQueryHandler.cs`
- Test: find or create `GetCurrentSessionQueryHandlerTests.cs`

**Interfaces:**
- Produces: `CurrentUserDto.EmployeeId` (`Guid?`, null for accounts with no employee record — tenant owners, platform accounts).

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public async Task Handle_UserHasEmployeeRecord_IncludesEmployeeIdInResponse()
{
    // Arrange: _currentUser.UserId resolves via IEmployeeRepository.GetByUserIdAsync to a
    // known Employee. Assert result.Value!.User.EmployeeId equals that employee's id.
}

[Fact]
public async Task Handle_UserHasNoEmployeeRecord_EmployeeIdIsNull()
{
    // Arrange: GetByUserIdAsync returns null (e.g. tenant owner account). Assert
    // result.Value!.User.EmployeeId is null.
}
```

Match this test file's real existing mock setup once found/created — read `GetCurrentSessionQueryHandler.cs`'s current constructor (`ICurrentUser`, `ITenantContext`, `IModuleEntitlementService`, `ITenantRepository`) before writing mocks.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~GetCurrentSessionQueryHandlerTests"`
Expected: FAIL

- [ ] **Step 3: Add `EmployeeId` to `CurrentUserDto` and resolve it in the handler**

Add `Guid? EmployeeId` as the last parameter of the `CurrentUserDto` record. In `GetCurrentSessionQueryHandler.cs`, add `Common.RepositoryInterfaces.IEmployeeRepository _employees` to the constructor, and before constructing `response`:

```csharp
        var employee = await _employees.GetByUserIdAsync(_tenantContext.TenantId, _currentUser.UserId, ct);
```

then pass `employee?.Id` as `CurrentUserDto`'s new last argument.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~GetCurrentSessionQueryHandlerTests"`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Features/Auth/Login/DTOs/Responses/AuthSessionResponseDto.cs src/ONEVO.Application/Features/Auth/Login/Queries/GetCurrentSession/GetCurrentSessionQueryHandler.cs tests/
git commit -m "feat: expose EmployeeId on GET /api/v1/auth/me"
```

---

### Task 9: Integration test — end-to-end sensitive position change

**Files:**
- Create: `tests/ONEVO.Tests.Integration/CoreHr/Employee/SensitivePositionChangeApprovalIntegrationTests.cs`

- [ ] **Step 1: Write the test**

Full flow against real Postgres: seed a `roles:manage` holder and a non-approver employees:write holder; the latter changes a third employee into a sensitive position; assert the target's old assignment is still active and the new one is `Planned`; the `roles:manage` holder approves; assert old assignment now `Ended`, new one `Active`, `ChangeReason` set. Separately: the same `roles:manage` holder who is also the requester gets `403` attempting to approve their own request. Separately: an employee attempting to change their own position gets `403` regardless of permissions held.

- [ ] **Step 2: Run it**

Run: `dotnet test tests/ONEVO.Tests.Integration/ONEVO.Tests.Integration.csproj --filter "FullyQualifiedName~SensitivePositionChangeApprovalIntegrationTests"`
Expected: PASS

- [ ] **Step 3: Commit**

```bash
git add tests/ONEVO.Tests.Integration/CoreHr/Employee/SensitivePositionChangeApprovalIntegrationTests.cs
git commit -m "test: add end-to-end coverage for sensitive position change approval"
```

---

### Task 10: `GET /api/v1/employees/{id}/position-history` (Job Journey)

**Files:**
- Create: `src/ONEVO.Application/Features/CoreHr/Employee/Queries/GetEmployeePositionHistory/` (Query + Handler + Response)
- Modify: `src/ONEVO.Api/Controllers/Tenant/CoreHr/EmployeesController.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/GetEmployeePositionHistoryQueryHandlerTests.cs`

**Interfaces:**
- Produces: `GetEmployeePositionHistoryQuery(Guid EmployeeId) : IRequest<Result<IReadOnlyList<PositionHistoryEntryResponse>>>`, `GET /api/v1/employees/{id}/position-history`, `[RequirePermission("employees:read")]`.

- [ ] **Step 1: Write the failing unit test**

```csharp
[Fact]
public async Task Handle_ReturnsEntriesOldestFirst_WithApprovedByOnlyWhenApprovalRequestExists()
{
    // Arrange: two PositionAssignment rows for the employee (one Ended with EffectiveTo set,
    // one Active with EffectiveTo null), the newer one carrying ChangeReason = "Promotion" and
    // CreatedById = requesterId. Arrange one AccessGrantRequest with
    // ReservedPositionAssignmentId pointing at the newer assignment, ApprovalStatus =
    // "Approved", DecidedByUserId = approverId. Assert the response has 2 entries ordered by
    // EffectiveFrom ascending, the first (hire) entry has ChangeReason null and ApprovedByName
    // null, the second has ChangeReason "Promotion", InitiatedByName resolved from
    // requesterId, ApprovedByName resolved from approverId.

    // Also assert visibility: caller outside the employee's EmployeeVisibilityScope gets 403,
    // matching GetEmployeeDetailQueryHandler's existing behavior - mock
    // IEmployeeVisibilityScopeResolver/GetVisibleByIdAsync the same way that handler's own
    // tests already do, and copy that setup here.
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~GetEmployeePositionHistoryQueryHandlerTests"`
Expected: FAIL

- [ ] **Step 3: Add `IPositionAssignmentRepository.ListHistoryForEmployeeAsync`**

```csharp
Task<IReadOnlyList<PositionAssignment>> ListHistoryForEmployeeAsync(
    Guid tenantId, Guid employeeId, CancellationToken ct = default);
```

Implementation: every `PrimaryEmployment` assignment for the employee (both `Active` and `Ended` — not `Planned`, a pending sensitive request isn't history yet), ordered by `EffectiveFrom` ascending.

- [ ] **Step 4: Create the response, query, and handler**

```csharp
public sealed record PositionHistoryEntryResponse(
    string PositionName, string? DepartmentName, DateOnly EffectiveFrom, DateOnly? EffectiveTo,
    string? ChangeReason, string InitiatedByName, string? ApprovedByName);

public sealed record GetEmployeePositionHistoryQuery(Guid EmployeeId)
    : IRequest<Result<IReadOnlyList<PositionHistoryEntryResponse>>>;
```

Handler: reuse `GetEmployeeDetailQueryHandler`'s exact visibility-check shape (`GetByIdAsync` 404-if-null, then `org:manage`-bypass-or-`EmployeeVisibilityScopeResolver` + `GetVisibleByIdAsync` 403-if-null — copy this block verbatim, don't re-derive it). Then:

```csharp
        var history = await _positionAssignmentRepository.ListHistoryForEmployeeAsync(tenantId, request.EmployeeId, ct);
        var positionIds = history.Select(h => h.PositionId).Distinct().ToList();
        var positions = await _positionRepository.GetByIdsAsync(tenantId, positionIds, ct); // add this batch method if it doesn't already exist, matching GetByIdForLegalEntityAsync's existing conventions
        var positionsById = positions.ToDictionary(p => p.Id);

        var userIds = history.Select(h => h.CreatedById).Distinct().ToList();
        var approvedByUserIds = await _accessGrantRequestRepository.GetApprovedByUserIdsForAssignmentsAsync(
            tenantId, history.Select(h => h.Id).ToList(), ct); // new repository method: Task<IReadOnlyDictionary<Guid, Guid>> keyed by ReservedPositionAssignmentId -> DecidedByUserId, filtered to ApprovalStatus == "Approved"
        userIds.AddRange(approvedByUserIds.Values);
        var users = await _userRepository.GetByIdsAsync(userIds.Distinct().ToList(), ct); // add this batch method if it doesn't already exist
        var usersById = users.ToDictionary(u => u.Id);

        var entries = history.Select(h =>
        {
            var position = positionsById.GetValueOrDefault(h.PositionId);
            var approvedByUserId = approvedByUserIds.GetValueOrDefault(h.Id);
            return new PositionHistoryEntryResponse(
                position?.Name ?? "Unknown position",
                position?.DepartmentId is Guid deptId ? departmentsById.GetValueOrDefault(deptId)?.Name : null, // resolve departments the same batched way if department names are wanted - or omit DepartmentName entirely and let the frontend show position name only, implementer's call given this is a minor display detail not central to the feature
                h.EffectiveFrom, h.EffectiveTo, h.ChangeReason,
                usersById.GetValueOrDefault(h.CreatedById)?.FullName ?? "Unknown",
                approvedByUserId != Guid.Empty ? usersById.GetValueOrDefault(approvedByUserId)?.FullName : null);
        }).ToList();

        return Result<IReadOnlyList<PositionHistoryEntryResponse>>.Success(entries);
```

Adjust `User`'s actual name-field shape (`FullName` vs separate `FirstName`/`LastName`) to match the real entity — this plan's earlier research read `User { FirstName, LastName }` on `ApproveAccessGrantRequestCommandHandler`'s user-creation path, not a single `FullName` — use `$"{u.FirstName} {u.LastName}".Trim()` instead if that's what the entity actually has.

- [ ] **Step 5: Wire the endpoint**

```csharp
    [HttpGet("{id:guid}/position-history")]
    [RequirePermission("employees:read")]
    public async Task<IActionResult> GetPositionHistory(Guid id, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetEmployeePositionHistoryQuery(id), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
```

- [ ] **Step 6: Run the test to verify it passes, then the full suite**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj`
Expected: PASS

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Application/Features/CoreHr/Employee/Queries/GetEmployeePositionHistory/ src/ONEVO.Api/Controllers/Tenant/CoreHr/EmployeesController.cs tests/
git commit -m "feat: add GET /api/v1/employees/{id}/position-history (Job Journey)"
```

---

## Done — hands off to the frontend plan

`Hrms--Web-application---front-end---v1/docs/superpowers/plans/2026-08-17-sensitive-position-approval-frontend.md` consumes `ChangeEmployeePositionResponse.PendingApproval`, `PositionListItemResponse.RequiresApproval`, `GET .../pending-for-me`, and `GET .../position-history` built here.
