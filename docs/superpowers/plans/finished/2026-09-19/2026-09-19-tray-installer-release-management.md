# Tray Installer Release Management Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the static, empty `TrayInstaller` appsettings block with a DB-backed, version-controlled tray installer registry that the Platform Admin app manages, CI publishes to, the employee web app surfaces as a Download step next to the existing activation code, and the tray uses to check for updates.

**Architecture:** A platform-level `tray_app_releases` table (no tenant, no RLS — same class as `platform_service_keys`) is the single source of truth. Admins (cookie auth) manage rows via `/admin/v1/tray-releases`; GitHub Actions registers new builds via a token-guarded ingest endpoint as *inactive beta*, and an admin promotes them. Two anonymous read endpoints under `/api/v1/tray/releases` serve the web app and the tray (installer metadata is non-secret; integrity is enforced by SHA-256). The tray Service performs the update check over its existing "OnevoApi" HttpClient and reports to the TrayApp over the existing named-pipe IPC.

**Tech Stack:** .NET 10 / EF Core / PostgreSQL / MediatR (backend); Angular 21 + @ngrx/signals + Jest + Tailwind (admin FE); Angular 21 + Vitest (employee web FE); .NET MAUI + Windows Service + xUnit (tray); GitHub Actions; Cloudflare R2 (public bucket) for installer storage.

**Spec:** None separate — this plan's "Scope decisions" below are the spec. Conversation origin: 2026-09-19 session (installer + one-time code flow). Existing code this builds on:
- Code flow (already built, do NOT rebuild): `HRMS-Backend-v1/src/ONEVO.Application/Features/Monitoring/TrayActivation/Commands/GenerateActivationCode/GenerateActivationCodeCommandHandler.cs`, `.../ExchangeActivationCode/`, web `Hrms--Web-application---front-end---v1/src/app/core/tray-presence/`.
- Pattern to mirror for admin CRUD: `HRMS-Backend-v1/src/ONEVO.Application/Features/DevPlatform/SystemConfig/PlatformServiceKeys/` and `.../Controllers/Admin/DevPlatform/SystemConfig/PlatformServiceKeysController.cs`.
- Pattern to mirror for admin FE: `HRMS-Platform-Administration-Front-End-v1/src/app/modules/system-config/` (service-keys).

## Scope decisions (read first)

1. **Four repos are touched:** `HRMS-Backend-v1` (Tasks 1–3), `HRMS-Platform-Administration-Front-End-v1` (Task 4), `Hrms--Web-application---front-end---v1` (Task 5), `HRMS_TrayApp` (Tasks 6–7). Each has its own git repo/branch. Do NOT use git worktrees. Before starting each task, run `git status` and `git branch --show-current` fresh in that repo (the shared working tree can be modified by other sessions; the session-start git snapshot showed a detached `HEAD`). Create/checkout a feature branch `feature/tray-release-management` in each repo before the first commit in it.
2. **The one-time code flow is unchanged.** 8-char code, 10-minute expiry, single use, hashed, 3/hour rate limit already exist. This plan only adds the *download* step to the web gate.
3. **Admin page registers metadata; it does not upload the binary.** The installer file gets to storage via CI (Task 6) or manually through the R2 dashboard; the admin form takes a URL + SHA-256 + size. In-browser presigned upload is deferred (YAGNI).
4. **CI-registered builds start `is_active=false`, channel `beta`.** Nothing reaches employees until an admin activates/promotes it in the admin page. Rollback = deactivate the row.
5. **"Latest" = highest `x.y.z` among `is_active` rows in a channel.** Versions are strictly numeric `x.y.z` (regex `^\d+\.\d+\.\d+$`); pre-release tags are expressed via `channel`, not a suffix.
6. **`mandatory` update** = the caller's version is lower than the *latest active* release's `min_supported_version`.
7. **Signing:** self-signed PFX is fine for test machines (each must trust it, see `HRMS_TrayApp/build-msix.ps1` header). A trusted code-signing certificate is required before real customers; the workflow reads the PFX from a GitHub secret so swapping certs needs no code change.
8. **Task 7 touches MAUI views I have not read.** It gives full code for the Shared/Service/TrayApp-service layers and states exactly which existing file to mirror for page registration.

## Global Constraints

- Backend is Clean Architecture/CQRS: Domain entity → Application (handler, DTOs, repo interface) → Infrastructure (EF config, repo, DI) → Api controller. Handlers return `Result<T>` (`ONEVO.Application.Common.Models`); controllers map `result.IsSuccess ? Ok(...) : Problem(result.Error, statusCode: result.StatusCode ?? 400)`.
- Platform-admin endpoints: `[Authorize(Policy = "AdminPolicy")]` + `[RequirePlatformPermission(PlatformPermissionCatalog.SystemConfigRead|SystemConfigManage)]`, routes under `admin/v1/...`.
- Tenant-host anonymous endpoints must carry `[AllowAnonymous]` (see `TenantEnforcementMiddleware` — without it, tenant-mode requests get 401).
- JSON for tray/web contracts is `snake_case` via `[JsonPropertyName]` (see `TrayInstallerResponseDto`, `ActivationCodeResponseDto`). Admin endpoints return camelCase (default) like service keys.
- Admin FE: Angular 21 zoneless, standalone components, signals, Tailwind v4, Jest, `withCredentials: true` on every HTTP call, every route string in `core/config/api-endpoints.ts`, no tokens in browser storage.
- Employee web FE: test runner is **Vitest** (`vi.fn()`, `vi.spyOn()`), not Jest.
- Tray: `net10.0-windows10.0.19041.0`, `global.json` pins SDK `10.0.300` with `rollForward: disable`. A build needs the running tray `.exe` stopped first.
- Commit messages end with: `Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>`
- Never commit secrets (PFX, R2 keys, ingest token). They live in GitHub Secrets / `.env`.
- Backend `.env` silently overrides `appsettings*.json` for shared keys; put `TrayReleases__IngestToken` in `.env` for local testing.

## File Structure

**HRMS-Backend-v1/src**
| File | Responsibility |
|---|---|
| `ONEVO.Domain/Features/DevPlatform/SystemConfig/TrayReleases/Entities/TrayAppRelease.cs` | Entity |
| `ONEVO.Application/Features/DevPlatform/SystemConfig/TrayReleases/Helpers/TrayReleaseVersion.cs` | Parse/compare `x.y.z`, mandatory-update rule |
| `ONEVO.Application/Features/DevPlatform/SystemConfig/TrayReleases/RepositoryInterfaces/ITrayAppReleaseRepository.cs` | Persistence contract |
| `ONEVO.Application/Features/DevPlatform/SystemConfig/TrayReleases/DTOs/TrayReleaseDtos.cs` | Admin DTO + public DTOs + request records |
| `ONEVO.Application/Features/DevPlatform/SystemConfig/TrayReleases/Commands/*` | Create, Update, Ingest handlers |
| `ONEVO.Application/Features/DevPlatform/SystemConfig/TrayReleases/Queries/*` | List, GetLatest, CheckForUpdate handlers |
| `ONEVO.Infrastructure/Persistence/Configurations/DevPlatform/SystemConfig/TrayAppReleaseConfiguration.cs` | EF mapping |
| `ONEVO.Infrastructure/Persistence/Repositories/DevPlatform/SystemConfig/EfTrayAppReleaseRepository.cs` | EF repo |
| `ONEVO.Api/Controllers/Admin/DevPlatform/SystemConfig/TrayReleasesController.cs` | Admin CRUD + CI ingest |
| `ONEVO.Api/Controllers/Tenant/Monitoring/TrayActivation/TrayReleasesPublicController.cs` | Anonymous latest/check |
| Delete | `TrayInstallerOptions.cs`, `TrayInstallerResponseDto.cs`, `TrayInstaller` block in `appsettings.json`, Program.cs lines 56–57 |

**HRMS-Platform-Administration-Front-End-v1/src/app/modules/tray-releases/** — `data/tray-release.model.ts`, `data/tray-releases.service.ts`, `feature/tray-releases-list/tray-releases-list.ts` (+ `.html`), `feature/tray-release-form-modal/tray-release-form-modal.ts`.

**Hrms--Web-application---front-end---v1/src/app/core/tray-presence/** — `models/tray-installer.model.ts`, `data-access/tray-installer-api.service.ts`, modify `feature/tray-activation-gate/tray-activation-gate.component.{ts,html,css}`.

**HRMS_TrayApp** — `.github/workflows/release.yml`; `ONEVO.Agent.Shared/IPC/IpcMessages.cs` (new payloads); `ONEVO.Agent.Service/Api/OnevoApiClient.cs` + `AgentApiRoutes.cs`; `ONEVO.Agent.Service/AgentWorker.cs`; `ONEVO.Agent.TrayApp/Services/{INamedPipeClient,NamedPipeClient,UpdateChecker}.cs`.

---

## Task 1: Backend persistence — entity, EF config, repository, migration; remove static config

**Files:**
- Create: `HRMS-Backend-v1/src/ONEVO.Domain/Features/DevPlatform/SystemConfig/TrayReleases/Entities/TrayAppRelease.cs`
- Create: `HRMS-Backend-v1/src/ONEVO.Application/Features/DevPlatform/SystemConfig/TrayReleases/Helpers/TrayReleaseVersion.cs`
- Create: `HRMS-Backend-v1/src/ONEVO.Application/Features/DevPlatform/SystemConfig/TrayReleases/RepositoryInterfaces/ITrayAppReleaseRepository.cs`
- Create: `HRMS-Backend-v1/src/ONEVO.Infrastructure/Persistence/Configurations/DevPlatform/SystemConfig/TrayAppReleaseConfiguration.cs`
- Create: `HRMS-Backend-v1/src/ONEVO.Infrastructure/Persistence/Repositories/DevPlatform/SystemConfig/EfTrayAppReleaseRepository.cs`
- Modify: `HRMS-Backend-v1/src/ONEVO.Infrastructure/Persistence/ApplicationDbContext.cs` (add DbSet next to line 206, `PlatformServiceKeys`)
- Modify: `HRMS-Backend-v1/src/ONEVO.Infrastructure/DependencyInjection.cs` (register next to line 434)
- Modify: `HRMS-Backend-v1/src/ONEVO.Api/appsettings.json` (delete `TrayInstaller` block, lines 25–31), `HRMS-Backend-v1/src/ONEVO.Api/Program.cs` (delete lines 56–57)
- Delete: `.../TrayActivation/Options/TrayInstallerOptions.cs`, `.../TrayActivation/DTOs/Responses/TrayInstallerResponseDto.cs`
- Create (generated): migration `AddTrayAppReleases`
- Test: `HRMS-Backend-v1/tests/ONEVO.Tests.Unit/Features/DevPlatform/SystemConfig/TrayReleases/TrayReleaseVersionTests.cs`, `.../EfTrayAppReleaseRepositoryTests.cs`

**Interfaces:**
- Produces:
  - `TrayAppRelease` (properties below)
  - `static class TrayReleaseVersion { bool TryParse(string? value, out Version parsed); int Compare(string a, string b); bool IsMandatory(string currentVersion, string? minSupportedVersion); }`
  - `ITrayAppReleaseRepository { Task<IReadOnlyList<TrayAppRelease>> ListAllAsync(CancellationToken); Task<TrayAppRelease?> GetByIdAsync(Guid, CancellationToken); Task<TrayAppRelease?> GetByChannelAndVersionAsync(string channel, string version, CancellationToken); Task<TrayAppRelease?> GetLatestActiveAsync(string channel, CancellationToken); Task AddAsync(TrayAppRelease, CancellationToken); Task SaveChangesAsync(CancellationToken); }`
  - `ApplicationDbContext.TrayAppReleases`

- [ ] **Step 1: Confirm the old config really has no consumers**

Run (from `HRMS-Backend-v1`): `grep -rn "TrayInstaller" src tests --include=*.cs | grep -v "/Migrations/" | grep -v "/bin/\|/obj/"`
Expected: only the two type definitions and `Program.cs:56-57`. If anything else appears, keep that consumer working by pointing it at the repository instead, and note it in the commit.

- [ ] **Step 2: Write the failing version tests**

`tests/ONEVO.Tests.Unit/Features/DevPlatform/SystemConfig/TrayReleases/TrayReleaseVersionTests.cs`:

```csharp
using ONEVO.Application.Features.DevPlatform.SystemConfig.TrayReleases.Helpers;

namespace ONEVO.Tests.Unit.Features.DevPlatform.SystemConfig.TrayReleases;

public sealed class TrayReleaseVersionTests
{
    [Theory]
    [InlineData("1.2.3", true)]
    [InlineData("10.0.0", true)]
    [InlineData("1.2", false)]
    [InlineData("1.2.3.4", false)]
    [InlineData("1.2.3-beta", false)]
    [InlineData("v1.2.3", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void TryParse_AcceptsOnlyThreePartNumericVersions(string? input, bool expected)
    {
        Assert.Equal(expected, TrayReleaseVersion.TryParse(input, out _));
    }

    [Theory]
    [InlineData("1.10.0", "1.9.0", 1)]   // numeric, not lexicographic
    [InlineData("1.0.0", "1.0.0", 0)]
    [InlineData("0.9.9", "1.0.0", -1)]
    public void Compare_OrdersNumerically(string a, string b, int expectedSign)
    {
        Assert.Equal(expectedSign, Math.Sign(TrayReleaseVersion.Compare(a, b)));
    }

    [Theory]
    [InlineData("1.0.0", "1.1.0", true)]    // current below minimum
    [InlineData("1.1.0", "1.1.0", false)]   // equal is fine
    [InlineData("2.0.0", "1.1.0", false)]
    [InlineData("1.0.0", null, false)]      // no minimum set
    [InlineData("1.0.0", "", false)]
    public void IsMandatory_TrueOnlyWhenCurrentIsBelowMinimum(string current, string? min, bool expected)
    {
        Assert.Equal(expected, TrayReleaseVersion.IsMandatory(current, min));
    }

    [Fact]
    public void IsMandatory_FalseWhenCurrentVersionIsUnparseable()
    {
        // A tray reporting garbage must not be hard-blocked.
        Assert.False(TrayReleaseVersion.IsMandatory("garbage", "1.0.0"));
    }
}
```

- [ ] **Step 3: Run to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~TrayReleaseVersionTests"`
Expected: build FAIL — `TrayReleaseVersion` does not exist.

- [ ] **Step 4: Create the entity**

`.../TrayReleases/Entities/TrayAppRelease.cs`:

```csharp
namespace ONEVO.Domain.Features.DevPlatform.SystemConfig.TrayReleases.Entities;

/// <summary>
/// One published build of the ONEVO tray installer (.msix). Platform-level: no tenant, no RLS.
/// Replaces the static "TrayInstaller" appsettings block. Canonical table: tray_app_releases.
/// "Latest" for a channel is the highest x.y.z among is_active rows in that channel.
/// </summary>
public class TrayAppRelease
{
    public Guid Id { get; set; }

    /// <summary>Strict x.y.z (regex ^\d+\.\d+\.\d+$). Unique per channel.</summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>"stable" or "beta".</summary>
    public string Channel { get; set; } = string.Empty;

    public string DownloadUrl { get; set; } = string.Empty;

    /// <summary>Lowercase hex SHA-256 of the installer file (64 chars).</summary>
    public string Sha256 { get; set; } = string.Empty;

    public long FileSizeBytes { get; set; }

    /// <summary>Certificate subject shown to the user, e.g. "CN=ONEVO".</summary>
    public string Publisher { get; set; } = string.Empty;

    /// <summary>e.g. "10.0.19041.0".</summary>
    public string MinimumWindowsVersion { get; set; } = string.Empty;

    /// <summary>Trays older than this must update before continuing. Null = no forced update.</summary>
    public string? MinSupportedVersion { get; set; }

    public string? ReleaseNotes { get; set; }

    /// <summary>Only active rows are ever served to employees or trays.</summary>
    public bool IsActive { get; set; }

    /// <summary>"admin" (created in the admin app) or "ci" (registered by the release pipeline).</summary>
    public string Source { get; set; } = "admin";

    /// <summary>Platform user who created the row; null when Source = "ci".</summary>
    public Guid? CreatedById { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
```

- [ ] **Step 5: Create the version helper**

`.../TrayReleases/Helpers/TrayReleaseVersion.cs`:

```csharp
using System.Text.RegularExpressions;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.TrayReleases.Helpers;

public static class TrayReleaseVersion
{
    private static readonly Regex ThreePart = new(@"^\d+\.\d+\.\d+$", RegexOptions.Compiled);

    public static bool TryParse(string? value, out Version parsed)
    {
        parsed = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(value) || !ThreePart.IsMatch(value))
            return false;
        return Version.TryParse(value, out parsed!);
    }

    /// <summary>Numeric comparison. Both arguments must already be valid x.y.z.</summary>
    public static int Compare(string a, string b)
    {
        TryParse(a, out var va);
        TryParse(b, out var vb);
        return va.CompareTo(vb);
    }

    /// <summary>
    /// True when the caller must update: current is a valid version strictly below a valid minimum.
    /// Unparseable input never forces an update.
    /// </summary>
    public static bool IsMandatory(string currentVersion, string? minSupportedVersion)
    {
        if (!TryParse(currentVersion, out var current)) return false;
        if (!TryParse(minSupportedVersion, out var min)) return false;
        return current < min;
    }
}
```

- [ ] **Step 6: Run version tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~TrayReleaseVersionTests"`
Expected: PASS (all).

- [ ] **Step 7: Create the repository interface**

`.../TrayReleases/RepositoryInterfaces/ITrayAppReleaseRepository.cs`:

```csharp
using ONEVO.Domain.Features.DevPlatform.SystemConfig.TrayReleases.Entities;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.TrayReleases.RepositoryInterfaces;

public interface ITrayAppReleaseRepository
{
    Task<IReadOnlyList<TrayAppRelease>> ListAllAsync(CancellationToken ct);
    /// <summary>Tracked entity (for updates).</summary>
    Task<TrayAppRelease?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<TrayAppRelease?> GetByChannelAndVersionAsync(string channel, string version, CancellationToken ct);
    /// <summary>Highest x.y.z among active rows in the channel; null when none.</summary>
    Task<TrayAppRelease?> GetLatestActiveAsync(string channel, CancellationToken ct);
    /// <summary>Adds only; does not save.</summary>
    Task AddAsync(TrayAppRelease release, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}
```

- [ ] **Step 8: Add DbSet, EF configuration**

In `ApplicationDbContext.cs`, add `using ONEVO.Domain.Features.DevPlatform.SystemConfig.TrayReleases.Entities;` and after the `PlatformServiceKeys` DbSet:

```csharp
    public DbSet<TrayAppRelease> TrayAppReleases => Set<TrayAppRelease>();
```

`.../Configurations/DevPlatform/SystemConfig/TrayAppReleaseConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.DevPlatform.SystemConfig.TrayReleases.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.DevPlatform.SystemConfig;

/// <summary>EF configuration for tray_app_releases (platform-level, no tenant, no RLS).</summary>
public class TrayAppReleaseConfiguration : IEntityTypeConfiguration<TrayAppRelease>
{
    public void Configure(EntityTypeBuilder<TrayAppRelease> builder)
    {
        builder.ToTable("tray_app_releases");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Version).HasMaxLength(32).IsRequired();
        builder.Property(r => r.Channel).HasMaxLength(16).IsRequired();
        builder.Property(r => r.DownloadUrl).HasMaxLength(2048).IsRequired();
        builder.Property(r => r.Sha256).HasMaxLength(64).IsRequired();
        builder.Property(r => r.FileSizeBytes).IsRequired();
        builder.Property(r => r.Publisher).HasMaxLength(200).IsRequired();
        builder.Property(r => r.MinimumWindowsVersion).HasMaxLength(32).IsRequired();
        builder.Property(r => r.MinSupportedVersion).HasMaxLength(32);
        builder.Property(r => r.ReleaseNotes).HasColumnType("text");
        builder.Property(r => r.IsActive).IsRequired();
        builder.Property(r => r.Source).HasMaxLength(16).IsRequired();
        builder.Property(r => r.CreatedById);
        builder.Property(r => r.CreatedAt).IsRequired();
        builder.Property(r => r.UpdatedAt).IsRequired();

        builder.HasIndex(r => new { r.Channel, r.Version }).IsUnique();
        builder.HasIndex(r => new { r.Channel, r.IsActive });
    }
}
```

Check how existing configurations get discovered: `grep -n "ApplyConfigurationsFromAssembly" src/ONEVO.Infrastructure/Persistence/ApplicationDbContext.cs`. If it is used, nothing else to register; if configurations are registered one by one, add `builder.ApplyConfiguration(new TrayAppReleaseConfiguration());` beside `PlatformServiceKeyConfiguration`.

- [ ] **Step 9: Write the failing repository tests**

`tests/ONEVO.Tests.Unit/Features/DevPlatform/SystemConfig/TrayReleases/EfTrayAppReleaseRepositoryTests.cs`. Copy the private `BuildInMemoryDb(...)` and `SaveChangesObserver` helpers verbatim from `tests/ONEVO.Tests.Unit/Features/DevPlatform/SystemConfig/EfPlatformServiceKeyRepositoryTests.cs` (they configure the InMemory provider plus the interceptors the real context needs). Then:

```csharp
using Microsoft.EntityFrameworkCore;
using ONEVO.Domain.Features.DevPlatform.SystemConfig.TrayReleases.Entities;
using ONEVO.Infrastructure.Persistence.Repositories.DevPlatform.SystemConfig;

namespace ONEVO.Tests.Unit.Features.DevPlatform.SystemConfig.TrayReleases;

public sealed class EfTrayAppReleaseRepositoryTests
{
    // >>> paste BuildInMemoryDb / SaveChangesObserver helpers here <<<

    private static TrayAppRelease Make(string version, string channel = "stable", bool active = true) => new()
    {
        Id = Guid.NewGuid(),
        Version = version,
        Channel = channel,
        DownloadUrl = $"https://dl.example.com/onevo-{version}.msix",
        Sha256 = new string('a', 64),
        FileSizeBytes = 1234,
        Publisher = "CN=ONEVO",
        MinimumWindowsVersion = "10.0.19041.0",
        IsActive = active,
        Source = "admin"
    };

    [Fact]
    public async Task GetLatestActive_ReturnsHighestNumericVersion_NotLexicographic()
    {
        await using var db = BuildInMemoryDb();
        db.TrayAppReleases.AddRange(Make("1.9.0"), Make("1.10.0"), Make("1.2.0"));
        await db.SaveChangesAsync();

        var latest = await new EfTrayAppReleaseRepository(db)
            .GetLatestActiveAsync("stable", CancellationToken.None);

        Assert.Equal("1.10.0", latest!.Version);
    }

    [Fact]
    public async Task GetLatestActive_IgnoresInactiveAndOtherChannels()
    {
        await using var db = BuildInMemoryDb();
        db.TrayAppReleases.AddRange(
            Make("2.0.0", active: false),
            Make("3.0.0", channel: "beta"),
            Make("1.0.0"));
        await db.SaveChangesAsync();

        var latest = await new EfTrayAppReleaseRepository(db)
            .GetLatestActiveAsync("stable", CancellationToken.None);

        Assert.Equal("1.0.0", latest!.Version);
    }

    [Fact]
    public async Task GetLatestActive_ReturnsNull_WhenNothingActive()
    {
        await using var db = BuildInMemoryDb();
        db.TrayAppReleases.Add(Make("1.0.0", active: false));
        await db.SaveChangesAsync();

        Assert.Null(await new EfTrayAppReleaseRepository(db)
            .GetLatestActiveAsync("stable", CancellationToken.None));
    }

    [Fact]
    public async Task GetByChannelAndVersion_FindsExactMatch()
    {
        await using var db = BuildInMemoryDb();
        db.TrayAppReleases.AddRange(Make("1.0.0"), Make("1.0.0", channel: "beta"));
        await db.SaveChangesAsync();

        var hit = await new EfTrayAppReleaseRepository(db)
            .GetByChannelAndVersionAsync("beta", "1.0.0", CancellationToken.None);

        Assert.Equal("beta", hit!.Channel);
    }

    [Fact]
    public async Task AddAsync_DoesNotSaveAutomatically()
    {
        await using var db = BuildInMemoryDb();
        var repo = new EfTrayAppReleaseRepository(db);
        var release = Make("1.0.0");

        await repo.AddAsync(release, CancellationToken.None);

        Assert.Equal(EntityState.Added, db.Entry(release).State);
        Assert.Equal(0, await db.TrayAppReleases.CountAsync());
    }
}
```

- [ ] **Step 10: Run to verify failure, then implement the repository**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~EfTrayAppReleaseRepositoryTests"`
Expected: build FAIL — `EfTrayAppReleaseRepository` missing.

`.../Repositories/DevPlatform/SystemConfig/EfTrayAppReleaseRepository.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.DevPlatform.SystemConfig.TrayReleases.Helpers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.TrayReleases.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.SystemConfig.TrayReleases.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.DevPlatform.SystemConfig;

public sealed class EfTrayAppReleaseRepository : ITrayAppReleaseRepository
{
    private readonly ApplicationDbContext _db;

    public EfTrayAppReleaseRepository(ApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<TrayAppRelease>> ListAllAsync(CancellationToken ct)
    {
        var rows = await _db.TrayAppReleases.AsNoTracking().ToListAsync(ct);
        // Version ordering must be numeric; do it in memory (the table is tiny).
        return rows
            .OrderByDescending(r => TrayReleaseVersion.TryParse(r.Version, out var v) ? v : new Version(0, 0, 0))
            .ThenBy(r => r.Channel)
            .ToList();
    }

    public Task<TrayAppRelease?> GetByIdAsync(Guid id, CancellationToken ct) =>
        _db.TrayAppReleases.FirstOrDefaultAsync(r => r.Id == id, ct);

    public Task<TrayAppRelease?> GetByChannelAndVersionAsync(string channel, string version, CancellationToken ct) =>
        _db.TrayAppReleases.FirstOrDefaultAsync(r => r.Channel == channel && r.Version == version, ct);

    public async Task<TrayAppRelease?> GetLatestActiveAsync(string channel, CancellationToken ct)
    {
        var active = await _db.TrayAppReleases.AsNoTracking()
            .Where(r => r.Channel == channel && r.IsActive)
            .ToListAsync(ct);

        return active
            .Where(r => TrayReleaseVersion.TryParse(r.Version, out _))
            .OrderByDescending(r => { TrayReleaseVersion.TryParse(r.Version, out var v); return v; })
            .FirstOrDefault();
    }

    public async Task AddAsync(TrayAppRelease release, CancellationToken ct) =>
        await _db.TrayAppReleases.AddAsync(release, ct);

    public Task SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);
}
```

Register in `DependencyInjection.cs` next to line 434 (add the two `using` lines at the top with the other PlatformServiceKeys usings):

```csharp
        services.AddScoped<ITrayAppReleaseRepository, EfTrayAppReleaseRepository>();
```

- [ ] **Step 11: Run repository tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~TrayReleases"`
Expected: PASS (version + repository tests).

- [ ] **Step 12: Remove the static config**

- Delete `TrayInstallerOptions.cs` and `TrayInstallerResponseDto.cs`.
- Remove the `"TrayInstaller": { ... },` block from `src/ONEVO.Api/appsettings.json`.
- Remove the `builder.Services.Configure<...TrayInstallerOptions>(builder.Configuration.GetSection("TrayInstaller"));` statement at `Program.cs:56-57`.

Run: `dotnet build src/ONEVO.Api/ONEVO.Api.csproj`
Expected: build succeeds, 0 errors.

- [ ] **Step 13: Generate the migration**

The known-broken pre-existing migration noted in project memory may block `database update`; generating the migration file is still safe.

Run (from `HRMS-Backend-v1`):
```bash
dotnet ef migrations add AddTrayAppReleases --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api
```
Expected: new `*_AddTrayAppReleases.cs` + Designer + updated `ApplicationDbContextModelSnapshot.cs`. Open the `Up()` and confirm it ONLY creates `tray_app_releases` with the unique index `(channel, version)`. If it contains unrelated operations (pending model drift from other branches), delete the migration (`dotnet ef migrations remove ...`), do not ship unrelated schema, and report the drift.

- [ ] **Step 14: Run the RLS/compliance architecture tests**

Run: `dotnet test tests/ONEVO.Tests.Architecture/ONEVO.Tests.Architecture.csproj` and `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~SystemConfigCompliance"`
Expected: PASS. If a test enforces "every table has tenant_id/RLS or is on an allowlist", add `tray_app_releases` to that allowlist (it is platform-level by design) with a comment, then re-run.

- [ ] **Step 15: Commit**

```bash
git add src tests
git commit -m "feat(tray-releases): add tray_app_releases table, repository; drop static TrayInstaller config

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

## Task 2: Backend application layer — DTOs and admin/ingest/public handlers

**Files:**
- Create: `.../TrayReleases/DTOs/TrayReleaseDtos.cs`
- Create: `.../TrayReleases/Mappers/TrayReleaseMapper.cs`
- Create: `.../TrayReleases/Commands/CreateTrayRelease/CreateTrayReleaseCommandHandler.cs`
- Create: `.../TrayReleases/Commands/UpdateTrayRelease/UpdateTrayReleaseCommandHandler.cs`
- Create: `.../TrayReleases/Queries/ListTrayReleases/ListTrayReleasesQueryHandler.cs`
- Create: `.../TrayReleases/Queries/GetLatestTrayRelease/GetLatestTrayReleaseQueryHandler.cs`
- Create: `.../TrayReleases/Queries/CheckTrayUpdate/CheckTrayUpdateQueryHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/DevPlatform/SystemConfig/TrayReleases/TrayReleaseHandlersTests.cs`

(All paths under `HRMS-Backend-v1/src/ONEVO.Application/Features/DevPlatform/SystemConfig/TrayReleases/` unless starting with `tests`.)

**Interfaces:**
- Consumes: `ITrayAppReleaseRepository`, `TrayReleaseVersion`, `TrayAppRelease` (Task 1); `Result<T>` with `Success/Failure/NotFound/Conflict`.
- Produces:
  - `TrayReleaseDto` (admin, camelCase): `Guid Id, string Version, string Channel, string DownloadUrl, string Sha256, long FileSizeBytes, string Publisher, string MinimumWindowsVersion, string? MinSupportedVersion, string? ReleaseNotes, bool IsActive, string Source, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt`
  - `TrayInstallerInfoDto` (public, snake_case JSON): `version, download_url, sha256, file_size_bytes, publisher, minimum_windows_version, release_notes, channel`
  - `TrayUpdateCheckDto` (public, snake_case JSON): `update_available, mandatory, latest` (`TrayInstallerInfoDto?`)
  - `CreateTrayReleaseRequest(string Version, string Channel, string DownloadUrl, string Sha256, long FileSizeBytes, string Publisher, string MinimumWindowsVersion, string? MinSupportedVersion, string? ReleaseNotes, bool IsActive)`
  - `UpdateTrayReleaseRequest(string? Channel, string? MinSupportedVersion, string? ReleaseNotes, bool? IsActive)`
  - `CreateTrayReleaseCommand(CreateTrayReleaseRequest Request, string Source, Guid? ActorPlatformUserId) : IRequest<Result<TrayReleaseDto>>`
  - `UpdateTrayReleaseCommand(Guid Id, UpdateTrayReleaseRequest Request) : IRequest<Result<TrayReleaseDto>>`
  - `ListTrayReleasesQuery() : IRequest<Result<IReadOnlyList<TrayReleaseDto>>>`
  - `GetLatestTrayReleaseQuery(string Channel) : IRequest<Result<TrayInstallerInfoDto>>`
  - `CheckTrayUpdateQuery(string CurrentVersion, string Channel) : IRequest<Result<TrayUpdateCheckDto>>`
  - `TrayReleaseChannels.IsValid(string)` (`"stable"`, `"beta"`)

- [ ] **Step 1: Write the failing handler tests**

`tests/.../TrayReleases/TrayReleaseHandlersTests.cs` (uses Moq, already used by the unit-test project):

```csharp
using Moq;
using ONEVO.Application.Features.DevPlatform.SystemConfig.TrayReleases.Commands.CreateTrayRelease;
using ONEVO.Application.Features.DevPlatform.SystemConfig.TrayReleases.Commands.UpdateTrayRelease;
using ONEVO.Application.Features.DevPlatform.SystemConfig.TrayReleases.DTOs;
using ONEVO.Application.Features.DevPlatform.SystemConfig.TrayReleases.Queries.CheckTrayUpdate;
using ONEVO.Application.Features.DevPlatform.SystemConfig.TrayReleases.Queries.GetLatestTrayRelease;
using ONEVO.Application.Features.DevPlatform.SystemConfig.TrayReleases.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.SystemConfig.TrayReleases.Entities;

namespace ONEVO.Tests.Unit.Features.DevPlatform.SystemConfig.TrayReleases;

public sealed class TrayReleaseHandlersTests
{
    private static CreateTrayReleaseRequest ValidRequest(string version = "1.2.0") => new(
        Version: version, Channel: "beta",
        DownloadUrl: "https://dl.example.com/onevo.msix",
        Sha256: new string('a', 64), FileSizeBytes: 100,
        Publisher: "CN=ONEVO", MinimumWindowsVersion: "10.0.19041.0",
        MinSupportedVersion: null, ReleaseNotes: "notes", IsActive: false);

    private static TrayAppRelease Row(string version, string? min = null, bool active = true) => new()
    {
        Id = Guid.NewGuid(), Version = version, Channel = "stable",
        DownloadUrl = "https://dl.example.com/x.msix", Sha256 = new string('b', 64),
        FileSizeBytes = 5, Publisher = "CN=ONEVO", MinimumWindowsVersion = "10.0.19041.0",
        MinSupportedVersion = min, IsActive = active, Source = "admin"
    };

    // ---- Create ----

    [Fact]
    public async Task Create_Succeeds_AndSaves()
    {
        var repo = new Mock<ITrayAppReleaseRepository>();
        repo.Setup(r => r.GetByChannelAndVersionAsync("beta", "1.2.0", It.IsAny<CancellationToken>()))
            .ReturnsAsync((TrayAppRelease?)null);
        var handler = new CreateTrayReleaseCommandHandler(repo.Object);

        var result = await handler.Handle(
            new CreateTrayReleaseCommand(ValidRequest(), "admin", Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsSuccess);
        repo.Verify(r => r.AddAsync(It.Is<TrayAppRelease>(x => x.Version == "1.2.0" && x.Source == "admin"), It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("1.2")]
    [InlineData("v1.2.0")]
    public async Task Create_RejectsBadVersion(string version)
    {
        var handler = new CreateTrayReleaseCommandHandler(new Mock<ITrayAppReleaseRepository>().Object);
        var result = await handler.Handle(
            new CreateTrayReleaseCommand(ValidRequest(version), "admin", null), CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task Create_RejectsBadChannel_BadSha_AndNonHttpsUrl()
    {
        var handler = new CreateTrayReleaseCommandHandler(new Mock<ITrayAppReleaseRepository>().Object);

        var badChannel = ValidRequest() with { Channel = "nightly" };
        var badSha = ValidRequest() with { Sha256 = "xyz" };
        var badUrl = ValidRequest() with { DownloadUrl = "http://insecure.example.com/a.msix" };

        foreach (var req in new[] { badChannel, badSha, badUrl })
        {
            var r = await handler.Handle(new CreateTrayReleaseCommand(req, "admin", null), CancellationToken.None);
            Assert.Equal(400, r.StatusCode);
        }
    }

    [Fact]
    public async Task Create_ReturnsConflict_WhenChannelVersionExists()
    {
        var repo = new Mock<ITrayAppReleaseRepository>();
        repo.Setup(r => r.GetByChannelAndVersionAsync("beta", "1.2.0", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Row("1.2.0"));
        var handler = new CreateTrayReleaseCommandHandler(repo.Object);

        var result = await handler.Handle(
            new CreateTrayReleaseCommand(ValidRequest(), "ci", null), CancellationToken.None);

        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task Create_NormalisesSha256ToLowercase()
    {
        var repo = new Mock<ITrayAppReleaseRepository>();
        TrayAppRelease? saved = null;
        repo.Setup(r => r.AddAsync(It.IsAny<TrayAppRelease>(), It.IsAny<CancellationToken>()))
            .Callback<TrayAppRelease, CancellationToken>((r, _) => saved = r)
            .Returns(Task.CompletedTask);
        var handler = new CreateTrayReleaseCommandHandler(repo.Object);

        await handler.Handle(new CreateTrayReleaseCommand(
            ValidRequest() with { Sha256 = new string('A', 64) }, "admin", null), CancellationToken.None);

        Assert.Equal(new string('a', 64), saved!.Sha256);
    }

    // ---- Update ----

    [Fact]
    public async Task Update_TogglesActive_AndSaves()
    {
        var row = Row("1.0.0", active: false);
        var repo = new Mock<ITrayAppReleaseRepository>();
        repo.Setup(r => r.GetByIdAsync(row.Id, It.IsAny<CancellationToken>())).ReturnsAsync(row);
        var handler = new UpdateTrayReleaseCommandHandler(repo.Object);

        var result = await handler.Handle(
            new UpdateTrayReleaseCommand(row.Id, new UpdateTrayReleaseRequest(null, null, null, true)),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(row.IsActive);
        repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Update_NotFound()
    {
        var repo = new Mock<ITrayAppReleaseRepository>();
        repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TrayAppRelease?)null);
        var result = await new UpdateTrayReleaseCommandHandler(repo.Object).Handle(
            new UpdateTrayReleaseCommand(Guid.NewGuid(), new UpdateTrayReleaseRequest(null, null, null, true)),
            CancellationToken.None);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Update_Promote_ReturnsConflict_WhenTargetChannelAlreadyHasThatVersion()
    {
        var beta = Row("1.0.0"); beta.Channel = "beta";
        var repo = new Mock<ITrayAppReleaseRepository>();
        repo.Setup(r => r.GetByIdAsync(beta.Id, It.IsAny<CancellationToken>())).ReturnsAsync(beta);
        repo.Setup(r => r.GetByChannelAndVersionAsync("stable", "1.0.0", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Row("1.0.0"));

        var result = await new UpdateTrayReleaseCommandHandler(repo.Object).Handle(
            new UpdateTrayReleaseCommand(beta.Id, new UpdateTrayReleaseRequest("stable", null, null, null)),
            CancellationToken.None);

        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task Update_RejectsInvalidMinSupportedVersion()
    {
        var row = Row("1.0.0");
        var repo = new Mock<ITrayAppReleaseRepository>();
        repo.Setup(r => r.GetByIdAsync(row.Id, It.IsAny<CancellationToken>())).ReturnsAsync(row);

        var result = await new UpdateTrayReleaseCommandHandler(repo.Object).Handle(
            new UpdateTrayReleaseCommand(row.Id, new UpdateTrayReleaseRequest(null, "1.x", null, null)),
            CancellationToken.None);

        Assert.Equal(400, result.StatusCode);
    }

    // ---- GetLatest ----

    [Fact]
    public async Task GetLatest_ReturnsSnakeCaseInfo_ForActiveRelease()
    {
        var repo = new Mock<ITrayAppReleaseRepository>();
        repo.Setup(r => r.GetLatestActiveAsync("stable", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Row("1.4.0"));

        var result = await new GetLatestTrayReleaseQueryHandler(repo.Object)
            .Handle(new GetLatestTrayReleaseQuery("stable"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("1.4.0", result.Value!.Version);
    }

    [Fact]
    public async Task GetLatest_NotFound_WhenNoActiveRelease()
    {
        var repo = new Mock<ITrayAppReleaseRepository>();
        repo.Setup(r => r.GetLatestActiveAsync("stable", It.IsAny<CancellationToken>()))
            .ReturnsAsync((TrayAppRelease?)null);

        var result = await new GetLatestTrayReleaseQueryHandler(repo.Object)
            .Handle(new GetLatestTrayReleaseQuery("stable"), CancellationToken.None);

        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task GetLatest_RejectsUnknownChannel()
    {
        var result = await new GetLatestTrayReleaseQueryHandler(new Mock<ITrayAppReleaseRepository>().Object)
            .Handle(new GetLatestTrayReleaseQuery("nightly"), CancellationToken.None);
        Assert.Equal(400, result.StatusCode);
    }

    // ---- CheckUpdate ----

    [Theory]
    [InlineData("1.0.0", "1.2.0", null,    true,  false)] // newer exists, not forced
    [InlineData("1.2.0", "1.2.0", null,    false, false)] // already latest
    [InlineData("2.0.0", "1.2.0", null,    false, false)] // ahead of latest (dev build)
    [InlineData("1.0.0", "1.2.0", "1.1.0", true,  true )] // below min supported => mandatory
    [InlineData("1.1.0", "1.2.0", "1.1.0", true,  false)] // at min => optional
    public async Task Check_ComputesUpdateAvailableAndMandatory(
        string current, string latest, string? min, bool expectedAvailable, bool expectedMandatory)
    {
        var repo = new Mock<ITrayAppReleaseRepository>();
        repo.Setup(r => r.GetLatestActiveAsync("stable", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Row(latest, min));

        var result = await new CheckTrayUpdateQueryHandler(repo.Object)
            .Handle(new CheckTrayUpdateQuery(current, "stable"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(expectedAvailable, result.Value!.UpdateAvailable);
        Assert.Equal(expectedMandatory, result.Value.Mandatory);
        Assert.Equal(expectedAvailable, result.Value.Latest is not null);
    }

    [Fact]
    public async Task Check_NoActiveRelease_MeansNoUpdate_NotAnError()
    {
        var repo = new Mock<ITrayAppReleaseRepository>();
        repo.Setup(r => r.GetLatestActiveAsync("stable", It.IsAny<CancellationToken>()))
            .ReturnsAsync((TrayAppRelease?)null);

        var result = await new CheckTrayUpdateQueryHandler(repo.Object)
            .Handle(new CheckTrayUpdateQuery("1.0.0", "stable"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.UpdateAvailable);
    }

    [Fact]
    public async Task Check_RejectsUnparseableCurrentVersion()
    {
        var result = await new CheckTrayUpdateQueryHandler(new Mock<ITrayAppReleaseRepository>().Object)
            .Handle(new CheckTrayUpdateQuery("garbage", "stable"), CancellationToken.None);
        Assert.Equal(400, result.StatusCode);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~TrayReleaseHandlersTests"`
Expected: build FAIL — handler and DTO types missing.

- [ ] **Step 3: Create DTOs and mapper**

`.../TrayReleases/DTOs/TrayReleaseDtos.cs`:

```csharp
using System.Text.Json.Serialization;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.TrayReleases.DTOs;

public static class TrayReleaseChannels
{
    public const string Stable = "stable";
    public const string Beta = "beta";
    public static bool IsValid(string? channel) => channel is Stable or Beta;
}

public static class TrayReleaseSources
{
    public const string Admin = "admin";
    public const string Ci = "ci";
}

/// <summary>Admin-facing row (camelCase JSON).</summary>
public sealed record TrayReleaseDto(
    Guid Id, string Version, string Channel, string DownloadUrl, string Sha256,
    long FileSizeBytes, string Publisher, string MinimumWindowsVersion,
    string? MinSupportedVersion, string? ReleaseNotes, bool IsActive, string Source,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

/// <summary>Public installer metadata (snake_case JSON, same field names the old TrayInstallerResponseDto used).</summary>
public sealed record TrayInstallerInfoDto(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("download_url")] string DownloadUrl,
    [property: JsonPropertyName("sha256")] string Sha256,
    [property: JsonPropertyName("file_size_bytes")] long FileSizeBytes,
    [property: JsonPropertyName("publisher")] string Publisher,
    [property: JsonPropertyName("minimum_windows_version")] string MinimumWindowsVersion,
    [property: JsonPropertyName("release_notes")] string? ReleaseNotes,
    [property: JsonPropertyName("channel")] string Channel);

public sealed record TrayUpdateCheckDto(
    [property: JsonPropertyName("update_available")] bool UpdateAvailable,
    [property: JsonPropertyName("mandatory")] bool Mandatory,
    [property: JsonPropertyName("latest")] TrayInstallerInfoDto? Latest);

public sealed record CreateTrayReleaseRequest(
    string Version, string Channel, string DownloadUrl, string Sha256, long FileSizeBytes,
    string Publisher, string MinimumWindowsVersion, string? MinSupportedVersion,
    string? ReleaseNotes, bool IsActive);

public sealed record UpdateTrayReleaseRequest(
    string? Channel, string? MinSupportedVersion, string? ReleaseNotes, bool? IsActive);
```

`.../TrayReleases/Mappers/TrayReleaseMapper.cs`:

```csharp
using ONEVO.Application.Features.DevPlatform.SystemConfig.TrayReleases.DTOs;
using ONEVO.Domain.Features.DevPlatform.SystemConfig.TrayReleases.Entities;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.TrayReleases.Mappers;

public static class TrayReleaseMapper
{
    public static TrayReleaseDto ToDto(TrayAppRelease r) => new(
        r.Id, r.Version, r.Channel, r.DownloadUrl, r.Sha256, r.FileSizeBytes, r.Publisher,
        r.MinimumWindowsVersion, r.MinSupportedVersion, r.ReleaseNotes, r.IsActive, r.Source,
        r.CreatedAt, r.UpdatedAt);

    public static TrayInstallerInfoDto ToInfo(TrayAppRelease r) => new(
        r.Version, r.DownloadUrl, r.Sha256, r.FileSizeBytes, r.Publisher,
        r.MinimumWindowsVersion, r.ReleaseNotes, r.Channel);
}
```

- [ ] **Step 4: Create the Create handler**

`.../Commands/CreateTrayRelease/CreateTrayReleaseCommandHandler.cs`:

```csharp
using System.Text.RegularExpressions;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.SystemConfig.TrayReleases.DTOs;
using ONEVO.Application.Features.DevPlatform.SystemConfig.TrayReleases.Helpers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.TrayReleases.Mappers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.TrayReleases.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.SystemConfig.TrayReleases.Entities;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.TrayReleases.Commands.CreateTrayRelease;

/// <summary>Creates a release row. Used by the admin form (Source "admin") and CI ingest (Source "ci").</summary>
public sealed record CreateTrayReleaseCommand(
    CreateTrayReleaseRequest Request,
    string Source,
    Guid? ActorPlatformUserId) : IRequest<Result<TrayReleaseDto>>;

public sealed class CreateTrayReleaseCommandHandler
    : IRequestHandler<CreateTrayReleaseCommand, Result<TrayReleaseDto>>
{
    private static readonly Regex Sha256Hex = new("^[0-9a-fA-F]{64}$", RegexOptions.Compiled);

    private readonly ITrayAppReleaseRepository _repo;

    public CreateTrayReleaseCommandHandler(ITrayAppReleaseRepository repo) => _repo = repo;

    public async Task<Result<TrayReleaseDto>> Handle(
        CreateTrayReleaseCommand command, CancellationToken cancellationToken)
    {
        var req = command.Request;

        if (!TrayReleaseVersion.TryParse(req.Version, out _))
            return Result<TrayReleaseDto>.Failure("version must be x.y.z (numbers only).", 400);
        if (!TrayReleaseChannels.IsValid(req.Channel))
            return Result<TrayReleaseDto>.Failure("channel must be 'stable' or 'beta'.", 400);
        if (!Uri.TryCreate(req.DownloadUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            return Result<TrayReleaseDto>.Failure("downloadUrl must be an absolute https URL.", 400);
        if (string.IsNullOrWhiteSpace(req.Sha256) || !Sha256Hex.IsMatch(req.Sha256))
            return Result<TrayReleaseDto>.Failure("sha256 must be 64 hex characters.", 400);
        if (req.FileSizeBytes <= 0)
            return Result<TrayReleaseDto>.Failure("fileSizeBytes must be greater than zero.", 400);
        if (string.IsNullOrWhiteSpace(req.Publisher) || req.Publisher.Length > 200)
            return Result<TrayReleaseDto>.Failure("publisher is required (max 200 characters).", 400);
        if (string.IsNullOrWhiteSpace(req.MinimumWindowsVersion))
            return Result<TrayReleaseDto>.Failure("minimumWindowsVersion is required.", 400);
        if (!string.IsNullOrWhiteSpace(req.MinSupportedVersion) &&
            !TrayReleaseVersion.TryParse(req.MinSupportedVersion, out _))
            return Result<TrayReleaseDto>.Failure("minSupportedVersion must be x.y.z (numbers only).", 400);

        var existing = await _repo.GetByChannelAndVersionAsync(req.Channel, req.Version, cancellationToken);
        if (existing is not null)
            return Result<TrayReleaseDto>.Conflict(
                $"Version {req.Version} already exists in channel '{req.Channel}'.");

        var now = DateTimeOffset.UtcNow;
        var entity = new TrayAppRelease
        {
            Id = Guid.NewGuid(),
            Version = req.Version,
            Channel = req.Channel,
            DownloadUrl = req.DownloadUrl,
            Sha256 = req.Sha256.ToLowerInvariant(),
            FileSizeBytes = req.FileSizeBytes,
            Publisher = req.Publisher.Trim(),
            MinimumWindowsVersion = req.MinimumWindowsVersion.Trim(),
            MinSupportedVersion = string.IsNullOrWhiteSpace(req.MinSupportedVersion) ? null : req.MinSupportedVersion,
            ReleaseNotes = string.IsNullOrWhiteSpace(req.ReleaseNotes) ? null : req.ReleaseNotes,
            IsActive = req.IsActive,
            Source = command.Source,
            CreatedById = command.ActorPlatformUserId,
            CreatedAt = now,
            UpdatedAt = now
        };

        await _repo.AddAsync(entity, cancellationToken);
        await _repo.SaveChangesAsync(cancellationToken);
        return Result<TrayReleaseDto>.Success(TrayReleaseMapper.ToDto(entity));
    }
}
```

`Result<T>.Conflict` is used by `CreatePlatformServiceKeyCommandHandler`, so it exists; confirm with `grep -n "Conflict" src/ONEVO.Application/Common/Models/Result.cs`.

- [ ] **Step 5: Create the Update handler**

`.../Commands/UpdateTrayRelease/UpdateTrayReleaseCommandHandler.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.SystemConfig.TrayReleases.DTOs;
using ONEVO.Application.Features.DevPlatform.SystemConfig.TrayReleases.Helpers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.TrayReleases.Mappers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.TrayReleases.RepositoryInterfaces;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.TrayReleases.Commands.UpdateTrayRelease;

/// <summary>
/// Partial update: change channel (promote beta→stable), min supported version, notes, or activate/deactivate.
/// Pass null to leave a field unchanged; pass "" for MinSupportedVersion/ReleaseNotes to clear.
/// </summary>
public sealed record UpdateTrayReleaseCommand(Guid Id, UpdateTrayReleaseRequest Request)
    : IRequest<Result<TrayReleaseDto>>;

public sealed class UpdateTrayReleaseCommandHandler
    : IRequestHandler<UpdateTrayReleaseCommand, Result<TrayReleaseDto>>
{
    private readonly ITrayAppReleaseRepository _repo;

    public UpdateTrayReleaseCommandHandler(ITrayAppReleaseRepository repo) => _repo = repo;

    public async Task<Result<TrayReleaseDto>> Handle(
        UpdateTrayReleaseCommand command, CancellationToken cancellationToken)
    {
        var entity = await _repo.GetByIdAsync(command.Id, cancellationToken);
        if (entity is null)
            return Result<TrayReleaseDto>.NotFound("Tray release was not found.");

        var req = command.Request;

        if (req.Channel is not null && req.Channel != entity.Channel)
        {
            if (!TrayReleaseChannels.IsValid(req.Channel))
                return Result<TrayReleaseDto>.Failure("channel must be 'stable' or 'beta'.", 400);

            var clash = await _repo.GetByChannelAndVersionAsync(req.Channel, entity.Version, cancellationToken);
            if (clash is not null)
                return Result<TrayReleaseDto>.Conflict(
                    $"Version {entity.Version} already exists in channel '{req.Channel}'.");

            entity.Channel = req.Channel;
        }

        if (req.MinSupportedVersion is not null)
        {
            if (req.MinSupportedVersion.Length == 0)
                entity.MinSupportedVersion = null;
            else if (!TrayReleaseVersion.TryParse(req.MinSupportedVersion, out _))
                return Result<TrayReleaseDto>.Failure("minSupportedVersion must be x.y.z (numbers only).", 400);
            else
                entity.MinSupportedVersion = req.MinSupportedVersion;
        }

        if (req.ReleaseNotes is not null)
            entity.ReleaseNotes = req.ReleaseNotes.Length == 0 ? null : req.ReleaseNotes;

        if (req.IsActive is bool active)
            entity.IsActive = active;

        entity.UpdatedAt = DateTimeOffset.UtcNow;
        await _repo.SaveChangesAsync(cancellationToken);
        return Result<TrayReleaseDto>.Success(TrayReleaseMapper.ToDto(entity));
    }
}
```

- [ ] **Step 6: Create the query handlers**

`.../Queries/ListTrayReleases/ListTrayReleasesQueryHandler.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.SystemConfig.TrayReleases.DTOs;
using ONEVO.Application.Features.DevPlatform.SystemConfig.TrayReleases.Mappers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.TrayReleases.RepositoryInterfaces;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.TrayReleases.Queries.ListTrayReleases;

public sealed record ListTrayReleasesQuery() : IRequest<Result<IReadOnlyList<TrayReleaseDto>>>;

public sealed class ListTrayReleasesQueryHandler
    : IRequestHandler<ListTrayReleasesQuery, Result<IReadOnlyList<TrayReleaseDto>>>
{
    private readonly ITrayAppReleaseRepository _repo;
    public ListTrayReleasesQueryHandler(ITrayAppReleaseRepository repo) => _repo = repo;

    public async Task<Result<IReadOnlyList<TrayReleaseDto>>> Handle(
        ListTrayReleasesQuery request, CancellationToken cancellationToken)
    {
        var rows = await _repo.ListAllAsync(cancellationToken);
        return Result<IReadOnlyList<TrayReleaseDto>>.Success(rows.Select(TrayReleaseMapper.ToDto).ToList());
    }
}
```

`.../Queries/GetLatestTrayRelease/GetLatestTrayReleaseQueryHandler.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.SystemConfig.TrayReleases.DTOs;
using ONEVO.Application.Features.DevPlatform.SystemConfig.TrayReleases.Mappers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.TrayReleases.RepositoryInterfaces;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.TrayReleases.Queries.GetLatestTrayRelease;

public sealed record GetLatestTrayReleaseQuery(string Channel) : IRequest<Result<TrayInstallerInfoDto>>;

public sealed class GetLatestTrayReleaseQueryHandler
    : IRequestHandler<GetLatestTrayReleaseQuery, Result<TrayInstallerInfoDto>>
{
    private readonly ITrayAppReleaseRepository _repo;
    public GetLatestTrayReleaseQueryHandler(ITrayAppReleaseRepository repo) => _repo = repo;

    public async Task<Result<TrayInstallerInfoDto>> Handle(
        GetLatestTrayReleaseQuery request, CancellationToken cancellationToken)
    {
        if (!TrayReleaseChannels.IsValid(request.Channel))
            return Result<TrayInstallerInfoDto>.Failure("channel must be 'stable' or 'beta'.", 400);

        var latest = await _repo.GetLatestActiveAsync(request.Channel, cancellationToken);
        return latest is null
            ? Result<TrayInstallerInfoDto>.NotFound("No tray installer has been published yet.")
            : Result<TrayInstallerInfoDto>.Success(TrayReleaseMapper.ToInfo(latest));
    }
}
```

`.../Queries/CheckTrayUpdate/CheckTrayUpdateQueryHandler.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.SystemConfig.TrayReleases.DTOs;
using ONEVO.Application.Features.DevPlatform.SystemConfig.TrayReleases.Helpers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.TrayReleases.Mappers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.TrayReleases.RepositoryInterfaces;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.TrayReleases.Queries.CheckTrayUpdate;

public sealed record CheckTrayUpdateQuery(string CurrentVersion, string Channel)
    : IRequest<Result<TrayUpdateCheckDto>>;

public sealed class CheckTrayUpdateQueryHandler
    : IRequestHandler<CheckTrayUpdateQuery, Result<TrayUpdateCheckDto>>
{
    private readonly ITrayAppReleaseRepository _repo;
    public CheckTrayUpdateQueryHandler(ITrayAppReleaseRepository repo) => _repo = repo;

    public async Task<Result<TrayUpdateCheckDto>> Handle(
        CheckTrayUpdateQuery request, CancellationToken cancellationToken)
    {
        if (!TrayReleaseChannels.IsValid(request.Channel))
            return Result<TrayUpdateCheckDto>.Failure("channel must be 'stable' or 'beta'.", 400);
        if (!TrayReleaseVersion.TryParse(request.CurrentVersion, out _))
            return Result<TrayUpdateCheckDto>.Failure("current must be x.y.z (numbers only).", 400);

        var latest = await _repo.GetLatestActiveAsync(request.Channel, cancellationToken);
        if (latest is null || TrayReleaseVersion.Compare(latest.Version, request.CurrentVersion) <= 0)
            return Result<TrayUpdateCheckDto>.Success(new TrayUpdateCheckDto(false, false, null));

        var mandatory = TrayReleaseVersion.IsMandatory(request.CurrentVersion, latest.MinSupportedVersion);
        return Result<TrayUpdateCheckDto>.Success(
            new TrayUpdateCheckDto(true, mandatory, TrayReleaseMapper.ToInfo(latest)));
    }
}
```

- [ ] **Step 7: Run handler tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~TrayReleases"`
Expected: PASS (all handler + Task 1 tests). If MediatR handlers are auto-registered by assembly scan, no extra DI is needed; verify with `grep -n "RegisterServicesFromAssembly" src/ONEVO.Application/*.cs src/ONEVO.Api/Program.cs`.

- [ ] **Step 8: Commit**

```bash
git add src tests
git commit -m "feat(tray-releases): add release CRUD/latest/check handlers

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

## Task 3: Backend API — admin controller, CI ingest, public endpoints

**Files:**
- Create: `HRMS-Backend-v1/src/ONEVO.Api/Controllers/Admin/DevPlatform/SystemConfig/TrayReleasesController.cs`
- Create: `HRMS-Backend-v1/src/ONEVO.Api/Controllers/Tenant/Monitoring/TrayActivation/TrayReleasesPublicController.cs`
- Create: `HRMS-Backend-v1/src/ONEVO.Api/Options/TrayReleasesOptions.cs` (if an `Options` folder does not exist in `ONEVO.Api`, put it in the same folder as `Program.cs`'s other option classes — run `grep -rn "class .*Options" src/ONEVO.Api --include=*.cs -l` and follow the existing location)
- Modify: `HRMS-Backend-v1/src/ONEVO.Api/Program.cs` (bind options), `HRMS-Backend-v1/src/ONEVO.Api/appsettings.json` (add empty `TrayReleases` block)
- Test: `HRMS-Backend-v1/tests/ONEVO.Tests.Unit/Api/TrayReleasesIngestTokenTests.cs`

**Interfaces:**
- Consumes: all Task 2 commands/queries; `ICurrentPlatformUserContext.UserId`; `PlatformPermissionCatalog.SystemConfigRead/Manage`.
- Produces (HTTP):
  - `GET  /admin/v1/tray-releases` → `TrayReleaseDto[]`
  - `POST /admin/v1/tray-releases` body `CreateTrayReleaseRequest` → 201 `TrayReleaseDto`
  - `PUT  /admin/v1/tray-releases/{id}` body `UpdateTrayReleaseRequest` → `TrayReleaseDto`
  - `POST /admin/v1/tray-releases/ingest` header `X-Release-Token` + body `CreateTrayReleaseRequest` → 201 (CI; forces `IsActive=false`, `Source="ci"`, channel forced to `beta` unless body says otherwise but never active)
  - `GET  /api/v1/tray/releases/latest?channel=stable` (anonymous) → `TrayInstallerInfoDto`
  - `GET  /api/v1/tray/releases/check?current=1.0.0&channel=stable` (anonymous) → `TrayUpdateCheckDto`
  - `TrayReleasesOptions { string IngestToken }`, `static class IngestTokenValidator { bool IsValid(string? configured, string? presented) }`

- [ ] **Step 1: Write the failing ingest-token test**

`tests/ONEVO.Tests.Unit/Api/TrayReleasesIngestTokenTests.cs` (adjust the namespace/folder to where other `ONEVO.Api`-level unit tests live; `grep -rl "using ONEVO.Api" tests/ONEVO.Tests.Unit | head -3`):

```csharp
using ONEVO.Api.Options;

namespace ONEVO.Tests.Unit.Api;

public sealed class TrayReleasesIngestTokenTests
{
    [Fact]
    public void Valid_WhenTokensMatch() =>
        Assert.True(IngestTokenValidator.IsValid("s3cret-token-value", "s3cret-token-value"));

    [Theory]
    [InlineData("s3cret-token-value", "wrong")]
    [InlineData("s3cret-token-value", "")]
    [InlineData("s3cret-token-value", null)]
    public void Invalid_WhenPresentedTokenWrongOrMissing(string configured, string? presented) =>
        Assert.False(IngestTokenValidator.IsValid(configured, presented));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Invalid_WhenNoTokenConfigured_EvenIfPresentedIsEmpty(string? configured) =>
        // Ingest is DISABLED unless a token is configured; an empty==empty match must not pass.
        Assert.False(IngestTokenValidator.IsValid(configured, configured ?? ""));
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~TrayReleasesIngestTokenTests"`
Expected: build FAIL — `IngestTokenValidator` missing.

- [ ] **Step 3: Create options + validator**

`src/ONEVO.Api/Options/TrayReleasesOptions.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;

namespace ONEVO.Api.Options;

public sealed class TrayReleasesOptions
{
    public const string SectionName = "TrayReleases";

    /// <summary>Shared secret the release pipeline sends in X-Release-Token. Empty = ingest disabled.</summary>
    public string IngestToken { get; set; } = string.Empty;
}

public static class IngestTokenValidator
{
    public static bool IsValid(string? configured, string? presented)
    {
        if (string.IsNullOrWhiteSpace(configured) || string.IsNullOrEmpty(presented))
            return false;

        var a = SHA256.HashData(Encoding.UTF8.GetBytes(configured));
        var b = SHA256.HashData(Encoding.UTF8.GetBytes(presented));
        return CryptographicOperations.FixedTimeEquals(a, b);
    }
}
```

In `appsettings.json`, where the `TrayInstaller` block used to be, add:

```json
  "TrayReleases": {
    "IngestToken": ""
  },
```

In `Program.cs` (near where the old `TrayInstaller` registration was):

```csharp
builder.Services.Configure<ONEVO.Api.Options.TrayReleasesOptions>(
    builder.Configuration.GetSection(ONEVO.Api.Options.TrayReleasesOptions.SectionName));
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~TrayReleasesIngestTokenTests"`
Expected: PASS.

- [ ] **Step 5: Create the admin controller**

`src/ONEVO.Api/Controllers/Admin/DevPlatform/SystemConfig/TrayReleasesController.cs`:

```csharp
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using ONEVO.Api.Filters;
using ONEVO.Api.Options;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Helpers;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.TrayReleases.Commands.CreateTrayRelease;
using ONEVO.Application.Features.DevPlatform.SystemConfig.TrayReleases.Commands.UpdateTrayRelease;
using ONEVO.Application.Features.DevPlatform.SystemConfig.TrayReleases.DTOs;
using ONEVO.Application.Features.DevPlatform.SystemConfig.TrayReleases.Queries.ListTrayReleases;

namespace ONEVO.Api.Controllers.Admin.DevPlatform.SystemConfig;

/// <summary>
/// Tray installer release registry (platform admin).
///   GET  /admin/v1/tray-releases          → list
///   POST /admin/v1/tray-releases          → create (admin)
///   PUT  /admin/v1/tray-releases/{id}     → update / promote / activate / deactivate
///   POST /admin/v1/tray-releases/ingest   → CI registration (X-Release-Token, always inactive)
/// </summary>
[ApiController]
public sealed class TrayReleasesController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ICurrentPlatformUserContext _currentUser;
    private readonly TrayReleasesOptions _options;

    public TrayReleasesController(
        IMediator mediator,
        ICurrentPlatformUserContext currentUser,
        IOptions<TrayReleasesOptions> options)
    {
        _mediator = mediator;
        _currentUser = currentUser;
        _options = options.Value;
    }

    [HttpGet("admin/v1/tray-releases")]
    [Authorize(Policy = "AdminPolicy")]
    [RequirePlatformPermission(PlatformPermissionCatalog.SystemConfigRead)]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var result = await _mediator.Send(new ListTrayReleasesQuery(), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("admin/v1/tray-releases")]
    [Authorize(Policy = "AdminPolicy")]
    [RequirePlatformPermission(PlatformPermissionCatalog.SystemConfigManage)]
    public async Task<IActionResult> Create([FromBody] CreateTrayReleaseRequest request, CancellationToken ct)
    {
        var actorId = _currentUser.UserId;
        if (actorId is null) return Forbid();

        var result = await _mediator.Send(
            new CreateTrayReleaseCommand(request, TrayReleaseSources.Admin, actorId), ct);
        return result.IsSuccess
            ? Created($"admin/v1/tray-releases/{result.Value!.Id}", result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPut("admin/v1/tray-releases/{id:guid}")]
    [Authorize(Policy = "AdminPolicy")]
    [RequirePlatformPermission(PlatformPermissionCatalog.SystemConfigManage)]
    public async Task<IActionResult> Update(
        Guid id, [FromBody] UpdateTrayReleaseRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new UpdateTrayReleaseCommand(id, request), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Called by the tray release pipeline. Never activates a build.</summary>
    [HttpPost("admin/v1/tray-releases/ingest")]
    [AllowAnonymous]
    public async Task<IActionResult> Ingest([FromBody] CreateTrayReleaseRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.IngestToken))
            return NotFound(); // feature off unless a token is configured

        var presented = Request.Headers["X-Release-Token"].ToString();
        if (!IngestTokenValidator.IsValid(_options.IngestToken, presented))
            return Unauthorized();

        var safe = request with { IsActive = false };
        var result = await _mediator.Send(
            new CreateTrayReleaseCommand(safe, TrayReleaseSources.Ci, null), ct);
        return result.IsSuccess
            ? Created($"admin/v1/tray-releases/{result.Value!.Id}", result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
```

The class has no class-level `[Authorize]` on purpose so `Ingest` can be anonymous; every other action carries its own `[Authorize(Policy = "AdminPolicy")]`. If the project has an architecture test requiring class-level auth on `Controllers/Admin`, run the architecture tests in Step 8 and, if it fails, add `TrayReleasesController` to that test's allowlist with a comment explaining the token-guarded ingest.

- [ ] **Step 6: Create the public controller**

`src/ONEVO.Api/Controllers/Tenant/Monitoring/TrayActivation/TrayReleasesPublicController.cs`:

```csharp
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Application.Features.DevPlatform.SystemConfig.TrayReleases.Queries.CheckTrayUpdate;
using ONEVO.Application.Features.DevPlatform.SystemConfig.TrayReleases.Queries.GetLatestTrayRelease;

namespace ONEVO.Api.Controllers.Tenant.Monitoring.TrayActivation;

/// <summary>
/// Anonymous, read-only installer metadata. Not secret: the file is public and integrity is enforced
/// client-side via sha256. Anonymous so the tray can check for updates before/without a session.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/v1/tray/releases")]
public sealed class TrayReleasesPublicController : ControllerBase
{
    private readonly IMediator _mediator;
    public TrayReleasesPublicController(IMediator mediator) => _mediator = mediator;

    [HttpGet("latest")]
    public async Task<IActionResult> Latest([FromQuery] string channel = "stable", CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetLatestTrayReleaseQuery(channel), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpGet("check")]
    public async Task<IActionResult> Check(
        [FromQuery] string current, [FromQuery] string channel = "stable", CancellationToken ct = default)
    {
        var result = await _mediator.Send(new CheckTrayUpdateQuery(current, channel), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
```

- [ ] **Step 7: Build and smoke-test locally**

Stop any running `ONEVO.Api.exe` first (it auto-respawns; re-check right before building). Put `TrayReleases__IngestToken=dev-local-token` in `HRMS-Backend-v1/.env`, then:

Run: `dotnet build src/ONEVO.Api/ONEVO.Api.csproj` → Expected: 0 errors.

Apply the migration to the LOCAL dev DB (if the known broken pre-existing migration blocks `dotnet ef database update`, apply just this table's SQL from `dotnet ef migrations script <previous> AddTrayAppReleases` manually to local dev and note it):
`dotnet ef database update --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api`

Start the API, then (use your real admin host, e.g. `https://admin.onexso.com` per project memory — local dev root domain is `onexso.com`; adjust to what `HRMS-Backend-v1/PORTS.md` says):

```bash
# ingest works and is inactive
curl -sk -X POST https://<admin-host>/admin/v1/tray-releases/ingest \
  -H "Content-Type: application/json" -H "X-Release-Token: dev-local-token" \
  -d '{"version":"0.0.1","channel":"beta","downloadUrl":"https://example.com/a.msix","sha256":"'"$(printf 'a%.0s' {1..64})"'","fileSizeBytes":10,"publisher":"CN=ONEVO","minimumWindowsVersion":"10.0.19041.0","isActive":true,"minSupportedVersion":null,"releaseNotes":"smoke"}'
```
Expected: `201` and JSON with `"isActive":false,"source":"ci"` (even though the body asked for `true`).

```bash
# wrong token
curl -sk -o /dev/null -w "%{http_code}\n" -X POST https://<admin-host>/admin/v1/tray-releases/ingest -H "X-Release-Token: nope" -H "Content-Type: application/json" -d '{}'
# public latest before activation
curl -sk -o /dev/null -w "%{http_code}\n" "https://<tenant-host>/api/v1/tray/releases/latest?channel=beta"
```
Expected: `401`, then `404` (not active yet → not served). Then activate it through the admin API in the Task 4 UI (or SQL `update tray_app_releases set is_active=true`) and confirm `latest?channel=beta` returns `{"version":"0.0.1","download_url":...}` and `check?current=0.0.0&channel=beta` returns `"update_available":true`. Delete the smoke row afterward.

- [ ] **Step 8: Run the full backend suites**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj` and `dotnet test tests/ONEVO.Tests.Architecture/ONEVO.Tests.Architecture.csproj`
Expected: all PASS (baseline before this plan was 3940+ unit tests green per project memory; the count only grows). Fix any failure this plan introduced; report pre-existing unrelated failures separately with their output.

- [ ] **Step 9: Commit**

```bash
git add src tests
git commit -m "feat(tray-releases): admin CRUD, CI ingest, and anonymous latest/check endpoints

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

## Task 4: Platform Admin app — "Tray Releases" page

**Files (all under `HRMS-Platform-Administration-Front-End-v1/src/app/`):**
- Modify: `core/config/api-endpoints.ts` (add `trayReleases` next to `systemConfig.serviceKeys`, ~line 128)
- Modify: `app.routes.ts` (add a route beside `system-config/service-keys`, ~line 359)
- Modify: `layouts/main-layout/sidebar/sidebar.ts` (add a nav item beside the service-keys entry, ~line 92)
- Create: `modules/tray-releases/data/tray-release.model.ts`
- Create: `modules/tray-releases/data/tray-releases.service.ts`, `tray-releases.service.spec.ts`
- Create: `modules/tray-releases/feature/tray-releases-list/tray-releases-list.ts`, `tray-releases-list.html`, `tray-releases-list.spec.ts`
- Create: `modules/tray-releases/feature/tray-release-form-modal/tray-release-form-modal.ts`, `tray-release-form-modal.spec.ts`

**Interfaces:**
- Consumes: the Task 3 admin endpoints (camelCase JSON).
- Produces: `TrayRelease` model, `TrayReleasesService { list(); create(payload); update(id, patch) }`, route `/system-config/tray-releases`, permission gate `platform.system_config.read` (page) / `.manage` (mutations).

Before starting, read (do not skip): `CLAUDE.md` in this repo (permissions + navigation shell sections), `modules/system-config/feature/service-keys-list/service-keys-list.ts` and its route entry at `app.routes.ts:359-370`, `modules/system-config/feature/add-service-key-modal/add-service-key-modal.ts`, and `layouts/main-layout/sidebar/sidebar.ts:85-100`. The new files must copy their component structure, permission directive usage (`*appPermission`), `shared/ui` components (Table, Modal, StatusBadge, ConfirmationDialog, ErrorBanner, EmptyState), and toast/error handling verbatim in style — the code below shows the data layer completely and the list/modal logic; template markup should be assembled from those same `shared/ui` pieces exactly as the service-keys list does.

- [ ] **Step 1: Add endpoints**

In `core/config/api-endpoints.ts`, inside the exported `API_ENDPOINTS` object (use the same nesting level as `systemConfig`):

```ts
  trayReleases: {
    list: '/tray-releases',
    create: '/tray-releases',
    update: (id: string) => `/tray-releases/${id}`,
  },
```

- [ ] **Step 2: Model**

`modules/tray-releases/data/tray-release.model.ts`:

```ts
export type TrayReleaseChannel = 'stable' | 'beta';

export interface TrayRelease {
  id: string;
  version: string;
  channel: TrayReleaseChannel;
  downloadUrl: string;
  sha256: string;
  fileSizeBytes: number;
  publisher: string;
  minimumWindowsVersion: string;
  minSupportedVersion: string | null;
  releaseNotes: string | null;
  isActive: boolean;
  source: 'admin' | 'ci';
  createdAt: string;
  updatedAt: string;
}

export interface CreateTrayReleasePayload {
  version: string;
  channel: TrayReleaseChannel;
  downloadUrl: string;
  sha256: string;
  fileSizeBytes: number;
  publisher: string;
  minimumWindowsVersion: string;
  minSupportedVersion: string | null;
  releaseNotes: string | null;
  isActive: boolean;
}

/** null = leave unchanged; '' clears minSupportedVersion / releaseNotes. */
export interface UpdateTrayReleasePayload {
  channel?: TrayReleaseChannel | null;
  minSupportedVersion?: string | null;
  releaseNotes?: string | null;
  isActive?: boolean | null;
}
```

- [ ] **Step 3: Failing service spec**

`modules/tray-releases/data/tray-releases.service.spec.ts` (mirror `service-keys.service.spec.ts` for the TestBed/`provideHttpClient`/`HttpTestingController` setup — copy its imports and `beforeEach`):

```ts
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TrayReleasesService } from './tray-releases.service';
import { environment } from '../../../../environments/environment';

describe('TrayReleasesService', () => {
  let service: TrayReleasesService;
  let http: HttpTestingController;
  const base = environment.apiUrl;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(TrayReleasesService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('lists releases with credentials', () => {
    service.list().subscribe();
    const req = http.expectOne(`${base}/tray-releases`);
    expect(req.request.method).toBe('GET');
    expect(req.request.withCredentials).toBe(true);
    req.flush([]);
  });

  it('creates a release', () => {
    const payload = {
      version: '1.2.0', channel: 'beta' as const, downloadUrl: 'https://x/y.msix',
      sha256: 'a'.repeat(64), fileSizeBytes: 10, publisher: 'CN=ONEVO',
      minimumWindowsVersion: '10.0.19041.0', minSupportedVersion: null,
      releaseNotes: null, isActive: false,
    };
    service.create(payload).subscribe();
    const req = http.expectOne(`${base}/tray-releases`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(payload);
    expect(req.request.withCredentials).toBe(true);
    req.flush({});
  });

  it('updates a release by id', () => {
    service.update('abc', { isActive: true }).subscribe();
    const req = http.expectOne(`${base}/tray-releases/abc`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ isActive: true });
    expect(req.request.withCredentials).toBe(true);
    req.flush({});
  });
});
```

Run: `npx jest src/app/modules/tray-releases/data/tray-releases.service.spec.ts`
Expected: FAIL — `./tray-releases.service` not found.

- [ ] **Step 4: Implement the service**

`modules/tray-releases/data/tray-releases.service.ts`:

```ts
import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { API_ENDPOINTS } from '../../../core/config/api-endpoints';
import { environment } from '../../../../environments/environment';
import { CreateTrayReleasePayload, TrayRelease, UpdateTrayReleasePayload } from './tray-release.model';

@Injectable({ providedIn: 'root' })
export class TrayReleasesService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = environment.apiUrl;

  list(): Observable<TrayRelease[]> {
    return this.http.get<TrayRelease[]>(`${this.baseUrl}${API_ENDPOINTS.trayReleases.list}`, {
      withCredentials: true,
    });
  }

  create(payload: CreateTrayReleasePayload): Observable<TrayRelease> {
    return this.http.post<TrayRelease>(`${this.baseUrl}${API_ENDPOINTS.trayReleases.create}`, payload, {
      withCredentials: true,
    });
  }

  update(id: string, patch: UpdateTrayReleasePayload): Observable<TrayRelease> {
    return this.http.put<TrayRelease>(`${this.baseUrl}${API_ENDPOINTS.trayReleases.update(id)}`, patch, {
      withCredentials: true,
    });
  }
}
```

Run the spec again. Expected: PASS (3 tests). Confirm `environment.apiUrl` already includes the `/admin/v1` prefix by comparing with `service-keys.service.ts` (it uses `${baseUrl}${API_ENDPOINTS.systemConfig.serviceKeys.list}` with `'/system-config/service-keys'`, so the prefix is in `apiUrl`). If the service-keys spec expects a different base, follow it.

- [ ] **Step 5: Failing list-component spec**

`modules/tray-releases/feature/tray-releases-list/tray-releases-list.spec.ts` — mirror the setup of `service-keys-list.spec.ts` (mock the service with `jest.fn()`; grant the permission store whatever that spec grants for `platform.system_config.manage`). Cases to assert:

```ts
it('loads and renders releases newest-first with channel and status', async () => { /* list() returns 2 rows; expect both versions in the DOM, "Active"/"Inactive" badges */ });
it('shows an empty state when there are no releases', async () => { /* list() returns [] */ });
it('shows an error banner when loading fails', async () => { /* list() throws */ });
it('activating an inactive release calls update(id, { isActive: true }) then reloads', async () => {});
it('promoting a beta release calls update(id, { channel: "stable" }) after confirmation', async () => {});
it('hides create/activate/promote controls without the manage permission', async () => {});
it('opens the create modal from the New release button', async () => {});
```
Write each with real arrange/act/assert following `service-keys-list.spec.ts` idioms. Run `npx jest src/app/modules/tray-releases/feature/tray-releases-list` → Expected: FAIL (component missing).

- [ ] **Step 6: Implement the list component**

`tray-releases-list.ts` (structure to follow; adapt selector prefix/imports from `service-keys-list.ts`):

```ts
import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { TrayReleasesService } from '../../data/tray-releases.service';
import { TrayRelease } from '../../data/tray-release.model';
// import Table/Modal/StatusBadge/ConfirmationDialog/ErrorBanner/EmptyState, PermissionDirective,
// NotificationService exactly as service-keys-list.ts imports them.

@Component({
  selector: 'app-tray-releases-list',
  standalone: true,
  templateUrl: './tray-releases-list.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [/* same shared/ui + PermissionDirective imports as service-keys-list */],
})
export class TrayReleasesList implements OnInit {
  private readonly api = inject(TrayReleasesService);

  readonly releases = signal<TrayRelease[]>([]);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly formOpen = signal(false);
  readonly pendingPromote = signal<TrayRelease | null>(null);

  ngOnInit(): void {
    void this.load();
  }

  async load(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      this.releases.set(await firstValueFrom(this.api.list()));
    } catch {
      this.error.set('Could not load tray releases.');
    } finally {
      this.loading.set(false);
    }
  }

  async setActive(release: TrayRelease, isActive: boolean): Promise<void> {
    try {
      await firstValueFrom(this.api.update(release.id, { isActive }));
      await this.load();
    } catch {
      this.error.set(`Could not ${isActive ? 'activate' : 'deactivate'} ${release.version}.`);
    }
  }

  askPromote(release: TrayRelease): void {
    this.pendingPromote.set(release);
  }

  async confirmPromote(): Promise<void> {
    const release = this.pendingPromote();
    if (!release) return;
    this.pendingPromote.set(null);
    try {
      // Promote = move to stable and make it live in one step.
      await firstValueFrom(this.api.update(release.id, { channel: 'stable', isActive: true }));
      await this.load();
    } catch {
      this.error.set(`Could not promote ${release.version}. A stable ${release.version} may already exist.`);
    }
  }

  openForm(): void { this.formOpen.set(true); }
  async onFormClosed(created: boolean): Promise<void> {
    this.formOpen.set(false);
    if (created) await this.load();
  }

  formatSize(bytes: number): string {
    return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
  }
}
```

The template must render, per row: version, channel badge, Active/Inactive `StatusBadge`, source (`admin`/`ci`), size (`formatSize`), created date, truncated SHA-256 with full value in `title`, and — behind `*appPermission="'platform.system_config.manage'"` — buttons: **Activate/Deactivate** (calls `setActive`), and **Promote to stable** (only when `channel === 'beta'`, calls `askPromote`), plus a top-right **New release** button. Include the loading skeleton, `ErrorBanner` bound to `error()`, and `EmptyState` when the list is empty, matching service-keys-list. Confirmation copy for promote: "Promote {version} to stable? Employees will be offered this installer immediately."

Run: `npx jest src/app/modules/tray-releases/feature/tray-releases-list`
Expected: PASS.

- [ ] **Step 7: Create form modal (failing spec first, then implementation)**

`tray-release-form-modal.spec.ts` cases: (1) submit disabled until version is `x.y.z`, HTTPS URL, 64-hex SHA, size > 0, publisher, min Windows version are valid; (2) valid submit calls `create` with `isActive: false` default and emits closed(true); (3) server 409 shows "That version already exists in this channel."; (4) cancel emits closed(false) without calling `create`.

`tray-release-form-modal.ts` — use a reactive form like `add-service-key-modal.ts` does, with these validators (exact patterns):

```ts
const VERSION = /^\d+\.\d+\.\d+$/;
const SHA256 = /^[0-9a-fA-F]{64}$/;
// fields: version[VERSION], channel ('beta' default), downloadUrl[https regex /^https:\/\/\S+$/],
// sha256[SHA256], fileSizeBytes[min 1], publisher (default 'CN=ONEVO'), minimumWindowsVersion (default '10.0.19041.0'),
// minSupportedVersion (optional, VERSION when present), releaseNotes (optional), isActive (default false)
```
Submit maps blanks to `null` for `minSupportedVersion`/`releaseNotes`, lowercases nothing (backend normalises), calls `TrayReleasesService.create`, and on `HttpErrorResponse` with `status === 409` shows the message above; any other error shows the API's `detail` if present, else "Could not create the release."

Run: `npx jest src/app/modules/tray-releases` → Expected: all PASS.

- [ ] **Step 8: Route + sidebar**

`app.routes.ts` — beside the `system-config/service-keys` entry, add an entry with the same `canActivate`/`data` shape, changing only path/permission wiring:

```ts
      {
        path: 'system-config/tray-releases',
        // copy canActivate + data (breadcrumb, permission 'platform.system_config.read') from the service-keys route above
        loadComponent: () =>
          import('./modules/tray-releases/feature/tray-releases-list/tray-releases-list').then(
            (m) => m.TrayReleasesList,
          ),
      },
```

`sidebar.ts` — add an item directly after the service-keys item with `route: '/system-config/tray-releases'`, label `Tray Releases`, the same permission as service-keys, and an existing icon key (reuse `'service-keys'`'s icon if there is no download/package icon; do not add a new icon system). Update `sidebar.spec.ts` only if it asserts an exact item count.

- [ ] **Step 9: Build + test + look at it**

Run: `npx ng build` then `npx jest` (whole suite).
Expected: build OK, all tests PASS.
Then start the admin app against the local backend (per `PORTS.md`), log in as a platform admin, open **System Config → Tray Releases**, create a release, activate it, and confirm `GET <tenant-host>/api/v1/tray/releases/latest?channel=<its channel>` now returns it. Take a screenshot for the PR.

- [ ] **Step 10: Commit**

```bash
git add src
git commit -m "feat(admin): tray releases page (list, create, activate, promote)

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

## Task 5: Employee web app — Download step on the tray activation gate

**Files (under `Hrms--Web-application---front-end---v1/src/app/core/tray-presence/`):**
- Create: `models/tray-installer.model.ts`
- Create: `data-access/tray-installer-api.service.ts`, `data-access/tray-installer-api.service.spec.ts`
- Modify: `feature/tray-activation-gate/tray-activation-gate.component.ts`, `.html`, `.css`
- Create: `feature/tray-activation-gate/tray-activation-gate.component.spec.ts`

**Interfaces:**
- Consumes: `GET {environment.apiUrl}/tray/releases/latest?channel=stable` → `{ version, download_url, sha256, file_size_bytes, publisher, minimum_windows_version, release_notes, channel }`; existing `TrayPresenceStore` (`phase()`, `code()`, `hasUsableCode()`, `codeSecondsRemaining()`, `copyCode()`, `retry()`), existing `AuthStore.logout()`.
- Produces: `TrayInstallerInfo` model; `TrayInstallerApiService.getLatest(): Observable<TrayInstallerInfo>`; gate component signals `installer()`, `installerState()` (`'loading' | 'ready' | 'unavailable'`), `sizeLabel()`.

Context: the gate today says "Open the **already-installed** ONEVO tray app, paste this code…" — there is no way to obtain the app. This task adds "Step 1: Download" above the code, without touching the store or the code-generation flow. Runner is **Vitest**.

- [ ] **Step 1: Model**

`models/tray-installer.model.ts`:

```ts
export interface TrayInstallerInfo {
  version: string;
  download_url: string;
  sha256: string;
  file_size_bytes: number;
  publisher: string;
  minimum_windows_version: string;
  release_notes: string | null;
  channel: 'stable' | 'beta';
}
```

- [ ] **Step 2: Failing API service spec**

`data-access/tray-installer-api.service.spec.ts` — mirror the setup used by an existing spec next to a `*-api.service.ts` in this repo (e.g. `grep -rl "HttpTestingController" src/app/core | head -3` and copy its providers):

```ts
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import { TrayInstallerApiService } from './tray-installer-api.service';
import { environment } from '../../../../environments/environment';

describe('TrayInstallerApiService', () => {
  let service: TrayInstallerApiService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(TrayInstallerApiService);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  it('requests the stable channel installer', () => {
    service.getLatest().subscribe();
    const req = http.expectOne(`${environment.apiUrl}/tray/releases/latest?channel=stable`);
    expect(req.request.method).toBe('GET');
    req.flush({ version: '1.0.0' });
  });
});
```
Run: `npx vitest run src/app/core/tray-presence/data-access/tray-installer-api.service.spec.ts` (use this repo's actual test command from `package.json`'s `scripts.test`; adapt if it is `ng test`). Expected: FAIL.

- [ ] **Step 3: Implement the API service**

`data-access/tray-installer-api.service.ts`:

```ts
import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../../environments/environment';
import { TrayInstallerInfo } from '../models/tray-installer.model';

@Injectable({ providedIn: 'root' })
export class TrayInstallerApiService {
  private readonly http = inject(HttpClient);

  getLatest(): Observable<TrayInstallerInfo> {
    return this.http.get<TrayInstallerInfo>(
      `${environment.apiUrl}/tray/releases/latest?channel=stable`
    );
  }
}
```
Run the spec again. Expected: PASS.

- [ ] **Step 4: Failing gate component spec**

`tray-activation-gate.component.spec.ts` — model the TestBed on `main-layout.tray-presence.spec.ts` for how `TrayPresenceStore` and `AuthStore` are provided/mocked in this repo. Cases:

```ts
it('shows the download link with version and size when an installer is published', async () => {
  // getLatest -> { version: '1.2.0', download_url: 'https://dl.example.com/onevo-1.2.0.msix', file_size_bytes: 52428800, ... }
  // expect an <a> with href = download_url, text containing 'Download' and '1.2.0', and '50.0 MB' somewhere
});
it('shows a "not available yet" message and no download link when getLatest returns 404', async () => {});
it('still renders the activation code and Copy button while the installer is loading or unavailable', async () => {
  // store phase 'waiting' with usable code -> code text present regardless of installer state
});
it('download link opens in a new tab safely', async () => {
  // expect target="_blank" and rel containing "noopener"
});
```
Run → Expected: FAIL.

- [ ] **Step 5: Implement gate changes**

`tray-activation-gate.component.ts` — add (keep every existing member):

```ts
import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { AuthStore } from '../../../auth/state/auth.store';
import { TrayPresenceStore } from '../../state/tray-presence.store';
import { TrayInstallerApiService } from '../../data-access/tray-installer-api.service';
import { TrayInstallerInfo } from '../../models/tray-installer.model';

// ...@Component decorator unchanged...
export class TrayActivationGateComponent implements OnInit {
  readonly presence = inject(TrayPresenceStore);
  private readonly auth = inject(AuthStore);
  private readonly installerApi = inject(TrayInstallerApiService);

  readonly installer = signal<TrayInstallerInfo | null>(null);
  readonly installerState = signal<'loading' | 'ready' | 'unavailable'>('loading');
  readonly sizeLabel = computed(() => {
    const info = this.installer();
    return info ? `${(info.file_size_bytes / 1024 / 1024).toFixed(1)} MB` : '';
  });

  ngOnInit(): void {
    void this.loadInstaller();
  }

  private async loadInstaller(): Promise<void> {
    try {
      this.installer.set(await firstValueFrom(this.installerApi.getLatest()));
      this.installerState.set('ready');
    } catch {
      // 404 (nothing published) and network errors both degrade to "unavailable";
      // the code flow below must keep working for people who already have the app.
      this.installerState.set('unavailable');
    }
  }

  // copyCode / regenerate / retry / logout unchanged
}
```

`tray-activation-gate.component.html` — change the `waiting && hasUsableCode` branch to a two-step layout; leave every other branch as is. Replace the single line `<p>Open the already-installed ONEVO tray app, paste this code, and select <strong>Connect &amp; Login</strong>.</p>` with:

```html
      <section class="step" aria-labelledby="step-download">
        <h2 id="step-download">1. Install the ONEVO tray app</h2>
        @if (installerState() === 'ready' && installer(); as info) {
          <a class="primary download" [href]="info.download_url" target="_blank" rel="noopener noreferrer" download>
            Download for Windows (v{{ info.version }}, {{ sizeLabel() }})
          </a>
          <p class="hint">Already installed? Skip to step 2.</p>
        } @else if (installerState() === 'loading') {
          <p class="hint" aria-live="polite">Checking for the latest installer…</p>
        } @else {
          <p class="hint">The installer isn't available yet. If the app is already installed, continue with step 2; otherwise contact your administrator.</p>
        }
      </section>

      <section class="step" aria-labelledby="step-code">
        <h2 id="step-code">2. Connect it to your account</h2>
        <p>Open the ONEVO tray app, paste this code, and select <strong>Connect &amp; Login</strong>.</p>
      </section>
```
Keep the existing `<div class="code">`, expiry paragraph, actions, and waiting paragraph after it, unchanged.

`tray-activation-gate.component.css` — add minimal styling consistent with the existing file (reuse its existing button/typography variables; do not introduce new colors): `.step { margin: 1rem 0; }`, `.step h2 { font-size: 1rem; margin: 0 0 .5rem; }`, `.download { display: inline-block; text-decoration: none; }`, `.hint { opacity: .75; font-size: .875rem; }`. Because Angular scoped CSS applies here (static template markup, not dynamically inserted), no `::ng-deep` is needed.

- [ ] **Step 6: Run tests + build**

Run: this repo's test command (Vitest) for `src/app/core/tray-presence`, then the full suite, then `npx ng build`.
Expected: new specs PASS, no regressions in `tray-presence.store.spec.ts` or `main-layout.tray-presence.spec.ts`, build OK.

- [ ] **Step 7: Verify in the real browser**

Start the web app + backend (Task 3 dev row activated). Log in as a monitored employee with no paired device. Expected: the gate shows "1. Install…" with a working Download link (version + size), and "2. Connect…" with the live code + Copy. Deactivate the release in the admin page and reload: expected "installer isn't available yet" text while the code still shows. Screenshot both states.

- [ ] **Step 8: Commit**

```bash
git add src
git commit -m "feat(web): show installer download step on tray activation gate

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

## Task 6: Tray release pipeline — GitHub Actions + R2 + ingest

**Files (in `HRMS_TrayApp`):**
- Create: `.github/workflows/release.yml`
- Modify: `build-msix.ps1` (only if needed to accept a version + cert from environment — see Step 3)
- Modify: `README.md` (append a "Releasing" section)

**Interfaces:**
- Consumes: backend `POST /admin/v1/tray-releases/ingest` (Task 3); `X-Release-Token`.
- Produces: on `git push origin vX.Y.Z` — a signed `ONEVO-X.Y.Z.msix` in R2 at `https://<public-base>/tray/X.Y.Z/ONEVO-X.Y.Z.msix` and an **inactive beta** row in `tray_app_releases`.

**One-time manual setup (do this before Step 1; it is not code):**
1. **Cloudflare R2:** dashboard → R2 → create bucket `onevo-installers` → Settings → enable **Public access** (for testing use the `r2.dev` URL; for production attach a custom domain like `downloads.<yourdomain>`). Note the public base URL.
2. R2 → Manage API tokens → create token with *Object Read & Write* on that bucket → note Access Key ID, Secret, and the S3 endpoint `https://<accountid>.r2.cloudflarestorage.com`.
3. GitHub repo `hrms-wms-2026/HRMS_TrayApp` → Settings → Secrets and variables → Actions. Add **secrets**: `R2_ACCESS_KEY_ID`, `R2_SECRET_ACCESS_KEY`, `R2_ENDPOINT`, `RELEASE_INGEST_TOKEN` (same value as the backend's `TrayReleases__IngestToken`), `SIGNING_PFX_BASE64` (`[Convert]::ToBase64String([IO.File]::ReadAllBytes('dev-cert.pfx'))`), `SIGNING_PFX_PASSWORD`. Add **variables**: `R2_BUCKET` (`onevo-installers`), `R2_PUBLIC_BASE_URL`, `ONEVO_API_BASE` (the admin API base, e.g. `https://admin.<yourdomain>`).
4. Never commit `dev-cert.pfx` (check `.gitignore` covers `*.pfx`; add it if not).

- [ ] **Step 1: Prove the version flows into the MSIX (local, before writing CI)**

MAUI's mapping of `ApplicationDisplayVersion` → MSIX identity version must be verified, not assumed. From `HRMS_TrayApp` (stop any running tray `.exe` first):

```powershell
.\build-msix.ps1
$msix = Get-ChildItem publish\msix -Recurse -Filter *.msix | Select-Object -First 1
Add-Type -A System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead($msix.FullName)
$entry = $zip.Entries | Where-Object FullName -eq 'AppxManifest.xml'
(New-Object IO.StreamReader($entry.Open())).ReadToEnd() | Select-String 'Identity'
$zip.Dispose()
```
Expected: an `<Identity ... Version="1.0.0.x" ...>` line. Record the exact `Version` string. Now rebuild with `-p:ApplicationDisplayVersion=1.2.3` added to the `dotnet publish` line of `build-msix.ps1` (Step 3 does this properly) and confirm the manifest reads `1.2.3.<n>`. If it does not change, STOP and fix the mapping (Package.appxmanifest / `ApplicationVersion` property) before continuing — CI is worthless if the installed package version cannot be controlled.

- [ ] **Step 2: Make the build script CI-friendly**

In `build-msix.ps1`, add parameters and pass them through, keeping the default local behavior unchanged:

```powershell
param(
    [string]$CertPassword = "Dev@1234",
    [string]$OutDir       = "$PSScriptRoot\publish\msix",
    [string]$Version      = "1.0.0",      # x.y.z
    [string]$CertFile     = "$PSScriptRoot\dev-cert.pfx"
)
$certFile = $CertFile
```
(replace the existing `$certFile = "$PSScriptRoot\dev-cert.pfx"` line), and in the `dotnet publish` invocation add:

```powershell
    -p:ApplicationDisplayVersion=$Version `
```
Run `.\build-msix.ps1 -Version 1.2.3` locally; Expected: MSIX produced, manifest version matches (Step 1 check).

- [ ] **Step 3: Write the workflow**

`.github/workflows/release.yml`:

```yaml
name: Release

on:
  push:
    tags: ["v*.*.*"]

permissions:
  contents: read

jobs:
  release:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4

      - name: Derive version from tag
        id: ver
        shell: pwsh
        run: |
          $v = "${{ github.ref_name }}".TrimStart('v')
          if ($v -notmatch '^\d+\.\d+\.\d+$') { throw "Tag must look like vX.Y.Z, got '${{ github.ref_name }}'" }
          "version=$v" >> $env:GITHUB_OUTPUT

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          global-json-file: global.json
          dotnet-version: "10.0.300"

      - name: Install MAUI workload
        run: dotnet workload install maui-windows --skip-manifest-update

      - name: Run tests
        run: |
          dotnet test tests/ONEVO.Agent.Shared.Tests/ONEVO.Agent.Shared.Tests.csproj --configuration Release
          dotnet test tests/ONEVO.Agent.Service.Tests/ONEVO.Agent.Service.Tests.csproj --configuration Release
          dotnet test tests/ONEVO.Agent.TrayApp.Tests/ONEVO.Agent.TrayApp.Tests.csproj --configuration Release

      - name: Restore signing certificate
        shell: pwsh
        run: |
          [IO.File]::WriteAllBytes("$env:RUNNER_TEMP\signing.pfx", [Convert]::FromBase64String($env:PFX_B64))
        env:
          PFX_B64: ${{ secrets.SIGNING_PFX_BASE64 }}

      - name: Build signed MSIX
        shell: pwsh
        run: |
          ./build-msix.ps1 -Version "${{ steps.ver.outputs.version }}" `
            -CertFile "$env:RUNNER_TEMP\signing.pfx" -CertPassword $env:PFX_PASSWORD
        env:
          PFX_PASSWORD: ${{ secrets.SIGNING_PFX_PASSWORD }}

      - name: Locate artifact, rename, hash
        id: art
        shell: pwsh
        run: |
          $src = Get-ChildItem publish\msix -Recurse -Filter *.msix | Select-Object -First 1
          if (-not $src) { throw "No .msix produced" }
          $name = "ONEVO-${{ steps.ver.outputs.version }}.msix"
          Copy-Item $src.FullName $name
          $sha = (Get-FileHash $name -Algorithm SHA256).Hash.ToLower()
          "name=$name"            >> $env:GITHUB_OUTPUT
          "sha=$sha"              >> $env:GITHUB_OUTPUT
          "size=$((Get-Item $name).Length)" >> $env:GITHUB_OUTPUT

      - name: Upload to R2
        shell: pwsh
        run: |
          aws s3 cp "${{ steps.art.outputs.name }}" `
            "s3://${{ vars.R2_BUCKET }}/tray/${{ steps.ver.outputs.version }}/${{ steps.art.outputs.name }}" `
            --endpoint-url "${{ secrets.R2_ENDPOINT }}" `
            --content-type "application/msix"
        env:
          AWS_ACCESS_KEY_ID: ${{ secrets.R2_ACCESS_KEY_ID }}
          AWS_SECRET_ACCESS_KEY: ${{ secrets.R2_SECRET_ACCESS_KEY }}
          AWS_DEFAULT_REGION: auto

      - name: Register release (inactive beta)
        shell: pwsh
        run: |
          $body = @{
            version               = "${{ steps.ver.outputs.version }}"
            channel               = "beta"
            downloadUrl           = "${{ vars.R2_PUBLIC_BASE_URL }}/tray/${{ steps.ver.outputs.version }}/${{ steps.art.outputs.name }}"
            sha256                = "${{ steps.art.outputs.sha }}"
            fileSizeBytes         = [long]"${{ steps.art.outputs.size }}"
            publisher             = "CN=ONEVO"
            minimumWindowsVersion = "10.0.19041.0"
            minSupportedVersion   = $null
            releaseNotes          = "Built from ${{ github.ref_name }} (${{ github.sha }})"
            isActive              = $false
          } | ConvertTo-Json
          Invoke-RestMethod -Method Post `
            -Uri "${{ vars.ONEVO_API_BASE }}/admin/v1/tray-releases/ingest" `
            -Headers @{ "X-Release-Token" = $env:INGEST_TOKEN } `
            -ContentType "application/json" -Body $body
        env:
          INGEST_TOKEN: ${{ secrets.RELEASE_INGEST_TOKEN }}

      - name: Keep a copy on the workflow run
        uses: actions/upload-artifact@v4
        with:
          name: ${{ steps.art.outputs.name }}
          path: ${{ steps.art.outputs.name }}
          retention-days: 30
```

`windows-latest` runners ship the AWS CLI. If `aws` is not found in the log, add a step `choco install awscli -y` before the upload step. The publisher (`CN=ONEVO`) must equal the certificate subject used to sign; if you later switch to a production certificate, change the string here to that subject.

- [ ] **Step 4: Dry-run the pipeline on a throwaway tag**

Set `RELEASE_INGEST_TOKEN` etc. as described. Then:
```bash
git tag v0.0.1-test0  # will NOT match — confirms the guard
```
Better: tag a real test version that does match, then delete it afterwards:
```bash
git tag v0.9.0 && git push origin v0.9.0
```
Watch the run in GitHub → Actions. Expected results, in order: tests pass → MSIX built → object exists in R2 (`aws s3 ls s3://onevo-installers/tray/0.9.0/ --endpoint-url <endpoint>`) → ingest step returns JSON with `"isActive": false, "source": "ci"` → the row appears in the admin **Tray Releases** page as inactive/beta. Download the URL in a browser and confirm `Get-FileHash` equals the `sha256` shown in the admin page.

- [ ] **Step 5: Document releasing**

Append to `README.md`:

```markdown
## Releasing

1. Merge to `main` with green CI.
2. `git tag vX.Y.Z && git push origin vX.Y.Z` — the Release workflow builds, signs, uploads to R2 and registers the build as an **inactive beta** in the platform DB.
3. In the Platform Admin app → System Config → Tray Releases: activate the beta, test on a real machine (machine must trust the signing cert), then **Promote to stable**.
4. To roll back: deactivate the bad release (previous stable becomes "latest" again). To force everyone forward, set a release's *minimum supported version*.
Required repo secrets/variables are listed in `.github/workflows/release.yml`'s header comment.
```
Also add a comment block at the top of `release.yml` listing the six secrets and three variables from the manual setup above.

- [ ] **Step 6: Commit**

```bash
git add .github/workflows/release.yml build-msix.ps1 README.md .gitignore
git commit -m "ci(tray): tag-triggered signed MSIX build, R2 upload, and release registration

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

## Task 7: Tray update check (Service → IPC → TrayApp)

**Files (in `HRMS_TrayApp`):**
- Modify: `ONEVO.Agent.Shared/IPC/IpcMessages.cs` (add 2 type constants + 2 payload records)
- Modify: `ONEVO.Agent.Service/Api/AgentApiRoutes.cs` (add route), `ONEVO.Agent.Service/Api/OnevoApiClient.cs` (add `CheckForUpdateAsync`)
- Modify: `ONEVO.Agent.Service/AgentWorker.cs` (add `case` at ~line 262 + `HandleUpdateCheckRequestAsync`)
- Modify: `ONEVO.Agent.TrayApp/Services/INamedPipeClient.cs`, `NamedPipeClient.cs` (add `SendUpdateCheckAsync`)
- Create: `ONEVO.Agent.TrayApp/Services/UpdateChecker.cs`, `IUpdateChecker.cs`
- Modify: `ONEVO.Agent.TrayApp/MauiProgram.cs` (register `IUpdateChecker`), plus one display surface (Step 7)
- Test: `tests/ONEVO.Agent.Service.Tests/Api/OnevoApiClientUpdateCheckTests.cs`, `tests/ONEVO.Agent.TrayApp.Tests/Services/UpdateCheckerTests.cs`

**Interfaces:**
- Consumes: `GET /api/v1/tray/releases/check?current={x.y.z}&channel=stable` → `{ update_available, mandatory, latest: { version, download_url, sha256, file_size_bytes, ... } | null }`.
- Produces:
  - `IpcMessageTypes.UpdateCheckRequest = "UpdateCheckRequest"`, `IpcMessageTypes.UpdateCheckResult = "UpdateCheckResult"`
  - `record UpdateCheckRequestPayload(string CurrentVersion)`
  - `record UpdateCheckResultPayload(bool Success, bool UpdateAvailable, bool Mandatory, string? LatestVersion, string? DownloadUrl, string? Sha256, long FileSizeBytes, string? ReleaseNotes, string? ErrorCode)`
  - `OnevoApiClient.CheckForUpdateAsync(string currentVersion, CancellationToken) : Task<UpdateCheckResultPayload>`
  - `INamedPipeClient.SendUpdateCheckAsync(string currentVersion, CancellationToken) : Task<UpdateCheckResultPayload?>`
  - `IUpdateChecker { Task<UpdateCheckResultPayload?> CheckAsync(CancellationToken); }`

Design: the Service owns network access (it already owns the `OnevoApi` client and base URL); the TrayApp asks over the existing pipe. Failure is silent — an update check must never disturb clocking in/out.

- [ ] **Step 1: Read the mirror code**

Read (do not skip) `ONEVO.Agent.Service/AgentWorker.cs` lines 240–300 and 1347–1385 (`HandleLogoutRequestAsync`), `ONEVO.Agent.TrayApp/Services/NamedPipeClient.cs` lines 247–283 (`SendLogoutAsync`), `ONEVO.Agent.Service/Api/OnevoApiClient.cs` lines 115–160, and `tests/ONEVO.Agent.Service.Tests/Api/OnevoApiClientTests.cs` (for the fake `IHttpClientFactory`/handler idiom). The code below reproduces these patterns.

- [ ] **Step 2: Add IPC types**

In `IpcMessages.cs`, inside `IpcMessageTypes` (after `LogoutResult`):

```csharp
    /// <summary>Tray → Service: ask the backend whether a newer installer exists.</summary>
    public const string UpdateCheckRequest = "UpdateCheckRequest";

    /// <summary>Service → Tray: result of an update check.</summary>
    public const string UpdateCheckResult = "UpdateCheckResult";
```
After `LogoutResultPayload`:

```csharp
public sealed record UpdateCheckRequestPayload(string CurrentVersion);

public sealed record UpdateCheckResultPayload(
    bool Success,
    bool UpdateAvailable,
    bool Mandatory,
    string? LatestVersion,
    string? DownloadUrl,
    string? Sha256,
    long FileSizeBytes,
    string? ReleaseNotes,
    string? ErrorCode);
```

- [ ] **Step 3: Failing API-client test**

`tests/ONEVO.Agent.Service.Tests/Api/OnevoApiClientUpdateCheckTests.cs` — reuse the fake handler/factory helper from `OnevoApiClientTests.cs` (copy its private helper types into this file or into a shared test helper if one already exists). Cases:

```csharp
[Fact]
public async Task CheckForUpdate_MapsUpdateAvailableResponse()
{
    // handler returns 200 with:
    // {"update_available":true,"mandatory":true,"latest":{"version":"1.3.0","download_url":"https://dl.example.com/ONEVO-1.3.0.msix","sha256":"<64 a>","file_size_bytes":123,"release_notes":"n"}}
    var result = await client.CheckForUpdateAsync("1.0.0", CancellationToken.None);
    Assert.True(result.Success);
    Assert.True(result.UpdateAvailable);
    Assert.True(result.Mandatory);
    Assert.Equal("1.3.0", result.LatestVersion);
    Assert.Equal("https://dl.example.com/ONEVO-1.3.0.msix", result.DownloadUrl);
    // and: the captured request URL ends with "/api/v1/tray/releases/check?current=1.0.0&channel=stable"
}

[Fact]
public async Task CheckForUpdate_NoUpdate_ReturnsSuccessWithoutLatest() { /* {"update_available":false,"mandatory":false,"latest":null} */ }

[Fact]
public async Task CheckForUpdate_HttpFailure_ReturnsSuccessFalse_NeverThrows() { /* 500 → Success false, ErrorCode "HTTP_500" */ }

[Fact]
public async Task CheckForUpdate_NetworkException_ReturnsSuccessFalse_NeverThrows() { /* handler throws HttpRequestException → ErrorCode "SERVICE_UNAVAILABLE" */ }

[Fact]
public async Task CheckForUpdate_RejectsHttpDownloadUrl()
{
    // latest.download_url = "http://insecure/x.msix" → treated as failure ErrorCode "INSECURE_URL"
    // (defence in depth: never hand the UI a non-https installer link)
}
```

Run: `dotnet test tests/ONEVO.Agent.Service.Tests/ONEVO.Agent.Service.Tests.csproj --filter "FullyQualifiedName~OnevoApiClientUpdateCheckTests"` (stop the running tray exe first). Expected: FAIL to compile — `CheckForUpdateAsync` missing.

- [ ] **Step 4: Implement the API client method**

`AgentApiRoutes.cs`: add `public const string TrayReleaseCheck = "/api/v1/tray/releases/check";`

In `OnevoApiClient.cs` (add `using System.Text.Json;` and `using System.Text.Json.Serialization;` if absent):

```csharp
    /// <summary>
    /// Asks the backend whether a newer installer exists. Anonymous endpoint; never throws —
    /// failures come back as Success=false so an update check can never break the agent.
    /// </summary>
    public async Task<UpdateCheckResultPayload> CheckForUpdateAsync(string currentVersion, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("OnevoApi");
        var url = $"{AgentApiRoutes.TrayReleaseCheck}?current={Uri.EscapeDataString(currentVersion)}&channel=stable";

        try
        {
            using var response = await client.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
                return Failed($"HTTP_{(int)response.StatusCode}");

            var body = await response.Content.ReadFromJsonAsync<UpdateCheckWire>(cancellationToken: ct);
            if (body is null)
                return Failed("EMPTY_RESPONSE");

            if (!body.UpdateAvailable || body.Latest is null)
                return new UpdateCheckResultPayload(true, false, false, null, null, null, 0, null, null);

            if (!Uri.TryCreate(body.Latest.DownloadUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
                return Failed("INSECURE_URL");

            return new UpdateCheckResultPayload(
                true, true, body.Mandatory,
                body.Latest.Version, body.Latest.DownloadUrl, body.Latest.Sha256,
                body.Latest.FileSizeBytes, body.Latest.ReleaseNotes, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "OnevoApi call to {Route} failed", AgentApiRoutes.TrayReleaseCheck);
            return Failed("SERVICE_UNAVAILABLE");
        }

        static UpdateCheckResultPayload Failed(string code) =>
            new(false, false, false, null, null, null, 0, null, code);
    }

    private sealed record UpdateCheckWire(
        [property: JsonPropertyName("update_available")] bool UpdateAvailable,
        [property: JsonPropertyName("mandatory")] bool Mandatory,
        [property: JsonPropertyName("latest")] UpdateLatestWire? Latest);

    private sealed record UpdateLatestWire(
        [property: JsonPropertyName("version")] string Version,
        [property: JsonPropertyName("download_url")] string DownloadUrl,
        [property: JsonPropertyName("sha256")] string Sha256,
        [property: JsonPropertyName("file_size_bytes")] long FileSizeBytes,
        [property: JsonPropertyName("release_notes")] string? ReleaseNotes);
```
`ReadFromJsonAsync` needs `using System.Net.Http.Json;` — check the top of the file; other methods in this class already parse JSON, so follow whichever deserialization helper they use if it is not `ReadFromJsonAsync`.

Run the Step 3 tests. Expected: PASS (5).

- [ ] **Step 5: Service IPC handler**

In `AgentWorker.cs`, add a case next to `LogoutRequest` (~line 262):

```csharp
            case IpcMessageTypes.UpdateCheckRequest:
                await HandleUpdateCheckRequestAsync(envelope, reply);
                break;
```
and the handler beside `HandleLogoutRequestAsync`:

```csharp
    private async Task HandleUpdateCheckRequestAsync(IpcEnvelope envelope, Func<IpcEnvelope, Task> reply)
    {
        var request = envelope.Payload?.Deserialize<UpdateCheckRequestPayload>();
        var result = string.IsNullOrWhiteSpace(request?.CurrentVersion)
            ? new UpdateCheckResultPayload(false, false, false, null, null, null, 0, null, "BAD_REQUEST")
            : await _apiClient.CheckForUpdateAsync(request.CurrentVersion, CancellationToken.None);

        await reply(new IpcEnvelope
        {
            Type = IpcMessageTypes.UpdateCheckResult,
            CorrelationId = envelope.CorrelationId,
            Payload = JsonSerializer.SerializeToElement(result)
        });
    }
```
Build: `dotnet build ONEVO.Agent.Service/ONEVO.Agent.Service.csproj --configuration Release` → Expected: 0 errors. If `AgentWorker` has a unit test that enumerates handled message types, extend it; run `dotnet test tests/ONEVO.Agent.Service.Tests` → Expected: PASS.

- [ ] **Step 6: TrayApp pipe client + UpdateChecker (failing test first)**

`INamedPipeClient.cs` — add:

```csharp
    /// <summary>Asks the Service to check for a newer installer and waits for UpdateCheckResult (or timeout).</summary>
    Task<UpdateCheckResultPayload?> SendUpdateCheckAsync(string currentVersion, CancellationToken ct);
```
(The interface has a default-implementation precedent for `SubmitInactivityAttemptAsync`; do NOT use one here — make it abstract and update `tests/ONEVO.Agent.TrayApp.Tests/Fakes/FakeNamedPipeClient.cs` to implement it, returning a configurable `UpdateCheckResultPayload? NextUpdateCheckResult`.)

`NamedPipeClient.cs` — copy `SendLogoutAsync` (lines 247–283) as `SendUpdateCheckAsync`, changing: the envelope `Type = IpcMessageTypes.UpdateCheckRequest`, `Payload = JsonSerializer.SerializeToElement(new UpdateCheckRequestPayload(currentVersion))`, the timeout to `TimeSpan.FromSeconds(20)`, the warning text to "Update check timed out", and the deserialized type to `UpdateCheckResultPayload`.

`tests/ONEVO.Agent.TrayApp.Tests/Services/UpdateCheckerTests.cs`:

```csharp
public sealed class UpdateCheckerTests
{
    [Fact]
    public async Task Check_ReturnsResult_WhenUpdateAvailable()
    {
        var pipe = new FakeNamedPipeClient { NextUpdateCheckResult = new UpdateCheckResultPayload(
            true, true, false, "1.3.0", "https://dl.example.com/a.msix", new string('a', 64), 10, null, null) };
        var checker = new UpdateChecker(pipe, () => "1.0.0");

        var result = await checker.CheckAsync(CancellationToken.None);

        Assert.True(result!.UpdateAvailable);
        Assert.Equal("1.3.0", result.LatestVersion);
    }

    [Fact]
    public async Task Check_ReturnsNull_WhenServiceDidNotAnswer()
    {
        var checker = new UpdateChecker(new FakeNamedPipeClient { NextUpdateCheckResult = null }, () => "1.0.0");
        Assert.Null(await checker.CheckAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Check_ReturnsNull_WhenCheckFailed()
    {
        var pipe = new FakeNamedPipeClient { NextUpdateCheckResult =
            new UpdateCheckResultPayload(false, false, false, null, null, null, 0, null, "SERVICE_UNAVAILABLE") };
        Assert.Null(await new UpdateChecker(pipe, () => "1.0.0").CheckAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Check_ReturnsNull_WhenNoUpdate()
    {
        var pipe = new FakeNamedPipeClient { NextUpdateCheckResult =
            new UpdateCheckResultPayload(true, false, false, null, null, null, 0, null, null) };
        Assert.Null(await new UpdateChecker(pipe, () => "1.0.0").CheckAsync(CancellationToken.None));
    }
}
```
Run `dotnet test tests/ONEVO.Agent.TrayApp.Tests/ONEVO.Agent.TrayApp.Tests.csproj --filter "FullyQualifiedName~UpdateCheckerTests"` → Expected: FAIL (missing types).

`ONEVO.Agent.TrayApp/Services/IUpdateChecker.cs`:

```csharp
namespace ONEVO.Agent.TrayApp.Services;

using ONEVO.Agent.Shared.IPC;

public interface IUpdateChecker
{
    /// <summary>Returns the pending update, or null when up to date / the check failed (failures are silent by design).</summary>
    Task<UpdateCheckResultPayload?> CheckAsync(CancellationToken ct);
}
```

`ONEVO.Agent.TrayApp/Services/UpdateChecker.cs`:

```csharp
namespace ONEVO.Agent.TrayApp.Services;

using ONEVO.Agent.Shared.IPC;

public sealed class UpdateChecker : IUpdateChecker
{
    private readonly INamedPipeClient _pipe;
    private readonly Func<string> _currentVersion;

    /// <summary>Production ctor: reads the packaged x.y.z (ApplicationDisplayVersion) via MAUI AppInfo.</summary>
    public UpdateChecker(INamedPipeClient pipe) : this(pipe, () => AppInfo.Current.VersionString) { }

    public UpdateChecker(INamedPipeClient pipe, Func<string> currentVersion)
    {
        _pipe = pipe;
        _currentVersion = currentVersion;
    }

    public async Task<UpdateCheckResultPayload?> CheckAsync(CancellationToken ct)
    {
        var result = await _pipe.SendUpdateCheckAsync(_currentVersion(), ct);
        return result is { Success: true, UpdateAvailable: true } ? result : null;
    }
}
```
If `AppInfo.Current.VersionString` returns a 4-part string on packaged Windows builds (check via Step 1 of Task 6: the manifest version has 4 parts), normalise inside the production constructor lambda: take the first three dot-separated parts. Add a test for that normalisation if you add it.

Run the tests. Expected: PASS (4). Register in `MauiProgram.cs` next to the other singleton services: `builder.Services.AddSingleton<IUpdateChecker, UpdateChecker>();` (mirror how `INamedPipeClient` is registered there).

- [ ] **Step 7: Surface the result**

Pick the least invasive surface after reading the existing UI: read `ONEVO.Agent.TrayApp/Services/NotificationService.cs` and `ViewModels/ConnectWorkspaceViewModel.cs`.

- **Optional update** (`Mandatory == false`): after the pipe connects and once every ~6 hours, call `IUpdateChecker.CheckAsync`; if non-null, show one toast through the existing `NotificationService` ("ONEVO update available — v{LatestVersion}") whose activation opens `DownloadUrl` via `Launcher.Default.OpenAsync(new Uri(url))`. Trigger the check from `MauiProgram`/`App.xaml.cs` startup where the pipe client is started (`grep -n "StartAsync" ONEVO.Agent.TrayApp/*.cs`), on a `PeriodicTimer`, with all exceptions swallowed and logged at debug.
- **Mandatory update** (`Mandatory == true`): add a `HintText`/`ErrorMessage` banner on `ConnectWorkspaceViewModel` ("A required update (v{LatestVersion}) is available. Download and install it to continue.") plus a **Download update** command that opens `DownloadUrl`. Do NOT hard-lock clock-in/out in this task: forced blocking belongs to a follow-up once the download-and-verify flow below is exercised in real use, because a bad `min_supported_version` would otherwise lock every employee out.
- **Before launching an installer the app downloads itself** (not required here — we only open the browser URL): if that is added later, verify `SHA-256` of the downloaded file equals `Sha256` from the payload before executing.

Write a ViewModel test (`ConnectWorkspaceViewModelTests` pattern, `FakeNamedPipeClient`/fake `IUpdateChecker`) asserting: when the checker returns a mandatory result the banner text is set and the download command is executable; when it returns null the banner is empty. Then implement until PASS.

- [ ] **Step 8: Full tray test + build**

Stop the running tray exe. Run:
```bash
dotnet test tests/ONEVO.Agent.Shared.Tests/ONEVO.Agent.Shared.Tests.csproj --configuration Release
dotnet test tests/ONEVO.Agent.Service.Tests/ONEVO.Agent.Service.Tests.csproj --configuration Release
dotnet test tests/ONEVO.Agent.TrayApp.Tests/ONEVO.Agent.TrayApp.Tests.csproj --configuration Release
```
Expected: all PASS (this is the same gate `ci.yml` enforces).

- [ ] **Step 9: End-to-end check with two versions**

1. Tag/build `v0.9.0` (Task 6 dry run) and install it on a test machine (trust the cert first).
2. In the admin app promote `0.9.0` to stable. Confirm the employee web gate offers the download (Task 5) and that the code flow connects the tray.
3. Tag `v0.9.1`, promote it. Within the check interval (or after restarting the tray), the installed `0.9.0` tray must raise the update toast pointing at the `0.9.1` URL.
4. In the admin app set `0.9.1`'s minimum supported version to `0.9.1`; restart the `0.9.0` tray; expected: mandatory banner on the connect screen.
5. Deactivate `0.9.1`; expected: no update offered (previous stable is not "newer").
Record the results in the PR description.

- [ ] **Step 10: Commit**

```bash
git add ONEVO.Agent.Shared ONEVO.Agent.Service ONEVO.Agent.TrayApp tests
git commit -m "feat(tray): check for updates via backend release registry

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

## Self-Review

**Spec coverage (the five steps requested):**
1. Move `TrayInstaller` config → DB table + public `latest` endpoint: Tasks 1–3.
2. Platform Admin "Tray Releases" page: Task 4 (metadata registration; in-browser upload explicitly deferred in Scope decision 3).
3. Web first-visit download + code: Task 5 (code generation already exists; only the download step is new).
4. GitHub Actions + storage: Task 6 (R2 + ingest; inactive-beta-then-promote lifecycle).
5. Tray update check: Task 7 (optional toast + mandatory banner; hard-lock deferred deliberately).
Version control: git tag → CI → `tray_app_releases` rows with channel/active/min-supported = the versioned, admin-controlled registry; rollback = deactivate.

**Placeholder scan:** The only intentionally delegated code is (a) copying existing private test helpers (`BuildInMemoryDb`, fake HTTP handler) from named files, (b) Angular template markup composed from named `shared/ui` components that the executor must read first (Task 4 Step 6), and (c) Task 7 Step 7's UI surface, which depends on MAUI views not read while planning. Each states exactly what to read and what behavior to assert.

**Type consistency:** `TrayReleaseDto` (camelCase) ↔ admin FE `TrayRelease`; `TrayInstallerInfoDto` (snake_case) ↔ web `TrayInstallerInfo` ↔ tray `UpdateLatestWire`; `TrayUpdateCheckDto` ↔ `UpdateCheckWire`; `CreateTrayReleaseRequest` fields ↔ `CreateTrayReleasePayload` ↔ CI ingest body (all camelCase, `isActive` included); `UpdateTrayReleaseRequest` ↔ `UpdateTrayReleasePayload`; channel strings `stable`/`beta`, source strings `admin`/`ci` used identically everywhere.

**Known risks to surface to the executor:**
- Pre-existing broken migration (project memory) may block `dotnet ef database update`; generating the migration is unaffected.
- MAUI MSIX version mapping is verified empirically in Task 6 Step 1 before CI is built on it.
- Anonymous endpoints on the tenant host rely on `[AllowAnonymous]` (confirmed in `TenantEnforcementMiddleware`); CSRF is skipped for cookie-less requests (confirmed in `CsrfProtectionMiddleware.ShouldValidate`).
- `Result<T>.Conflict` presence must be confirmed (used by `CreatePlatformServiceKeyCommandHandler`).
- Self-signed certs require manual trust on every test machine; a real code-signing certificate is required before customers.
