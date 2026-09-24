# Employee Detail Screen — Backend Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a full-detail admin read endpoint for a single employee (Job/Personal Info, Emergency Contacts, sensitive-gated Payroll) and a minimal capacity-checked "Change Position" action.

**Architecture:** `GetEmployeeDetailQueryHandler` mirrors the existing `GetEmployeeQueryHandler` (visibility/coverage check, invitation-status join) and reuses `IEmployeeProfileRepository` (already `employeeId`-parameterized, not self-only) exactly the way `GetMyProfileQueryHandler` does — no new repository methods for read data. `ChangeEmployeePositionCommandHandler` reuses the atomic-reservation SQL pattern from the multi-legal-entity-employment-foundation plan (`TryReservePositionAssignmentAsync`), adapted to insert `"active"` directly instead of `"planned"`.

**Tech Stack:** .NET (C#), EF Core, MediatR, FluentValidation, xUnit + Moq, Testcontainers.

## Global Constraints

- Depends on the finished `2026-08-16-multi-legal-entity-employment-foundation` plan (`IPositionAssignmentRepository.TryReservePositionAssignmentAsync` and the atomic-reserve SQL pattern it established).
- Snake_case DB column naming (EF Core convention already configured project-wide).
- `employees:read:sensitive` gates the Payroll section by **omission**, not a 403 — the rest of the detail response is always returned to anyone with `employees:read` + coverage.
- No new admin-facing write endpoints for Personal Information/Emergency Contacts/Payroll — those stay self-service-only, unchanged from the 2026-08-15 work.

---

### Task 1: `employees:read:sensitive` permission

**Files:**
- Modify: `src/ONEVO.Infrastructure/Persistence/Seeders/PermissionSeeder.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Auth/PermissionSeederTests.cs`

- [ ] **Step 1: Write the failing test**

Extend the existing seeded-codes assertion in `PermissionSeederTests.cs` to also expect `"employees:read:sensitive"` (match this file's existing assertion style — confirmed present from the multi-legal-entity-foundation plan's Task 1).

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~PermissionSeederTests"`
Expected: FAIL

- [ ] **Step 3: Add the permission**

In `PermissionSeeder.cs`, add directly after `employees:write`:

```csharp
Perm("employees:read", "View all employees in scope.", "core_hr"),
Perm("employees:write", "Create, update employees.", "core_hr"),
Perm("employees:read:sensitive", "View sensitive employee data (bank details) on the employee detail screen.", "core_hr"),
Perm("invitations:manage", "Resend or revoke employee onboarding invitations.", "core_hr"),
```

(The `invitations:manage` line already exists from the prior plan — shown here only to confirm insertion order; do not duplicate it.)

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~PermissionSeederTests"`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Infrastructure/Persistence/Seeders/PermissionSeeder.cs tests/ONEVO.Tests.Unit/Features/Auth/PermissionSeederTests.cs
git commit -m "feat: add employees:read:sensitive permission"
```

---

### Task 2: Atomic active-assignment creation (Change Position primitive)

**Files:**
- Modify: `src/ONEVO.Application/Features/CoreHr/PositionAssignment/RepositoryInterfaces/IPositionAssignmentRepository.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/CoreHr/EfPositionAssignmentRepository.cs`
- Test: `tests/ONEVO.Tests.Integration/CoreHr/PositionAssignment/TryCreateActiveAssignmentTests.cs` (create)

**Interfaces:**
- Produces: `Task<Guid?> TryCreateActiveAssignmentAsync(Guid tenantId, Guid employeeId, Guid positionId, DateOnly effectiveFrom, Guid createdById, CancellationToken ct = default)` — same atomic INSERT...WHERE-capacity-subquery shape as `TryReservePositionAssignmentAsync`, but the inserted row's `assignment_status` is `"active"` directly (no invitation/planned lifecycle — this is an immediate action, not an invite).
- Also: `Task<bool> EndActiveAsync(Guid tenantId, Guid positionAssignmentId, DateOnly effectiveTo, CancellationToken ct = default)` — sets `assignment_status = 'ended'`, `effective_to`, on an active row.

- [ ] **Step 1: Write the failing integration test**

Mirror `tests/ONEVO.Tests.Integration/CoreHr/PositionAssignment/TryReservePositionAssignmentTests.cs` (from the prior plan) exactly, but asserting `AssignmentStatus == "active"` instead of `"planned"`, plus the same capacity/concurrency test shapes:

```csharp
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Infrastructure.Persistence.Repositories.CoreHr;
using Xunit;

namespace ONEVO.Tests.Integration.CoreHr.PositionAssignment;

public class TryCreateActiveAssignmentTests : IntegrationTestBase
{
    [Fact]
    public async Task TryCreateActive_WhenSeatAvailable_InsertsActiveRowAndReturnsId()
    {
        var tenantId = await SeedTenantAsync();
        var (employeeId, positionId) = await SeedEmployeeAndUniquePositionAsync(tenantId);

        var repo = new EfPositionAssignmentRepository(Db);
        var createdId = await repo.TryCreateActiveAssignmentAsync(
            tenantId, employeeId, positionId, DateOnly.FromDateTime(DateTime.UtcNow), Guid.NewGuid());

        Assert.NotNull(createdId);
        var row = await Db.PositionAssignments.FindAsync(createdId!.Value);
        Assert.Equal("active", row!.AssignmentStatus);
    }

    [Fact]
    public async Task TryCreateActive_WhenPositionAtCapacity_ReturnsNull()
    {
        var tenantId = await SeedTenantAsync();
        var (employeeA, positionId) = await SeedEmployeeAndUniquePositionAsync(tenantId);
        var (employeeB, _) = await SeedEmployeeAndUniquePositionAsync(tenantId, existingPositionId: positionId);

        var repo = new EfPositionAssignmentRepository(Db);
        await repo.TryCreateActiveAssignmentAsync(tenantId, employeeA, positionId, DateOnly.FromDateTime(DateTime.UtcNow), Guid.NewGuid());
        var second = await repo.TryCreateActiveAssignmentAsync(tenantId, employeeB, positionId, DateOnly.FromDateTime(DateTime.UtcNow), Guid.NewGuid());

        Assert.Null(second);
    }

    [Fact]
    public async Task EndActive_SetsEndedStatusAndEffectiveTo()
    {
        var tenantId = await SeedTenantAsync();
        var (employeeId, positionId) = await SeedEmployeeAndUniquePositionAsync(tenantId);
        var repo = new EfPositionAssignmentRepository(Db);
        var createdId = await repo.TryCreateActiveAssignmentAsync(
            tenantId, employeeId, positionId, DateOnly.FromDateTime(DateTime.UtcNow), Guid.NewGuid());

        var effectiveTo = DateOnly.FromDateTime(DateTime.UtcNow);
        var ended = await repo.EndActiveAsync(tenantId, createdId!.Value, effectiveTo);

        Assert.True(ended);
        var row = await Db.PositionAssignments.FindAsync(createdId.Value);
        Assert.Equal("ended", row!.AssignmentStatus);
        Assert.Equal(effectiveTo, row.EffectiveTo);
    }
}
```

Match `SeedTenantAsync`/`SeedEmployeeAndUniquePositionAsync` to the real helper names already used by `TryReservePositionAssignmentTests.cs` (same file, copy its setup).

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Integration/ONEVO.Tests.Integration.csproj --filter "FullyQualifiedName~TryCreateActiveAssignment"`
Expected: FAIL (build error)

- [ ] **Step 3: Add the interface methods**

In `IPositionAssignmentRepository.cs`, add:

```csharp
    /// <summary>Same atomic capacity-guarded INSERT as TryReservePositionAssignmentAsync, but
    /// inserts the row as "active" directly - used for immediate, non-invitation position
    /// changes (Change Position action) rather than an invitation's reserve-then-activate
    /// lifecycle.</summary>
    Task<Guid?> TryCreateActiveAssignmentAsync(
        Guid tenantId, Guid employeeId, Guid positionId, DateOnly effectiveFrom, Guid createdById,
        CancellationToken ct = default);

    Task<bool> EndActiveAsync(Guid tenantId, Guid positionAssignmentId, DateOnly effectiveTo, CancellationToken ct = default);
```

- [ ] **Step 4: Implement both methods**

In `EfPositionAssignmentRepository.cs`, add (mirroring `TryReservePositionAssignmentAsync`'s exact SQL shape from the prior plan, changing only the literal status value):

```csharp
    public async Task<Guid?> TryCreateActiveAssignmentAsync(
        Guid tenantId, Guid employeeId, Guid positionId, DateOnly effectiveFrom, Guid createdById,
        CancellationToken ct = default)
    {
        var newId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var rowsAffected = await _db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO position_assignments
                (id, tenant_id, employee_id, position_id, assignment_kind, effective_from,
                 assignment_status, created_by_id, created_at, is_deleted)
            SELECT {newId}, {tenantId}, {employeeId}, {positionId}, {PositionAssignmentKind.PrimaryEmployment},
                   {effectiveFrom}, {PositionAssignmentStatus.Active}, {createdById}, {now}, false
            WHERE (
                SELECT COUNT(*) FROM position_assignments
                WHERE tenant_id = {tenantId} AND position_id = {positionId}
                  AND assignment_kind = {PositionAssignmentKind.PrimaryEmployment}
                  AND assignment_status IN ({PositionAssignmentStatus.Active}, {PositionAssignmentStatus.Planned})
            ) < (
                SELECT max_occupancy FROM positions WHERE id = {positionId} AND tenant_id = {tenantId}
            )
        ", ct);

        return rowsAffected > 0 ? newId : null;
    }

    public async Task<bool> EndActiveAsync(Guid tenantId, Guid positionAssignmentId, DateOnly effectiveTo, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var rowsAffected = await _db.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE position_assignments
            SET assignment_status = {PositionAssignmentStatus.Ended}, effective_to = {effectiveTo}, updated_at = {now}
            WHERE id = {positionAssignmentId} AND tenant_id = {tenantId}
              AND assignment_status = {PositionAssignmentStatus.Active}
        ", ct);
        return rowsAffected > 0;
    }
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Integration/ONEVO.Tests.Integration.csproj --filter "FullyQualifiedName~TryCreateActiveAssignment"`
Expected: PASS (all 3 tests)

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Application/Features/CoreHr/PositionAssignment/RepositoryInterfaces/IPositionAssignmentRepository.cs src/ONEVO.Infrastructure/Persistence/Repositories/CoreHr/EfPositionAssignmentRepository.cs tests/ONEVO.Tests.Integration/CoreHr/PositionAssignment/TryCreateActiveAssignmentTests.cs
git commit -m "feat: add atomic active-assignment create/end to IPositionAssignmentRepository"
```

---

### Task 3: `GET /api/v1/employees/{id}/detail`

**Files:**
- Create: `src/ONEVO.Application/Features/CoreHr/Employee/DTOs/Responses/EmployeeDetailResponse.cs`
- Create: `src/ONEVO.Application/Features/CoreHr/Employee/Queries/GetEmployeeDetail/GetEmployeeDetailQuery.cs`
- Create: `src/ONEVO.Application/Features/CoreHr/Employee/Queries/GetEmployeeDetail/GetEmployeeDetailQueryHandler.cs`
- Modify: `src/ONEVO.Api/Controllers/Tenant/CoreHr/EmployeesController.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/GetEmployeeDetailQueryHandlerTests.cs` (create)

**Interfaces:**
- Produces: `GetEmployeeDetailQuery(Guid EmployeeId) : IRequest<Result<EmployeeDetailResponse>>`, `GET /api/v1/employees/{id}/detail`, `[RequirePermission("employees:read")]`.

- [ ] **Step 1: Define the response DTO**

`EmployeeDetailResponse.cs`:

```csharp
namespace ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;

public record EmployeeDetailResponse(
    Guid Id,
    EmployeeDetailJobInformation JobInformation,
    EmployeeDetailPersonalInformation PersonalInformation,
    IReadOnlyList<EmployeeDetailEmergencyContact> EmergencyContacts,
    EmployeeDetailPayroll? Payroll,
    string? InvitationStatus,
    DateTimeOffset? InvitationExpiresAt);

public record EmployeeDetailJobInformation(
    string EmployeeNumber, Guid? LegalEntityId, string? LegalEntityName, string? DepartmentName, string? PositionName,
    Guid? PositionId, string? ReportingManagerName, string EmploymentTypeLabel, string Status,
    DateOnly HireDate, DateOnly? ProbationEndDate);

public record EmployeeDetailPersonalInformation(
    string FirstName, string LastName, string Email, string? Phone, DateOnly? DateOfBirth,
    string? Gender, Guid? NationalityId, IReadOnlyList<EmployeeDetailAddress> Addresses);

public record EmployeeDetailAddress(Guid Id, string AddressType, string AddressJson, bool IsPrimary);

public record EmployeeDetailEmergencyContact(Guid Id, string Name, string Relationship, string Phone, string? Email, bool IsPrimary);

public record EmployeeDetailPayroll(bool HasBankDetailsOnFile, string? BankName, string? MaskedAccountNumber, string? AccountType);
```

- [ ] **Step 2: Write the failing unit test**

Create `GetEmployeeDetailQueryHandlerTests.cs`, matching `GetEmployeeQueryHandlerTests.cs`'s existing constructor/mock pattern exactly (same dependencies plus `IEmployeeProfileRepository`, `IEncryptionService`, `ILegalEntityRepository`, `IWorkModeRepository` — read that file first to copy its mock-setup helper style):

```csharp
    [Fact]
    public async Task Handle_CallerLacksSensitivePermission_OmitsPayroll()
    {
        // Arrange: employee exists, visible, has a bank detail on file, caller does NOT have
        // employees:read:sensitive.
        _currentUser.Setup(c => c.HasPermission("employees:read:sensitive")).Returns(false);

        var result = await _handler.Handle(new GetEmployeeDetailQuery(employeeId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.Payroll);
        _profile.Verify(p => p.GetPrimaryBankDetailAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_CallerHasSensitivePermission_IncludesMaskedPayroll()
    {
        _currentUser.Setup(c => c.HasPermission("employees:read:sensitive")).Returns(true);
        // Arrange bank detail mock + _encryption.Decrypt/BankAccountMasker as needed.

        var result = await _handler.Handle(new GetEmployeeDetailQuery(employeeId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value!.Payroll);
        Assert.True(result.Value!.Payroll!.HasBankDetailsOnFile);
    }

    [Fact]
    public async Task Handle_EmployeeOutsideVisibilityScope_ReturnsForbidden()
    {
        // Arrange: GetVisibleByIdAsync returns null (mirrors GetEmployeeQueryHandlerTests'
        // existing equivalent test - copy its exact setup).

        var result = await _handler.Handle(new GetEmployeeDetailQuery(employeeId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_EmployeeNotFound_ReturnsNotFound()
    {
        // Arrange: GetByIdAsync returns null.

        var result = await _handler.Handle(new GetEmployeeDetailQuery(employeeId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~GetEmployeeDetailQueryHandlerTests"`
Expected: FAIL (build error — types don't exist)

- [ ] **Step 4: Create the query**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;

namespace ONEVO.Application.Features.CoreHr.Employee.Queries.GetEmployeeDetail;

public sealed record GetEmployeeDetailQuery(Guid EmployeeId) : IRequest<Result<EmployeeDetailResponse>>;
```

- [ ] **Step 5: Create the handler**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Invite.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Employee.Helpers;
using ONEVO.Application.Features.CoreHr.Employee.Models;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Domain.Features.Auth.Entities;

namespace ONEVO.Application.Features.CoreHr.Employee.Queries.GetEmployeeDetail;

/// <summary>
/// Admin-facing full detail read for one employee - Job/Personal Info and Emergency Contacts are
/// always included once the caller passes the same employees:read + coverage check
/// GetEmployeeQueryHandler already enforces; Payroll is included only when the caller additionally
/// holds employees:read:sensitive (omitted, not a separate 403, so the rest of the screen still
/// renders for a caller without it).
/// </summary>
public class GetEmployeeDetailQueryHandler : IRequestHandler<GetEmployeeDetailQuery, Result<EmployeeDetailResponse>>
{
    private readonly IEmployeeRepository _employeeRepository;
    private readonly IEmployeeVisibilityScopeResolver _visibilityScopeResolver;
    private readonly IEmployeeProfileRepository _profile;
    private readonly IInvitationTokenRepository _invitationTokenRepository;
    private readonly IEncryptionService _encryption;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    public GetEmployeeDetailQueryHandler(
        IEmployeeRepository employeeRepository,
        IEmployeeVisibilityScopeResolver visibilityScopeResolver,
        IEmployeeProfileRepository profile,
        IInvitationTokenRepository invitationTokenRepository,
        IEncryptionService encryption,
        ICurrentUser currentUser,
        IDateTimeProvider clock)
    {
        _employeeRepository = employeeRepository;
        _visibilityScopeResolver = visibilityScopeResolver;
        _profile = profile;
        _invitationTokenRepository = invitationTokenRepository;
        _encryption = encryption;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Result<EmployeeDetailResponse>> Handle(GetEmployeeDetailQuery request, CancellationToken ct)
    {
        var tenantId = _currentUser.TenantId;

        var existing = await _employeeRepository.GetByIdAsync(tenantId, request.EmployeeId, ct);
        if (existing is null)
            return Result<EmployeeDetailResponse>.NotFound("The employee or selected organization record could not be found.");

        var scope = _currentUser.HasPermission("org:manage")
            ? EmployeeVisibilityScope.Unrestricted()
            : await _visibilityScopeResolver.ResolveAsync(tenantId, _currentUser.UserId, ct);

        var visible = await _employeeRepository.GetVisibleByIdAsync(tenantId, scope, request.EmployeeId, ct);
        if (visible is null)
            return Result<EmployeeDetailResponse>.Forbidden("You do not have access to manage this employee.");

        var addresses = await _profile.ListAddressesAsync(tenantId, request.EmployeeId, ct);
        var emergencyContacts = await _profile.ListEmergencyContactsAsync(tenantId, request.EmployeeId, ct);

        EmployeeDetailPayroll? payroll = null;
        if (_currentUser.HasPermission("employees:read:sensitive"))
        {
            var bankDetail = await _profile.GetPrimaryBankDetailAsync(tenantId, request.EmployeeId, ct);
            var maskedAccountNumber = bankDetail is null
                ? null
                : BankAccountMasker.Mask(_encryption.Decrypt(bankDetail.AccountNumberEncrypted));
            payroll = new EmployeeDetailPayroll(bankDetail is not null, bankDetail?.BankName, maskedAccountNumber, bankDetail?.AccountType);
        }

        var invitation = await _invitationTokenRepository.GetLatestByEmployeeIdAsync(tenantId, request.EmployeeId, ct);

        var jobInformation = new EmployeeDetailJobInformation(
            visible.EmployeeNumber, existing.LegalEntityId, visible.LegalEntityName, visible.DepartmentName, visible.PositionName,
            visible.PositionId, visible.ReportingManagerName, visible.EmploymentTypeLabel, visible.Status,
            existing.HireDate, existing.ProbationEndDate);

        var personalInformation = new EmployeeDetailPersonalInformation(
            existing.FirstName, existing.LastName, existing.Email, existing.Phone, existing.DateOfBirth,
            existing.Gender, existing.NationalityId,
            addresses.Select(a => new EmployeeDetailAddress(a.Id, a.AddressType, a.AddressJson, a.IsPrimary)).ToList());

        return Result<EmployeeDetailResponse>.Success(new EmployeeDetailResponse(
            request.EmployeeId, jobInformation, personalInformation,
            emergencyContacts.Select(c => new EmployeeDetailEmergencyContact(c.Id, c.Name, c.Relationship, c.Phone, c.Email, c.IsPrimary)).ToList(),
            payroll,
            InvitationStatusOf(invitation, _clock.UtcNow), invitation?.ExpiresAt));
    }

    private static string? InvitationStatusOf(InvitationToken? invitation, DateTimeOffset now)
    {
        if (invitation is null) return null;
        if (invitation.UsedAt is not null) return "accepted";
        if (invitation.RevokedAt is not null) return "revoked";
        if (invitation.ExpiresAt <= now) return "expired";
        return "pending";
    }
}
```

- [ ] **Step 6: Wire the endpoint**

In `EmployeesController.cs`, add directly after the existing `GetById` action:

```csharp
    /// <summary>Full section-by-section detail read for one employee. Payroll is included only
    /// when the caller holds employees:read:sensitive - omitted (not a 403) otherwise.</summary>
    [HttpGet("{id:guid}/detail")]
    [RequirePermission("employees:read")]
    public async Task<IActionResult> GetDetail(Guid id, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetEmployeeDetailQuery(id), ct);

        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
```

Add `using ONEVO.Application.Features.CoreHr.Employee.Queries.GetEmployeeDetail;` to the controller's usings.

- [ ] **Step 7: Run the test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~GetEmployeeDetailQueryHandlerTests"`
Expected: PASS (all 4 tests)

- [ ] **Step 8: Commit**

```bash
git add src/ONEVO.Application/Features/CoreHr/Employee/DTOs/Responses/EmployeeDetailResponse.cs src/ONEVO.Application/Features/CoreHr/Employee/Queries/GetEmployeeDetail/ src/ONEVO.Api/Controllers/Tenant/CoreHr/EmployeesController.cs tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/GetEmployeeDetailQueryHandlerTests.cs
git commit -m "feat: add GET /api/v1/employees/{id}/detail"
```

---

### Task 4: `POST /api/v1/employees/{id}/change-position`

**Files:**
- Create: `src/ONEVO.Api/Contracts/CoreHr/Employees/ChangePositionRequest.cs`
- Create: `src/ONEVO.Application/Features/CoreHr/Employee/Commands/ChangeEmployeePosition/ChangeEmployeePositionCommand.cs`
- Create: `src/ONEVO.Application/Features/CoreHr/Employee/Commands/ChangeEmployeePosition/ChangeEmployeePositionCommandHandler.cs`
- Create: `src/ONEVO.Application/Features/CoreHr/Employee/Commands/ChangeEmployeePosition/ChangeEmployeePositionCommandValidator.cs`
- Modify: `src/ONEVO.Api/Controllers/Tenant/CoreHr/EmployeesController.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/ChangeEmployeePositionCommandHandlerTests.cs` (create)

**Interfaces:**
- Consumes: `IPositionAssignmentRepository.TryCreateActiveAssignmentAsync`/`EndActiveAsync`/`GetActivePrimaryAsync` (the last already exists, confirmed in the prior plan's grounding).

- [ ] **Step 1: Write the failing unit test**

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Commands.ChangeEmployeePosition;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.PositionAssignment.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.CoreHr.Employee;

public class ChangeEmployeePositionCommandHandlerTests
{
    private readonly Mock<Common.RepositoryInterfaces.IEmployeeRepository> _employees = new();
    private readonly Mock<IPositionRepository> _positions = new();
    private readonly Mock<IPositionAssignmentRepository> _assignments = new();
    private readonly Mock<ICurrentUser> _currentUser = new();

    private ChangeEmployeePositionCommandHandler CreateHandler() =>
        new(_employees.Object, _positions.Object, _assignments.Object, _currentUser.Object);

    [Fact]
    public async Task Handle_PositionAtCapacity_ReturnsConflict_DoesNotEndCurrentAssignment()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var newPositionId = Guid.NewGuid();
        _currentUser.Setup(c => c.TenantId).Returns(tenantId);
        _currentUser.Setup(c => c.UserId).Returns(Guid.NewGuid());
        _employees.Setup(e => e.GetTrackedByIdAsync(tenantId, employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ONEVO.Domain.Features.CoreHr.Entities.Employee { Id = employeeId, TenantId = tenantId, LegalEntityId = Guid.NewGuid() });
        _positions.Setup(p => p.GetByIdForLegalEntityAsync(tenantId, It.IsAny<Guid>(), newPositionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Position { Id = newPositionId, TenantId = tenantId, IsActive = true });
        _assignments
            .Setup(a => a.TryCreateActiveAssignmentAsync(tenantId, employeeId, newPositionId, It.IsAny<DateOnly>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)null);

        var handler = CreateHandler();
        var result = await handler.Handle(
            new ChangeEmployeePositionCommand(employeeId, newPositionId, DateOnly.FromDateTime(DateTime.UtcNow)),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        _assignments.Verify(a => a.EndActiveAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_SeatAvailable_EndsCurrentAssignmentAndCreatesNew()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var newPositionId = Guid.NewGuid();
        var oldAssignmentId = Guid.NewGuid();
        var newAssignmentId = Guid.NewGuid();
        _currentUser.Setup(c => c.TenantId).Returns(tenantId);
        _currentUser.Setup(c => c.UserId).Returns(Guid.NewGuid());
        _employees.Setup(e => e.GetTrackedByIdAsync(tenantId, employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ONEVO.Domain.Features.CoreHr.Entities.Employee { Id = employeeId, TenantId = tenantId, LegalEntityId = Guid.NewGuid() });
        _positions.Setup(p => p.GetByIdForLegalEntityAsync(tenantId, It.IsAny<Guid>(), newPositionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Position { Id = newPositionId, TenantId = tenantId, IsActive = true });
        _assignments
            .Setup(a => a.GetActivePrimaryAsync(tenantId, employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ONEVO.Domain.Features.CoreHr.Entities.PositionAssignment { Id = oldAssignmentId, TenantId = tenantId, EmployeeId = employeeId });
        _assignments
            .Setup(a => a.TryCreateActiveAssignmentAsync(tenantId, employeeId, newPositionId, It.IsAny<DateOnly>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(newAssignmentId);

        var handler = CreateHandler();
        var result = await handler.Handle(
            new ChangeEmployeePositionCommand(employeeId, newPositionId, DateOnly.FromDateTime(DateTime.UtcNow)),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        _assignments.Verify(a => a.EndActiveAsync(tenantId, oldAssignmentId, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_PositionNotFoundInEmployeesLegalEntity_ReturnsNotFound()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var newPositionId = Guid.NewGuid();
        _currentUser.Setup(c => c.TenantId).Returns(tenantId);
        _employees.Setup(e => e.GetTrackedByIdAsync(tenantId, employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ONEVO.Domain.Features.CoreHr.Entities.Employee { Id = employeeId, TenantId = tenantId, LegalEntityId = Guid.NewGuid() });
        _positions.Setup(p => p.GetByIdForLegalEntityAsync(tenantId, It.IsAny<Guid>(), newPositionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Position?)null);

        var handler = CreateHandler();
        var result = await handler.Handle(
            new ChangeEmployeePositionCommand(employeeId, newPositionId, DateOnly.FromDateTime(DateTime.UtcNow)),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }
}
```

Adjust `Common.RepositoryInterfaces.IEmployeeRepository`'s exact namespace alias to match how `ApproveAccessGrantRequestCommandHandler.cs` imports it (confirmed used there as `Application.Common.RepositoryInterfaces.IEmployeeRepository` for `GetTrackedByIdAsync` — verify against that file before finalizing this test's usings).

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~ChangeEmployeePositionCommandHandlerTests"`
Expected: FAIL (build error)

- [ ] **Step 3: Create the contract, command, validator**

`ChangePositionRequest.cs`:

```csharp
namespace ONEVO.Api.Contracts.CoreHr.Employees;

public sealed record ChangePositionRequest(Guid PositionId, DateOnly EffectiveFrom);
```

`ChangeEmployeePositionCommand.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.CoreHr.Employee.Commands.ChangeEmployeePosition;

public sealed record ChangeEmployeePositionCommand(Guid EmployeeId, Guid PositionId, DateOnly EffectiveFrom)
    : IRequest<Result<Unit>>;
```

`ChangeEmployeePositionCommandValidator.cs`:

```csharp
using FluentValidation;

namespace ONEVO.Application.Features.CoreHr.Employee.Commands.ChangeEmployeePosition;

public sealed class ChangeEmployeePositionCommandValidator : AbstractValidator<ChangeEmployeePositionCommand>
{
    public ChangeEmployeePositionCommandValidator()
    {
        RuleFor(x => x.EmployeeId).NotEmpty();
        RuleFor(x => x.PositionId).NotEmpty();
        RuleFor(x => x.EffectiveFrom).NotEmpty();
    }
}
```

- [ ] **Step 4: Create the handler**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.PositionAssignment.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;

namespace ONEVO.Application.Features.CoreHr.Employee.Commands.ChangeEmployeePosition;

/// <summary>
/// Minimal capacity-checked position reassignment - not the full approval-routed Promotion/
/// Transfer workflow described in the OneVo-HR docs (that workflow doesn't exist in this
/// codebase; per explicit product decision this is the deliberately smaller version). Reuses the
/// same atomic seat-reservation SQL pattern as onboarding invitations, adapted to create the new
/// assignment "active" immediately rather than "planned".
/// </summary>
public class ChangeEmployeePositionCommandHandler : IRequestHandler<ChangeEmployeePositionCommand, Result<Unit>>
{
    private readonly IEmployeeRepository _employeeRepository;
    private readonly IPositionRepository _positionRepository;
    private readonly IPositionAssignmentRepository _positionAssignmentRepository;
    private readonly ICurrentUser _currentUser;

    public ChangeEmployeePositionCommandHandler(
        IEmployeeRepository employeeRepository,
        IPositionRepository positionRepository,
        IPositionAssignmentRepository positionAssignmentRepository,
        ICurrentUser currentUser)
    {
        _employeeRepository = employeeRepository;
        _positionRepository = positionRepository;
        _positionAssignmentRepository = positionAssignmentRepository;
        _currentUser = currentUser;
    }

    public async Task<Result<Unit>> Handle(ChangeEmployeePositionCommand request, CancellationToken ct)
    {
        var tenantId = _currentUser.TenantId;

        var employee = await _employeeRepository.GetTrackedByIdAsync(tenantId, request.EmployeeId, ct);
        if (employee is null)
            return Result<Unit>.NotFound("The employee could not be found.");

        if (employee.LegalEntityId is not Guid legalEntityId)
            return Result<Unit>.UnprocessableEntity("This employee has no assigned legal entity.");

        var position = await _positionRepository.GetByIdForLegalEntityAsync(tenantId, legalEntityId, request.PositionId, ct);
        if (position is null || !position.IsActive)
            return Result<Unit>.NotFound("The selected position does not exist or is not active in this employee's company.");

        var reservedAssignmentId = await _positionAssignmentRepository.TryCreateActiveAssignmentAsync(
            tenantId, employee.Id, position.Id, request.EffectiveFrom, _currentUser.UserId, ct);
        if (reservedAssignmentId is null)
            return Result<Unit>.Conflict("This position has reached its capacity.");

        var currentAssignment = await _positionAssignmentRepository.GetActivePrimaryAsync(tenantId, employee.Id, ct);
        if (currentAssignment is not null)
        {
            var effectiveTo = request.EffectiveFrom.AddDays(-1);
            if (effectiveTo < currentAssignment.EffectiveFrom)
                effectiveTo = currentAssignment.EffectiveFrom;
            await _positionAssignmentRepository.EndActiveAsync(tenantId, currentAssignment.Id, effectiveTo, ct);
        }

        return Result<Unit>.Success(Unit.Value);
    }
}
```

Note: `GetActivePrimaryAsync` returns the assignment being replaced *before* the new one was created — since the new one is a different row (different `Position`), both can briefly coexist as `active` between the two calls above; this is intentional (never a moment where the employee has zero active primary assignment) and matches how promotions/transfers are conventionally modeled (overlap-free by date range, not by row count).

- [ ] **Step 5: Wire the endpoint**

In `EmployeesController.cs`:

```csharp
    /// <summary>Reassign an employee's primary position. Minimal capacity-checked reassignment -
    /// not an approval-routed workflow. See ChangeEmployeePositionCommandHandler.</summary>
    [HttpPost("{id:guid}/change-position")]
    [RequirePermission("employees:write")]
    public async Task<IActionResult> ChangePosition(
        Guid id, [FromBody] ChangePositionRequest request, CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new ChangeEmployeePositionCommand(id, request.PositionId, request.EffectiveFrom), ct);

        return result.IsSuccess
            ? NoContent()
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
```

Add `using ONEVO.Api.Contracts.CoreHr.Employees;` and `using ONEVO.Application.Features.CoreHr.Employee.Commands.ChangeEmployeePosition;` to the controller's usings.

- [ ] **Step 6: Run the test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~ChangeEmployeePositionCommandHandlerTests"`
Expected: PASS (all 3 tests)

- [ ] **Step 7: Run the full unit suite**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj`
Expected: PASS (every test)

- [ ] **Step 8: Commit**

```bash
git add src/ONEVO.Api/Contracts/CoreHr/Employees/ChangePositionRequest.cs src/ONEVO.Application/Features/CoreHr/Employee/Commands/ChangeEmployeePosition/ src/ONEVO.Api/Controllers/Tenant/CoreHr/EmployeesController.cs tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/ChangeEmployeePositionCommandHandlerTests.cs
git commit -m "feat: add POST /api/v1/employees/{id}/change-position"
```

---

### Task 5: Integration test

**Files:**
- Create: `tests/ONEVO.Tests.Integration/CoreHr/Employee/EmployeeDetailAndChangePositionIntegrationTests.cs`

- [ ] **Step 1: Write the test**

Follow this repo's existing full-stack integration pattern (same as the prior plan's Task 5 precedent). Cover: detail read with/without `employees:read:sensitive` (payroll present/absent), coverage-denied caller gets 403, change-position happy path (old assignment ended, new one active), change-position against a full-capacity position returns 409 and leaves the old assignment untouched.

- [ ] **Step 2: Run it**

Run: `dotnet test tests/ONEVO.Tests.Integration/ONEVO.Tests.Integration.csproj --filter "FullyQualifiedName~EmployeeDetailAndChangePosition"`
Expected: PASS

- [ ] **Step 3: Commit**

```bash
git add tests/ONEVO.Tests.Integration/CoreHr/Employee/EmployeeDetailAndChangePositionIntegrationTests.cs
git commit -m "test: add end-to-end coverage for employee detail and change-position"
```

---

## Done — hands off to the frontend plan

`Hrms--Web-application---front-end---v1/docs/superpowers/plans/2026-08-16-employee-detail-screen-frontend.md` consumes `GET /api/v1/employees/{id}/detail` and `POST /api/v1/employees/{id}/change-position` built here.
