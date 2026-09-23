# Profile Avatar URL Fix (Phase 0) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `GET /api/v1/employees/me` return a resolvable avatar reference so the frontend can
actually display an uploaded profile photo, by adding a streaming `GET /employees/me/avatar`
endpoint (mirroring the existing Legal Entity logo pattern) and exposing `AvatarFileId` from the
profile query.

**Architecture:** No new architecture — this reuses the exact pattern `LegalEntitiesController.GetLogo`
/ `GetLegalEntityLogoQueryHandler` already establishes: a dedicated authenticated `GET` endpoint that
streams the file via `IFileStorageService.OpenReadAsync`, with the frontend building the display URL
client-side from a raw file-id field (`?v={fileId}` cache-busting), the same way
`LegalEntityApiService.getLogoUrl`/`ProjectApiService.getLogoUrl` already do. `MyPersonalInformationResponse`'s
existing (always-null, unused) `AvatarUrl: string?` field is replaced with `AvatarFileId: Guid?` so the
frontend has something real to build a URL from.

**Tech Stack:** .NET / ASP.NET Core, MediatR (CQRS), xUnit + Moq + FluentAssertions, PostgreSQL via EF Core (no migration needed — `Employee.AvatarFileId` already exists).

**Spec:** `docs/superpowers/specs/next/2026-09-22-centralized-file-upload-design.md` (§ "Phase 0"). Companion frontend plan: `Hrms--Web-application---front-end---v1/docs/superpowers/plans/next/2026-09-22-centralized-file-upload-phase0-avatar-fix-frontend.md` — that plan depends on this one shipping first (it consumes the `avatarFileId` field and calls the new endpoint).

## Global Constraints

- Every feature handler must call `IFileStorageService` — never `IObjectStorageAdapter` or the file
  repositories directly (see that interface's doc comment).
- Self-service `me/*` endpoints on `EmployeesController` require no `[RequirePermission]` — matches
  every existing `me/*` route in that controller (they rely on `[Authorize(Policy = "TenantPolicy")]`
  at the controller level plus resolving the employee from the authenticated user, not a permission code).
- `Result<T>` failure handling in controllers is always `Problem(result.Error, statusCode: result.StatusCode ?? 400)` — follow this exactly, do not introduce a different error-response shape.

---

### Task 1: Expose `AvatarFileId` from `GetMyProfileQuery`

**Files:**
- Modify: `src/ONEVO.Application/Features/CoreHr/Employee/DTOs/Responses/MyProfileResponse.cs:3-7`
- Modify: `src/ONEVO.Application/Features/CoreHr/Employee/Queries/GetMyProfile/GetMyProfileQueryHandler.cs:84-89`
- Test: `tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/GetMyProfileQueryHandlerTests.cs`

**Interfaces:**
- Consumes: `Employee.AvatarFileId` (`Guid?`, already exists on `ONEVO.Domain.Features.CoreHr.Entities.Employee`).
- Produces: `MyPersonalInformationResponse.AvatarFileId` (`Guid?`) — Task 3's controller test and the
  frontend plan both rely on this exact property name and type (camelCase `avatarFileId` on the wire).

- [x] **Step 1: Write the failing test**

Add to `tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/GetMyProfileQueryHandlerTests.cs` (new test,
same file, alongside the existing two):

```csharp
    [Fact]
    public async Task Handle_ReturnsAvatarFileId_WhenEmployeeHasOneSet()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var avatarFileId = Guid.NewGuid();

        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(c => c.IsAuthenticated).Returns(true);
        currentUser.SetupGet(c => c.TenantId).Returns(tenantId);
        currentUser.SetupGet(c => c.UserId).Returns(userId);

        var commonRepo = new Mock<CommonEmployeeRepo>();
        commonRepo.Setup(r => r.GetByUserIdAsync(tenantId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ONEVO.Domain.Features.CoreHr.Entities.Employee
            {
                Id = employeeId, TenantId = tenantId, UserId = userId,
                FirstName = "Jane", LastName = "Doe", Email = "jane@example.com",
                HireDate = DateOnly.FromDateTime(DateTime.UtcNow), EmployeeNumber = "E-001",
                AvatarFileId = avatarFileId
            });

        var featureRepo = new Mock<FeatureEmployeeRepo>();
        var workModes = new Mock<IWorkModeRepository>();

        var profileRepo = new Mock<IEmployeeProfileRepository>();
        profileRepo.Setup(r => r.ListAddressesAsync(tenantId, employeeId, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        profileRepo.Setup(r => r.ListEmergencyContactsAsync(tenantId, employeeId, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        profileRepo.Setup(r => r.ListDependentsAsync(tenantId, employeeId, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        profileRepo.Setup(r => r.GetPrimaryBankDetailAsync(tenantId, employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ONEVO.Domain.Features.CoreHr.Entities.EmployeeBankDetail?)null);

        var users = new Mock<IUserRepository>();
        users.Setup(u => u.GetByIdAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync((ONEVO.Domain.Features.Auth.Entities.User?)null);
        var userMfa = new Mock<IUserMfaRepository>();

        var handler = new GetMyProfileQueryHandler(
            commonRepo.Object, featureRepo.Object, profileRepo.Object, workModes.Object,
            users.Object, userMfa.Object, new Mock<IEncryptionService>().Object,
            new Mock<ILegalEntityRepository>().Object, currentUser.Object);

        var result = await handler.Handle(new GetMyProfileQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(avatarFileId, result.Value!.PersonalInformation.AvatarFileId);
    }
```

- [x] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~GetMyProfileQueryHandlerTests.Handle_ReturnsAvatarFileId_WhenEmployeeHasOneSet"`
Expected: FAIL — compile error, `MyPersonalInformationResponse` has no `AvatarFileId` member yet.

- [x] **Step 3: Change the response record**

In `src/ONEVO.Application/Features/CoreHr/Employee/DTOs/Responses/MyProfileResponse.cs`, replace:

```csharp
public record MyPersonalInformationResponse(
    string FirstName, string LastName, string Email, string? Phone,
    DateOnly? DateOfBirth, string? Gender, Guid? NationalityId, string? CountryName,
    string? DisplayTimezone, string? LegalEntityTimezone, string? AvatarUrl,
    IReadOnlyList<MyAddressResponse> Addresses, string Version);
```

with:

```csharp
public record MyPersonalInformationResponse(
    string FirstName, string LastName, string Email, string? Phone,
    DateOnly? DateOfBirth, string? Gender, Guid? NationalityId, string? CountryName,
    string? DisplayTimezone, string? LegalEntityTimezone, Guid? AvatarFileId,
    IReadOnlyList<MyAddressResponse> Addresses, string Version);
```

- [x] **Step 4: Populate it in the handler**

In `src/ONEVO.Application/Features/CoreHr/Employee/Queries/GetMyProfile/GetMyProfileQueryHandler.cs`,
change the `MyPersonalInformationResponse` construction (lines 84-89) from:

```csharp
        var personalInformation = new MyPersonalInformationResponse(
            employee.FirstName, employee.LastName, employee.Email, employee.Phone,
            employee.DateOfBirth, employee.Gender, employee.NationalityId, null,
            employee.DisplayTimezone, legalEntityTimezone, null,
            addresses.Select(a => new MyAddressResponse(a.Id, a.AddressType, a.AddressJson, a.IsPrimary)).ToList(),
            versionToken?.ToString() ?? string.Empty);
```

to:

```csharp
        var personalInformation = new MyPersonalInformationResponse(
            employee.FirstName, employee.LastName, employee.Email, employee.Phone,
            employee.DateOfBirth, employee.Gender, employee.NationalityId, null,
            employee.DisplayTimezone, legalEntityTimezone, employee.AvatarFileId,
            addresses.Select(a => new MyAddressResponse(a.Id, a.AddressType, a.AddressJson, a.IsPrimary)).ToList(),
            versionToken?.ToString() ?? string.Empty);
```

- [x] **Step 5: Run the test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~GetMyProfileQueryHandlerTests"`
Expected: PASS, all 3 tests in the file (the 2 pre-existing plus the new one) green.

- [x] **Step 6: Commit**

```bash
git add src/ONEVO.Application/Features/CoreHr/Employee/DTOs/Responses/MyProfileResponse.cs src/ONEVO.Application/Features/CoreHr/Employee/Queries/GetMyProfile/GetMyProfileQueryHandler.cs tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/GetMyProfileQueryHandlerTests.cs
git commit -m "fix: expose employee AvatarFileId from GetMyProfile instead of a dead AvatarUrl field"
```

---

### Task 2: `GetMyAvatarQuery` + handler — stream the caller's own avatar

**Files:**
- Create: `src/ONEVO.Application/Features/CoreHr/Employee/Queries/GetMyAvatar/GetMyAvatarQuery.cs`
- Create: `src/ONEVO.Application/Features/CoreHr/Employee/Queries/GetMyAvatar/GetMyAvatarQueryHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/GetMyAvatarQueryHandlerTests.cs`

**Interfaces:**
- Consumes: `Common.RepositoryInterfaces.IEmployeeRepository.GetByUserIdAsync(Guid tenantId, Guid userId, CancellationToken)` (already used identically in `SetMyAvatarCommandHandler`); `IFileStorageService.OpenReadAsync(Guid tenantId, Guid fileId, CancellationToken) : Task<Result<FileStreamDto>>` (already used identically in `GetLegalEntityLogoQueryHandler`).
- Produces: `GetMyAvatarQuery` (no params, `IRequest<Result<FileStreamDto>>`) and `GetMyAvatarQueryHandler` — Task 3's controller sends this query.

- [x] **Step 1: Write the failing tests**

Create `tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/GetMyAvatarQueryHandlerTests.cs`:

```csharp
using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Queries.GetMyAvatar;
using ONEVO.Application.Features.Storage.File.DTOs.Responses;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using Xunit;
using CommonEmployeeRepo = ONEVO.Application.Common.RepositoryInterfaces.IEmployeeRepository;
using EmployeeEntity = ONEVO.Domain.Features.CoreHr.Entities.Employee;

namespace ONEVO.Tests.Unit.Features.CoreHr.Employee;

public class GetMyAvatarQueryHandlerTests
{
    private readonly Mock<CommonEmployeeRepo> _employees = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<IFileStorageService> _fileStorage = new();

    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UserId = Guid.NewGuid();

    private void AuthenticateCurrentUser()
    {
        _currentUser.SetupGet(c => c.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(c => c.TenantId).Returns(TenantId);
        _currentUser.SetupGet(c => c.UserId).Returns(UserId);
    }

    [Fact]
    public async Task Handle_NoEmployeeRecordForCurrentUser_ReturnsNotFound()
    {
        AuthenticateCurrentUser();
        _employees.Setup(r => r.GetByUserIdAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((EmployeeEntity?)null);
        var sut = new GetMyAvatarQueryHandler(_employees.Object, _fileStorage.Object, _currentUser.Object);

        var result = await sut.Handle(new GetMyAvatarQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
        _fileStorage.Verify(f => f.OpenReadAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_NoAvatarSet_ReturnsNotFound()
    {
        AuthenticateCurrentUser();
        _employees.Setup(r => r.GetByUserIdAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeEntity { Id = Guid.NewGuid(), TenantId = TenantId, UserId = UserId, AvatarFileId = null });
        var sut = new GetMyAvatarQueryHandler(_employees.Object, _fileStorage.Object, _currentUser.Object);

        var result = await sut.Handle(new GetMyAvatarQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Handle_AvatarSet_DelegatesToFileStorageWithTheStoredFileId()
    {
        AuthenticateCurrentUser();
        var avatarFileId = Guid.NewGuid();
        _employees.Setup(r => r.GetByUserIdAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeEntity { Id = Guid.NewGuid(), TenantId = TenantId, UserId = UserId, AvatarFileId = avatarFileId });
        using var stream = new MemoryStream();
        _fileStorage.Setup(f => f.OpenReadAsync(TenantId, avatarFileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileStreamDto>.Success(new FileStreamDto(stream, "image/png")));
        var sut = new GetMyAvatarQueryHandler(_employees.Object, _fileStorage.Object, _currentUser.Object);

        var result = await sut.Handle(new GetMyAvatarQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ContentType.Should().Be("image/png");
        _fileStorage.Verify(f => f.OpenReadAsync(TenantId, avatarFileId, It.IsAny<CancellationToken>()), Times.Once);
    }
}
```

- [x] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~GetMyAvatarQueryHandlerTests"`
Expected: FAIL to compile — `GetMyAvatarQuery`/`GetMyAvatarQueryHandler` don't exist yet.

- [x] **Step 3: Write the query**

Create `src/ONEVO.Application/Features/CoreHr/Employee/Queries/GetMyAvatar/GetMyAvatarQuery.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Storage.File.DTOs.Responses;

namespace ONEVO.Application.Features.CoreHr.Employee.Queries.GetMyAvatar;

public record GetMyAvatarQuery() : IRequest<Result<FileStreamDto>>;
```

- [x] **Step 4: Write the handler**

Create `src/ONEVO.Application/Features/CoreHr/Employee/Queries/GetMyAvatar/GetMyAvatarQueryHandler.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Storage.File.DTOs.Responses;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;

namespace ONEVO.Application.Features.CoreHr.Employee.Queries.GetMyAvatar;

public class GetMyAvatarQueryHandler : IRequestHandler<GetMyAvatarQuery, Result<FileStreamDto>>
{
    private readonly Common.RepositoryInterfaces.IEmployeeRepository _commonEmployees;
    private readonly IFileStorageService _fileStorage;
    private readonly ICurrentUser _currentUser;

    public GetMyAvatarQueryHandler(
        Common.RepositoryInterfaces.IEmployeeRepository commonEmployees,
        IFileStorageService fileStorage,
        ICurrentUser currentUser)
    {
        _commonEmployees = commonEmployees;
        _fileStorage = fileStorage;
        _currentUser = currentUser;
    }

    public async Task<Result<FileStreamDto>> Handle(GetMyAvatarQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<FileStreamDto>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var employee = await _commonEmployees.GetByUserIdAsync(tenantId, _currentUser.UserId, ct);
        if (employee is null || employee.AvatarFileId is null)
            return Result<FileStreamDto>.NotFound("Avatar not found.");

        return await _fileStorage.OpenReadAsync(tenantId, employee.AvatarFileId.Value, ct);
    }
}
```

- [x] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~GetMyAvatarQueryHandlerTests"`
Expected: PASS, all 3 tests green.

- [x] **Step 6: Commit**

```bash
git add src/ONEVO.Application/Features/CoreHr/Employee/Queries/GetMyAvatar/ tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/GetMyAvatarQueryHandlerTests.cs
git commit -m "feat: add GetMyAvatarQuery to stream the caller's own avatar"
```

---

### Task 3: `GET /api/v1/employees/me/avatar` endpoint

**Files:**
- Modify: `src/ONEVO.Api/Controllers/Tenant/CoreHr/EmployeesController.cs`

**Interfaces:**
- Consumes: `GetMyAvatarQuery` (Task 2), `IMediator.Send` (already injected as `_mediator` in this controller).
- Produces: `GET /api/v1/employees/me/avatar` — the frontend plan's `ProfileApiService.getAvatarUrl` builds URLs pointing at this exact route.

- [x] **Step 1: Add the `using` and the endpoint**

In `src/ONEVO.Api/Controllers/Tenant/CoreHr/EmployeesController.cs`, add to the `using` block (after
the existing `using ONEVO.Application.Features.CoreHr.Employee.Queries.GetMyPayroll;` line):

```csharp
using ONEVO.Application.Features.CoreHr.Employee.Queries.GetMyAvatar;
```

Then add this method immediately after `SetMyAvatar` (after line 199, before the
`AddMyEmergencyContact` method):

```csharp
    /// <summary>Streams the caller's own avatar image. 404 if no avatar is set.</summary>
    [HttpGet("me/avatar")]
    public async Task<IActionResult> GetMyAvatar(CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetMyAvatarQuery(), ct);
        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        return File(result.Value!.Content, result.Value!.ContentType);
    }
```

- [x] **Step 2: Build to verify it compiles**

Run: `dotnet build src/ONEVO.Api/ONEVO.Api.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Manual verification against the dev API** — skipped by explicit user decision
  2026-09-22 (automated coverage judged sufficient; this dev environment's custom-domain auth flow
  makes ad hoc manual verification costly — do this when next actually using the feature).

This is a thin controller wrapper around an already-unit-tested handler (Task 2), so no new
integration test is required — verify manually instead:
1. Start the backend dev server (see this repo's own run/launch instructions).
2. Log in as a tenant user whose employee record has `AvatarFileId` set (or run
   `PUT /api/v1/employees/me/avatar` with a test image first).
3. `GET /api/v1/employees/me/avatar` with the session's auth cookie/header — expect a 200 with the
   image bytes and the correct `Content-Type`.
4. `GET /api/v1/employees/me/avatar` for a user with no avatar set — expect 404.

- [x] **Step 4: Commit**

```bash
git add src/ONEVO.Api/Controllers/Tenant/CoreHr/EmployeesController.cs
git commit -m "feat: add GET /employees/me/avatar streaming endpoint"
```

## Self-Review Notes

- Spec coverage: this plan implements exactly the backend half of the spec's "Phase 0" section — the
  `AvatarUrl`→`AvatarFileId` contract fix and the new streaming endpoint. Phase 1 (generic
  `FilesController`, `entity_assets` extension) and Phase 2 (frontend shared component) are
  deliberately out of scope for this plan — they get their own plans once this phase ships and is
  verified, per the spec's phased-delivery section.
- No placeholders: every step has real, compilable code; no "add error handling" style steps.
- Type consistency checked: `AvatarFileId` is `Guid?` everywhere (response record, handler, test
  assertions) — never a `string` anywhere in this plan, since the frontend plan is responsible for
  turning it into a URL string, not this one.
