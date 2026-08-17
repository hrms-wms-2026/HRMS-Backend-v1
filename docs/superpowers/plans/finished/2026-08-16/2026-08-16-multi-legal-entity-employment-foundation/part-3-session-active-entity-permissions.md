# Session Active-Entity & Permission Recompute Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the topbar company switcher actually change the caller's effective permission set, so a person with an `Employee` row (and therefore a position/role) in more than one legal entity gets the permissions that belong to whichever company is currently active, not the union of both.

**Architecture:** Tenant sessions in this codebase are server-side/reference sessions (`TenantDatabaseTicketStore : ITicketStore`, `Session` row in Postgres) — the cookie carries only an opaque key, and `RetrieveAsync` re-resolves the full permission set from the database **on every single request** via `IPermissionResolver.ResolveAsync`. This means adding an `ActiveEmployeeId` column to `Session` and threading it through `ResolveAsync` is enough — no cache-busting or forced re-login is needed, the very next request after a switch already reflects it. Permission resolution today (`PermissionResolver.ResolveAsync` → `IPermissionRepository.ListRolePermissionCodesWithModulesAsync`) reads `UserRole` rows purely by `UserId`, with no legal-entity dimension at all — `UserRole.SourcePositionId` already exists (used today only as audit provenance) and is the join key this plan uses to filter role grants by the active entity's legal entity.

**Tech Stack:** .NET (C#), EF Core, ASP.NET Core Cookie Authentication (`ITicketStore`), MediatR, Angular 21 (frontend piece), xUnit + Moq, Testcontainers.

## Global Constraints

- This plan depends on Part 2 (`part-2-cross-legal-entity-invitation.md`) for there to be any real-world case where one person has `Employee` rows in two legal entities — it can and should be built/tested independently of Part 2 using directly-seeded test data, but is only user-visibly meaningful once Part 2 ships too.
- `UserRole` rows with `SourcePositionId == null` (manually assigned roles not tied to any position — e.g. a platform-level admin role) must always stay in the effective set regardless of active entity. Only position-sourced grants are entity-filtered.
- Snake_case DB column naming (EF Core convention already configured project-wide).
- Do not change `PlatformPermissionResolver.cs` — that's a separate, unrelated platform/admin permission system, out of scope.

---

### Task 1: Add `ActiveEmployeeId` to `Session`

**Files:**
- Modify: `src/ONEVO.Domain/Features/Auth/Login/Entities/Session.cs`
- Create: EF Core migration

**Interfaces:**
- Produces: `Session.ActiveEmployeeId` (`Guid?`) — which `Employee` row (i.e. which legal entity) is currently active for this session. `null` until first set (defaults to the user's sole/most-recent `Employee` row, computed in Task 3, not stored as a DB default).

- [x] **Step 1: Add the property**

In `src/ONEVO.Domain/Features/Auth/Login/Entities/Session.cs`, add directly after `CsrfTokenHash`:

```csharp
    public string CsrfTokenHash { get; set; } = string.Empty;
    public Guid? ActiveEmployeeId { get; set; }
    public string KeyHash { get; set; } = string.Empty;
```

- [x] **Step 2: Generate and inspect the migration**

Run:
```bash
dotnet ef migrations add AddSessionActiveEmployeeId --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api
```
Open the generated file and confirm it contains exactly one `AddColumn` for `ActiveEmployeeId` (nullable uuid) on the `sessions` table.

- [x] **Step 3: Apply and verify**

Run: `dotnet ef database update --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api`
Expected: succeeds; `sessions` now has a nullable `active_employee_id` uuid column.

- [x] **Step 4: Commit**

```bash
git add src/ONEVO.Domain/Features/Auth/Login/Entities/Session.cs src/ONEVO.Infrastructure/Migrations/
git commit -m "feat: add Session.ActiveEmployeeId column"
```

---

### Task 2: Entity-scope permission resolution

**Files:**
- Modify: `src/ONEVO.Application/Features/Auth/Permission/RepositoryInterfaces/IPermissionRepository.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/Auth/Login/EfAuthRepository.cs` (around line 353)
- Modify: `src/ONEVO.Application/Features/Auth/Permission/ServiceInterfaces/IPermissionResolver.cs`
- Modify: `src/ONEVO.Infrastructure/Security/PermissionResolver.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Auth/Permission/PermissionResolverTests.cs` (find existing file, extend) and an integration test for the repository query

**Interfaces:**
- `IPermissionResolver.ResolveAsync` gains a new optional parameter: `Task<List<string>> ResolveAsync(Guid userId, Guid tenantId, Guid? activeLegalEntityId, CancellationToken ct = default)`.
- `IPermissionRepository.ListRolePermissionCodesWithModulesAsync` gains the same new parameter.

- [x] **Step 1: Write the failing unit test for `PermissionResolver`**

Find the existing `PermissionResolverTests.cs` (or create it if it doesn't exist, matching this class's constructor-mock pattern exactly — 4 dependencies: `IPermissionRepository`, `IUserPermissionOverrideRepository`, `IModuleEntitlementService`, `IDateTimeProvider`). Add:

```csharp
    [Fact]
    public async Task ResolveAsync_PassesActiveLegalEntityIdThroughToRepository()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();

        _permissions.Setup(p => p.UserHasPermissionCodeAsync(userId, "*", It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _entitlements.Setup(e => e.GetActiveModuleKeysForTenantAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "core_hr" });
        _permissionOverrides.Setup(o => o.ListForUserAsync(tenantId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<UserPermissionOverrideGrant>());
        _permissions
            .Setup(p => p.ListRolePermissionCodesWithModulesAsync(userId, It.IsAny<DateTimeOffset>(), legalEntityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PermissionCodeWithModule> { new("employees:read", "core_hr") });

        var resolver = new PermissionResolver(_permissions.Object, _permissionOverrides.Object, _entitlements.Object, _clock.Object);
        var result = await resolver.ResolveAsync(userId, tenantId, legalEntityId, CancellationToken.None);

        Assert.Contains("employees:read", result);
        _permissions.Verify(
            p => p.ListRolePermissionCodesWithModulesAsync(userId, It.IsAny<DateTimeOffset>(), legalEntityId, It.IsAny<CancellationToken>()),
            Times.Once);
    }
```

- [x] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~PermissionResolverTests"`
Expected: FAIL (build error — neither method accepts a `legalEntityId` parameter yet)

- [x] **Step 3: Update `IPermissionRepository` and `IPermissionResolver`**

In `IPermissionRepository.cs`, change:

```csharp
    Task<IReadOnlyList<PermissionCodeWithModule>> ListRolePermissionCodesWithModulesAsync(
        Guid userId,
        DateTimeOffset now,
        CancellationToken ct = default);
```

to:

```csharp
    /// <summary>Role-derived permission codes for a user, filtered to grants effective now.
    /// When activeLegalEntityId is not null, a UserRole row is only included if it has no
    /// SourcePositionId (not entity-scoped - e.g. a manually assigned admin role) OR its
    /// SourcePositionId's Position belongs to activeLegalEntityId. Pass null to skip entity
    /// filtering entirely (returns every role grant regardless of entity - used where no active-
    /// entity concept applies yet, e.g. before Task 3 of this plan sets one).</summary>
    Task<IReadOnlyList<PermissionCodeWithModule>> ListRolePermissionCodesWithModulesAsync(
        Guid userId,
        DateTimeOffset now,
        Guid? activeLegalEntityId,
        CancellationToken ct = default);
```

In `IPermissionResolver.cs`, apply the identical parameter addition to `ResolveAsync`.

- [x] **Step 4: Update the EF implementation**

In `EfAuthRepository.cs`, replace the method (confirmed at line ~353):

```csharp
    public async Task<IReadOnlyList<PermissionCodeWithModule>> ListRolePermissionCodesWithModulesAsync(
        Guid userId,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        var query = _db.UserRoles
            .Where(ur => ur.UserId == userId && (ur.ExpiresAt == null || ur.ExpiresAt > now))
            .Join(_db.RolePermissions, ur => ur.RoleId, rp => rp.RoleId, (ur, rp) => rp)
            .Join(_db.Permissions, rp => rp.PermissionId, p => p.Id, (rp, p) => new { p.Code, p.Module })
            .Distinct();

        var rows = await query.ToListAsync(ct);
        var result = rows.Select(r => new PermissionCodeWithModule(r.Code, r.Module)).ToList();
        return result;
    }
```

with:

```csharp
    public async Task<IReadOnlyList<PermissionCodeWithModule>> ListRolePermissionCodesWithModulesAsync(
        Guid userId,
        DateTimeOffset now,
        Guid? activeLegalEntityId,
        CancellationToken ct = default)
    {
        var userRoles = _db.UserRoles
            .Where(ur => ur.UserId == userId && (ur.ExpiresAt == null || ur.ExpiresAt > now));

        if (activeLegalEntityId is Guid legalEntityId)
        {
            userRoles = userRoles
                .Join(_db.Positions.AsQueryable().DefaultIfEmpty(), // left-join semantics via GroupJoin below
                    ur => ur.SourcePositionId, pos => (Guid?)pos.Id,
                    (ur, pos) => new { ur, pos })
                .GroupJoin(_db.Positions, x => x.ur.SourcePositionId, pos => (Guid?)pos.Id, (x, positions) => new { x.ur, positions })
                .SelectMany(x => x.positions.DefaultIfEmpty(), (x, pos) => new { x.ur, pos })
                .Where(x => x.ur.SourcePositionId == null || (x.pos != null && x.pos.LegalEntityId == legalEntityId))
                .Select(x => x.ur);
        }

        var query = userRoles
            .Join(_db.RolePermissions, ur => ur.RoleId, rp => rp.RoleId, (ur, rp) => rp)
            .Join(_db.Permissions, rp => rp.PermissionId, p => p.Id, (rp, p) => new { p.Code, p.Module })
            .Distinct();

        var rows = await query.ToListAsync(ct);
        var result = rows.Select(r => new PermissionCodeWithModule(r.Code, r.Module)).ToList();
        return result;
    }
```

Note: the double-join above is written defensively for a left-join in LINQ-to-Entities; if this doesn't translate cleanly to SQL in this EF Core version (test in Step 6 will show a runtime translation error if so), simplify to:

```csharp
        var userRoles = _db.UserRoles
            .Where(ur => ur.UserId == userId && (ur.ExpiresAt == null || ur.ExpiresAt > now));

        if (activeLegalEntityId is Guid legalEntityId)
        {
            var entityPositionIds = _db.Positions
                .Where(p => p.LegalEntityId == legalEntityId)
                .Select(p => p.Id);
            userRoles = userRoles.Where(ur => ur.SourcePositionId == null || entityPositionIds.Contains(ur.SourcePositionId!.Value));
        }
```

which achieves the identical filter via a `Contains` subquery instead of an explicit join, and is simpler EF Core SQL to reason about — prefer this form.

Also update the existing (unfiltered) `ListRolePermissionCodesAsync` method just above this one only if it's still called anywhere with an implicit assumption of "all entities" that should now change — grep for its callers first; if the only caller was `PermissionResolver` and it now exclusively uses the `WithModules` variant, leave `ListRolePermissionCodesAsync` untouched (it may serve a different, non-entity-aware caller).

- [x] **Step 5: Update `PermissionResolver.ResolveAsync`**

In `PermissionResolver.cs`, change the signature and the one call site:

```csharp
    public async Task<List<string>> ResolveAsync(Guid userId, Guid tenantId, Guid? activeLegalEntityId, CancellationToken ct = default)
    {
        ...
        var roleRows = await _permissions.ListRolePermissionCodesWithModulesAsync(userId, now, activeLegalEntityId, ct);
        ...
    }
```

Every other line in the method body stays exactly as-is (module gating, overrides, derived-permissions steps are all unaffected by entity scoping — only the role-grant source rows are filtered).

- [x] **Step 6: Run the test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~PermissionResolverTests"`
Expected: PASS. Also update every other pre-existing call site of `ResolveAsync`/`ListRolePermissionCodesWithModulesAsync` in this codebase (grep for both method names) to pass an explicit argument — most callers outside `TenantDatabaseTicketStore` (Task 3) should pass `activeLegalEntityId: null` to preserve their current unfiltered behavior, since they don't have a session/active-entity context to work from.

- [x] **Step 7: Write an integration test for the entity-filtering query itself**

Create/extend an integration test (find this repo's existing pattern for testing `EfAuthRepository`, or create `tests/ONEVO.Tests.Integration/Auth/Permission/ListRolePermissionCodesWithModulesEntityFilterTests.cs`) that: seeds one user with two `UserRole` rows, each `SourcePositionId` pointing at a position in a different legal entity, each role granting a different permission code; asserts that passing legal-entity-A's id returns only entity-A's permission code, passing entity-B's id returns only entity-B's, and passing `null` returns both (backward-compatible unfiltered behavior).

Run: `dotnet test tests/ONEVO.Tests.Integration/ONEVO.Tests.Integration.csproj --filter "FullyQualifiedName~ListRolePermissionCodesWithModulesEntityFilter"`
Expected: PASS

- [x] **Step 8: Commit**

```bash
git add src/ONEVO.Application/Features/Auth/Permission/RepositoryInterfaces/IPermissionRepository.cs src/ONEVO.Application/Features/Auth/Permission/ServiceInterfaces/IPermissionResolver.cs src/ONEVO.Infrastructure/Persistence/Repositories/Auth/Login/EfAuthRepository.cs src/ONEVO.Infrastructure/Security/PermissionResolver.cs tests/ONEVO.Tests.Unit/Features/Auth/Permission/PermissionResolverTests.cs tests/ONEVO.Tests.Integration/Auth/Permission/ListRolePermissionCodesWithModulesEntityFilterTests.cs
git commit -m "feat: scope role-permission resolution to the active legal entity"
```

---

### Task 3: Wire `ActiveEmployeeId` through session retrieval and expose `SessionId` on `ICurrentUser`

**Files:**
- Modify: `src/ONEVO.Application/Common/ServiceInterfaces/ICurrentUser.cs`
- Modify: `src/ONEVO.Infrastructure/Identity/CurrentUser/CurrentUserService.cs`
- Modify: `src/ONEVO.Infrastructure/Identity/Sessions/TenantDatabaseTicketStore.cs`
- Test: find this repo's existing test coverage for `TenantDatabaseTicketStore` (search `tests/` for the class name) and extend it; if none exists, add integration coverage in Task 5 instead of unit-testing this class in isolation (it's a `singleton` wired deeply into ASP.NET Core's cookie auth pipeline, which earlier investigation found this repo tests at the integration level, not via handler-style unit mocks)

**Interfaces:**
- Produces: `ICurrentUser.SessionId` (`Guid?`, default `null` via interface default method like `SessionBinding`/`SessionExpiresAt` already do) and a new `session_id` claim.
- `RetrieveAsync` now resolves `activeLegalEntityId` from `session.ActiveEmployeeId` (via a new `IEmployeeRepository` lookup) and passes it into `IPermissionResolver.ResolveAsync`.

- [x] **Step 1: Add `SessionId` to `ICurrentUser`**

In `ICurrentUser.cs`, add directly after `SessionExpiresAt`:

```csharp
    DateTimeOffset? SessionExpiresAt { get => null; }
    Guid? SessionId { get => null; }
```

- [x] **Step 2: Implement it in `CurrentUserService`**

In `CurrentUserService.cs`, add:

```csharp
    public Guid? SessionId
    {
        get
        {
            var value = _httpContextAccessor.HttpContext?.User?.FindFirstValue("session_id");
            return Guid.TryParse(value, out var id) ? id : null;
        }
    }
```

- [x] **Step 3: Add the `session_id` claim and entity-scoped resolution in `TenantDatabaseTicketStore.RetrieveAsync`**

In `TenantDatabaseTicketStore.cs`, inside `RetrieveAsync`, add an `IEmployeeRepository` resolution alongside the existing scoped services:

```csharp
        var sessions = scope.ServiceProvider.GetRequiredService<ISessionRepository>();
        var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var permissionResolver = scope.ServiceProvider.GetRequiredService<IPermissionResolver>();
        var tenants = scope.ServiceProvider.GetRequiredService<ITenantRepository>();
        var tenantSwitcher = scope.ServiceProvider.GetRequiredService<ITenantContextSwitcher>();
        var employees = scope.ServiceProvider.GetRequiredService<IEmployeeRepository>();
```

(Add `using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;` at the top of the file.)

Then, after the existing `await SwitchToTenantAsync(...)` call and before `var permissions = await permissionResolver.ResolveAsync(...)`, add:

```csharp
        Guid? activeLegalEntityId = null;
        if (session.ActiveEmployeeId is Guid activeEmployeeId)
        {
            var activeEmployee = await employees.GetByIdAsync(session.TenantId, activeEmployeeId, ct);
            activeLegalEntityId = activeEmployee?.LegalEntityId;
        }
```

(`ct` here is `cancellationToken`, the method's actual parameter name — match whatever the existing code in this method already calls it.)

Change the resolve call:

```csharp
        var permissions = await permissionResolver.ResolveAsync(session.UserId, session.TenantId, activeLegalEntityId, cancellationToken);
```

Add the new claim to the `claims` list, alongside the existing ones:

```csharp
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, session.UserId.ToString()),
            new("tenant_id", session.TenantId.ToString()),
            new(ClaimTypes.Email, user.Email),
            new("csrf_token_hash", session.CsrfTokenHash ?? string.Empty),
            new("session_expires_at", session.ExpiresAt.ToString("O")),
            new("session_id", session.Id.ToString()),
        };
```

- [x] **Step 4: Default a brand-new session's `ActiveEmployeeId` at login**

In `StoreAsync` (same file), find where the `Session` object is constructed:

```csharp
        var session = new Session
        {
            Id = Guid.NewGuid(),
            KeyHash = HashKey(rawKey),
            UserId = userId,
            TenantId = tenantId,
            ...
        };
```

Add, just before constructing it: resolve the user's default active Employee (their sole one, or the most recently effective one if they have several — reuse `IEmployeeRepository` the same way Step 3 does, resolved from this method's own `scope`):

```csharp
        var employees = scope.ServiceProvider.GetRequiredService<IEmployeeRepository>();
        var defaultEmployee = await employees.GetDefaultForUserAsync(tenantId, userId, cancellationToken); // see Step 4a below for this new repository method
```

and set `ActiveEmployeeId = defaultEmployee?.Id` on the constructed `Session`.

- [x] **Step 4a: Add `IEmployeeRepository.GetDefaultForUserAsync`**

In `IEmployeeRepository.cs`, add:

```csharp
    /// <summary>The Employee row a fresh session should default its ActiveEmployeeId to: the
    /// user's only Employee row if they have exactly one, or - if they have more than one (Part
    /// 2 of this feature set) - the one with the most recent active PrimaryEmployment
    /// PositionAssignment.EffectiveFrom. Returns null if the user has no Employee row at all
    /// (e.g. a tenant-owner/platform account with no employee profile).</summary>
    Task<ONEVO.Domain.Features.CoreHr.Entities.Employee?> GetDefaultForUserAsync(
        Guid tenantId, Guid userId, CancellationToken ct = default);
```

In `EfEmployeeRepository.cs`, implement it:

```csharp
    public async Task<Employee?> GetDefaultForUserAsync(Guid tenantId, Guid userId, CancellationToken ct = default)
    {
        var query =
            from e in _db.Employees.AsNoTracking()
            where e.TenantId == tenantId && e.UserId == userId
            join pa in _db.PositionAssignments.AsNoTracking()
                .Where(pa => pa.TenantId == tenantId
                    && pa.AssignmentKind == PositionAssignmentKind.PrimaryEmployment
                    && pa.AssignmentStatus == PositionAssignmentStatus.Active)
                on e.Id equals pa.EmployeeId into paJoin
            from pa in paJoin.DefaultIfEmpty()
            orderby pa != null ? pa.EffectiveFrom : DateOnly.MinValue descending
            select e;

        return await query.FirstOrDefaultAsync(ct);
    }
```

(Match this file's existing `using`/namespace-alias conventions for `PositionAssignmentKind`/`PositionAssignmentStatus` — they're already imported for other methods in this file per Task 3 of `part-1-invitation-capacity-lifecycle.md`.)

- [x] **Step 5: Run the full unit suite to confirm nothing broke**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj`
Expected: PASS. `TenantDatabaseTicketStore` itself has no direct unit tests (confirmed by this task's own investigation step), so this is a compile-and-regression check, not new-behavior verification — that comes in Task 5's integration test.

- [x] **Step 6: Commit**

```bash
git add src/ONEVO.Application/Common/ServiceInterfaces/ICurrentUser.cs src/ONEVO.Infrastructure/Identity/CurrentUser/CurrentUserService.cs src/ONEVO.Infrastructure/Identity/Sessions/TenantDatabaseTicketStore.cs src/ONEVO.Application/Features/CoreHr/Employee/RepositoryInterfaces/IEmployeeRepository.cs src/ONEVO.Infrastructure/Persistence/Repositories/EfEmployeeRepository.cs
git commit -m "feat: resolve permissions against the session's active employee/legal entity"
```

---

### Task 4: Switch-active-company endpoint

**Files:**
- Create: `src/ONEVO.Application/Features/Auth/Session/Commands/SwitchActiveCompany/SwitchActiveCompanyCommand.cs`
- Create: `src/ONEVO.Application/Features/Auth/Session/Commands/SwitchActiveCompany/SwitchActiveCompanyCommandHandler.cs`
- Create: `src/ONEVO.Application/Features/Auth/Session/Commands/SwitchActiveCompany/SwitchActiveCompanyCommandValidator.cs`
- Create: `src/ONEVO.Api/Contracts/Auth/Session/SwitchActiveCompanyRequest.cs`
- Create or modify: a session-actions controller under `src/ONEVO.Api/Controllers/Tenant/Auth/` (check whether one already exists for session-scoped actions before creating a new file — search for `SessionController`)
- Test: `tests/ONEVO.Tests.Unit/Features/Auth/Session/SwitchActiveCompanyCommandHandlerTests.cs` (create)

**Interfaces:**
- Produces: `POST /api/v1/session/active-company`, body `{ employeeId: string }`, `[Authorize(Policy = "TenantPolicy")]`. `204 No Content` on success (the frontend re-fetches its own session/permission state afterward, per Task 5 — this endpoint doesn't need to echo the new permission set back).

- [x] **Step 1: Write the failing unit test**

Create `tests/ONEVO.Tests.Unit/Features/Auth/Session/SwitchActiveCompanyCommandHandlerTests.cs`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Session.Commands.SwitchActiveCompany;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;
using Xunit;
using IUnitOfWork = ONEVO.Application.Common.RepositoryInterfaces.IUnitOfWork;

namespace ONEVO.Tests.Unit.Features.Auth.Session;

public class SwitchActiveCompanyCommandHandlerTests
{
    private readonly Mock<ISessionRepository> _sessions = new();
    private readonly Mock<IEmployeeRepository> _employees = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<ICurrentUser> _currentUser = new();

    private SwitchActiveCompanyCommandHandler CreateHandler() =>
        new(_sessions.Object, _employees.Object, _unitOfWork.Object, _currentUser.Object);

    [Fact]
    public async Task Handle_TargetEmployeeBelongsToCaller_UpdatesSessionActiveEmployeeId()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var targetEmployeeId = Guid.NewGuid();
        var session = new ONEVO.Domain.Features.Auth.Entities.Session { Id = sessionId, UserId = userId, TenantId = tenantId };
        var targetEmployee = new Employee { Id = targetEmployeeId, TenantId = tenantId, UserId = userId };

        _currentUser.Setup(c => c.TenantId).Returns(tenantId);
        _currentUser.Setup(c => c.UserId).Returns(userId);
        _currentUser.Setup(c => c.SessionId).Returns(sessionId);
        _sessions.Setup(s => s.GetByIdAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync(session);
        _employees.Setup(e => e.GetByIdAsync(tenantId, targetEmployeeId, It.IsAny<CancellationToken>())).ReturnsAsync(targetEmployee);

        var handler = CreateHandler();
        var result = await handler.Handle(new SwitchActiveCompanyCommand(targetEmployeeId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(targetEmployeeId, session.ActiveEmployeeId);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_TargetEmployeeBelongsToDifferentUser_ReturnsForbidden()
    {
        var tenantId = Guid.NewGuid();
        var callerId = Guid.NewGuid();
        var someoneElsesUserId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var targetEmployeeId = Guid.NewGuid();
        var session = new ONEVO.Domain.Features.Auth.Entities.Session { Id = sessionId, UserId = callerId, TenantId = tenantId };
        var someoneElsesEmployee = new Employee { Id = targetEmployeeId, TenantId = tenantId, UserId = someoneElsesUserId };

        _currentUser.Setup(c => c.TenantId).Returns(tenantId);
        _currentUser.Setup(c => c.UserId).Returns(callerId);
        _currentUser.Setup(c => c.SessionId).Returns(sessionId);
        _sessions.Setup(s => s.GetByIdAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync(session);
        _employees.Setup(e => e.GetByIdAsync(tenantId, targetEmployeeId, It.IsAny<CancellationToken>())).ReturnsAsync(someoneElsesEmployee);

        var handler = CreateHandler();
        var result = await handler.Handle(new SwitchActiveCompanyCommand(targetEmployeeId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_TargetEmployeeDoesNotExist_ReturnsNotFound()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var targetEmployeeId = Guid.NewGuid();
        var session = new ONEVO.Domain.Features.Auth.Entities.Session { Id = sessionId, UserId = userId, TenantId = tenantId };

        _currentUser.Setup(c => c.TenantId).Returns(tenantId);
        _currentUser.Setup(c => c.UserId).Returns(userId);
        _currentUser.Setup(c => c.SessionId).Returns(sessionId);
        _sessions.Setup(s => s.GetByIdAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync(session);
        _employees.Setup(e => e.GetByIdAsync(tenantId, targetEmployeeId, It.IsAny<CancellationToken>())).ReturnsAsync((Employee?)null);

        var handler = CreateHandler();
        var result = await handler.Handle(new SwitchActiveCompanyCommand(targetEmployeeId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }
}
```

- [x] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~SwitchActiveCompanyCommandHandlerTests"`
Expected: FAIL (build error — types don't exist yet)

- [x] **Step 3: Create the command and validator**

`SwitchActiveCompanyCommand.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Auth.Session.Commands.SwitchActiveCompany;

public sealed record SwitchActiveCompanyCommand(Guid EmployeeId) : IRequest<Result<Unit>>;
```

`SwitchActiveCompanyCommandValidator.cs`:

```csharp
using FluentValidation;

namespace ONEVO.Application.Features.Auth.Session.Commands.SwitchActiveCompany;

public sealed class SwitchActiveCompanyCommandValidator : AbstractValidator<SwitchActiveCompanyCommand>
{
    public SwitchActiveCompanyCommandValidator()
    {
        RuleFor(x => x.EmployeeId).NotEmpty();
    }
}
```

- [x] **Step 4: Create the handler**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using IUnitOfWork = ONEVO.Application.Common.RepositoryInterfaces.IUnitOfWork;

namespace ONEVO.Application.Features.Auth.Session.Commands.SwitchActiveCompany;

/// <summary>
/// Switches which of the caller's own Employee rows (i.e. which legal entity/company) is active
/// for their current session. Permission resolution reads this on the very next request
/// (TenantDatabaseTicketStore.RetrieveAsync runs per-request, not once at login) - no forced
/// re-login or token refresh needed.
/// </summary>
public sealed class SwitchActiveCompanyCommandHandler : IRequestHandler<SwitchActiveCompanyCommand, Result<Unit>>
{
    private readonly ISessionRepository _sessions;
    private readonly IEmployeeRepository _employees;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;

    public SwitchActiveCompanyCommandHandler(
        ISessionRepository sessions, IEmployeeRepository employees, IUnitOfWork unitOfWork, ICurrentUser currentUser)
    {
        _sessions = sessions;
        _employees = employees;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
    }

    public async Task<Result<Unit>> Handle(SwitchActiveCompanyCommand request, CancellationToken ct)
    {
        var tenantId = _currentUser.TenantId;

        if (_currentUser.SessionId is not Guid sessionId)
            return Result<Unit>.Failure("No active session.", 401);

        var session = await _sessions.GetByIdAsync(sessionId, ct);
        if (session is null)
            return Result<Unit>.Failure("No active session.", 401);

        var targetEmployee = await _employees.GetByIdAsync(tenantId, request.EmployeeId, ct);
        if (targetEmployee is null)
            return Result<Unit>.NotFound("The selected company could not be found.");

        if (targetEmployee.UserId != _currentUser.UserId)
            return Result<Unit>.Failure("You do not have access to this company.", 403);

        session.ActiveEmployeeId = targetEmployee.Id;
        await _unitOfWork.SaveChangesAsync(ct);

        return Result<Unit>.Success(Unit.Value);
    }
}
```

- [x] **Step 5: Create the request contract**

`src/ONEVO.Api/Contracts/Auth/Session/SwitchActiveCompanyRequest.cs`:

```csharp
namespace ONEVO.Api.Contracts.Auth.Session;

public sealed record SwitchActiveCompanyRequest(Guid EmployeeId);
```

- [x] **Step 6: Wire the endpoint**

Search `src/ONEVO.Api/Controllers/Tenant/Auth/` for an existing session-scoped controller (e.g. anything exposing `GET /api/v1/auth/me` or similar "current session info" route) before creating a new one — if one exists, add the action there; otherwise create `SessionController.cs`:

```csharp
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.Auth.Session;
using ONEVO.Application.Features.Auth.Session.Commands.SwitchActiveCompany;

namespace ONEVO.Api.Controllers.Tenant.Auth;

[ApiController]
[Route("api/v1/session")]
[Authorize(Policy = "TenantPolicy")]
public class SessionController : ControllerBase
{
    private readonly IMediator _mediator;

    public SessionController(IMediator mediator) => _mediator = mediator;

    /// <summary>Switch which of the caller's own Employee rows (company/legal entity) is active
    /// for this session. Permissions reflect the new active company on the next request.</summary>
    [HttpPost("active-company")]
    public async Task<IActionResult> SwitchActiveCompany(
        [FromBody] SwitchActiveCompanyRequest request, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new SwitchActiveCompanyCommand(request.EmployeeId), ct);
        return result.IsSuccess
            ? NoContent()
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
```

- [x] **Step 7: Run the test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~SwitchActiveCompanyCommandHandlerTests"`
Expected: PASS (all 3 tests)

- [x] **Step 8: Commit**

```bash
git add src/ONEVO.Application/Features/Auth/Session/Commands/SwitchActiveCompany/ src/ONEVO.Api/Contracts/Auth/Session/SwitchActiveCompanyRequest.cs src/ONEVO.Api/Controllers/Tenant/Auth/SessionController.cs tests/ONEVO.Tests.Unit/Features/Auth/Session/SwitchActiveCompanyCommandHandlerTests.cs
git commit -m "feat: add POST /api/v1/session/active-company endpoint"
```

---

### Task 5: End-to-end integration test + frontend wiring

**Files:**
- Create: `tests/ONEVO.Tests.Integration/Auth/Session/SwitchActiveCompanyIntegrationTests.cs`
- Modify (frontend repo `Hrms--Web-application---front-end---v1`): `src/app/layouts/main-layout/top-navbar/company-selector/company-selector.component.ts`
- Modify (frontend repo): the auth/session data-access service that currently exposes login/session state (find it: search `data-access/` for whatever service already calls `GET /api/v1/auth/me` or equivalent, used at app bootstrap)

**Interfaces:**
- No new production interfaces on the frontend beyond calling the new endpoint — this task is verification + wiring, not new design.

- [x] **Step 1: Backend integration test**

Create `tests/ONEVO.Tests.Integration/Auth/Session/SwitchActiveCompanyIntegrationTests.cs`, following this repo's existing full-stack pattern (`WebApplicationFactory` + real login flow to get a cookie, matching whatever an existing auth integration test already does):

```csharp
[Fact]
public async Task SwitchActiveCompany_ChangesEffectivePermissionsOnNextRequest()
{
    // 1. Seed a tenant, a user with two Employee rows in two different legal entities,
    //    each with a UserRole granting a DIFFERENT permission via a position-sourced grant
    //    (SourcePositionId set, matching Task 2's filter).
    // 2. Log in (real HTTP call through the login endpoint, real cookie captured by the test's
    //    HttpClient) - default ActiveEmployeeId should resolve to one of the two per Task 3
    //    Step 4a's ordering rule.
    // 3. Call an endpoint gated by entity-A's permission - expect success or 403 depending on
    //    which entity defaulted active.
    // 4. POST /api/v1/session/active-company with the OTHER employeeId.
    // 5. Re-call the same gated endpoint - the result must flip (403 becomes success, or vice
    //    versa), proving the permission set actually changed after the switch, using the same
    //    session cookie throughout (no re-login).
}
```

Fill in exact seeding/assertion calls using this repo's real integration-test helpers (read an existing auth integration test file first, matching its login-flow HTTP call shape).

- [x] **Step 2: Run it**

Run: `dotnet test tests/ONEVO.Tests.Integration/ONEVO.Tests.Integration.csproj --filter "FullyQualifiedName~SwitchActiveCompanyIntegrationTests"`
Expected: PASS

- [x] **Step 3: Commit backend integration test**

```bash
git add tests/ONEVO.Tests.Integration/Auth/Session/SwitchActiveCompanyIntegrationTests.cs
git commit -m "test: add end-to-end coverage for active-company permission switching"
```

- [x] **Step 4: Read the current frontend company-selector and session service**

Open `Hrms--Web-application---front-end---v1/src/app/layouts/main-layout/top-navbar/company-selector/company-selector.component.ts` and `modules/organization/state/legal-entity.store.ts`. Confirm the exact current `selectCompany()` implementation (expected, per earlier investigation: it only calls `patchState(store, { selectedLegalEntityId })`, no HTTP call) and find the app's session/auth data-access service (the one already called at bootstrap to load the current user/permissions — likely under `core/` or `modules/auth/data-access/`).

- [x] **Step 5: Wire the switch call**

In `company-selector.component.ts`, change `selectCompany()` (or whatever the actual method name is, confirmed in Step 4) from a local-only `patchState` call to:

```typescript
async selectCompany(employeeId: string): Promise<void> {
  await this.companyApi.switchActiveCompany(employeeId); // new method, Step 6
  await this.authService.refreshSession(); // re-fetches /api/v1/auth/me (or equivalent) so permission-gated UI updates
  patchState(this.store, { selectedLegalEntityId: /* resolve from the employee's legal entity, same as today */ });
}
```

Adjust exact syntax/DI-injected service names to match this component's real current structure (confirmed in Step 4) — this is Angular 21 with standalone components and `inject()`-style DI per the architecture doc, so match whatever pattern the file already uses.

- [x] **Step 6: Add the API call**

In this module's `data-access/*-api.service.ts` (the one already used for other organization/legal-entity calls — confirmed in Step 4), add:

```typescript
switchActiveCompany(employeeId: string): Observable<void> {
  return this.http.post<void>('/api/v1/session/active-company', { employeeId });
}
```

Match the existing service's exact base-URL/`HttpClient` injection pattern rather than hardcoding `/api/v1/...` if a base-path constant already exists elsewhere in this file.

- [x] **Step 7: Manual verification**

Start the frontend dev server and backend API locally. Log in as a seeded user with two Employee rows in two legal entities (use Task 1's integration test seeding as a reference for what "two Employee rows" looks like, or seed it directly via the dev-smoke seeder if this repo has one for local testing). Switch company via the topbar selector. Confirm a permission-gated nav item or button that should only appear for one entity's role toggles visibility after the switch, without a full page reload or re-login.

- [x] **Step 8: Commit frontend changes**

```bash
git add src/app/layouts/main-layout/top-navbar/company-selector/company-selector.component.ts
git commit -m "feat: call session active-company switch endpoint and refresh session on company change"
```

(Run from the `Hrms--Web-application---front-end---v1` repo root, not `HRMS-Backend-v1` — this is a separate git repository.)

---

## Part 3 done — foundation complete

All three parts of the multi-legal-entity employment foundation are now implemented. The follow-up spec (Employee Detail screen, `employees:read:sensitive` gating, "Change Position" action wired to the existing Transfer/Promotion workflow) can now be brainstormed and planned on top of this foundation — it depends on this plan's coverage-manager infrastructure (already existing, unmodified by this plan) and the permission/session mechanics built here.
