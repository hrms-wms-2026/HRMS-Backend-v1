# PlatformUserDetailResponse Shape Fix Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix `PlatformUserDetailResponse`/`PlatformAccessMapper.MapDetail` to expose `FullName`/`Status` directly instead of a stale `FirstName`/`LastName`/`IsActive` shape that collapses `Pending` and `Inactive` users into the same `IsActive: false` value.

**Architecture:** Single DTO + single mapper method change, mirroring the pattern already used by the sibling list-response mapper (`PlatformAccessMapper.Map(PlatformUser, string)`). No handler signature change — `GetPlatformUserDetailQueryHandler` calls `PlatformAccessMapper.MapDetail(user, roles)` today and continues to after this fix; only the DTO shape and the mapper's internals change.

**Tech Stack:** C# / .NET 10, xUnit + FluentAssertions.

## Global Constraints

- This is a companion backend fix for the platform-administration frontend's "User Profile Drawer" feature (`docs/superpowers/specs/2026-08-07-user-profile-drawer-design.md` in that repo) — the drawer needs an accurate three-state status, not a collapsed boolean.
- No existing test references `PlatformUserDetailResponse`, `GetPlatformUserDetailQueryHandler`, or `MapDetail(user` (confirmed via repo-wide grep) — this is a green-field test addition, not an update to something pre-existing.
- Match the already-fixed sibling mapper exactly: `PlatformAccessMapper.Map(PlatformUser user, string role)` passes `user.FullName` and `user.Status` straight through with no transformation — do the same in `MapDetail`.

---

### Task 1: Fix PlatformUserDetailResponse shape and its mapper

**Files:**
- Modify: `src/ONEVO.Application/Features/DevPlatform/PlatformAccess/DTOs/Responses/PlatformUserDetailResponse.cs`
- Modify: `src/ONEVO.Application/Features/DevPlatform/PlatformAccess/Mappers/PlatformAccessMapper.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/DevPlatform/PlatformAccess/PlatformAccessMapperTests.cs`

**Interfaces:**
- Consumes: nothing new — `PlatformUser` entity (`Id`, `Email`, `FullName`, `Status`, `CreatedAt`, `LastLoginAt`) and `PlatformRole` entity, both already exist.
- Produces: `PlatformUserDetailResponse(Guid Id, string Email, string FullName, string Status, DateTimeOffset CreatedAt, DateTimeOffset? LastLoginAt, IReadOnlyList<PlatformRoleResponse> Roles)` — the platform-administration frontend's User Profile Drawer sub-project consumes this exact shape (camelCase JSON: `id, email, fullName, status, createdAt, lastLoginAt, roles`). `PlatformAccessMapper.MapDetail(PlatformUser user, IEnumerable<PlatformRole> roles)` keeps its exact signature — only its return value's shape changes, so `GetPlatformUserDetailQueryHandler.cs`'s call site (`PlatformAccessMapper.MapDetail(user, roles)`) needs no edit.

- [ ] **Step 1: Write the failing test**

Add to `tests/ONEVO.Tests.Unit/Features/DevPlatform/PlatformAccess/PlatformAccessMapperTests.cs` (append after the existing `Map_PendingUser_ReturnsPendingStatus` test, inside the same `PlatformAccessMapperTests` class):

```csharp
    [Fact]
    public void MapDetail_ActiveUserWithRoles_ReturnsFullNameStatusAndRoles()
    {
        var user = new PlatformUser
        {
            Id = Guid.NewGuid(),
            Email = "manager@onevo.io",
            FullName = "Arun Selvan",
            Status = PlatformUser.StatusActive,
            CreatedAt = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero),
            LastLoginAt = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
        };
        var role = new PlatformRole
        {
            Id = Guid.NewGuid(),
            Name = "Security Auditor",
            Description = "Read-only audit access",
            IsSystem = false,
            CreatedAt = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
        };

        var result = PlatformAccessMapper.MapDetail(user, new[] { role });

        result.Id.Should().Be(user.Id);
        result.Email.Should().Be("manager@onevo.io");
        result.FullName.Should().Be("Arun Selvan");
        result.Status.Should().Be(PlatformUser.StatusActive);
        result.CreatedAt.Should().Be(user.CreatedAt);
        result.LastLoginAt.Should().Be(user.LastLoginAt);
        result.Roles.Should().ContainSingle(r => r.Id == role.Id && r.Name == "Security Auditor");
    }

    [Fact]
    public void MapDetail_PendingUser_ReturnsPendingStatus_NotCollapsedToBoolean()
    {
        var user = new PlatformUser
        {
            Id = Guid.NewGuid(),
            Email = "pending@onevo.io",
            FullName = "Pending User",
            Status = PlatformUser.StatusPending,
        };

        var result = PlatformAccessMapper.MapDetail(user, Array.Empty<PlatformRole>());

        result.Status.Should().Be(PlatformUser.StatusPending);
    }

    [Fact]
    public void MapDetail_InactiveUser_ReturnsInactiveStatus()
    {
        var user = new PlatformUser
        {
            Id = Guid.NewGuid(),
            Email = "inactive@onevo.io",
            FullName = "Inactive User",
            Status = PlatformUser.StatusInactive,
        };

        var result = PlatformAccessMapper.MapDetail(user, Array.Empty<PlatformRole>());

        result.Status.Should().Be(PlatformUser.StatusInactive);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~PlatformAccessMapperTests"`
Expected: FAIL — compile error, since `PlatformUserDetailResponse` has no `FullName`/`Status` properties yet (only `FirstName`/`LastName`/`IsActive`).

- [ ] **Step 3: Fix the DTO**

Replace the full contents of `src/ONEVO.Application/Features/DevPlatform/PlatformAccess/DTOs/Responses/PlatformUserDetailResponse.cs`:

```csharp
namespace ONEVO.Application.Features.DevPlatform.PlatformAccess.DTOs.Responses;

public record PlatformUserDetailResponse(
    Guid Id,
    string Email,
    string FullName,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt,
    IReadOnlyList<PlatformRoleResponse> Roles);
```

- [ ] **Step 4: Fix the mapper**

In `src/ONEVO.Application/Features/DevPlatform/PlatformAccess/Mappers/PlatformAccessMapper.cs`, replace the `MapDetail(PlatformUser user, IEnumerable<PlatformRole> roles)` method body:

```csharp
    public static PlatformUserDetailResponse MapDetail(PlatformUser user, IEnumerable<PlatformRole> roles)
    {
        var mappedRoles = roles.Select(Map).ToList();

        return new PlatformUserDetailResponse(
            user.Id,
            user.Email,
            user.FullName,
            user.Status,
            user.CreatedAt,
            user.LastLoginAt,
            mappedRoles);
    }
```

(Only the body changes — `null` and `user.Status == PlatformUser.StatusActive` are replaced with `user.FullName` and `user.Status` respectively, matching the sibling `Map(PlatformUser, string)` method's pattern above it in the same file.)

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~PlatformAccessMapperTests"`
Expected: PASS — all 7 tests green (4 existing + 3 new).

- [ ] **Step 6: Run the full backend unit test suite**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj`
Expected: PASS — no regressions. (If this fails to build due to a locked DLL from a running `dotnet run` process, stop the running backend server first, then retry.)

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Application/Features/DevPlatform/PlatformAccess/DTOs/Responses/PlatformUserDetailResponse.cs src/ONEVO.Application/Features/DevPlatform/PlatformAccess/Mappers/PlatformAccessMapper.cs tests/ONEVO.Tests.Unit/Features/DevPlatform/PlatformAccess/PlatformAccessMapperTests.cs
git commit -m "fix: expose FullName/Status on PlatformUserDetailResponse instead of stale FirstName/LastName/IsActive"
```

---

## Self-Review Notes

- **Spec coverage:** the frontend spec's "Small required backend fix" section is fully covered — DTO shape ✓, mapper ✓, new unit test coverage (since none existed) ✓, no handler change needed since the call site's signature is untouched ✓.
- **Placeholder scan:** none — every step has literal file contents.
- **Type consistency:** `PlatformUserDetailResponse`'s new shape (`FullName: string`, `Status: string`) matches exactly what `MapDetail` now returns, and matches the sibling `PlatformUserResponse`/`Map(PlatformUser, string)` pattern this fix mirrors.
