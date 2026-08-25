# Tray App Employee Access Toggle (Phase A) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let an admin/HR user (`employees:write`) block a specific employee from connecting
the OneXso Workspace desktop tray agent, via a new `TrayAppAccessEnabled` flag on `Employee`,
enforced at both activation-code generate and exchange.

**Architecture:** One new boolean column + EF migration, one new toggle command + endpoint
(mirrors the existing `RevokeEmployeeInvitation` command/endpoint shape exactly), and a gate
inserted into two existing handlers (`GenerateActivationCodeCommandHandler`,
`ExchangeActivationCodeCommandHandler`) that already have (or, for Exchange, are given) access
to the employee's tenant-scoped profile. No new tables, no new permission code.

**Tech Stack:** .NET 10 / C# 14, MediatR (CQRS), EF Core + PostgreSQL, xUnit + Moq (unit),
Testcontainers PostgreSQL (integration).

**Spec:** `docs/superpowers/specs/2026-08-25-tray-app-employee-access-toggle-backend-design.md`

---

### Task 1: `TrayAppAccessEnabled` column + migration

**Files:**
- Modify: `src/ONEVO.Domain/Features/CoreHr/Entities/Employee.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Configurations/CoreHr/Employee/EmployeeConfiguration.cs`
- Create: EF migration (via `dotnet ef migrations add`)

- [ ] **Step 1: Add the property to the domain entity**

In `src/ONEVO.Domain/Features/CoreHr/Entities/Employee.cs`, add after `DisplayTimezone`:

```csharp
    public string? DisplayTimezone { get; set; }
    public bool TrayAppAccessEnabled { get; set; } = true;
```

- [ ] **Step 2: Configure the column with a database-level default**

In `EmployeeConfiguration.cs`, add after the `DisplayTimezone` property mapping (line 22):

```csharp
        builder.Property(e => e.DisplayTimezone).HasMaxLength(50);
        builder.Property(e => e.TrayAppAccessEnabled).IsRequired().HasDefaultValue(true);
```

- [ ] **Step 3: Generate the migration**

Run (from `HRMS-Backend-v1/`):
```bash
dotnet ef migrations add AddEmployeeTrayAppAccessEnabled --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api --output-dir Migrations
```
Expected: a new `Migrations/{timestamp}_AddEmployeeTrayAppAccessEnabled.cs` /
`.Designer.cs` pair is generated, and `ApplicationDbContextModelSnapshot.cs` is updated to
include `TrayAppAccessEnabled` on the `employees` table. Open the generated migration and
confirm its `Up()` contains an `AddColumn<bool>(name: "tray_app_access_enabled", ..., defaultValue: true)`
call (exact column name may vary slightly by the project's snake_case convention — match
whatever the generator produces, don't hand-edit it to a different name).

- [ ] **Step 4: Apply the migration to the local database**

Run (from `HRMS-Backend-v1/`):
```bash
dotnet ef database update --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api
```
Expected: migration applies cleanly, no errors.

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Domain/Features/CoreHr/Entities/Employee.cs src/ONEVO.Infrastructure/Persistence/Configurations/CoreHr/Employee/EmployeeConfiguration.cs src/ONEVO.Infrastructure/Migrations/
git commit -m "feat: add TrayAppAccessEnabled column to Employee"
```

---

### Task 2: `SetEmployeeTrayAppAccessCommand` + handler + endpoint

**Files:**
- Create: `src/ONEVO.Application/Features/CoreHr/Employee/Commands/SetEmployeeTrayAppAccess/SetEmployeeTrayAppAccessCommand.cs`
- Create: `src/ONEVO.Application/Features/CoreHr/Employee/Commands/SetEmployeeTrayAppAccess/SetEmployeeTrayAppAccessCommandHandler.cs`
- Create: `src/ONEVO.Api/Contracts/CoreHr/Employees/SetTrayAppAccessRequest.cs`
- Modify: `src/ONEVO.Api/Controllers/Tenant/CoreHr/EmployeesController.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/SetEmployeeTrayAppAccessCommandHandlerTests.cs`

- [ ] **Step 1: Write the failing handler test**

```csharp
// tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/SetEmployeeTrayAppAccessCommandHandlerTests.cs
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Commands.SetEmployeeTrayAppAccess;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.CoreHr.Employee;

public class SetEmployeeTrayAppAccessCommandHandlerTests
{
    private readonly Mock<IEmployeeRepository> _employeeRepository = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<IDateTimeProvider> _clock = new();

    private SetEmployeeTrayAppAccessCommandHandler CreateHandler() => new(
        _employeeRepository.Object,
        _currentUser.Object,
        _clock.Object);

    private static ONEVO.Domain.Features.CoreHr.Entities.Employee BuildEmployee(Guid tenantId, Guid id, bool trayAppAccessEnabled) => new()
    {
        Id = id,
        TenantId = tenantId,
        EmployeeNumber = "EMP-0001",
        FirstName = "Priya",
        LastName = "Employee",
        Email = "priya.employee@test.dev",
        HireDate = DateOnly.FromDateTime(DateTime.UtcNow),
        TrayAppAccessEnabled = trayAppAccessEnabled
    };

    [Fact]
    public async Task Handle_DisablesAccess_SavesFlagFalse()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var employee = BuildEmployee(tenantId, employeeId, trayAppAccessEnabled: true);

        _currentUser.Setup(c => c.TenantId).Returns(tenantId);
        _clock.Setup(c => c.UtcNow).Returns(DateTimeOffset.UtcNow);
        _employeeRepository
            .Setup(r => r.GetTrackedByIdAsync(tenantId, employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(employee);

        var handler = CreateHandler();
        var result = await handler.Handle(
            new SetEmployeeTrayAppAccessCommand(employeeId, false), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(employee.TrayAppAccessEnabled);
        _employeeRepository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_EnablesAccess_SavesFlagTrue()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var employee = BuildEmployee(tenantId, employeeId, trayAppAccessEnabled: false);

        _currentUser.Setup(c => c.TenantId).Returns(tenantId);
        _clock.Setup(c => c.UtcNow).Returns(DateTimeOffset.UtcNow);
        _employeeRepository
            .Setup(r => r.GetTrackedByIdAsync(tenantId, employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(employee);

        var handler = CreateHandler();
        var result = await handler.Handle(
            new SetEmployeeTrayAppAccessCommand(employeeId, true), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(employee.TrayAppAccessEnabled);
    }

    [Fact]
    public async Task Handle_EmployeeNotFound_ReturnsFailure()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        _currentUser.Setup(c => c.TenantId).Returns(tenantId);
        _employeeRepository
            .Setup(r => r.GetTrackedByIdAsync(tenantId, employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ONEVO.Domain.Features.CoreHr.Entities.Employee?)null);

        var handler = CreateHandler();
        var result = await handler.Handle(
            new SetEmployeeTrayAppAccessCommand(employeeId, false), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("Employee not found.", result.Error);
        _employeeRepository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run (from `HRMS-Backend-v1/`):
```bash
dotnet test tests/ONEVO.Tests.Unit --filter SetEmployeeTrayAppAccessCommandHandlerTests
```
Expected: FAIL to build — `SetEmployeeTrayAppAccessCommand`/`SetEmployeeTrayAppAccessCommandHandler` don't exist yet.

- [ ] **Step 3: Implement the command**

```csharp
// src/ONEVO.Application/Features/CoreHr/Employee/Commands/SetEmployeeTrayAppAccess/SetEmployeeTrayAppAccessCommand.cs
using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.CoreHr.Employee.Commands.SetEmployeeTrayAppAccess;

public sealed record SetEmployeeTrayAppAccessCommand(Guid EmployeeId, bool Enabled)
    : IRequest<Result<Unit>>;
```

- [ ] **Step 4: Implement the handler**

```csharp
// src/ONEVO.Application/Features/CoreHr/Employee/Commands/SetEmployeeTrayAppAccess/SetEmployeeTrayAppAccessCommandHandler.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;

namespace ONEVO.Application.Features.CoreHr.Employee.Commands.SetEmployeeTrayAppAccess;

/// <summary>
/// Admin/HR toggle controlling whether an employee may connect (or keep connecting) the
/// OneXso Workspace desktop tray agent. Enforced at activation-code generate and exchange time -
/// see GenerateActivationCodeCommandHandler and ExchangeActivationCodeCommandHandler. Does not
/// revoke an already-active device registration; that is a separate, not-yet-built action.
/// </summary>
public sealed class SetEmployeeTrayAppAccessCommandHandler
    : IRequestHandler<SetEmployeeTrayAppAccessCommand, Result<Unit>>
{
    private readonly IEmployeeRepository _employeeRepository;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    public SetEmployeeTrayAppAccessCommandHandler(
        IEmployeeRepository employeeRepository,
        ICurrentUser currentUser,
        IDateTimeProvider clock)
    {
        _employeeRepository = employeeRepository;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Result<Unit>> Handle(SetEmployeeTrayAppAccessCommand request, CancellationToken ct)
    {
        var tenantId = _currentUser.TenantId;

        var employee = await _employeeRepository.GetTrackedByIdAsync(tenantId, request.EmployeeId, ct);
        if (employee is null)
            return Result<Unit>.Failure("Employee not found.", 404);

        employee.TrayAppAccessEnabled = request.Enabled;
        employee.UpdatedAt = _clock.UtcNow;

        await _employeeRepository.SaveChangesAsync(ct);

        return Result<Unit>.Success(Unit.Value);
    }
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter SetEmployeeTrayAppAccessCommandHandlerTests`
Expected: PASS — all 3 tests green.

- [ ] **Step 6: Add the request contract**

```csharp
// src/ONEVO.Api/Contracts/CoreHr/Employees/SetTrayAppAccessRequest.cs
namespace ONEVO.Api.Contracts.CoreHr.Employees;

public sealed record SetTrayAppAccessRequest(bool Enabled);
```

- [ ] **Step 7: Wire the endpoint**

In `src/ONEVO.Api/Controllers/Tenant/CoreHr/EmployeesController.cs`, add the using statement
alongside the existing `Commands.RevokeEmployeeInvitation` import:

```csharp
using ONEVO.Application.Features.CoreHr.Employee.Commands.SetEmployeeTrayAppAccess;
```

and add the endpoint right after `RevokeInvitation` (currently ends at line 138):

```csharp
    /// <summary>Enable or disable this employee's ability to connect (or keep connecting) the
    /// OneXso Workspace desktop tray agent. Enforced server-side at activation generate/exchange
    /// time - see MonitoringActivationController. Does not revoke an already-active device.</summary>
    [HttpPost("{id:guid}/tray-app-access")]
    [RequirePermission("employees:write")]
    public async Task<IActionResult> SetTrayAppAccess(
        Guid id, [FromBody] SetTrayAppAccessRequest request, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new SetEmployeeTrayAppAccessCommand(id, request.Enabled), ct);

        return result.IsSuccess
            ? NoContent()
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
```

- [ ] **Step 8: Build to verify the controller wires up cleanly**

Run: `dotnet build src/ONEVO.Api --no-restore`
Expected: build succeeds, 0 errors.

- [ ] **Step 9: Commit**

```bash
git add src/ONEVO.Application/Features/CoreHr/Employee/Commands/SetEmployeeTrayAppAccess/ src/ONEVO.Api/Contracts/CoreHr/Employees/SetTrayAppAccessRequest.cs src/ONEVO.Api/Controllers/Tenant/CoreHr/EmployeesController.cs tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/SetEmployeeTrayAppAccessCommandHandlerTests.cs
git commit -m "feat: add POST /employees/{id}/tray-app-access toggle endpoint"
```

---

### Task 3: Surface the flag on `GET /employees/{id}/detail`

**Files:**
- Modify: `src/ONEVO.Application/Features/CoreHr/Employee/DTOs/Responses/EmployeeDetailResponse.cs`
- Modify: `src/ONEVO.Application/Features/CoreHr/Employee/Queries/GetEmployeeDetail/GetEmployeeDetailQueryHandler.cs`

- [ ] **Step 1: Add the field to the response record**

In `EmployeeDetailResponse.cs`, add `bool TrayAppAccessEnabled` as the last positional
parameter:

```csharp
public record EmployeeDetailResponse(
    Guid Id,
    EmployeeDetailJobInformation JobInformation,
    EmployeeDetailPersonalInformation PersonalInformation,
    IReadOnlyList<EmployeeDetailEmergencyContact> EmergencyContacts,
    EmployeeDetailPayroll? Payroll,
    string? InvitationStatus,
    DateTimeOffset? InvitationExpiresAt,
    bool TrayAppAccessEnabled);
```

- [ ] **Step 2: Populate it in the query handler**

Open `GetEmployeeDetailQueryHandler.cs`, find the line that constructs the final
`EmployeeDetailResponse` (it will be a `new EmployeeDetailResponse(...)` call using the loaded
`Employee` entity — the exact local variable name depends on the handler's existing code, e.g.
`employee`). Add `employee.TrayAppAccessEnabled` as the last constructor argument, matching the
field order added in Step 1.

- [ ] **Step 3: Build to verify the DTO change compiles everywhere it's constructed**

Run: `dotnet build src/ONEVO.Application --no-restore`
Expected: build succeeds. If any other place constructs `EmployeeDetailResponse` (e.g. a test
helper), the compiler error will name it — add `TrayAppAccessEnabled` there too, sourced from
that call site's already-available `Employee` entity.

- [ ] **Step 4: Commit**

```bash
git add src/ONEVO.Application/Features/CoreHr/Employee/DTOs/Responses/EmployeeDetailResponse.cs src/ONEVO.Application/Features/CoreHr/Employee/Queries/GetEmployeeDetail/GetEmployeeDetailQueryHandler.cs
git commit -m "feat: surface trayAppAccessEnabled on GET /employees/{id}/detail"
```

---

### Task 4: Gate `GenerateActivationCodeCommandHandler`

**Files:**
- Modify: `src/ONEVO.Application/Features/Monitoring/TrayActivation/Commands/GenerateActivationCode/GenerateActivationCodeCommandHandler.cs`
- Modify: `src/ONEVO.Application/Features/Monitoring/TrayActivation/RepositoryInterfaces/ITrayActivationRepository.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/Monitoring/TrayActivation/EfTrayActivationRepository.cs`

- [ ] **Step 1: Extend `TrayEmployeeProfile` with the new field (append last)**

In `ITrayActivationRepository.cs:29`, change:

```csharp
public sealed record TrayEmployeeProfile(string FirstName, string LastName, string Email, string EmployeeNumber);
```
to:
```csharp
public sealed record TrayEmployeeProfile(string FirstName, string LastName, string Email, string EmployeeNumber, bool TrayAppAccessEnabled);
```

- [ ] **Step 2: Update the EF projection**

In `EfTrayActivationRepository.cs`, update `FindEmployeeProfileAsync`:

```csharp
    public async Task<TrayEmployeeProfile?> FindEmployeeProfileAsync(Guid userId, Guid tenantId, CancellationToken ct)
    {
        return await _db.Employees
            .Where(e => e.UserId == userId && e.TenantId == tenantId)
            .Select(e => new TrayEmployeeProfile(e.FirstName, e.LastName, e.Email, e.EmployeeNumber, e.TrayAppAccessEnabled))
            .FirstOrDefaultAsync(ct);
    }
```

- [ ] **Step 3: Build to confirm `RefreshTrayTokenCommandHandler` still compiles unchanged**

Run: `dotnet build src/ONEVO.Application --no-restore`
Expected: build succeeds — `RefreshTrayTokenCommandHandler.cs:132`'s
`profile.FirstName`/`.LastName`/`.Email`/`.EmployeeNumber` named-property access is unaffected
by the new appended field.

- [ ] **Step 4: Add the access check to `GenerateActivationCodeCommandHandler`**

In `GenerateActivationCodeCommandHandler.cs`, insert the check as the first thing in `Handle`,
before the rate-limit check (currently the method starts with `var userId = _currentUser.UserId;`):

```csharp
    public async Task<Result<ActivationCodeResponseDto>> Handle(
        GenerateActivationCodeCommand request,
        CancellationToken cancellationToken)
    {
        var userId = _currentUser.UserId;
        var tenantId = _currentUser.TenantId;
        var now = _clock.UtcNow;

        var profile = await _repository.FindEmployeeProfileAsync(userId, tenantId, cancellationToken);
        if (profile is not null && !profile.TrayAppAccessEnabled)
            return Result<ActivationCodeResponseDto>.Failure(
                "Your account is not permitted to connect a desktop device. Contact your admin.", 403);

        var recentCount = await _repository.CountRecentCodesForUserAsync(
            userId, tenantId, now.AddHours(-1), cancellationToken);
```

(the rest of the method — rate-limit check, code generation, save, return — is unchanged).

- [ ] **Step 5: Build**

Run: `dotnet build src/ONEVO.Application --no-restore`
Expected: build succeeds.

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Application/Features/Monitoring/TrayActivation/
git commit -m "feat: gate activation-code generate on employee tray app access"
```

---

### Task 5: Gate `ExchangeActivationCodeCommandHandler`

**Files:**
- Modify: `src/ONEVO.Application/Features/Monitoring/TrayActivation/Commands/ExchangeActivationCode/ExchangeActivationCodeCommandHandler.cs`

- [ ] **Step 1: Rewrite the handler**

Replace the full contents of `ExchangeActivationCodeCommandHandler.cs` (this restructures the
class: the tenant lookup + context switch move from the old `ResolveEmployeeIdentityAsync`
private method to happen right after the code is found, so the new access check can run before
`MarkCodeUsedAsync`; the already-fetched `profile` is then reused for the identity fields
instead of being re-fetched, so `ResolveEmployeeIdentityAsync` is removed):

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.TrayActivation.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.TrayActivation.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.TrayActivation.ServiceInterfaces;
using ONEVO.Domain.Features.Monitoring.TrayActivation.Entities;

namespace ONEVO.Application.Features.Monitoring.TrayActivation.Commands.ExchangeActivationCode;

public class ExchangeActivationCodeCommandHandler
    : IRequestHandler<ExchangeActivationCodeCommand, Result<TrayAuthResponseDto>>
{
    private static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(90);
    private const int AccessTokenExpiresInSeconds = 3600;
    private const int RefreshTokenExpiresInSeconds = 7_776_000; // 90 days

    private readonly ITrayActivationRepository _repository;
    private readonly IUserRepository _userRepository;
    private readonly ITenantRepository _tenantRepository;
    private readonly ITenantContextSwitcher _tenantSwitcher;
    private readonly ITrayTokenService _tokenService;
    private readonly IDateTimeProvider _clock;
    private readonly IUnitOfWork _unitOfWork;

    public ExchangeActivationCodeCommandHandler(
        ITrayActivationRepository repository,
        IUserRepository userRepository,
        ITenantRepository tenantRepository,
        ITenantContextSwitcher tenantSwitcher,
        ITrayTokenService tokenService,
        IDateTimeProvider clock,
        IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _userRepository = userRepository;
        _tenantRepository = tenantRepository;
        _tenantSwitcher = tenantSwitcher;
        _tokenService = tokenService;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<TrayAuthResponseDto>> Handle(
        ExchangeActivationCodeCommand request,
        CancellationToken cancellationToken)
    {
        var codeHash = _tokenService.HashToken(request.Code);

        // TenantId is unknown at this stage — the code lookup must match globally
        // within the hashed code; the repository filters by hash only here.
        var activationCode = await _repository.FindActiveCodeByHashAsync(codeHash, cancellationToken);

        if (activationCode is null)
            return Result<TrayAuthResponseDto>.Failure("Invalid or expired activation code.", 401);

        // Exchange runs anonymously at the base host (System tenant-context mode): switch into
        // the now-resolved tenant before reading employees/users, both RLS-protected to 'admin'
        // or matching 'tenant' mode only. Done here (before the code is consumed) rather than
        // after, so the new access-control check below can run before anything is created — a
        // missing tenant now fails cleanly instead of leaving an orphaned device registration.
        var tenant = await _tenantRepository.GetByIdAsync(activationCode.TenantId, cancellationToken);
        if (tenant is null)
            return Result<TrayAuthResponseDto>.Failure("Invalid or expired activation code.", 401);

        await _tenantSwitcher.SwitchToTenantAsync(
            new TenantRegistryEntry(tenant.Id, tenant.Slug, tenant.Status, PlanCode: null), cancellationToken);

        var profile = await _repository.FindEmployeeProfileAsync(
            activationCode.UserId, activationCode.TenantId, cancellationToken);

        // No Employee row yet (auth User not linked to HR onboarding) - never block on a gate
        // that doesn't apply yet; only an existing employee record with the flag explicitly off
        // is denied.
        if (profile is not null && !profile.TrayAppAccessEnabled)
            return Result<TrayAuthResponseDto>.Failure(
                "Your account is not permitted to connect a desktop device. Contact your admin.", 403);

        var now = _clock.UtcNow;

        await _repository.MarkCodeUsedAsync(activationCode, cancellationToken);

        var device = new TrayDeviceRegistration
        {
            Id = Guid.NewGuid(),
            TenantId = activationCode.TenantId,
            UserId = activationCode.UserId,
            DeviceName = request.DeviceName,
            DeviceOs = request.DeviceOs,
            DeviceFingerprint = request.DeviceFingerprint,
            IsActive = true,
            ActivatedAt = now,
            CreatedAt = now
        };

        await _repository.AddDeviceRegistrationAsync(device, cancellationToken);

        var rawRefreshToken = _tokenService.GenerateRawRefreshToken();
        var refreshTokenHash = _tokenService.HashToken(rawRefreshToken);

        var refreshToken = new TrayDeviceRefreshToken
        {
            Id = Guid.NewGuid(),
            TenantId = activationCode.TenantId,
            UserId = activationCode.UserId,
            DeviceRegistrationId = device.Id,
            TokenHash = refreshTokenHash,
            ExpiresAt = now.Add(RefreshTokenLifetime),
            IsRevoked = false,
            CreatedAt = now
        };

        await _repository.AddRefreshTokenAsync(refreshToken, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var accessToken = _tokenService.GenerateAccessToken(
            device.Id, activationCode.UserId, activationCode.TenantId);

        var (employeeName, employeeEmail, employeeNumber) = await ResolveDisplayIdentityAsync(
            profile, activationCode.UserId, cancellationToken);

        return Result<TrayAuthResponseDto>.Success(new TrayAuthResponseDto(
            accessToken,
            AccessTokenExpiresInSeconds,
            rawRefreshToken,
            RefreshTokenExpiresInSeconds,
            employeeName,
            employeeEmail,
            employeeNumber));
    }

    /// <summary>
    /// Display-only identity for the tray UI. Prefers the already-fetched HR Employee profile
    /// (name, email, employee number); falls back to the auth User's name/email if no Employee
    /// row is linked yet, so a fresh device activation never fails just because HR onboarding
    /// hasn't finished. Tenant context is already switched by the time this runs (see Handle).
    /// </summary>
    private async Task<(string? Name, string? Email, string? Number)> ResolveDisplayIdentityAsync(
        TrayEmployeeProfile? profile, Guid userId, CancellationToken ct)
    {
        if (profile is not null)
            return (FullNameOrNull(profile.FirstName, profile.LastName), profile.Email, profile.EmployeeNumber);

        var user = await _userRepository.GetByIdAsync(userId, ct);
        if (user is not null)
            return (FullNameOrNull(user.FirstName, user.LastName), user.Email, null);

        return (null, null, null);
    }

    private static string? FullNameOrNull(string first, string last)
    {
        var name = $"{first} {last}".Trim();
        return string.IsNullOrEmpty(name) ? null : name;
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build src/ONEVO.Application --no-restore`
Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git add src/ONEVO.Application/Features/Monitoring/TrayActivation/Commands/ExchangeActivationCode/ExchangeActivationCodeCommandHandler.cs
git commit -m "feat: gate activation-code exchange on employee tray app access"
```

---

### Task 6: Integration tests for the new gate

**Files:**
- Modify: `tests/ONEVO.Tests.Integration/Monitoring/TrayActivation/TrayActivationIntegrationTests.cs`

- [ ] **Step 1: Write the failing integration tests**

Add these three `[Fact]` methods to the class, near the existing `Generate_*`/`Exchange_*` tests
(around line 91-150). They use the same `SeedActiveUserAsync`/`SeedActiveUserWithEmployeeAsync`/
`LoginAndGetSessionAsync`/`PostGenerateAsync`/`GenerateCodeAsync`/`PostExchangeAsync` helpers
already defined later in the file:

```csharp
    [Fact]
    public async Task Generate_TrayAppAccessDisabled_Returns403()
    {
        var user = await SeedActiveUserWithEmployeeAsync(
            "gen-blocked-test", "gen-blocked@test.dev", "GenPass1!", "EMP-BLOCK-01");
        await SetTrayAppAccessAsync(user.TenantId, "EMP-BLOCK-01", enabled: false);
        var session = await LoginAndGetSessionAsync(user);

        var response = await PostGenerateAsync(session);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Exchange_TrayAppAccessDisabledAfterGenerate_Returns403()
    {
        var user = await SeedActiveUserWithEmployeeAsync(
            "exch-blocked-test", "exch-blocked@test.dev", "ExchPass1!", "EMP-BLOCK-02");
        var session = await LoginAndGetSessionAsync(user);
        var code = await GenerateCodeAsync(session);

        // Access revoked after the code was minted but before it's redeemed.
        await SetTrayAppAccessAsync(user.TenantId, "EMP-BLOCK-02", enabled: false);

        var response = await PostExchangeAsync(code, "My Laptop", "Windows 11", "fp-device-002");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Exchange_NoEmployeeRowYet_StillSucceeds()
    {
        // Auth User with no linked Employee row — the gate must not apply before HR onboarding
        // links one, matching the existing "never block just because onboarding isn't done" rule.
        var user = await SeedActiveUserAsync("exch-noemp-test", "exch-noemp@test.dev", "ExchPass1!");
        var session = await LoginAndGetSessionAsync(user);
        var code = await GenerateCodeAsync(session);

        var response = await PostExchangeAsync(code, "My Laptop", "Windows 11", "fp-device-003");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
```

Add the helper used above near the other private helpers (e.g. right after
`PostRevokeAsync`, around line 495):

```csharp
    private async Task SetTrayAppAccessAsync(Guid tenantId, string employeeNumber, bool enabled)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var employee = await db.Employees
            .IgnoreQueryFilters()
            .SingleAsync(e => e.TenantId == tenantId && e.EmployeeNumber == employeeNumber);
        employee.TrayAppAccessEnabled = enabled;
        await db.SaveChangesAsync();
    }
```

`.IgnoreQueryFilters()` is required here because this helper runs outside any tenant-context
scope the RLS/EF query filter expects — check the file's existing `using` statements for
`Microsoft.EntityFrameworkCore` (needed for `IgnoreQueryFilters`/`SingleAsync`); it's already
imported at the top of the file for `Migrations_ApplyCleanly_AndLeaveNoPendingMigrations`.

- [ ] **Step 2: Run the tests to verify they fail**

Run (from `HRMS-Backend-v1/`, requires Docker for Testcontainers):
```bash
dotnet test tests/ONEVO.Tests.Integration --filter "FullyQualifiedName~TrayActivationIntegrationTests"
```
Expected: FAIL — `Generate_TrayAppAccessDisabled_Returns403` and
`Exchange_TrayAppAccessDisabledAfterGenerate_Returns403` get `200`/`201` instead of `403` (the
gate doesn't exist without Tasks 4-5); `Exchange_NoEmployeeRowYet_StillSucceeds` should already
pass (no code change needed for it — it's a regression guard).

- [ ] **Step 3: Confirm the tests pass now that Tasks 4-5 are done**

Run the same command as Step 2.
Expected: PASS — all `TrayActivationIntegrationTests` green, including the 3 new cases.

- [ ] **Step 4: Commit**

```bash
git add tests/ONEVO.Tests.Integration/Monitoring/TrayActivation/TrayActivationIntegrationTests.cs
git commit -m "test: cover tray app access gate in TrayActivationIntegrationTests"
```

---

### Task 7: Full regression run

**Files:** none (verification only)

- [ ] **Step 1: Run the full unit test suite**

Run: `dotnet test tests/ONEVO.Tests.Unit`
Expected: PASS, no regressions.

- [ ] **Step 2: Run the full integration test suite**

Run: `dotnet test tests/ONEVO.Tests.Integration` (requires Docker)
Expected: PASS, no regressions — in particular
`Migrations_ApplyCleanly_AndLeaveNoPendingMigrations` confirms the new migration from Task 1
applies cleanly with no drift.

- [ ] **Step 3: Commit (only if the regression run surfaced a fix)**

```bash
git add -A
git commit -m "fix: <describe whatever the regression run turned up>"
```
