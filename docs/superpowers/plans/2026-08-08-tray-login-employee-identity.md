# Tray Login: Real Employee Identity Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** After a successful tray-app activation, show the real employee's name/email/employee-number instead of the hardcoded `"Pirakeerthan" / "pirakeerthan@onexso.com" / "ONEXSO1234"` currently baked into `PrepareWorkspaceViewModel`.

**Architecture:** Extend the existing `/api/v1/monitoring/activation/exchange` and `/refresh` responses (`TrayAuthResponseDto`) with three nullable fields sourced from the `Employees` table (falling back to the `Users` table if no `Employee` row exists yet). Thread those fields through the already-wired Agent Service IPC path (`OnevoApiClient` → `AgentWorker` → `EnrollmentResultPayload`) into the TrayApp, where `ConnectWorkspaceViewModel` caches them and `PrepareWorkspaceViewModel` reads them back instead of hardcoding. Preference storage is accessed through a new small `IPreferencesStore` seam (Task 5) rather than the static MAUI `Preferences` API directly, because that static API throws outside a running MAUI app and would make Tasks 6-7 untestable otherwise (confirmed empirically before writing this plan).

**Tech Stack:** ASP.NET Core / EF Core / PostgreSQL (backend, repo `HRMS-Backend-v1`), .NET Windows Service + .NET MAUI (Agent Service + TrayApp, repo `tray_app_maui`), xUnit + FluentAssertions + Testcontainers.

**Design doc:** `docs/superpowers/specs/2026-08-08-tray-login-employee-identity-design.md`

---

## Task 1: Backend — employee profile on the Exchange response

**Repo:** `HRMS-Backend-v1` (run all commands from this directory unless noted)

**Files:**
- Modify: `src/ONEVO.Application/Features/Monitoring/TrayActivation/DTOs/Responses/TrayAuthResponseDto.cs`
- Modify: `src/ONEVO.Application/Features/Monitoring/TrayActivation/RepositoryInterfaces/ITrayActivationRepository.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/Monitoring/TrayActivation/EfTrayActivationRepository.cs`
- Modify: `src/ONEVO.Application/Features/Monitoring/TrayActivation/Commands/ExchangeActivationCode/ExchangeActivationCodeCommandHandler.cs`
- Test: `tests/ONEVO.Tests.Integration/Monitoring/TrayActivation/TrayActivationIntegrationTests.cs`

- [ ] **Step 1: Extend the two integration tests that exercise Exchange — this will fail to compile/run until later steps**

In `tests/ONEVO.Tests.Integration/Monitoring/TrayActivation/TrayActivationIntegrationTests.cs`, add `using ONEVO.Domain.Features.CoreHr.Entities;` to the top `using` block (alongside the existing `using ONEVO.Domain.Features.Auth.Entities;` line).

Add a new private helper right after `SeedActiveUserAsync` (around line 332, before the `LoginAndGetSessionAsync` method):

```csharp
    private async Task<SeedResult> SeedActiveUserWithEmployeeAsync(
        string tenantSlug, string email, string password, string employeeNumber)
    {
        var seed = await SeedActiveUserAsync(tenantSlug, email, password);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Add(new Employee
        {
            Id = Guid.NewGuid(),
            TenantId = seed.TenantId,
            UserId = seed.UserId,
            EmployeeNumber = employeeNumber,
            FirstName = "Priya",
            LastName = "Employee",
            Email = email,
            EmploymentTypeId = 1,
            EmploymentStatusId = 1,
            WorkModeId = 1,
            HireDate = new DateOnly(2025, 1, 1),
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedById = seed.UserId
        });
        await db.SaveChangesAsync();

        return seed;
    }
```

Replace the existing `Exchange_ValidCode_Returns200WithAccessAndRefreshTokens` test body:

```csharp
    [Fact]
    public async Task Exchange_ValidCode_Returns200WithAccessAndRefreshTokens()
    {
        var user = await SeedActiveUserWithEmployeeAsync(
            "exchange-valid-test", "exchange-valid@test.dev", "ExchPass1!", "EMP-0001");
        var session = await LoginAndGetSessionAsync(user);
        var code = await GenerateCodeAsync(session);

        var response = await PostExchangeAsync(code, "My Laptop", "Windows 11", "fp-device-001");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("access_token").GetString().Should().StartWith("eyJ");
        doc.RootElement.GetProperty("refresh_token").GetString().Should().NotBeNullOrEmpty();
        doc.RootElement.GetProperty("expires_in_seconds").GetInt32().Should().Be(3600);
        doc.RootElement.GetProperty("refresh_expires_in_seconds").GetInt32().Should().Be(7_776_000);
        doc.RootElement.GetProperty("employee_name").GetString().Should().Be("Priya Employee");
        doc.RootElement.GetProperty("employee_email").GetString().Should().Be("exchange-valid@test.dev");
        doc.RootElement.GetProperty("employee_number").GetString().Should().Be("EMP-0001");
        body.Should().NotContain(user.UserId.ToString(), "response must never expose internal user ID");
        body.Should().NotContain(user.TenantId.ToString(), "response must never expose internal tenant ID");
    }

    [Fact]
    public async Task Exchange_UserWithoutEmployeeRecord_FallsBackToUserNameAndEmail()
    {
        var user = await SeedActiveUserAsync(
            "exchange-noemp-test", "exchange-noemp@test.dev", "NoEmpPass1!");
        var session = await LoginAndGetSessionAsync(user);
        var code = await GenerateCodeAsync(session);

        var response = await PostExchangeAsync(code, "My Laptop", "Windows 11", "fp-device-noemp");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("employee_name").GetString().Should().Be("Test User");
        doc.RootElement.GetProperty("employee_email").GetString().Should().Be("exchange-noemp@test.dev");
        doc.RootElement.TryGetProperty("employee_number", out var numberProp).Should().BeTrue();
        numberProp.ValueKind.Should().Be(JsonValueKind.Null);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests\ONEVO.Tests.Integration\ONEVO.Tests.Integration.csproj --filter "Exchange_ValidCode_Returns200WithAccessAndRefreshTokens|Exchange_UserWithoutEmployeeRecord_FallsBackToUserNameAndEmail" --verbosity minimal`

Expected: FAIL — `Exchange_ValidCode_...` throws `KeyNotFoundException` (or similar) on `doc.RootElement.GetProperty("employee_name")` because the current `TrayAuthResponseDto` doesn't serialize that property. `Exchange_UserWithoutEmployeeRecord_...` fails the same way. (Requires Docker running — these are Testcontainers-backed integration tests, same as the other 16 tests in this file.)

- [ ] **Step 3: Extend `TrayAuthResponseDto` with the 3 new fields**

Replace the full contents of `src/ONEVO.Application/Features/Monitoring/TrayActivation/DTOs/Responses/TrayAuthResponseDto.cs`:

```csharp
using System.Text.Json.Serialization;

namespace ONEVO.Application.Features.Monitoring.TrayActivation.DTOs.Responses;

public record TrayAuthResponseDto(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("expires_in_seconds")] int ExpiresInSeconds,
    [property: JsonPropertyName("refresh_token")] string RefreshToken,
    [property: JsonPropertyName("refresh_expires_in_seconds")] int RefreshExpiresInSeconds,
    [property: JsonPropertyName("employee_name")] string? EmployeeName,
    [property: JsonPropertyName("employee_email")] string? EmployeeEmail,
    [property: JsonPropertyName("employee_number")] string? EmployeeNumber);
```

- [ ] **Step 4: Add the employee-profile lookup to the repository interface**

Replace the full contents of `src/ONEVO.Application/Features/Monitoring/TrayActivation/RepositoryInterfaces/ITrayActivationRepository.cs`:

```csharp
using ONEVO.Domain.Features.Monitoring.TrayActivation.Entities;

namespace ONEVO.Application.Features.Monitoring.TrayActivation.RepositoryInterfaces;

public interface ITrayActivationRepository
{
    Task<int> CountRecentCodesForUserAsync(Guid userId, Guid tenantId, DateTimeOffset since, CancellationToken ct);
    Task AddActivationCodeAsync(TrayActivationCode code, CancellationToken ct);

    // Exchange endpoint: tenantId unknown at call time — hash is globally unique
    Task<TrayActivationCode?> FindActiveCodeByHashAsync(string codeHash, CancellationToken ct);
    Task MarkCodeUsedAsync(TrayActivationCode code, CancellationToken ct);

    Task AddDeviceRegistrationAsync(TrayDeviceRegistration device, CancellationToken ct);
    Task AddRefreshTokenAsync(TrayDeviceRefreshToken token, CancellationToken ct);

    Task<TrayDeviceRefreshToken?> FindActiveRefreshTokenAsync(string tokenHash, CancellationToken ct);
    Task RevokeRefreshTokenAsync(TrayDeviceRefreshToken token, string reason, CancellationToken ct);
    Task RevokeAllRefreshTokensForDeviceAsync(Guid deviceRegistrationId, string reason, CancellationToken ct);

    Task<TrayDeviceRegistration?> FindActiveDeviceAsync(Guid deviceRegistrationId, Guid tenantId, CancellationToken ct);
    Task UpdateDeviceLastSeenAsync(Guid deviceRegistrationId, DateTimeOffset lastSeenAt, CancellationToken ct);
    Task DeactivateDeviceAsync(Guid deviceRegistrationId, DateTimeOffset deactivatedAt, CancellationToken ct);

    /// <summary>HR profile for display purposes only — returns null if the user has no linked Employee row yet.</summary>
    Task<TrayEmployeeProfile?> FindEmployeeProfileAsync(Guid userId, Guid tenantId, CancellationToken ct);
}

public sealed record TrayEmployeeProfile(string FirstName, string LastName, string Email, string EmployeeNumber);
```

- [ ] **Step 5: Implement the lookup in `EfTrayActivationRepository`**

In `src/ONEVO.Infrastructure/Persistence/Repositories/Monitoring/TrayActivation/EfTrayActivationRepository.cs`, add one new using directive at the top of the file — `using ONEVO.Domain.Features.CoreHr.Entities;` (the `TrayEmployeeProfile`/`RepositoryInterfaces` namespace is already imported via the existing `using ONEVO.Application.Features.Monitoring.TrayActivation.RepositoryInterfaces;` line). Then add this method inside the `EfTrayActivationRepository` class, right after `DeactivateDeviceAsync`:

```csharp
    public async Task<TrayEmployeeProfile?> FindEmployeeProfileAsync(Guid userId, Guid tenantId, CancellationToken ct)
    {
        return await _db.Employees
            .Where(e => e.UserId == userId && e.TenantId == tenantId)
            .Select(e => new TrayEmployeeProfile(e.FirstName, e.LastName, e.Email, e.EmployeeNumber))
            .FirstOrDefaultAsync(ct);
    }
```

- [ ] **Step 6: Wire the lookup + User fallback into `ExchangeActivationCodeCommandHandler`**

Replace the full contents of `src/ONEVO.Application/Features/Monitoring/TrayActivation/Commands/ExchangeActivationCode/ExchangeActivationCodeCommandHandler.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
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
    private readonly ITrayTokenService _tokenService;
    private readonly IDateTimeProvider _clock;
    private readonly IUnitOfWork _unitOfWork;

    public ExchangeActivationCodeCommandHandler(
        ITrayActivationRepository repository,
        IUserRepository userRepository,
        ITrayTokenService tokenService,
        IDateTimeProvider clock,
        IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _userRepository = userRepository;
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

        var (employeeName, employeeEmail, employeeNumber) = await ResolveEmployeeIdentityAsync(
            activationCode.UserId, activationCode.TenantId, cancellationToken);

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
    /// Display-only identity for the tray UI. Prefers the HR Employee profile (name, email,
    /// employee number); falls back to the auth User's name/email if no Employee row is linked
    /// yet, so a fresh device activation never fails just because HR onboarding hasn't finished.
    /// </summary>
    private async Task<(string? Name, string? Email, string? Number)> ResolveEmployeeIdentityAsync(
        Guid userId, Guid tenantId, CancellationToken ct)
    {
        var profile = await _repository.FindEmployeeProfileAsync(userId, tenantId, ct);
        if (profile is not null)
            return ($"{profile.FirstName} {profile.LastName}".Trim(), profile.Email, profile.EmployeeNumber);

        var user = await _userRepository.GetByIdAsync(userId, ct);
        if (user is not null)
            return ($"{user.FirstName} {user.LastName}".Trim(), user.Email, null);

        return (null, null, null);
    }
}
```

- [ ] **Step 7: Run the tests again to verify they pass**

Run: `dotnet test tests\ONEVO.Tests.Integration\ONEVO.Tests.Integration.csproj --filter "TrayActivationIntegrationTests" --verbosity minimal`

Expected: PASS — all tests in the file, including the two changed/added in Step 1 and the 14 untouched existing ones (Generate/Refresh/Revoke/hash-storage/rate-limit tests must be unaffected).

- [ ] **Step 8: Commit**

```bash
git add src/ONEVO.Application/Features/Monitoring/TrayActivation/DTOs/Responses/TrayAuthResponseDto.cs src/ONEVO.Application/Features/Monitoring/TrayActivation/RepositoryInterfaces/ITrayActivationRepository.cs src/ONEVO.Infrastructure/Persistence/Repositories/Monitoring/TrayActivation/EfTrayActivationRepository.cs src/ONEVO.Application/Features/Monitoring/TrayActivation/Commands/ExchangeActivationCode/ExchangeActivationCodeCommandHandler.cs tests/ONEVO.Tests.Integration/Monitoring/TrayActivation/TrayActivationIntegrationTests.cs
git commit -m "feat: return employee identity on tray activation exchange"
```

---

## Task 2: Backend — same fields on the Refresh response

**Repo:** `HRMS-Backend-v1`

**Files:**
- Modify: `src/ONEVO.Application/Features/Monitoring/TrayActivation/Commands/RefreshTrayToken/RefreshTrayTokenCommandHandler.cs`
- Test: `tests/ONEVO.Tests.Integration/Monitoring/TrayActivation/TrayActivationIntegrationTests.cs`

- [ ] **Step 1: Extend the refresh test to assert the new fields — will fail until Step 3**

In `TrayActivationIntegrationTests.cs`, replace the `Refresh_ValidToken_RotatesRefreshToken_Returns200WithNewTokens` test body:

```csharp
    [Fact]
    public async Task Refresh_ValidToken_RotatesRefreshToken_Returns200WithNewTokens()
    {
        var user = await SeedActiveUserAsync("refresh-valid-test", "refresh-valid@test.dev", "RefPass1!");
        var session = await LoginAndGetSessionAsync(user);
        var code = await GenerateCodeAsync(session);
        const string fingerprint = "fp-refresh-valid-001";
        var (_, firstRefreshToken) = await ExchangeCodeAsync(code, fingerprint);

        var response = await PostRefreshAsync(firstRefreshToken, fingerprint);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("access_token").GetString().Should().StartWith("eyJ");
        var newRefreshToken = doc.RootElement.GetProperty("refresh_token").GetString()!;
        newRefreshToken.Should().NotBeNullOrEmpty();
        newRefreshToken.Should().NotBe(firstRefreshToken, "refresh token must be rotated on each use");
        doc.RootElement.GetProperty("employee_name").GetString().Should().Be("Test User");
        doc.RootElement.GetProperty("employee_email").GetString().Should().Be("refresh-valid@test.dev");
    }
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests\ONEVO.Tests.Integration\ONEVO.Tests.Integration.csproj --filter "Refresh_ValidToken_RotatesRefreshToken_Returns200WithNewTokens" --verbosity minimal`

Expected: FAIL — `KeyNotFoundException` on `GetProperty("employee_name")`.

- [ ] **Step 3: Wire the same lookup into `RefreshTrayTokenCommandHandler`**

Replace the full contents of `src/ONEVO.Application/Features/Monitoring/TrayActivation/Commands/RefreshTrayToken/RefreshTrayTokenCommandHandler.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.TrayActivation.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.TrayActivation.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.TrayActivation.ServiceInterfaces;
using ONEVO.Domain.Features.Monitoring.TrayActivation.Entities;

namespace ONEVO.Application.Features.Monitoring.TrayActivation.Commands.RefreshTrayToken;

public class RefreshTrayTokenCommandHandler
    : IRequestHandler<RefreshTrayTokenCommand, Result<TrayAuthResponseDto>>
{
    private static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(90);
    private const int AccessTokenExpiresInSeconds = 3600;
    private const int RefreshTokenExpiresInSeconds = 7_776_000;

    private readonly ITrayActivationRepository _repository;
    private readonly IUserRepository _userRepository;
    private readonly ITrayTokenService _tokenService;
    private readonly IDateTimeProvider _clock;
    private readonly IUnitOfWork _unitOfWork;

    public RefreshTrayTokenCommandHandler(
        ITrayActivationRepository repository,
        IUserRepository userRepository,
        ITrayTokenService tokenService,
        IDateTimeProvider clock,
        IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _userRepository = userRepository;
        _tokenService = tokenService;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<TrayAuthResponseDto>> Handle(
        RefreshTrayTokenCommand request,
        CancellationToken cancellationToken)
    {
        var tokenHash = _tokenService.HashToken(request.RefreshToken);
        var existingToken = await _repository.FindActiveRefreshTokenAsync(tokenHash, cancellationToken);

        if (existingToken is null)
            return Result<TrayAuthResponseDto>.Failure("Invalid or expired refresh token.", 401);

        var device = await _repository.FindActiveDeviceAsync(
            existingToken.DeviceRegistrationId, existingToken.TenantId, cancellationToken);

        if (device is null)
            return Result<TrayAuthResponseDto>.Failure("Device is no longer active.", 401);

        if (device.DeviceFingerprint != request.DeviceFingerprint)
        {
            // Fingerprint mismatch — possible token theft; revoke all tokens for this device
            await _repository.RevokeAllRefreshTokensForDeviceAsync(
                device.Id, "fingerprint_mismatch", cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result<TrayAuthResponseDto>.Failure("Device fingerprint mismatch.", 401);
        }

        var now = _clock.UtcNow;

        // Rotate: revoke old, issue new
        await _repository.RevokeRefreshTokenAsync(existingToken, "rotated", cancellationToken);

        var newRawToken = _tokenService.GenerateRawRefreshToken();
        var newTokenHash = _tokenService.HashToken(newRawToken);

        var newRefreshToken = new TrayDeviceRefreshToken
        {
            Id = Guid.NewGuid(),
            TenantId = existingToken.TenantId,
            UserId = existingToken.UserId,
            DeviceRegistrationId = existingToken.DeviceRegistrationId,
            TokenHash = newTokenHash,
            ExpiresAt = now.Add(RefreshTokenLifetime),
            IsRevoked = false,
            CreatedAt = now
        };

        await _repository.AddRefreshTokenAsync(newRefreshToken, cancellationToken);
        await _repository.UpdateDeviceLastSeenAsync(device.Id, now, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var accessToken = _tokenService.GenerateAccessToken(
            device.Id, existingToken.UserId, existingToken.TenantId);

        var profile = await _repository.FindEmployeeProfileAsync(
            existingToken.UserId, existingToken.TenantId, cancellationToken);
        string? employeeName = null, employeeEmail = null, employeeNumber = null;
        if (profile is not null)
        {
            employeeName = $"{profile.FirstName} {profile.LastName}".Trim();
            employeeEmail = profile.Email;
            employeeNumber = profile.EmployeeNumber;
        }
        else
        {
            var user = await _userRepository.GetByIdAsync(existingToken.UserId, cancellationToken);
            if (user is not null)
            {
                employeeName = $"{user.FirstName} {user.LastName}".Trim();
                employeeEmail = user.Email;
            }
        }

        return Result<TrayAuthResponseDto>.Success(new TrayAuthResponseDto(
            accessToken,
            AccessTokenExpiresInSeconds,
            newRawToken,
            RefreshTokenExpiresInSeconds,
            employeeName,
            employeeEmail,
            employeeNumber));
    }
}
```

- [ ] **Step 4: Run all TrayActivation integration tests to verify everything passes**

Run: `dotnet test tests\ONEVO.Tests.Integration\ONEVO.Tests.Integration.csproj --filter "TrayActivationIntegrationTests" --verbosity minimal`

Expected: PASS — all 18 tests in the file (16 original + 2 added in Task 1).

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Features/Monitoring/TrayActivation/Commands/RefreshTrayToken/RefreshTrayTokenCommandHandler.cs tests/ONEVO.Tests.Integration/Monitoring/TrayActivation/TrayActivationIntegrationTests.cs
git commit -m "feat: return employee identity on tray token refresh"
```

---

## Task 3: Agent Service — carry employee fields through `OnevoApiClient`

**Repo:** `tray_app_maui` (run all commands from this directory unless noted)

**Files:**
- Modify: `ONEVO.Agent.Service/Api/OnevoApiClient.cs`
- Test: `tests/ONEVO.Agent.Service.Tests/Api/OnevoApiClientTests.cs`

- [ ] **Step 1: Extend the Exchange success test to assert the new fields — will fail until Step 3**

In `tests/ONEVO.Agent.Service.Tests/Api/OnevoApiClientTests.cs`, replace `ExchangeActivationCodeAsync_Success_ReturnsAuthPayload`:

```csharp
    [Fact]
    public async Task ExchangeActivationCodeAsync_Success_ReturnsAuthPayload()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                access_token = "eyJ.test",
                expires_in_seconds = 3600,
                refresh_token = "raw-refresh",
                refresh_expires_in_seconds = 7_776_000,
                employee_name = "Priya Employee",
                employee_email = "priya@test.dev",
                employee_number = "EMP-0001"
            })
        });
        var client = Build(handler);

        var result = await client.ExchangeActivationCodeAsync("ABC12345", "Laptop", "Windows", "fp-1", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Null(result.ErrorCode);
        Assert.Equal("eyJ.test", result.Auth!.AccessToken);
        Assert.Equal("raw-refresh", result.Auth.RefreshToken);
        Assert.Equal(3600, result.Auth.ExpiresInSeconds);
        Assert.Equal("Priya Employee", result.Auth.EmployeeName);
        Assert.Equal("priya@test.dev", result.Auth.EmployeeEmail);
        Assert.Equal("EMP-0001", result.Auth.EmployeeNumber);
    }
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test .\tests\ONEVO.Agent.Service.Tests\ONEVO.Agent.Service.Tests.csproj --filter "ExchangeActivationCodeAsync_Success_ReturnsAuthPayload"`

Expected: FAIL — compile error, `TrayAuthPayload` has no `EmployeeName`/`EmployeeEmail`/`EmployeeNumber` members yet.

- [ ] **Step 3: Add the 3 fields to `TrayAuthPayload`**

In `ONEVO.Agent.Service/Api/OnevoApiClient.cs`, replace the `TrayAuthPayload` record at the bottom of the file:

```csharp
/// <summary>Wire-format mirror of the backend's TrayAuthResponseDto.</summary>
public sealed record TrayAuthPayload(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("expires_in_seconds")] int ExpiresInSeconds,
    [property: JsonPropertyName("refresh_token")] string RefreshToken,
    [property: JsonPropertyName("refresh_expires_in_seconds")] int RefreshExpiresInSeconds,
    [property: JsonPropertyName("employee_name")] string? EmployeeName,
    [property: JsonPropertyName("employee_email")] string? EmployeeEmail,
    [property: JsonPropertyName("employee_number")] string? EmployeeNumber);
```

- [ ] **Step 4: Run the test again to verify it passes**

Run: `dotnet test .\tests\ONEVO.Agent.Service.Tests\ONEVO.Agent.Service.Tests.csproj --filter "OnevoApiClientTests"`

Expected: PASS — all `OnevoApiClientTests` (the other 5 tests are unaffected since they don't reference employee fields, and records with extra nullable properties still bind fine from JSON that omits them).

- [ ] **Step 5: Commit**

```bash
git add ONEVO.Agent.Service/Api/OnevoApiClient.cs tests/ONEVO.Agent.Service.Tests/Api/OnevoApiClientTests.cs
git commit -m "feat: carry employee identity fields through OnevoApiClient"
```

---

## Task 4: Agent Service — thread employee fields through the IPC enrollment reply

**Repo:** `tray_app_maui`

**Files:**
- Modify: `ONEVO.Agent.Shared/IPC/IpcMessages.cs`
- Modify: `ONEVO.Agent.Service/AgentWorker.cs`

No dedicated unit test exists today for `AgentWorker` (it's exercised only end-to-end via a running Service + named pipe, which this plan doesn't add test infrastructure for — out of scope). This task is verified by a successful build; the field actually reaching the TrayApp is verified by Task 6's `FakeNamedPipeClient`-based test.

- [ ] **Step 1: Add the 2 new fields to `EnrollmentResultPayload`**

In `ONEVO.Agent.Shared/IPC/IpcMessages.cs`, replace the `EnrollmentResultPayload` record (currently lines 96-101):

```csharp
public sealed record EnrollmentResultPayload
{
    public required bool Success { get; init; }
    public string? ErrorCode { get; init; }   // "INVALID_CODE" | "EXPIRED" | "ALREADY_ENROLLED" | "SERVICE_UNAVAILABLE"
    public string? EmployeeName { get; init; }   // set on success for greeting
    public string? EmployeeEmail { get; init; }  // set on success for the workspace-setup screen
    public string? EmployeeNumber { get; init; } // set on success for the workspace-setup screen
}
```

- [ ] **Step 2: Pass the fields through `ReplyEnrollmentAsync` and the successful-exchange call site**

In `ONEVO.Agent.Service/AgentWorker.cs`, replace the `ReplyEnrollmentAsync` method (currently lines 568-586):

```csharp
    private async Task ReplyEnrollmentAsync(
        IpcEnvelope request,
        Func<IpcEnvelope, Task> reply,
        bool success,
        string? errorCode,
        string? employeeName,
        string? employeeEmail = null,
        string? employeeNumber = null)
    {
        await reply(new IpcEnvelope
        {
            Type = IpcMessageTypes.EnrollmentResult,
            CorrelationId = request.CorrelationId,
            Payload = JsonSerializer.SerializeToElement(new EnrollmentResultPayload
            {
                Success = success,
                ErrorCode = errorCode,
                EmployeeName = employeeName,
                EmployeeEmail = employeeEmail,
                EmployeeNumber = employeeNumber
            })
        });
    }
```

Then, in `HandleActivationCodeSubmitAsync`, replace this line (currently line 562):

```csharp
        await ReplyEnrollmentAsync(envelope, reply, true, null, null);
```

with:

```csharp
        await ReplyEnrollmentAsync(
            envelope, reply, true, null,
            result.Auth.EmployeeName, result.Auth.EmployeeEmail, result.Auth.EmployeeNumber);
```

(`result.Auth` is already null-checked earlier in the same method at the `if (!result.Success || result.Auth is null)` guard, so it's guaranteed non-null at this point.) Leave every other `ReplyEnrollmentAsync` call site in the file unchanged — the two new parameters are optional and default to `null`, which is correct for the failure/already-enrolled/logout-adjacent paths that never had employee data to begin with.

- [ ] **Step 3: Build to verify it compiles**

Run: `dotnet build .\ONEVO.Agent.slnx`

Expected: Build succeeds with no errors.

- [ ] **Step 4: Commit**

```bash
git add ONEVO.Agent.Shared/IPC/IpcMessages.cs ONEVO.Agent.Service/AgentWorker.cs
git commit -m "feat: thread employee identity through the enrollment IPC reply"
```

---

## Task 5: TrayApp — `IPreferencesStore` seam (required before Tasks 6-7 are testable)

**Repo:** `tray_app_maui`

**Why this task exists:** `Microsoft.Maui.Storage.Preferences` (the static API `ConnectWorkspaceViewModel`/`PrepareWorkspaceViewModel` currently call) throws `TypeInitializationException` when invoked outside a running MAUI app — confirmed by actually running a throwaway test against it in this exact test project (`ONEVO.Agent.TrayApp.Tests`), which failed with `System.Runtime.InteropServices.COMException: ClassFactory cannot supply requested class` deep inside `Preferences.Get_Default()`. Every existing production call site already wraps `Preferences` in a silent try/catch for exactly this reason (see the comments "unit tests" / "no MAUI context in unit tests"), and zero existing tests in this project touch `Preferences` directly. Tasks 6 and 7 need to actually assert that cached values round-trip correctly, which is impossible against the real static API in this test host. This task introduces a small seam — an injectable `IPreferencesStore` — so the ViewModels can be tested with a fake in-memory store instead.

**Files:**
- Create: `ONEVO.Agent.TrayApp/Services/IPreferencesStore.cs`
- Create: `ONEVO.Agent.TrayApp/Services/PreferencesStore.cs`
- Create: `tests/ONEVO.Agent.TrayApp.Tests/Fakes/FakePreferencesStore.cs`
- Modify: `ONEVO.Agent.TrayApp/MauiProgram.cs`
- Test: `tests/ONEVO.Agent.TrayApp.Tests/Services/PreferencesStoreTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/ONEVO.Agent.TrayApp.Tests/Services/PreferencesStoreTests.cs`:

```csharp
namespace ONEVO.Agent.TrayApp.Tests.Services;

using ONEVO.Agent.TrayApp.Services;

public sealed class PreferencesStoreTests
{
    [Fact]
    public void Get_NoPlatformContext_ReturnsDefaultWithoutThrowing()
    {
        var store = new PreferencesStore();
        var value = store.Get("any.key", "fallback");
        Assert.Equal("fallback", value);
    }

    [Fact]
    public void Set_NoPlatformContext_DoesNotThrow()
    {
        var store = new PreferencesStore();
        var exception = Record.Exception(() => store.Set("any.key", "value"));
        Assert.Null(exception);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test .\tests\ONEVO.Agent.TrayApp.Tests\ONEVO.Agent.TrayApp.Tests.csproj --filter "PreferencesStoreTests"`

Expected: FAIL — compile error, `ONEVO.Agent.TrayApp.Services.PreferencesStore` doesn't exist yet.

- [ ] **Step 3: Create the interface and default implementation**

Create `ONEVO.Agent.TrayApp/Services/IPreferencesStore.cs`:

```csharp
namespace ONEVO.Agent.TrayApp.Services;

/// <summary>Thin seam over platform Preferences storage so ViewModels are testable outside a running MAUI app.</summary>
public interface IPreferencesStore
{
    string Get(string key, string defaultValue);
    void Set(string key, string value);
}
```

Create `ONEVO.Agent.TrayApp/Services/PreferencesStore.cs`:

```csharp
namespace ONEVO.Agent.TrayApp.Services;

/// <summary>
/// Wraps Microsoft.Maui.Storage.Preferences. Swallows failures the same way every call site did
/// before this seam existed — Preferences throws when there's no running MAUI platform context
/// (e.g. a plain unit test host), and that's not a real error for display-only cached data.
/// </summary>
public sealed class PreferencesStore : IPreferencesStore
{
    public string Get(string key, string defaultValue)
    {
        try { return Preferences.Get(key, defaultValue); }
        catch { return defaultValue; }
    }

    public void Set(string key, string value)
    {
        try { Preferences.Set(key, value); }
        catch { /* no MAUI platform context (e.g. unit tests) */ }
    }
}
```

- [ ] **Step 4: Run the test again to verify it passes**

Run: `dotnet test .\tests\ONEVO.Agent.TrayApp.Tests\ONEVO.Agent.TrayApp.Tests.csproj --filter "PreferencesStoreTests"`

Expected: PASS — both tests.

- [ ] **Step 5: Add the in-memory fake for other tests to use**

Create `tests/ONEVO.Agent.TrayApp.Tests/Fakes/FakePreferencesStore.cs`:

```csharp
namespace ONEVO.Agent.TrayApp.Tests.Fakes;

using ONEVO.Agent.TrayApp.Services;

public sealed class FakePreferencesStore : IPreferencesStore
{
    private readonly Dictionary<string, string> _values = new();

    public string Get(string key, string defaultValue) =>
        _values.TryGetValue(key, out var value) ? value : defaultValue;

    public void Set(string key, string value) => _values[key] = value;
}
```

- [ ] **Step 6: Register the real implementation in DI**

In `ONEVO.Agent.TrayApp/MauiProgram.cs`, add this line next to the other singleton service registrations (near `builder.Services.AddSingleton<NotificationService>();`):

```csharp
        builder.Services.AddSingleton<IPreferencesStore, PreferencesStore>();
```

- [ ] **Step 7: Build to confirm DI wiring compiles**

Run: `dotnet build .\ONEVO.Agent.slnx`

Expected: Build succeeds with no errors.

- [ ] **Step 8: Commit**

```bash
git add ONEVO.Agent.TrayApp/Services/IPreferencesStore.cs ONEVO.Agent.TrayApp/Services/PreferencesStore.cs ONEVO.Agent.TrayApp/MauiProgram.cs tests/ONEVO.Agent.TrayApp.Tests/Services/PreferencesStoreTests.cs tests/ONEVO.Agent.TrayApp.Tests/Fakes/FakePreferencesStore.cs
git commit -m "feat: add testable IPreferencesStore seam over MAUI Preferences"
```

---

## Task 6: TrayApp — cache employee email/number alongside the existing name

**Repo:** `tray_app_maui`

**Files:**
- Modify: `ONEVO.Agent.TrayApp/ViewModels/ConnectWorkspaceViewModel.cs`
- Test: `tests/ONEVO.Agent.TrayApp.Tests/ViewModels/ConnectWorkspaceViewModelTests.cs`
- Modify: `tests/ONEVO.Agent.TrayApp.Tests/Fakes/FakeNamedPipeClient.cs`

- [ ] **Step 1: Extend the fake pipe client's canned result and rewrite the test file to inject `IPreferencesStore`**

In `tests/ONEVO.Agent.TrayApp.Tests/Fakes/FakeNamedPipeClient.cs`, replace the default `EnrollmentResultPayload` returned by `SendActivationAsync` (currently lines 53-59):

```csharp
        return Task.FromResult<EnrollmentResultPayload?>(
            new EnrollmentResultPayload
            {
                Success = true,
                ErrorCode = null,
                EmployeeName = "Test Employee",
                EmployeeEmail = "test.employee@test.dev",
                EmployeeNumber = "EMP-TEST-01"
            });
```

Replace the full contents of `tests/ONEVO.Agent.TrayApp.Tests/ViewModels/ConnectWorkspaceViewModelTests.cs`:

```csharp
using ONEVO.Agent.TrayApp.Tests.Fakes;
using ONEVO.Agent.TrayApp.ViewModels;

namespace ONEVO.Agent.TrayApp.Tests.ViewModels;

public sealed class ConnectWorkspaceViewModelTests
{
    private static ConnectWorkspaceViewModel Make() =>
        new(new FakeNamedPipeClient(), new FakePreferencesStore());

    [Fact]
    public void ActivationCode_DefaultsToEmpty()
    {
        var vm = Make();
        Assert.Equal(string.Empty, vm.ActivationCode);
    }

    [Fact]
    public void VerifyAndConnectCommand_DisabledWhenEmpty()
    {
        var vm = Make();
        vm.ActivationCode = string.Empty;
        Assert.False(vm.VerifyAndConnectCommand.CanExecute(null));
    }

    [Fact]
    public void VerifyAndConnectCommand_DisabledWhenFiveChars()
    {
        var vm = Make();
        vm.ActivationCode = "ABC12";
        Assert.False(vm.VerifyAndConnectCommand.CanExecute(null));
    }

    [Fact]
    public void VerifyAndConnectCommand_EnabledWhenSixChars()
    {
        var vm = Make();
        vm.ActivationCode = "ABC123";
        Assert.True(vm.VerifyAndConnectCommand.CanExecute(null));
    }

    [Fact]
    public void VerifyAndConnectCommand_EnabledForLongerPastedCode()
    {
        var vm = Make();
        vm.ActivationCode = "ABCD-EFGH-IJKL-MNOP";
        Assert.True(vm.VerifyAndConnectCommand.CanExecute(null));
    }

    [Fact]
    public void VerifyAndConnectCommand_DisabledForWhitespaceOnly()
    {
        var vm = Make();
        vm.ActivationCode = "      ";
        Assert.False(vm.VerifyAndConnectCommand.CanExecute(null));
    }

    [Fact]
    public async Task VerifyAndConnectCommand_SendsActivationToPipe()
    {
        var pipe = new FakeNamedPipeClient();
        var vm   = new ConnectWorkspaceViewModel(pipe, new FakePreferencesStore());
        vm.ActivationCode = "ABC123";
        await vm.VerifyAndConnectCommand.ExecuteAsync(null);
        Assert.Single(pipe.SentEnvelopes);
        Assert.Equal(ONEVO.Agent.Shared.IPC.IpcMessageTypes.ActivationCodeSubmit, pipe.SentEnvelopes[0].Type);
    }

    [Fact]
    public async Task VerifyAndConnectCommand_OnFailure_SetsError()
    {
        var pipe = new FakeNamedPipeClient
        {
            NextEnrollmentResult = new ONEVO.Agent.Shared.IPC.EnrollmentResultPayload
            {
                Success = false,
                ErrorCode = "INVALID_CODE"
            }
        };
        var vm = new ConnectWorkspaceViewModel(pipe, new FakePreferencesStore());
        vm.ActivationCode = "BAD";
        // length < 6 disables command — use long enough invalid path via canned fail with 6 chars
        vm.ActivationCode = "BADBAD";
        await vm.VerifyAndConnectCommand.ExecuteAsync(null);
        Assert.NotNull(vm.ErrorMessage);
        Assert.False(vm.IsConnected);
    }

    [Fact]
    public async Task VerifyAndConnectCommand_OnSuccess_CachesEmployeeEmailAndId()
    {
        var pipe = new FakeNamedPipeClient
        {
            NextEnrollmentResult = new ONEVO.Agent.Shared.IPC.EnrollmentResultPayload
            {
                Success = true,
                EmployeeName = "Priya Employee",
                EmployeeEmail = "priya@test.dev",
                EmployeeNumber = "EMP-0001"
            }
        };
        var preferences = new FakePreferencesStore();
        var vm = new ConnectWorkspaceViewModel(pipe, preferences);
        vm.ActivationCode = "ABC123";

        await vm.VerifyAndConnectCommand.ExecuteAsync(null);

        Assert.Equal("Priya Employee", preferences.Get("onevo.employee_display_name", string.Empty));
        Assert.Equal("priya@test.dev", preferences.Get("onevo.employee_email", string.Empty));
        Assert.Equal("EMP-0001", preferences.Get("onevo.employee_id", string.Empty));
    }
}
```

- [ ] **Step 2: Run to verify the new test fails**

Run: `dotnet test .\tests\ONEVO.Agent.TrayApp.Tests\ONEVO.Agent.TrayApp.Tests.csproj --filter "ConnectWorkspaceViewModelTests"`

Expected: FAIL — compile error, `ConnectWorkspaceViewModel` doesn't have a 2-argument constructor yet, and `EnrollmentResultPayload` doesn't have `EmployeeEmail`/`EmployeeNumber` yet (this task assumes Task 4 already landed `EmployeeEmail`/`EmployeeNumber` on `EnrollmentResultPayload` and Task 5 already landed `IPreferencesStore` — do those first if not already done).

- [ ] **Step 3: Inject `IPreferencesStore` and cache the 2 new fields**

Replace the full contents of `ONEVO.Agent.TrayApp/ViewModels/ConnectWorkspaceViewModel.cs`:

```csharp
namespace ONEVO.Agent.TrayApp.ViewModels;

using ONEVO.Agent.TrayApp.Services;
using ONEVO.Agent.Shared.IPC;

public sealed partial class ConnectWorkspaceViewModel : BaseViewModel
{
    private readonly INamedPipeClient _pipe;
    private readonly IPreferencesStore _preferences;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(VerifyAndConnectCommand))]
    private string _activationCode = string.Empty;

    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private bool _isConnecting;
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private string _connectionLabel = "Not Connected";
    [ObservableProperty] private string _versionText = "Version 1.0.0";
    [ObservableProperty] private string _hintText =
        "Paste the 8-character code from the employee portal, or tap below to open it.";

    public ConnectWorkspaceViewModel(INamedPipeClient pipe, IPreferencesStore preferences)
    {
        Title = "Connect Onexso Workspace";
        _pipe = pipe;
        _preferences = preferences;
        _pipe.OnDisconnected += () =>
        {
            try
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    IsConnected = false;
                    ConnectionLabel = "Not Connected";
                });
            }
            catch
            {
                IsConnected = false;
                ConnectionLabel = "Not Connected";
            }
        };
        _pipe.OnStateReceived += _ =>
        {
            try
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    IsConnected = true;
                    ConnectionLabel = "Connected";
                });
            }
            catch
            {
                IsConnected = true;
                ConnectionLabel = "Connected";
            }
        };
    }

    private bool CanVerify =>
        !IsConnecting &&
        ActivationCode.Trim().Length >= 6;

    [RelayCommand(CanExecute = nameof(CanVerify))]
    private async Task VerifyAndConnectAsync(CancellationToken ct)
    {
        IsConnecting = true;
        ErrorMessage = null;
        try
        {
            var code = ActivationCode.Trim().ToUpperInvariant();
            var result = await _pipe.SendActivationAsync(code, ct);

            if (result is null)
            {
                ErrorMessage = "No response from Onexso Agent Service. Is the service running?";
                IsConnected = false;
                ConnectionLabel = "Not Connected";
                return;
            }

            if (!result.Success)
            {
                ErrorMessage = result.ErrorCode switch
                {
                    "INVALID_CODE" => "Invalid or expired activation code. Get a new one from the employee portal.",
                    "LOCKED" => "Device is locked. Contact your admin.",
                    "SERVICE_UNAVAILABLE" => "Can't reach the Onexso backend right now. Check your connection and try again.",
                    _ => result.ErrorCode ?? "Activation failed."
                };
                IsConnected = false;
                ConnectionLabel = "Not Connected";
                return;
            }

            if (!string.IsNullOrWhiteSpace(result.EmployeeName))
                _preferences.Set("onevo.employee_display_name", result.EmployeeName);
            if (!string.IsNullOrWhiteSpace(result.EmployeeEmail))
                _preferences.Set("onevo.employee_email", result.EmployeeEmail);
            if (!string.IsNullOrWhiteSpace(result.EmployeeNumber))
                _preferences.Set("onevo.employee_id", result.EmployeeNumber);

            IsConnected = true;
            ConnectionLabel = "Connected";
            try { await Shell.Current.GoToAsync("//prepare"); }
            catch { /* unit tests */ }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Connection failed: {ex.Message}";
            IsConnected = false;
            ConnectionLabel = "Not Connected";
        }
        finally
        {
            IsConnecting = false;
        }
    }

    [RelayCommand]
    private async Task PasteActivationCodeAsync()
    {
        try
        {
            if (Clipboard.Default.HasText)
            {
                var text = await Clipboard.Default.GetTextAsync();
                if (!string.IsNullOrWhiteSpace(text))
                    ActivationCode = text.Trim().ToUpperInvariant();
            }
        }
        catch
        {
            // Clipboard unavailable in unit tests / restricted hosts.
        }
    }

    [RelayCommand]
    private static void OpenEmployeePortal() =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName        = "https://app.onexsoworkspace.com",
            UseShellExecute = true
        });
}
```

(`result.EmployeeNumber` maps to the `onevo.employee_id` preference key — that key name predates this change and is already read by `PrepareWorkspaceViewModel`/`ReviewSetupViewModel`; keep it as-is rather than renaming, to avoid touching every existing reader. `IPreferencesStore.Set` already swallows platform failures internally, so the per-call try/catch that used to wrap each `Preferences.Set` is no longer needed here.)

- [ ] **Step 4: Run the tests again to verify they pass**

Run: `dotnet test .\tests\ONEVO.Agent.TrayApp.Tests\ONEVO.Agent.TrayApp.Tests.csproj --filter "ConnectWorkspaceViewModelTests"`

Expected: PASS — all 8 tests in the class, including the new one.

- [ ] **Step 5: Commit**

```bash
git add ONEVO.Agent.TrayApp/ViewModels/ConnectWorkspaceViewModel.cs tests/ONEVO.Agent.TrayApp.Tests/ViewModels/ConnectWorkspaceViewModelTests.cs tests/ONEVO.Agent.TrayApp.Tests/Fakes/FakeNamedPipeClient.cs
git commit -m "feat: cache employee email and employee number on tray activation"
```

---

## Task 7: TrayApp — `PrepareWorkspaceViewModel` reads the real cached identity

**Repo:** `tray_app_maui`

**Files:**
- Modify: `ONEVO.Agent.TrayApp/ViewModels/PrepareWorkspaceViewModel.cs`
- Test: `tests/ONEVO.Agent.TrayApp.Tests/ViewModels/PrepareWorkspaceViewModelTests.cs`

- [ ] **Step 1: Replace the test file to inject `IPreferencesStore` and prove cached values are used**

Replace the full contents of `tests/ONEVO.Agent.TrayApp.Tests/ViewModels/PrepareWorkspaceViewModelTests.cs`:

```csharp
using ONEVO.Agent.TrayApp.Tests.Fakes;
using ONEVO.Agent.TrayApp.ViewModels;

namespace ONEVO.Agent.TrayApp.Tests.ViewModels;

public sealed class PrepareWorkspaceViewModelTests
{
    [Fact]
    public void InitialState_AllStepsFalse()
    {
        var vm = new PrepareWorkspaceViewModel(new FakePreferencesStore());
        Assert.False(vm.ActivationVerified);
        Assert.False(vm.UserDetailsFetched);
        Assert.False(vm.WorkspacePrepared);
    }

    [Fact]
    public void CanContinue_FalseUntilAllStepsComplete()
    {
        var vm = new PrepareWorkspaceViewModel(new FakePreferencesStore());
        Assert.False(vm.CanContinue);
    }

    [Fact]
    public void CanContinue_TrueWhenAllStepsComplete()
    {
        var vm = new PrepareWorkspaceViewModel(new FakePreferencesStore());
        vm.ActivationVerified = true;
        vm.UserDetailsFetched = true;
        vm.WorkspacePrepared  = true;
        Assert.True(vm.CanContinue);
    }

    [Fact]
    public void EmployeeId_DefaultsEmpty()
    {
        var vm = new PrepareWorkspaceViewModel(new FakePreferencesStore());
        Assert.Equal(string.Empty, vm.EmployeeId);
    }

    [Fact]
    public async Task LoadAsync_SetsAllStepsAndUserFields()
    {
        var preferences = new FakePreferencesStore();
        preferences.Set("onevo.employee_display_name", "Existing Name");
        preferences.Set("onevo.employee_email", "existing@test.dev");
        preferences.Set("onevo.employee_id", "EMP-EXISTING");
        var vm = new PrepareWorkspaceViewModel(preferences);

        await vm.LoadAsync(CancellationToken.None);

        Assert.True(vm.ActivationVerified);
        Assert.True(vm.UserDetailsFetched);
        Assert.True(vm.WorkspacePrepared);
        Assert.True(vm.CanContinue);
        Assert.False(vm.IsLoading);
        Assert.NotEmpty(vm.EmployeeFullName);
        Assert.NotEmpty(vm.EmployeeEmail);
        Assert.NotEmpty(vm.EmployeeId);
    }

    [Fact]
    public async Task LoadAsync_UsesCachedPreferences_NotHardcodedValues()
    {
        var preferences = new FakePreferencesStore();
        preferences.Set("onevo.employee_display_name", "Cached Name");
        preferences.Set("onevo.employee_email", "cached@test.dev");
        preferences.Set("onevo.employee_id", "EMP-CACHED");
        var vm = new PrepareWorkspaceViewModel(preferences);

        await vm.LoadAsync(CancellationToken.None);

        Assert.Equal("Cached Name", vm.EmployeeFullName);
        Assert.Equal("cached@test.dev", vm.EmployeeEmail);
        Assert.Equal("EMP-CACHED", vm.EmployeeId);
        Assert.NotEqual("Pirakeerthan", vm.EmployeeFullName);
    }

    [Fact]
    public async Task LoadAsync_NoCachedPreferences_LeavesFieldsEmpty()
    {
        var vm = new PrepareWorkspaceViewModel(new FakePreferencesStore());

        await vm.LoadAsync(CancellationToken.None);

        Assert.Equal(string.Empty, vm.EmployeeFullName);
        Assert.Equal(string.Empty, vm.EmployeeEmail);
        Assert.Equal(string.Empty, vm.EmployeeId);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test .\tests\ONEVO.Agent.TrayApp.Tests\ONEVO.Agent.TrayApp.Tests.csproj --filter "PrepareWorkspaceViewModelTests"`

Expected: FAIL — compile error, `PrepareWorkspaceViewModel` has no constructor taking `IPreferencesStore` yet.

- [ ] **Step 3: Inject `IPreferencesStore` and read from it instead of hardcoding**

Replace the full contents of `ONEVO.Agent.TrayApp/ViewModels/PrepareWorkspaceViewModel.cs`:

```csharp
namespace ONEVO.Agent.TrayApp.ViewModels;

using ONEVO.Agent.TrayApp.Services;

public sealed partial class PrepareWorkspaceViewModel : BaseViewModel
{
    private readonly IPreferencesStore _preferences;

    [ObservableProperty] private bool _activationVerified;
    [ObservableProperty] private bool _userDetailsFetched;
    [ObservableProperty] private bool _workspacePrepared;
    [ObservableProperty] private bool _isLoading = true;

    [ObservableProperty] private string _employeeFullName = string.Empty;
    [ObservableProperty] private string _employeeEmail    = string.Empty;
    [ObservableProperty] private string _employeeId       = string.Empty;

    public bool CanContinue => ActivationVerified && UserDetailsFetched && WorkspacePrepared;

    public PrepareWorkspaceViewModel(IPreferencesStore preferences)
    {
        _preferences = preferences;
        Title = "Setting Up Your Workspace";
    }

    public async Task LoadAsync(CancellationToken ct = default)
    {
        IsLoading = true;

        await Task.Delay(600, ct);
        ActivationVerified = true;

        await Task.Delay(900, ct);
        UserDetailsFetched = true;
        EmployeeFullName = _preferences.Get("onevo.employee_display_name", string.Empty);
        EmployeeEmail    = _preferences.Get("onevo.employee_email", string.Empty);
        EmployeeId       = _preferences.Get("onevo.employee_id", string.Empty);
        OnPropertyChanged(nameof(CanContinue));

        await Task.Delay(500, ct);
        WorkspacePrepared = true;
        IsLoading         = false;
        OnPropertyChanged(nameof(CanContinue));
    }

    [RelayCommand]
    private async Task NavigateToLocation()
    {
        try { await Shell.Current.GoToAsync("//location"); }
        catch { /* unit tests */ }
    }

    [RelayCommand]
    private async Task NavigateToPhoto()
    {
        try { await Shell.Current.GoToAsync("//photo"); }
        catch { /* unit tests */ }
    }

    [RelayCommand(CanExecute = nameof(CanContinue))]
    private async Task ContinueSetup()
    {
        try { await Shell.Current.GoToAsync("//location"); }
        catch { /* unit tests */ }
    }
}
```

(The `Preferences.Set` calls that used to follow the hardcoded assignment are gone — the values already came from `_preferences`, cached one screen earlier by `ConnectWorkspaceViewModel`, so writing them back here was always a no-op.)

- [ ] **Step 4: Run the full PrepareWorkspaceViewModel test class to verify everything passes**

Run: `dotnet test .\tests\ONEVO.Agent.TrayApp.Tests\ONEVO.Agent.TrayApp.Tests.csproj --filter "PrepareWorkspaceViewModelTests"`

Expected: PASS — all 7 tests.

- [ ] **Step 5: Commit**

```bash
git add ONEVO.Agent.TrayApp/ViewModels/PrepareWorkspaceViewModel.cs tests/ONEVO.Agent.TrayApp.Tests/ViewModels/PrepareWorkspaceViewModelTests.cs
git commit -m "feat: show real cached employee identity instead of hardcoded values"
```

---

## Task 8: Full-solution verification

**Files:** none (verification only)

- [ ] **Step 1: Run the full backend test suite**

Run (from `HRMS-Backend-v1`): `dotnet test tests\ONEVO.Tests.Integration\ONEVO.Tests.Integration.csproj --verbosity minimal`

Expected: PASS — every integration test in the solution, not just `TrayActivation`, to catch any unintended regression (e.g. in the `CheckIn` feature that shares `ITrayCurrentDevice`).

- [ ] **Step 2: Run the full Agent Service + TrayApp test suites**

Run (from `tray_app_maui`):
```bash
dotnet test .\tests\ONEVO.Agent.Service.Tests\ONEVO.Agent.Service.Tests.csproj
dotnet test .\tests\ONEVO.Agent.TrayApp.Tests\ONEVO.Agent.TrayApp.Tests.csproj
dotnet test .\tests\ONEVO.Agent.Shared.Tests\ONEVO.Agent.Shared.Tests.csproj
```

Expected: PASS across all three projects.

- [ ] **Step 3: Explicit build check for the backend API project and the tray solution**

The backend repo has no root `.sln` file (confirmed: `find . -maxdepth 2 -iname "*.sln"` returns nothing) — `dotnet test` in Steps 1 already builds the Application/Infrastructure/Domain projects transitively via the test project references, but the Api project itself isn't pulled in by that. Build it explicitly:

```bash
dotnet build "C:\HR\HRMS-Backend-v1\src\ONEVO.Api\ONEVO.Api.csproj"
dotnet build "C:\HR\tray_app_maui\ONEVO.Agent.slnx"
```

Expected: both build with zero errors.

- [ ] **Step 4: Manual smoke check (requires a running backend + installed Service + TrayApp — do this only if you have that environment available; otherwise skip and note it as pending manual verification)**

1. Generate an activation code via `POST /api/v1/monitoring/activation/generate` (authenticated tenant session) for a seeded tenant user that has a linked `Employee` row.
2. Paste the code into the TrayApp's Connect Workspace screen.
3. Confirm the "Setting Up Your Workspace" screen shows the real employee's name/email/employee-number, not `"Pirakeerthan"`/`"pirakeerthan@onexso.com"`/`"ONEXSO1234"`.
4. Repeat with a tenant user that has **no** linked `Employee` row and confirm the screen shows the `User` entity's name/email with a blank employee-number field instead of crashing.

---

## Out of scope (do not implement as part of this plan)

- Rate limiting on `/exchange` and `/refresh`.
- Named-pipe ACL creation-failure fallback hardening (`NamedPipeServer.CreateSecurePipe`).
- Rewriting the stale `ONEVO_Agent_Architecture_Flow_Folder_Structure.md` §9/§10 device-code/browser flow description to match the real paste-code flow.
- Distinct `"EXPIRED"` / `"ALREADY_ENROLLED"` `EnrollmentResultPayload` error codes.
- Any push mechanism for the Service to proactively notify a running TrayApp of employee-profile changes after activation (see design doc §Service section for why this is deliberately deferred).
