# Calendar External Sync (Google/Outlook) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the "External Calendar Integration" half of the Calendar spec that was never implemented: Google Calendar + Microsoft Graph (Outlook) OAuth connect/callback, connection management (list/update/disconnect/manual-sync), provider event clients, and a background sync job (pull/push/two-way) between `personal_calendar_events` and the external provider.

**Architecture:** Standard MediatR CQRS (Domain → Application → Infrastructure → Api), tenant-scoped via `TenantPolicy` + Postgres RLS for everything except the OAuth callback (a fixed, non-tenant-scoped, `[AllowAnonymous]` endpoint that recovers tenant identity from a signed state payload).

**Tech Stack:** .NET 10, EF Core (PostgreSQL, snake_case convention), MediatR, `IDataProtector` (OAuth state), `IEncryptionService` (token-at-rest), typed `HttpClient` (provider APIs), `BackgroundService` + `PeriodicTimer` (sync job, no Hangfire), xUnit + Moq + SQLite in-memory.

**Spec:** `docs/superpowers/specs/2026-08-28-calendar-core-external-sync-design.md` — this plan implements the spec's "External Calendar Integration" section only. The spec's "Calendar Core (CQRS)" section is **already shipped** on `development` (via PR #101 and follow-ups) — do not re-implement `calendar_events`/`calendar_event_participants`/`CalendarController`'s existing 9 routes.

## Global Constraints — read before Task 1

- **The spec is stale in several places — these corrections override anything the spec itself says:**
  - The core entity is `ONEVO.Domain.Features.Calendar.Entities.CalendarEvent`, table `personal_calendar_events` (renamed from `calendar_events` to avoid colliding with an unrelated `WorkManagement.CalendarEvents.CalendarEvent` → `calendar_events`). Never confuse the two.
  - `CalendarEvent` already has 24 properties including recurrence/timezone/RSVP-adjacent fields — you do not need to add anything to it.
  - `CalendarEventParticipant`'s `ResponseStatus` workflow (the spec called this "out of scope") is **already fully built**. Not your concern.
  - `CalendarController` (`src/ONEVO.Api/Controllers/Tenant/Calendar/CalendarController.cs`) already has 9 routes (list/create/update/delete/edit-occurrence/cancel-occurrence/respond/my-timezone/check-conflicts). This plan only **adds** new `connections/...` sub-routes to the same controller — never touch the existing 9 actions.
  - `PlatformOAuthProviderCatalog` provider keys are **`"google"`/`"microsoft"`** — a completely different vocabulary from `CalendarExternalSources`'s column values `"google_calendar"`/`"outlook_calendar"`. Every task below is explicit about which one to use where; never conflate them.
  - `feature/calendar-oauth-scopes` (the spec's "branch off this, not bare development" instruction) is already merged into `development` — branch normally, no special base needed.
  - `TenantRegistryEntry`'s fields are `(TenantId, Slug, Status, PlanCode)` — first field is named `TenantId`, not `Id`.
  - `SprintLifecycleJob` does **not** split into a separately-testable service class (only one pure static function is extracted) — do not cite it as prior art for testability. Task 9 below defines its own testable `CalendarSyncService` class instead.
- **Architecture-test landmine:** `tests/ONEVO.Tests.Architecture/PlatformOAuthAppsArchitectureTests.cs`'s `NoForbiddenLaterStepEntity_IsIntroduced` test currently asserts `"ExternalCalendarConnection"` does **not** exist anywhere in the Domain assembly. Task 1 removes it from that forbidden list — do this or every later task's build breaks.
- Tenant routes: `api/v1/calendar/connections/...`, `[Authorize(Policy = "TenantPolicy")]` + `[RequirePermission("calendar:read"|"calendar:write")]`. Exception: the callback controller (Task 4) is `[AllowAnonymous]` on its own `Controllers/Public/Calendar/` path — not tenant-scoped at all.
- Permissions already fully seeded (`calendar:read`/`calendar:write`/`calendar:admin`, module `"calendar"`) — do not add anything to `PermissionSeeder.cs`/`ModuleCatalogSeeder.cs`/`ModuleAutoGrants.cs`.
- Snake_case DB columns are automatic via `UseSnakeCaseNamingConvention()` — write PascalCase C# properties as normal.
- Every write handler runs inside `IUnitOfWork.ExecuteInTransactionAsync(...)`.
- Result pattern: `Result<T>.Success(value)` / `.Forbidden(msg)` / `.NotFound(msg)` / `.Failure(msg, statusCode)` — never throw for expected failure paths (the OAuth callback handler is the one place that also uses try/catch internally, because it must never surface a raw 500 — see Task 4).
- Token fields (`AccessTokenEncrypted`, `RefreshTokenEncrypted`) use `IEncryptionService.EncryptBytes(string) → byte[]` / `DecryptBytes(byte[]) → string` (note: `DecryptBytes` returns `string`, not `byte[]`). Never returned by any API response — the read-side DTOs in this plan never include them.
- `ExternalCalendarConnection`/`ExternalCalendarEventLink` implement `ITenantOwnedEntity` **directly** (NOT `BaseEntity`) — confirmed via `SoftDeleteInterceptor`/`AuditableEntityInterceptor`, both of which only target `ChangeTracker.Entries<BaseEntity>()`. Inheriting `BaseEntity` would silently soft-delete these rows on `Remove()` instead of hard-deleting them, and would add an unwanted `CreatedById`/`IsDeleted`/`DeletedAt` the spec's data model doesn't call for. The tenant RLS query filter still applies (it targets `ITenantOwnedEntity`, not `BaseEntity`), but `CreatedAt`/`UpdatedAt` must be set explicitly in handlers, not auto-stamped.
- HttpClient registrations use the codebase's existing typed-client convention (`services.AddHttpClient<TInterface, TImplementation>(...)`, see `GitHubOAuthTokenClient` in `src/ONEVO.Infrastructure/DependencyInjection.cs`) — not named-string clients.
- Config: add `"CalendarOAuthCallbackBaseUrl": ""` to `Urls` in `src/ONEVO.Api/appsettings.json`, and `"CalendarOAuthCallbackBaseUrl": "https://localhost:7229"` to `Urls` in `src/ONEVO.Api/appsettings.Development.json` (this matches the API's own Kestrel dev port from `launchSettings.json` — the callback must point at the API itself, not the SPA at `Urls:AppBaseUrl`).
- Tenant-subdomain redirects (Task 4) are built the same way `TenantSessionExchangeService.BuildContinueUrl` already does: `new UriBuilder(baseUri.Scheme, $"{slug}.{rootDomain}", baseUri.Port, path) { Query = query }`, reading `Tenancy:RootDomain` and `Urls:AppBaseUrl` from `IConfiguration`.

---

### Task 1: Domain entities, EF configuration, migration, and architecture-test fix

**Files:**
- Create: `src/ONEVO.Domain/Features/Calendar/Entities/ExternalCalendarConnection.cs`
- Create: `src/ONEVO.Domain/Features/Calendar/Entities/ExternalCalendarEventLink.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Configurations/Calendar/ExternalCalendarConnectionConfiguration.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Configurations/Calendar/ExternalCalendarEventLinkConfiguration.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/ApplicationDbContext.cs` — add two `DbSet<T>` properties
- Modify: `tests/ONEVO.Tests.Architecture/PlatformOAuthAppsArchitectureTests.cs` — remove `"ExternalCalendarConnection"` from the forbidden-type-names array
- Migration: generated via `dotnet ef migrations add AddExternalCalendarSync`

**Interfaces:**
- Produces: `ExternalCalendarConnection` (`Id, TenantId, UserId, Provider, ExternalAccountEmail, ExternalCalendarId, ExternalCalendarName, AccessTokenEncrypted, RefreshTokenEncrypted, ScopesJson, SyncDirection, Status, SyncTokenEncrypted, DeltaLinkEncrypted, FailureCount, LastSyncedAt, LastSuccessfulSyncAt, LastError, ExpiresAt, CreatedAt, UpdatedAt`), `ExternalCalendarEventLink` (`Id, TenantId, CalendarEventId, ExternalCalendarConnectionId, Provider, ExternalCalendarId, ExternalEventId, ExternalEtag, SyncDirection, SyncStatus, LastSyncedAt, LastError, CreatedAt, UpdatedAt`), plus constant classes `CalendarSyncDirections.{PullOnly,PushOnly,TwoWay,Disabled}`, `ExternalCalendarConnectionStatuses.{Active,ReauthRequired,Paused,Revoked,Failed}`, `ExternalCalendarSyncStatuses.{Synced,Pending,Failed,Skipped,Conflict}`, `ExternalCalendarLinkDirections.{Inbound,Outbound}`.

- [ ] **Step 1: Write the domain entities**

```csharp
// src/ONEVO.Domain/Features/Calendar/Entities/ExternalCalendarConnection.cs
using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Calendar.Entities;

public static class CalendarSyncDirections
{
    public const string PullOnly = "pull_only";
    public const string PushOnly = "push_only";
    public const string TwoWay = "two_way";
    public const string Disabled = "disabled";
}

public static class ExternalCalendarConnectionStatuses
{
    public const string Active = "active";
    public const string ReauthRequired = "reauth_required";
    public const string Paused = "paused";
    public const string Revoked = "revoked";
    public const string Failed = "failed";
}

public class ExternalCalendarConnection : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public string Provider { get; set; } = string.Empty; // CalendarExternalSources value: "google_calendar" | "outlook_calendar"
    public string ExternalAccountEmail { get; set; } = string.Empty;
    public string? ExternalCalendarId { get; set; }
    public string? ExternalCalendarName { get; set; }
    public byte[]? AccessTokenEncrypted { get; set; }
    public byte[] RefreshTokenEncrypted { get; set; } = [];
    public string ScopesJson { get; set; } = "[]";
    public string SyncDirection { get; set; } = CalendarSyncDirections.TwoWay;
    public string Status { get; set; } = ExternalCalendarConnectionStatuses.Active;
    public byte[]? SyncTokenEncrypted { get; set; }
    public byte[]? DeltaLinkEncrypted { get; set; }
    public int FailureCount { get; set; }
    public DateTimeOffset? LastSyncedAt { get; set; }
    public DateTimeOffset? LastSuccessfulSyncAt { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}
```

```csharp
// src/ONEVO.Domain/Features/Calendar/Entities/ExternalCalendarEventLink.cs
using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Calendar.Entities;

public static class ExternalCalendarSyncStatuses
{
    public const string Synced = "synced";
    public const string Pending = "pending";
    public const string Failed = "failed";
    public const string Skipped = "skipped";
    public const string Conflict = "conflict";
}

public static class ExternalCalendarLinkDirections
{
    public const string Inbound = "inbound";
    public const string Outbound = "outbound";
}

public class ExternalCalendarEventLink : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid CalendarEventId { get; set; }
    public Guid ExternalCalendarConnectionId { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string ExternalCalendarId { get; set; } = string.Empty;
    public string ExternalEventId { get; set; } = string.Empty;
    public string? ExternalEtag { get; set; }
    public string SyncDirection { get; set; } = ExternalCalendarLinkDirections.Inbound;
    public string SyncStatus { get; set; } = ExternalCalendarSyncStatuses.Pending;
    public DateTimeOffset? LastSyncedAt { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}
```

Both entities implement `ITenantOwnedEntity` directly (not `BaseEntity`) per the Global Constraints note above — do not add `IsDeleted`/`DeletedAt`/`CreatedById`.

- [ ] **Step 2: Write the EF configurations**

```csharp
// src/ONEVO.Infrastructure/Persistence/Configurations/Calendar/ExternalCalendarConnectionConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Calendar;

public class ExternalCalendarConnectionConfiguration : IEntityTypeConfiguration<ExternalCalendarConnection>
{
    public void Configure(EntityTypeBuilder<ExternalCalendarConnection> builder)
    {
        builder.ToTable("external_calendar_connections");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Provider).HasMaxLength(30).IsRequired();
        builder.Property(c => c.ExternalAccountEmail).HasMaxLength(255).IsRequired();
        builder.Property(c => c.ExternalCalendarId).HasMaxLength(255);
        builder.Property(c => c.ExternalCalendarName).HasMaxLength(255);
        builder.Property(c => c.ScopesJson).HasColumnName("scopes").HasColumnType("jsonb").IsRequired();
        builder.Property(c => c.SyncDirection).HasMaxLength(20).IsRequired();
        builder.Property(c => c.Status).HasMaxLength(20).IsRequired();
        builder.Property(c => c.RefreshTokenEncrypted).IsRequired();

        builder.HasIndex(c => new { c.TenantId, c.UserId })
            .HasDatabaseName("ix_external_calendar_connections_tenant_id_user_id");

        builder.HasIndex(c => new { c.TenantId, c.UserId, c.Provider })
            .IsUnique()
            .HasDatabaseName("ix_external_calendar_connections_one_per_user_provider");
    }
}
```

```csharp
// src/ONEVO.Infrastructure/Persistence/Configurations/Calendar/ExternalCalendarEventLinkConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Calendar;

public class ExternalCalendarEventLinkConfiguration : IEntityTypeConfiguration<ExternalCalendarEventLink>
{
    public void Configure(EntityTypeBuilder<ExternalCalendarEventLink> builder)
    {
        builder.ToTable("external_calendar_event_links");
        builder.HasKey(l => l.Id);

        builder.Property(l => l.Provider).HasMaxLength(30).IsRequired();
        builder.Property(l => l.ExternalCalendarId).HasMaxLength(255).IsRequired();
        builder.Property(l => l.ExternalEventId).HasMaxLength(255).IsRequired();
        builder.Property(l => l.ExternalEtag).HasMaxLength(255);
        builder.Property(l => l.SyncDirection).HasMaxLength(20).IsRequired();
        builder.Property(l => l.SyncStatus).HasMaxLength(20).IsRequired();

        builder.HasIndex(l => new { l.TenantId, l.CalendarEventId })
            .HasDatabaseName("ix_external_calendar_event_links_tenant_id_calendar_event_id");

        builder.HasIndex(l => new { l.TenantId, l.ExternalCalendarConnectionId })
            .HasDatabaseName("ix_external_calendar_event_links_tenant_id_connection_id");

        builder.HasIndex(l => new { l.ExternalCalendarConnectionId, l.ExternalEventId })
            .IsUnique()
            .HasDatabaseName("ix_external_calendar_event_links_one_per_connection_event");
    }
}
```

- [ ] **Step 3: Register the two new `DbSet<T>` properties**

Open `src/ONEVO.Infrastructure/Persistence/ApplicationDbContext.cs`, find where the other Calendar `DbSet`s (`CalendarEvents`, `CalendarEventParticipants`) are declared, and add next to them:

```csharp
public DbSet<ExternalCalendarConnection> ExternalCalendarConnections => Set<ExternalCalendarConnection>();
public DbSet<ExternalCalendarEventLink> ExternalCalendarEventLinks => Set<ExternalCalendarEventLink>();
```

- [ ] **Step 4: Fix the architecture-test landmine**

Open `tests/ONEVO.Tests.Architecture/PlatformOAuthAppsArchitectureTests.cs`, find the `forbiddenTypeNames` array inside `NoForbiddenLaterStepEntity_IsIntroduced`, and remove `"ExternalCalendarConnection"` from it, leaving the other entries (`EmailCredential`, `AiProviderConfig`, `TenantAiProviderOverride`, `SlackApp`, `SlackIntegration`, `SlackWorkspace`) untouched. Add a one-line comment above the array noting Calendar external sync now supersedes that forbidden entry.

- [ ] **Step 5: Build to confirm the entities/configs compile**

Stop the backend dev server if running (it locks the DLLs). Run: `dotnet build src/ONEVO.Infrastructure/ONEVO.Infrastructure.csproj --nologo`
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 6: Generate the migration**

```bash
export ConnectionStrings__MigrationConnection="Host=localhost;Port=5432;Database=OnevoDb;Username=onevo_migrator;Password=dapiyshanth@19"
```

Run: `dotnet ef migrations add AddExternalCalendarSync --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api`
Expected: a new file `src/ONEVO.Infrastructure/Migrations/<timestamp>_AddExternalCalendarSync.cs` and an updated `ApplicationDbContextModelSnapshot.cs`.

- [ ] **Step 7: Add the RLS policy SQL to the generated migration's `Up()`/`Down()`**

Mirror the existing pattern (check `20260831041001_AddCalendarCore.cs` or any recent migration in `src/ONEVO.Infrastructure/Migrations/` for the exact reference shape). Add this at the end of `Up()`:

```csharp
private static readonly string[] TenantTables = ["external_calendar_connections", "external_calendar_event_links"];

foreach (var table in TenantTables)
{
    migrationBuilder.Sql($@"
        ALTER TABLE {table} ENABLE ROW LEVEL SECURITY;
        ALTER TABLE {table} FORCE ROW LEVEL SECURITY;
        DROP POLICY IF EXISTS tenant_isolation ON {table};
        CREATE POLICY tenant_isolation ON {table}
            USING (
                current_setting('app.tenant_context_mode', true) = 'admin'
                OR (
                    current_setting('app.tenant_context_mode', true) = 'tenant'
                    AND tenant_id::text = current_setting('app.current_tenant_id', true)
                )
            )
            WITH CHECK (
                current_setting('app.tenant_context_mode', true) = 'admin'
                OR (
                    current_setting('app.tenant_context_mode', true) = 'tenant'
                    AND tenant_id::text = current_setting('app.current_tenant_id', true)
                )
            );
    ");
}
```

And in `Down()`, before the `DropTable` calls:

```csharp
foreach (var table in TenantTables)
{
    migrationBuilder.Sql($@"
        DROP POLICY IF EXISTS tenant_isolation ON {table};
        ALTER TABLE {table} DISABLE ROW LEVEL SECURITY;
    ");
}
```

- [ ] **Step 8: Apply the migration locally**

Run: `dotnet ef database update --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api`
Expected: `Applying migration '<timestamp>_AddExternalCalendarSync'.` then `Done.`

- [ ] **Step 9: Run the architecture suite to confirm the landmine is defused**

Run: `dotnet test tests/ONEVO.Tests.Architecture/ONEVO.Tests.Architecture.csproj --nologo`
Expected: all pass, including `NoForbiddenLaterStepEntity_IsIntroduced`.

- [ ] **Step 10: Commit**

```bash
git add src/ONEVO.Domain/Features/Calendar/Entities/ExternalCalendarConnection.cs src/ONEVO.Domain/Features/Calendar/Entities/ExternalCalendarEventLink.cs src/ONEVO.Infrastructure/Persistence/Configurations/Calendar/ src/ONEVO.Infrastructure/Persistence/ApplicationDbContext.cs src/ONEVO.Infrastructure/Migrations/ tests/ONEVO.Tests.Architecture/PlatformOAuthAppsArchitectureTests.cs
git commit -m "feat(calendar): add external_calendar_connections and external_calendar_event_links schema"
```

---

### Task 2: `ICalendarOAuthStateProtector`

**Files:**
- Create: `src/ONEVO.Application/Features/Calendar/ServiceInterfaces/ICalendarOAuthStateProtector.cs`
- Create: `src/ONEVO.Infrastructure/Security/CalendarOAuthStateProtector.cs`
- Modify: `src/ONEVO.Infrastructure/DependencyInjection.cs` — register the new protector
- Test: `tests/ONEVO.Tests.Unit/Features/Calendar/CalendarOAuthStateProtectorTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: `CalendarOAuthState(string Nonce, Guid TenantId, Guid UserId, string Provider, DateTimeOffset IssuedAtUtc, DateTimeOffset ExpiresAtUtc)`, `ICalendarOAuthStateProtector` with `Protect(CalendarOAuthState)` and `TryUnprotect(string, out CalendarOAuthState?)`.

- [ ] **Step 1: Confirm the existing `IOAuthStateProtector`'s exact shape**

Read `src/ONEVO.Infrastructure/Security/OAuthStateProtector.cs` and its interface (find it via the same directory or `src/ONEVO.Application/.../ServiceInterfaces/IOAuthStateProtector.cs`). Confirm the method names/signatures match what Step 2 below assumes (`Protect(TState state) => string`, `TryUnprotect(string, out TState?) => bool`, `IDataProtectionProvider.CreateProtector(purposeString)`). If the real interface differs, match its exact shape instead of the code below.

- [ ] **Step 2: Write the interface and state record**

```csharp
// src/ONEVO.Application/Features/Calendar/ServiceInterfaces/ICalendarOAuthStateProtector.cs
namespace ONEVO.Application.Features.Calendar.ServiceInterfaces;

public sealed record CalendarOAuthState(
    string Nonce,
    Guid TenantId,
    Guid UserId,
    string Provider,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset ExpiresAtUtc);

public interface ICalendarOAuthStateProtector
{
    string Protect(CalendarOAuthState state);
    bool TryUnprotect(string protectedState, out CalendarOAuthState? state);
}
```

- [ ] **Step 3: Write the implementation**

```csharp
// src/ONEVO.Infrastructure/Security/CalendarOAuthStateProtector.cs
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;

namespace ONEVO.Infrastructure.Security;

public sealed class CalendarOAuthStateProtector : ICalendarOAuthStateProtector
{
    private readonly IDataProtector _protector;

    public CalendarOAuthStateProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("ONEVO.CalendarOAuth.State.v1");
    }

    public string Protect(CalendarOAuthState state)
        => _protector.Protect(JsonSerializer.Serialize(state));

    public bool TryUnprotect(string protectedState, out CalendarOAuthState? state)
    {
        state = null;
        try
        {
            var json = _protector.Unprotect(protectedState);
            state = JsonSerializer.Deserialize<CalendarOAuthState>(json);
            return state is not null;
        }
        catch
        {
            return false;
        }
    }
}
```

The purpose string `"ONEVO.CalendarOAuth.State.v1"` must never be reused by any other feature's protector (each `IDataProtector` purpose string is its own isolated key namespace — mirrors `"ONEVO.GitHubUserOAuth.State.v1"`'s convention exactly).

- [ ] **Step 4: Register in DI**

Open `src/ONEVO.Infrastructure/DependencyInjection.cs`, find the line registering `IOAuthStateProtector`/`OAuthStateProtector` (likely `services.AddScoped<IOAuthStateProtector, OAuthStateProtector>();` or similar), and add next to it:

```csharp
services.AddScoped<ICalendarOAuthStateProtector, CalendarOAuthStateProtector>();
```

- [ ] **Step 5: Write and run the tests**

```csharp
// tests/ONEVO.Tests.Unit/Features/Calendar/CalendarOAuthStateProtectorTests.cs
using Microsoft.AspNetCore.DataProtection;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
using ONEVO.Infrastructure.Security;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class CalendarOAuthStateProtectorTests
{
    private static ICalendarOAuthStateProtector BuildSut()
    {
        var provider = DataProtectionProvider.Create("ONEVO.Tests.CalendarOAuth");
        return new CalendarOAuthStateProtector(provider);
    }

    [Fact]
    public void Protect_ThenUnprotect_RoundTripsTheState()
    {
        var sut = BuildSut();
        var state = new CalendarOAuthState("nonce-1", Guid.NewGuid(), Guid.NewGuid(), "google", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(10));

        var protectedState = sut.Protect(state);
        var ok = sut.TryUnprotect(protectedState, out var result);

        Assert.True(ok);
        Assert.Equal(state, result);
    }

    [Fact]
    public void TryUnprotect_TamperedPayload_ReturnsFalse()
    {
        var sut = BuildSut();
        var state = new CalendarOAuthState("nonce-1", Guid.NewGuid(), Guid.NewGuid(), "google", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(10));
        var protectedState = sut.Protect(state);
        var tampered = protectedState[..^2] + "xx";

        var ok = sut.TryUnprotect(tampered, out var result);

        Assert.False(ok);
        Assert.Null(result);
    }

    [Fact]
    public void TryUnprotect_ProtectedByADifferentProtector_ReturnsFalse()
    {
        var sut = BuildSut();
        var otherProvider = DataProtectionProvider.Create("ONEVO.Tests.SomeOtherPurpose");
        var otherProtector = new CalendarOAuthStateProtector(otherProvider);
        var state = new CalendarOAuthState("nonce-1", Guid.NewGuid(), Guid.NewGuid(), "google", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(10));
        var protectedByOther = otherProtector.Protect(state);

        var ok = sut.TryUnprotect(protectedByOther, out var result);

        Assert.False(ok);
        Assert.Null(result);
    }
}
```

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~CalendarOAuthStateProtectorTests" --nologo`
Expected: `Passed! - Failed: 0, Passed: 3`

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Application/Features/Calendar/ServiceInterfaces/ICalendarOAuthStateProtector.cs src/ONEVO.Infrastructure/Security/CalendarOAuthStateProtector.cs src/ONEVO.Infrastructure/DependencyInjection.cs tests/ONEVO.Tests.Unit/Features/Calendar/CalendarOAuthStateProtectorTests.cs
git commit -m "feat(calendar): add ICalendarOAuthStateProtector"
```

---

### Task 3: `StartCalendarConnectionCommand`

**Files:**
- Create: `src/ONEVO.Application/Features/Calendar/Commands/StartCalendarConnection/StartCalendarConnectionCommand.cs`
- Create: `src/ONEVO.Application/Features/Calendar/Commands/StartCalendarConnection/StartCalendarConnectionCommandHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Calendar/StartCalendarConnectionCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `ICalendarOAuthStateProtector` (Task 2), `IPlatformOAuthAppResolver.GetActiveAppForProviderAsync(string provider, CancellationToken ct)` → `ResolvedPlatformOAuthApp?` (existing, `src/ONEVO.Application/Features/DevPlatform/SystemConfig/PlatformOAuthApps/ServiceInterfaces/IPlatformOAuthAppResolver.cs`).
- Produces: `StartCalendarConnectionCommand(string Provider)` → `Result<StartCalendarConnectionResponse>`, `StartCalendarConnectionResponse(string AuthorizeUrl)`. Not wired into the controller yet — Task 6 wires this together with the rest of the connection-management routes.

- [ ] **Step 1: Write the command, response, and failing handler test**

```csharp
// src/ONEVO.Application/Features/Calendar/Commands/StartCalendarConnection/StartCalendarConnectionCommand.cs
using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Calendar.Commands.StartCalendarConnection;

public sealed record StartCalendarConnectionResponse(string AuthorizeUrl);

public sealed record StartCalendarConnectionCommand(string Provider) : IRequest<Result<StartCalendarConnectionResponse>>;
```

```csharp
// tests/ONEVO.Tests.Unit/Features/Calendar/StartCalendarConnectionCommandHandlerTests.cs
using Microsoft.Extensions.Configuration;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Commands.StartCalendarConnection;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.ServiceInterfaces;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class StartCalendarConnectionCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<IPlatformOAuthAppResolver> _appResolver = new();
    private readonly Mock<ICalendarOAuthStateProtector> _stateProtector = new();
    private readonly IConfiguration _configuration = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Urls:CalendarOAuthCallbackBaseUrl"] = "https://localhost:7229" })
        .Build();

    private StartCalendarConnectionCommandHandler BuildSut()
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        _currentUser.SetupGet(x => x.UserId).Returns(UserId);
        return new StartCalendarConnectionCommandHandler(_currentUser.Object, _appResolver.Object, _stateProtector.Object, _configuration);
    }

    [Fact]
    public async Task Handle_Unauthenticated_ReturnsForbidden()
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(false);
        var sut = new StartCalendarConnectionCommandHandler(_currentUser.Object, _appResolver.Object, _stateProtector.Object, _configuration);

        var result = await sut.Handle(new StartCalendarConnectionCommand("google"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Theory]
    [InlineData("dropbox")]
    [InlineData("")]
    public async Task Handle_UnsupportedProvider_ReturnsFailure(string provider)
    {
        var sut = BuildSut();

        var result = await sut.Handle(new StartCalendarConnectionCommand(provider), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ProviderNotConfigured_ReturnsFailure()
    {
        var sut = BuildSut();
        _appResolver.Setup(x => x.GetActiveAppForProviderAsync("google", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ResolvedPlatformOAuthApp?)null);

        var result = await sut.Handle(new StartCalendarConnectionCommand("google"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ValidProvider_ReturnsAuthorizeUrlContainingStateAndRedirectUri()
    {
        var sut = BuildSut();
        _appResolver.Setup(x => x.GetActiveAppForProviderAsync("google", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedPlatformOAuthApp("google", "client-123", "https://accounts.google.com/o/oauth2/v2/auth", "https://oauth2.googleapis.com/token", ["https://www.googleapis.com/auth/calendar"]));
        _stateProtector.Setup(x => x.Protect(It.Is<CalendarOAuthState>(s => s.TenantId == TenantId && s.UserId == UserId && s.Provider == "google")))
            .Returns("protected-state-token");

        var result = await sut.Handle(new StartCalendarConnectionCommand("google"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.StartsWith("https://accounts.google.com/o/oauth2/v2/auth?", result.Value!.AuthorizeUrl);
        Assert.Contains("client_id=client-123", result.Value.AuthorizeUrl);
        Assert.Contains("state=protected-state-token", result.Value.AuthorizeUrl);
        Assert.Contains(Uri.EscapeDataString("https://localhost:7229/api/v1/calendar/connections/google/callback"), result.Value.AuthorizeUrl);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~StartCalendarConnectionCommandHandlerTests" --nologo`
Expected: FAIL — `StartCalendarConnectionCommandHandler` does not exist yet.

- [ ] **Step 3: Write the handler**

```csharp
// src/ONEVO.Application/Features/Calendar/Commands/StartCalendarConnection/StartCalendarConnectionCommandHandler.cs
using System.Web;
using MediatR;
using Microsoft.Extensions.Configuration;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.ServiceInterfaces;

namespace ONEVO.Application.Features.Calendar.Commands.StartCalendarConnection;

public sealed class StartCalendarConnectionCommandHandler(
    ICurrentUser currentUser,
    IPlatformOAuthAppResolver appResolver,
    ICalendarOAuthStateProtector stateProtector,
    IConfiguration configuration)
    : IRequestHandler<StartCalendarConnectionCommand, Result<StartCalendarConnectionResponse>>
{
    private static readonly string[] SupportedProviders = ["google", "microsoft"];

    public async Task<Result<StartCalendarConnectionResponse>> Handle(StartCalendarConnectionCommand request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result<StartCalendarConnectionResponse>.Forbidden();

        if (!SupportedProviders.Contains(request.Provider, StringComparer.OrdinalIgnoreCase))
            return Result<StartCalendarConnectionResponse>.Failure("Unsupported calendar provider.", 400);

        var app = await appResolver.GetActiveAppForProviderAsync(request.Provider, ct);
        if (app is null)
            return Result<StartCalendarConnectionResponse>.Failure("This calendar provider is not configured.", 400);

        var now = DateTimeOffset.UtcNow;
        var state = new CalendarOAuthState(
            Nonce: Guid.NewGuid().ToString("N"),
            TenantId: currentUser.TenantId,
            UserId: currentUser.UserId,
            Provider: request.Provider,
            IssuedAtUtc: now,
            ExpiresAtUtc: now.AddMinutes(10));
        var protectedState = stateProtector.Protect(state);

        var callbackBaseUrl = (configuration["Urls:CalendarOAuthCallbackBaseUrl"] ?? string.Empty).TrimEnd('/');
        var redirectUri = $"{callbackBaseUrl}/api/v1/calendar/connections/{request.Provider}/callback";

        var query = HttpUtility.ParseQueryString(string.Empty);
        query["client_id"] = app.ClientId;
        query["redirect_uri"] = redirectUri;
        query["response_type"] = "code";
        query["scope"] = string.Join(' ', app.DefaultScopes);
        query["state"] = protectedState;
        if (request.Provider.Equals("google", StringComparison.OrdinalIgnoreCase))
        {
            query["access_type"] = "offline";
            query["prompt"] = "consent";
        }

        var authorizeUrl = $"{app.AuthorizationUrl}?{query}";
        return Result<StartCalendarConnectionResponse>.Success(new StartCalendarConnectionResponse(authorizeUrl));
    }
}
```

`HttpUtility.ParseQueryString(string.Empty).ToString()` URL-encodes values automatically, so the test's `Uri.EscapeDataString(...)` assertion on the redirect_uri substring must match the encoded form — if the assertion fails on encoding differences (e.g. `%3a` vs `%3A` case, or space-as-`+` vs `%20`), adjust the test assertion to match `HttpUtility`'s actual output rather than changing the handler.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~StartCalendarConnectionCommandHandlerTests" --nologo`
Expected: `Passed! - Failed: 0, Passed: 5`

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Features/Calendar/Commands/StartCalendarConnection/ tests/ONEVO.Tests.Unit/Features/Calendar/StartCalendarConnectionCommandHandlerTests.cs
git commit -m "feat(calendar): add StartCalendarConnectionCommand"
```

---

### Task 4: Repositories for connections and event links

**Files:**
- Create: `src/ONEVO.Application/Features/Calendar/RepositoryInterfaces/IExternalCalendarConnectionRepository.cs`
- Create: `src/ONEVO.Application/Features/Calendar/RepositoryInterfaces/IExternalCalendarEventLinkRepository.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Repositories/Calendar/EfExternalCalendarConnectionRepository.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Repositories/Calendar/EfExternalCalendarEventLinkRepository.cs`
- Modify: `src/ONEVO.Infrastructure/DependencyInjection.cs` — register both repositories
- Test: `tests/ONEVO.Tests.Unit/Features/Calendar/EfExternalCalendarConnectionRepositoryTests.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Calendar/EfExternalCalendarEventLinkRepositoryTests.cs`

**Interfaces:**
- Consumes: `ExternalCalendarConnection`, `ExternalCalendarEventLink` (Task 1).
- Produces: `IExternalCalendarConnectionRepository` with `AddAsync`, `GetByTenantUserProviderAsync`, `GetByIdForTenantAsync`, `GetTrackedByIdForTenantAsync`, `GetForUserAsync`, `GetActiveAsync`, `Update`, `Remove`. `IExternalCalendarEventLinkRepository` with `AddAsync`, `GetTrackedByConnectionAndExternalEventAsync`, `GetTrackedByCalendarEventAndConnectionAsync`, `GetByConnectionIdAsync`, `Update`, `Remove`, `RemoveRange`.

- [ ] **Step 1: Write the repository interfaces**

```csharp
// src/ONEVO.Application/Features/Calendar/RepositoryInterfaces/IExternalCalendarConnectionRepository.cs
using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Application.Features.Calendar.RepositoryInterfaces;

public interface IExternalCalendarConnectionRepository
{
    Task AddAsync(ExternalCalendarConnection connection, CancellationToken ct = default);
    Task<ExternalCalendarConnection?> GetByTenantUserProviderAsync(Guid tenantId, Guid userId, string provider, CancellationToken ct = default);
    Task<ExternalCalendarConnection?> GetByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default);
    Task<ExternalCalendarConnection?> GetTrackedByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<ExternalCalendarConnection>> GetForUserAsync(Guid tenantId, Guid userId, CancellationToken ct = default);

    /// <summary>All non-disabled connections visible under the currently-switched tenant context
    /// (RLS + the EF tenant query filter already scope this to one tenant at a time — the sync
    /// job calls this once per tenant inside its own per-tenant loop, not across all tenants).</summary>
    Task<IReadOnlyList<ExternalCalendarConnection>> GetActiveAsync(CancellationToken ct = default);

    void Update(ExternalCalendarConnection connection);
    void Remove(ExternalCalendarConnection connection);
}
```

```csharp
// src/ONEVO.Application/Features/Calendar/RepositoryInterfaces/IExternalCalendarEventLinkRepository.cs
using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Application.Features.Calendar.RepositoryInterfaces;

public interface IExternalCalendarEventLinkRepository
{
    Task AddAsync(ExternalCalendarEventLink link, CancellationToken ct = default);
    Task<ExternalCalendarEventLink?> GetTrackedByConnectionAndExternalEventAsync(Guid tenantId, Guid connectionId, string externalEventId, CancellationToken ct = default);
    Task<ExternalCalendarEventLink?> GetTrackedByCalendarEventAndConnectionAsync(Guid tenantId, Guid calendarEventId, Guid connectionId, CancellationToken ct = default);
    Task<IReadOnlyList<ExternalCalendarEventLink>> GetByConnectionIdAsync(Guid tenantId, Guid connectionId, CancellationToken ct = default);
    void Update(ExternalCalendarEventLink link);
    void Remove(ExternalCalendarEventLink link);
    void RemoveRange(IEnumerable<ExternalCalendarEventLink> links);
}
```

- [ ] **Step 2: Write the failing repository tests**

```csharp
// tests/ONEVO.Tests.Unit/Features/Calendar/EfExternalCalendarConnectionRepositoryTests.cs
using Microsoft.EntityFrameworkCore;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.Calendar;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class EfExternalCalendarConnectionRepositoryTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    [Fact]
    public async Task GetByTenantUserProviderAsync_ReturnsMatchingConnection()
    {
        await using var db = BuildInMemoryDb();
        var connection = MakeConnection(status: ExternalCalendarConnectionStatuses.Active);
        db.ExternalCalendarConnections.Add(connection);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfExternalCalendarConnectionRepository(db);
        var result = await repository.GetByTenantUserProviderAsync(TenantId, UserId, CalendarExternalSources.GoogleCalendar, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(connection.Id, result!.Id);
    }

    [Fact]
    public async Task GetActiveAsync_ExcludesDisabledConnections()
    {
        await using var db = BuildInMemoryDb();
        var active = MakeConnection(status: ExternalCalendarConnectionStatuses.Active);
        var failed = MakeConnection(status: ExternalCalendarConnectionStatuses.Failed);
        db.ExternalCalendarConnections.AddRange(active, failed);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfExternalCalendarConnectionRepository(db);
        var result = await repository.GetActiveAsync(CancellationToken.None);

        Assert.Single(result);
        Assert.Equal(active.Id, result[0].Id);
    }

    private static ExternalCalendarConnection MakeConnection(string status) => new()
    {
        Id = Guid.NewGuid(), TenantId = TenantId, UserId = UserId,
        Provider = CalendarExternalSources.GoogleCalendar, ExternalAccountEmail = "user@example.com",
        RefreshTokenEncrypted = [1, 2, 3], ScopesJson = "[]",
        SyncDirection = CalendarSyncDirections.TwoWay, Status = status, CreatedAt = DateTimeOffset.UtcNow
    };

    private static ApplicationDbContext BuildInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var currentUser = new Mock<ICurrentUser>();
        var dateTimeProvider = new Mock<IDateTimeProvider>();
        var publisher = new Mock<MediatR.IPublisher>();
        var tenantContext = new Mock<ONEVO.Application.Common.ServiceInterfaces.ITenantContext>();
        return new ApplicationDbContext(
            options,
            new AuditableEntityInterceptor(currentUser.Object, dateTimeProvider.Object),
            new SoftDeleteInterceptor(dateTimeProvider.Object),
            new DomainEventDispatchInterceptor(publisher.Object),
            tenantContext.Object);
    }
}
```

`GetActiveAsync`'s "active" filter means `Status != ExternalCalendarConnectionStatuses.Failed && Status != ExternalCalendarConnectionStatuses.Revoked` (the sync job's own `SyncDirection == Disabled` check happens separately in Task 9's loop, not here) — the test above only needs one negative case to prove the filter exists; do not over-specify every status combination in this repository test, the sync job's own tests (Task 9) cover the full status matrix.

Write `EfExternalCalendarEventLinkRepositoryTests.cs` following the exact same structure (`BuildInMemoryDb` helper duplicated, one test proving `GetTrackedByConnectionAndExternalEventAsync` finds a row by connection+external id, one proving `GetByConnectionIdAsync` returns all links for a connection and none for another).

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~EfExternalCalendarConnectionRepositoryTests|FullyQualifiedName~EfExternalCalendarEventLinkRepositoryTests" --nologo`
Expected: FAIL — the repository classes don't exist yet.

- [ ] **Step 4: Write the repository implementations**

```csharp
// src/ONEVO.Infrastructure/Persistence/Repositories/Calendar/EfExternalCalendarConnectionRepository.cs
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.Calendar;

public class EfExternalCalendarConnectionRepository(ApplicationDbContext db) : IExternalCalendarConnectionRepository
{
    public async Task AddAsync(ExternalCalendarConnection connection, CancellationToken ct = default)
        => await db.ExternalCalendarConnections.AddAsync(connection, ct);

    public async Task<ExternalCalendarConnection?> GetByTenantUserProviderAsync(Guid tenantId, Guid userId, string provider, CancellationToken ct = default)
        => await db.ExternalCalendarConnections.FirstOrDefaultAsync(
            c => c.TenantId == tenantId && c.UserId == userId && c.Provider == provider, ct);

    public async Task<ExternalCalendarConnection?> GetByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default)
        => await db.ExternalCalendarConnections.AsNoTracking().FirstOrDefaultAsync(c => c.TenantId == tenantId && c.Id == id, ct);

    public async Task<ExternalCalendarConnection?> GetTrackedByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default)
        => await db.ExternalCalendarConnections.FirstOrDefaultAsync(c => c.TenantId == tenantId && c.Id == id, ct);

    public async Task<IReadOnlyList<ExternalCalendarConnection>> GetForUserAsync(Guid tenantId, Guid userId, CancellationToken ct = default)
        => await db.ExternalCalendarConnections.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.UserId == userId)
            .OrderBy(c => c.Provider)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ExternalCalendarConnection>> GetActiveAsync(CancellationToken ct = default)
        => await db.ExternalCalendarConnections
            .Where(c => c.Status != ExternalCalendarConnectionStatuses.Failed && c.Status != ExternalCalendarConnectionStatuses.Revoked)
            .ToListAsync(ct);

    public void Update(ExternalCalendarConnection connection) => db.ExternalCalendarConnections.Update(connection);
    public void Remove(ExternalCalendarConnection connection) => db.ExternalCalendarConnections.Remove(connection);
}
```

```csharp
// src/ONEVO.Infrastructure/Persistence/Repositories/Calendar/EfExternalCalendarEventLinkRepository.cs
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.Calendar;

public class EfExternalCalendarEventLinkRepository(ApplicationDbContext db) : IExternalCalendarEventLinkRepository
{
    public async Task AddAsync(ExternalCalendarEventLink link, CancellationToken ct = default)
        => await db.ExternalCalendarEventLinks.AddAsync(link, ct);

    public async Task<ExternalCalendarEventLink?> GetTrackedByConnectionAndExternalEventAsync(Guid tenantId, Guid connectionId, string externalEventId, CancellationToken ct = default)
        => await db.ExternalCalendarEventLinks.FirstOrDefaultAsync(
            l => l.TenantId == tenantId && l.ExternalCalendarConnectionId == connectionId && l.ExternalEventId == externalEventId, ct);

    public async Task<ExternalCalendarEventLink?> GetTrackedByCalendarEventAndConnectionAsync(Guid tenantId, Guid calendarEventId, Guid connectionId, CancellationToken ct = default)
        => await db.ExternalCalendarEventLinks.FirstOrDefaultAsync(
            l => l.TenantId == tenantId && l.CalendarEventId == calendarEventId && l.ExternalCalendarConnectionId == connectionId, ct);

    public async Task<IReadOnlyList<ExternalCalendarEventLink>> GetByConnectionIdAsync(Guid tenantId, Guid connectionId, CancellationToken ct = default)
        => await db.ExternalCalendarEventLinks.AsNoTracking()
            .Where(l => l.TenantId == tenantId && l.ExternalCalendarConnectionId == connectionId)
            .ToListAsync(ct);

    public void Update(ExternalCalendarEventLink link) => db.ExternalCalendarEventLinks.Update(link);
    public void Remove(ExternalCalendarEventLink link) => db.ExternalCalendarEventLinks.Remove(link);
    public void RemoveRange(IEnumerable<ExternalCalendarEventLink> links) => db.ExternalCalendarEventLinks.RemoveRange(links);
}
```

- [ ] **Step 5: Register in DI**

Open `src/ONEVO.Infrastructure/DependencyInjection.cs`, find where `ICalendarEventRepository`/`EfCalendarEventRepository` is registered, and add next to it:

```csharp
services.AddScoped<IExternalCalendarConnectionRepository, EfExternalCalendarConnectionRepository>();
services.AddScoped<IExternalCalendarEventLinkRepository, EfExternalCalendarEventLinkRepository>();
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~EfExternalCalendarConnectionRepositoryTests|FullyQualifiedName~EfExternalCalendarEventLinkRepositoryTests" --nologo`
Expected: `Passed! - Failed: 0, Passed: 4`

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Application/Features/Calendar/RepositoryInterfaces/IExternalCalendarConnectionRepository.cs src/ONEVO.Application/Features/Calendar/RepositoryInterfaces/IExternalCalendarEventLinkRepository.cs src/ONEVO.Infrastructure/Persistence/Repositories/Calendar/EfExternalCalendarConnectionRepository.cs src/ONEVO.Infrastructure/Persistence/Repositories/Calendar/EfExternalCalendarEventLinkRepository.cs src/ONEVO.Infrastructure/DependencyInjection.cs tests/ONEVO.Tests.Unit/Features/Calendar/EfExternalCalendarConnectionRepositoryTests.cs tests/ONEVO.Tests.Unit/Features/Calendar/EfExternalCalendarEventLinkRepositoryTests.cs
git commit -m "feat(calendar): add external calendar connection/link repositories"
```

---

### Task 5: `ICalendarOAuthTokenExchangeClient` (token exchange + account lookup + refresh)

This is separate from the event-CRUD provider clients (Task 8/9) — it handles the one-time "exchange code for tokens" and "who is this / what's their primary calendar" calls made during connect (Task 6), plus the "refresh an expiring access token" call made repeatedly by the sync job (Task 10). Event CRUD (list/insert/patch/delete events) is a different concern with its own interface per provider.

**Files:**
- Create: `src/ONEVO.Application/Features/Calendar/ServiceInterfaces/ICalendarOAuthTokenExchangeClient.cs`
- Create: `src/ONEVO.Infrastructure/ExternalServices/Calendar/CalendarOAuthTokenExchangeClient.cs`
- Modify: `src/ONEVO.Infrastructure/DependencyInjection.cs` — register as a typed `HttpClient`
- Test: `tests/ONEVO.Tests.Unit/Features/Calendar/CalendarOAuthTokenExchangeClientTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks (pure HTTP client).
- Produces: `CalendarProviderTokens(string AccessToken, string? RefreshToken, DateTimeOffset? ExpiresAt)`, `CalendarProviderAccount(string AccountEmail, string? PrimaryCalendarId, string? PrimaryCalendarName)`, `ICalendarOAuthTokenExchangeClient` with `ExchangeCodeAsync`, `RefreshTokenAsync`, `GetAccountAsync`.

- [ ] **Step 1: Write the interface and DTOs**

```csharp
// src/ONEVO.Application/Features/Calendar/ServiceInterfaces/ICalendarOAuthTokenExchangeClient.cs
namespace ONEVO.Application.Features.Calendar.ServiceInterfaces;

public sealed record CalendarProviderTokens(string AccessToken, string? RefreshToken, DateTimeOffset? ExpiresAt);
public sealed record CalendarProviderAccount(string AccountEmail, string? PrimaryCalendarId, string? PrimaryCalendarName);

public interface ICalendarOAuthTokenExchangeClient
{
    Task<CalendarProviderTokens> ExchangeCodeAsync(string tokenUrl, string clientId, string clientSecret, string code, string redirectUri, CancellationToken ct);
    Task<CalendarProviderTokens> RefreshTokenAsync(string tokenUrl, string clientId, string clientSecret, string refreshToken, CancellationToken ct);

    /// <summary>provider is "google" | "microsoft" (PlatformOAuthProviderCatalog vocabulary, not
    /// CalendarExternalSources) — matches what StartCalendarConnectionCommand/the callback route
    /// already carry, so callers never have to remap.</summary>
    Task<CalendarProviderAccount> GetAccountAsync(string provider, string accessToken, CancellationToken ct);
}
```

- [ ] **Step 2: Write the implementation**

```csharp
// src/ONEVO.Infrastructure/ExternalServices/Calendar/CalendarOAuthTokenExchangeClient.cs
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;

namespace ONEVO.Infrastructure.ExternalServices.Calendar;

public sealed class CalendarOAuthTokenExchangeClient(HttpClient httpClient) : ICalendarOAuthTokenExchangeClient
{
    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: JsonPropertyName("expires_in")] int? ExpiresIn);

    public async Task<CalendarProviderTokens> ExchangeCodeAsync(string tokenUrl, string clientId, string clientSecret, string code, string redirectUri, CancellationToken ct)
    {
        var form = new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["grant_type"] = "authorization_code"
        };
        return await PostTokenRequestAsync(tokenUrl, form, ct);
    }

    public async Task<CalendarProviderTokens> RefreshTokenAsync(string tokenUrl, string clientId, string clientSecret, string refreshToken, CancellationToken ct)
    {
        var form = new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["refresh_token"] = refreshToken,
            ["grant_type"] = "refresh_token"
        };
        var tokens = await PostTokenRequestAsync(tokenUrl, form, ct);
        // Refresh responses often omit refresh_token (it doesn't rotate) - keep the caller's original.
        return tokens.RefreshToken is null ? tokens with { RefreshToken = refreshToken } : tokens;
    }

    private async Task<CalendarProviderTokens> PostTokenRequestAsync(string tokenUrl, Dictionary<string, string> form, CancellationToken ct)
    {
        using var response = await httpClient.PostAsync(tokenUrl, new FormUrlEncodedContent(form), ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Token endpoint returned an empty response.");
        var expiresAt = body.ExpiresIn.HasValue ? DateTimeOffset.UtcNow.AddSeconds(body.ExpiresIn.Value) : (DateTimeOffset?)null;
        return new CalendarProviderTokens(body.AccessToken, body.RefreshToken, expiresAt);
    }

    public async Task<CalendarProviderAccount> GetAccountAsync(string provider, string accessToken, CancellationToken ct)
    {
        return provider.Equals("google", StringComparison.OrdinalIgnoreCase)
            ? await GetGoogleAccountAsync(accessToken, ct)
            : await GetMicrosoftAccountAsync(accessToken, ct);
    }

    private async Task<CalendarProviderAccount> GetGoogleAccountAsync(string accessToken, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://www.googleapis.com/calendar/v3/users/me/calendarList");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        foreach (var item in doc.RootElement.GetProperty("items").EnumerateArray())
        {
            if (item.TryGetProperty("primary", out var primary) && primary.GetBoolean())
            {
                var id = item.GetProperty("id").GetString()!; // Google's primary calendar id IS the account email.
                var name = item.TryGetProperty("summary", out var summary) ? summary.GetString() : null;
                return new CalendarProviderAccount(id, id, name);
            }
        }
        throw new InvalidOperationException("No primary Google calendar found for this account.");
    }

    private async Task<CalendarProviderAccount> GetMicrosoftAccountAsync(string accessToken, CancellationToken ct)
    {
        using var meRequest = new HttpRequestMessage(HttpMethod.Get, "https://graph.microsoft.com/v1.0/me?$select=mail,userPrincipalName");
        meRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var meResponse = await httpClient.SendAsync(meRequest, ct);
        meResponse.EnsureSuccessStatusCode();
        using var meStream = await meResponse.Content.ReadAsStreamAsync(ct);
        using var meDoc = await JsonDocument.ParseAsync(meStream, cancellationToken: ct);
        var email = (meDoc.RootElement.TryGetProperty("mail", out var mail) ? mail.GetString() : null)
            ?? meDoc.RootElement.GetProperty("userPrincipalName").GetString()!;

        using var calRequest = new HttpRequestMessage(HttpMethod.Get, "https://graph.microsoft.com/v1.0/me/calendars");
        calRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var calResponse = await httpClient.SendAsync(calRequest, ct);
        calResponse.EnsureSuccessStatusCode();
        using var calStream = await calResponse.Content.ReadAsStreamAsync(ct);
        using var calDoc = await JsonDocument.ParseAsync(calStream, cancellationToken: ct);

        foreach (var item in calDoc.RootElement.GetProperty("value").EnumerateArray())
        {
            if (item.TryGetProperty("isDefaultCalendar", out var isDefault) && isDefault.GetBoolean())
            {
                var id = item.GetProperty("id").GetString();
                var name = item.TryGetProperty("name", out var n) ? n.GetString() : null;
                return new CalendarProviderAccount(email, id, name);
            }
        }
        return new CalendarProviderAccount(email, null, null);
    }
}
```

Per the spec's own "Open Items" note, confirm `primary`/`isDefaultCalendar` are still the correct current field names against Google Calendar API v3 / Microsoft Graph v1.0 docs before running this against real accounts — both are long-stable fields, low risk, but verify rather than trust this plan's memory of them.

- [ ] **Step 3: Register as a typed HttpClient**

Open `src/ONEVO.Infrastructure/DependencyInjection.cs`, find the `services.AddHttpClient<IGitHubOAuthClient, GitHubOAuthTokenClient>(client => {...})` registration and add a sibling:

```csharp
services.AddHttpClient<ICalendarOAuthTokenExchangeClient, CalendarOAuthTokenExchangeClient>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});
```

- [ ] **Step 4: Write and run the tests**

```csharp
// tests/ONEVO.Tests.Unit/Features/Calendar/CalendarOAuthTokenExchangeClientTests.cs
using System.Net;
using System.Text;
using System.Text.Json;
using ONEVO.Infrastructure.ExternalServices.Calendar;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class CalendarOAuthTokenExchangeClientTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(respond(request));
    }

    private static HttpResponseMessage JsonResponse(object body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
    };

    [Fact]
    public async Task ExchangeCodeAsync_ParsesTokensAndComputesExpiry()
    {
        var handler = new StubHandler(_ => JsonResponse(new { access_token = "at-1", refresh_token = "rt-1", expires_in = 3600 }));
        var sut = new CalendarOAuthTokenExchangeClient(new HttpClient(handler));

        var result = await sut.ExchangeCodeAsync("https://oauth2.googleapis.com/token", "client", "secret", "code", "https://localhost:7229/callback", CancellationToken.None);

        Assert.Equal("at-1", result.AccessToken);
        Assert.Equal("rt-1", result.RefreshToken);
        Assert.NotNull(result.ExpiresAt);
        Assert.True(result.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(59));
    }

    [Fact]
    public async Task RefreshTokenAsync_ProviderOmitsRefreshToken_KeepsOriginal()
    {
        var handler = new StubHandler(_ => JsonResponse(new { access_token = "at-2", expires_in = 3600 }));
        var sut = new CalendarOAuthTokenExchangeClient(new HttpClient(handler));

        var result = await sut.RefreshTokenAsync("https://oauth2.googleapis.com/token", "client", "secret", "original-refresh-token", CancellationToken.None);

        Assert.Equal("at-2", result.AccessToken);
        Assert.Equal("original-refresh-token", result.RefreshToken);
    }

    [Fact]
    public async Task GetAccountAsync_Google_ReturnsPrimaryCalendarAsAccountEmail()
    {
        var handler = new StubHandler(_ => JsonResponse(new
        {
            items = new[]
            {
                new { id = "coworker@example.com", primary = false, summary = "Coworker" },
                new { id = "me@example.com", primary = true, summary = "me@example.com" }
            }
        }));
        var sut = new CalendarOAuthTokenExchangeClient(new HttpClient(handler));

        var result = await sut.GetAccountAsync("google", "at-1", CancellationToken.None);

        Assert.Equal("me@example.com", result.AccountEmail);
        Assert.Equal("me@example.com", result.PrimaryCalendarId);
    }
}
```

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~CalendarOAuthTokenExchangeClientTests" --nologo`
Expected: `Passed! - Failed: 0, Passed: 3`

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Features/Calendar/ServiceInterfaces/ICalendarOAuthTokenExchangeClient.cs src/ONEVO.Infrastructure/ExternalServices/Calendar/CalendarOAuthTokenExchangeClient.cs src/ONEVO.Infrastructure/DependencyInjection.cs tests/ONEVO.Tests.Unit/Features/Calendar/CalendarOAuthTokenExchangeClientTests.cs
git commit -m "feat(calendar): add ICalendarOAuthTokenExchangeClient"
```

---

### Task 6: `CompleteCalendarConnectionCommand` + `CalendarOAuthCallbackController`

The OAuth callback. Runs `[AllowAnonymous]` on a fixed host — no `TenantPolicy`, no `ICurrentUser`. Tenant identity comes entirely from the decrypted state. Must never surface a raw 500: everything after the tenant is established and switched is wrapped so failures still produce a redirect with `connectionError=1`.

**Files:**
- Create: `src/ONEVO.Application/Features/Calendar/Commands/CompleteCalendarConnection/CompleteCalendarConnectionCommand.cs`
- Create: `src/ONEVO.Application/Features/Calendar/Commands/CompleteCalendarConnection/CompleteCalendarConnectionCommandHandler.cs`
- Create: `src/ONEVO.Api/Controllers/Public/Calendar/CalendarOAuthCallbackController.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Calendar/CompleteCalendarConnectionCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `ICalendarOAuthStateProtector` (Task 2), `IExternalCalendarConnectionRepository` (Task 4), `ICalendarOAuthTokenExchangeClient` (Task 5), `ITenantRepository.GetByIdAsync`, `ITenantContextSwitcher.SwitchToTenantAsync`, `IPlatformOAuthAppResolver` (existing), `IEncryptionService.EncryptBytes` (existing).
- Produces: `CompleteCalendarConnectionCommand(string Provider, string Code, string State)` → `Result<string>` (the string is a redirect URL — success and handled-failure both return `Result.Success` with a different URL; only state-validation failures return `Result.Failure` with `StatusCode = 400`).

- [ ] **Step 1: Confirm `Tenant.Status` and `TenantStatus.Active`**

Read `src/ONEVO.Domain/Features/InfrastructureModule/Tenancy/Entities/Tenant.cs` (or wherever the `Tenant` domain entity lives) to confirm it exposes `Id`, `Slug`, `Status` with types matching `TenantRegistryEntry`'s constructor (`Guid`, `string`, `TenantStatus`). This should already be true (`TenantSessionExchangeService` and the sync job in Task 10 both rely on the same shape) — this step is a sanity check, not new discovery.

- [ ] **Step 2: Write the command and its failing handler test**

```csharp
// src/ONEVO.Application/Features/Calendar/Commands/CompleteCalendarConnection/CompleteCalendarConnectionCommand.cs
using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Calendar.Commands.CompleteCalendarConnection;

public sealed record CompleteCalendarConnectionCommand(string Provider, string Code, string State) : IRequest<Result<string>>;
```

```csharp
// tests/ONEVO.Tests.Unit/Features/Calendar/CompleteCalendarConnectionCommandHandlerTests.cs
using Microsoft.Extensions.Configuration;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Commands.CompleteCalendarConnection;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Tenancy.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class CompleteCalendarConnectionCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly Mock<ICalendarOAuthStateProtector> _stateProtector = new();
    private readonly Mock<ITenantRepository> _tenants = new();
    private readonly Mock<ITenantContextSwitcher> _tenantSwitcher = new();
    private readonly Mock<IPlatformOAuthAppResolver> _appResolver = new();
    private readonly Mock<ICalendarOAuthTokenExchangeClient> _tokenClient = new();
    private readonly Mock<IExternalCalendarConnectionRepository> _connections = new();
    private readonly Mock<IEncryptionService> _encryption = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly IConfiguration _configuration = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Urls:CalendarOAuthCallbackBaseUrl"] = "https://localhost:7229",
            ["Urls:AppBaseUrl"] = "https://onexso.com:4200",
            ["Tenancy:RootDomain"] = "onexso.com"
        })
        .Build();

    private CompleteCalendarConnectionCommandHandler BuildSut()
    {
        _unitOfWork
            .Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result<string>>>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<Result<string>>>, CancellationToken>((action, ct) => action(ct));
        return new CompleteCalendarConnectionCommandHandler(
            _stateProtector.Object, _tenants.Object, _tenantSwitcher.Object, _appResolver.Object,
            _tokenClient.Object, _connections.Object, _encryption.Object, _unitOfWork.Object, _configuration);
    }

    private static CalendarOAuthState ValidState(DateTimeOffset? expiresAtUtc = null) => new(
        "nonce", TenantId, UserId, "google", DateTimeOffset.UtcNow, expiresAtUtc ?? DateTimeOffset.UtcNow.AddMinutes(5));

    [Fact]
    public async Task Handle_UnparseableState_ReturnsFailure400()
    {
        var sut = BuildSut();
        _stateProtector.Setup(x => x.TryUnprotect("bad-state", out It.Ref<CalendarOAuthState?>.IsAny)).Returns(false);

        var result = await sut.Handle(new CompleteCalendarConnectionCommand("google", "code", "bad-state"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ExpiredState_ReturnsFailure400()
    {
        var sut = BuildSut();
        var state = ValidState(expiresAtUtc: DateTimeOffset.UtcNow.AddMinutes(-1));
        _stateProtector.Setup(x => x.TryUnprotect("state", out state)).Returns(true);

        var result = await sut.Handle(new CompleteCalendarConnectionCommand("google", "code", "state"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task Handle_TenantNotFound_ReturnsFailure400()
    {
        var sut = BuildSut();
        var state = ValidState();
        _stateProtector.Setup(x => x.TryUnprotect("state", out state)).Returns(true);
        _tenants.Setup(x => x.GetByIdAsync(TenantId, It.IsAny<CancellationToken>())).ReturnsAsync((Tenant?)null);

        var result = await sut.Handle(new CompleteCalendarConnectionCommand("google", "code", "state"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task Handle_TokenExchangeThrows_ReturnsSuccessWithErrorRedirect()
    {
        var sut = BuildSut();
        var state = ValidState();
        _stateProtector.Setup(x => x.TryUnprotect("state", out state)).Returns(true);
        var tenant = new Tenant { Id = TenantId, Slug = "acme", Status = TenantStatus.Active };
        _tenants.Setup(x => x.GetByIdAsync(TenantId, It.IsAny<CancellationToken>())).ReturnsAsync(tenant);
        _appResolver.Setup(x => x.GetActiveAppForProviderAsync("google", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedPlatformOAuthApp("google", "client", "https://accounts.google.com/o/oauth2/v2/auth", "https://oauth2.googleapis.com/token", ["scope"]));
        _appResolver.Setup(x => x.GetActiveCredentialForProviderAsync("google", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedPlatformOAuthAppCredential("google", "client", "secret", null, 1));
        _tokenClient.Setup(x => x.ExchangeCodeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("provider unavailable"));

        var result = await sut.Handle(new CompleteCalendarConnectionCommand("google", "code", "state"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains("connectionError=1", result.Value);
        Assert.Contains("acme.onexso.com", result.Value);
    }

    [Fact]
    public async Task Handle_HappyPath_CreatesConnectionAndReturnsSuccessRedirect()
    {
        var sut = BuildSut();
        var state = ValidState();
        _stateProtector.Setup(x => x.TryUnprotect("state", out state)).Returns(true);
        var tenant = new Tenant { Id = TenantId, Slug = "acme", Status = TenantStatus.Active };
        _tenants.Setup(x => x.GetByIdAsync(TenantId, It.IsAny<CancellationToken>())).ReturnsAsync(tenant);
        _appResolver.Setup(x => x.GetActiveAppForProviderAsync("google", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedPlatformOAuthApp("google", "client", "https://accounts.google.com/o/oauth2/v2/auth", "https://oauth2.googleapis.com/token", ["scope"]));
        _appResolver.Setup(x => x.GetActiveCredentialForProviderAsync("google", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedPlatformOAuthAppCredential("google", "client", "secret", null, 1));
        _tokenClient.Setup(x => x.ExchangeCodeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CalendarProviderTokens("at-1", "rt-1", DateTimeOffset.UtcNow.AddHours(1)));
        _tokenClient.Setup(x => x.GetAccountAsync("google", "at-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CalendarProviderAccount("me@acme.com", "me@acme.com", "me@acme.com"));
        _connections.Setup(x => x.GetByTenantUserProviderAsync(TenantId, UserId, CalendarExternalSources.GoogleCalendar, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ExternalCalendarConnection?)null);
        _encryption.Setup(x => x.EncryptBytes(It.IsAny<string>())).Returns<string>(s => System.Text.Encoding.UTF8.GetBytes(s));

        var result = await sut.Handle(new CompleteCalendarConnectionCommand("google", "code", "state"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains("connected=google", result.Value);
        Assert.Contains("acme.onexso.com", result.Value);
        _connections.Verify(x => x.AddAsync(It.Is<ExternalCalendarConnection>(c =>
            c.TenantId == TenantId && c.UserId == UserId && c.ExternalAccountEmail == "me@acme.com"), It.IsAny<CancellationToken>()), Times.Once);
        _tenantSwitcher.Verify(x => x.SwitchToTenantAsync(It.Is<TenantRegistryEntry>(t => t.TenantId == TenantId && t.Slug == "acme"), It.IsAny<CancellationToken>()), Times.Once);
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~CompleteCalendarConnectionCommandHandlerTests" --nologo`
Expected: FAIL — `CompleteCalendarConnectionCommandHandler` does not exist yet.

- [ ] **Step 4: Write the handler**

```csharp
// src/ONEVO.Application/Features/Calendar/Commands/CompleteCalendarConnection/CompleteCalendarConnectionCommandHandler.cs
using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Configuration;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Tenancy.Entities;

namespace ONEVO.Application.Features.Calendar.Commands.CompleteCalendarConnection;

public sealed class CompleteCalendarConnectionCommandHandler(
    ICalendarOAuthStateProtector stateProtector,
    ITenantRepository tenants,
    ITenantContextSwitcher tenantSwitcher,
    IPlatformOAuthAppResolver appResolver,
    ICalendarOAuthTokenExchangeClient tokenExchangeClient,
    IExternalCalendarConnectionRepository connections,
    IEncryptionService encryption,
    IUnitOfWork unitOfWork,
    IConfiguration configuration)
    : IRequestHandler<CompleteCalendarConnectionCommand, Result<string>>
{
    public async Task<Result<string>> Handle(CompleteCalendarConnectionCommand request, CancellationToken ct)
    {
        if (!stateProtector.TryUnprotect(request.State, out var state) || state is null)
            return Result<string>.Failure("Invalid or expired connection request.", 400);

        if (state.ExpiresAtUtc <= DateTimeOffset.UtcNow || !string.Equals(state.Provider, request.Provider, StringComparison.OrdinalIgnoreCase))
            return Result<string>.Failure("Invalid or expired connection request.", 400);

        var tenant = await tenants.GetByIdAsync(state.TenantId, ct);
        if (tenant is null || tenant.Status != TenantStatus.Active)
            return Result<string>.Failure("This connection request is no longer valid.", 400);

        await tenantSwitcher.SwitchToTenantAsync(new TenantRegistryEntry(tenant.Id, tenant.Slug, tenant.Status, null), ct);

        var rootDomain = (configuration["Tenancy:RootDomain"] ?? string.Empty).Trim().TrimStart('.');
        var appBaseUrl = configuration["Urls:AppBaseUrl"] ?? string.Empty;
        Uri.TryCreate(appBaseUrl, UriKind.Absolute, out var baseUri);
        var scheme = baseUri?.Scheme ?? "https";
        var port = baseUri?.Port ?? 443;

        string BuildRedirect(string query)
            => new UriBuilder(scheme, $"{tenant.Slug}.{rootDomain}", port, "/calendar") { Query = query }.Uri.ToString();

        var errorRedirect = BuildRedirect("connectionError=1");

        try
        {
            return await CompleteConnectionAsync(request, state, tenant, errorRedirect, BuildRedirect, ct);
        }
        catch
        {
            return Result<string>.Success(errorRedirect);
        }
    }

    private async Task<Result<string>> CompleteConnectionAsync(
        CompleteCalendarConnectionCommand request, CalendarOAuthState state, Tenant tenant,
        string errorRedirect, Func<string, string> buildRedirect, CancellationToken ct)
    {
        var app = await appResolver.GetActiveAppForProviderAsync(request.Provider, ct);
        var credential = await appResolver.GetActiveCredentialForProviderAsync(request.Provider, ct);
        if (app is null || credential is null)
            return Result<string>.Success(errorRedirect);

        var callbackBaseUrl = (configuration["Urls:CalendarOAuthCallbackBaseUrl"] ?? string.Empty).TrimEnd('/');
        var redirectUri = $"{callbackBaseUrl}/api/v1/calendar/connections/{request.Provider}/callback";

        var tokens = await tokenExchangeClient.ExchangeCodeAsync(app.TokenUrl, credential.ClientId, credential.ClientSecret, request.Code, redirectUri, ct);
        var account = await tokenExchangeClient.GetAccountAsync(request.Provider, tokens.AccessToken, ct);

        var externalSource = request.Provider.Equals("google", StringComparison.OrdinalIgnoreCase)
            ? CalendarExternalSources.GoogleCalendar
            : CalendarExternalSources.OutlookCalendar;

        return await unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var existing = await connections.GetByTenantUserProviderAsync(tenant.Id, state.UserId, externalSource, innerCt);
            var now = DateTimeOffset.UtcNow;

            if (existing is not null)
            {
                existing.ExternalAccountEmail = account.AccountEmail;
                existing.ExternalCalendarId = account.PrimaryCalendarId;
                existing.ExternalCalendarName = account.PrimaryCalendarName;
                existing.AccessTokenEncrypted = encryption.EncryptBytes(tokens.AccessToken);
                if (tokens.RefreshToken is not null)
                    existing.RefreshTokenEncrypted = encryption.EncryptBytes(tokens.RefreshToken);
                existing.Status = ExternalCalendarConnectionStatuses.Active;
                existing.FailureCount = 0;
                existing.LastError = null;
                existing.ExpiresAt = tokens.ExpiresAt;
                existing.UpdatedAt = now;
                connections.Update(existing);
            }
            else
            {
                if (tokens.RefreshToken is null)
                    return Result<string>.Success(errorRedirect);

                await connections.AddAsync(new ExternalCalendarConnection
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenant.Id,
                    UserId = state.UserId,
                    Provider = externalSource,
                    ExternalAccountEmail = account.AccountEmail,
                    ExternalCalendarId = account.PrimaryCalendarId,
                    ExternalCalendarName = account.PrimaryCalendarName,
                    AccessTokenEncrypted = encryption.EncryptBytes(tokens.AccessToken),
                    RefreshTokenEncrypted = encryption.EncryptBytes(tokens.RefreshToken),
                    ScopesJson = JsonSerializer.Serialize(app.DefaultScopes),
                    SyncDirection = CalendarSyncDirections.TwoWay,
                    Status = ExternalCalendarConnectionStatuses.Active,
                    ExpiresAt = tokens.ExpiresAt,
                    CreatedAt = now
                }, innerCt);
            }

            await unitOfWork.SaveChangesAsync(innerCt);
            return Result<string>.Success(buildRedirect($"connected={request.Provider}"));
        }, ct);
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~CompleteCalendarConnectionCommandHandlerTests" --nologo`
Expected: `Passed! - Failed: 0, Passed: 5`

- [ ] **Step 6: Write the callback controller**

```csharp
// src/ONEVO.Api/Controllers/Public/Calendar/CalendarOAuthCallbackController.cs
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Application.Features.Calendar.Commands.CompleteCalendarConnection;

namespace ONEVO.Api.Controllers.Public.Calendar;

[ApiController]
[Route("api/v1/calendar/connections")]
[AllowAnonymous]
public class CalendarOAuthCallbackController(IMediator mediator) : ControllerBase
{
    [HttpGet("{provider}/callback")]
    public async Task<IActionResult> Callback(string provider, [FromQuery] string code, [FromQuery] string state, CancellationToken ct)
    {
        var result = await mediator.Send(new CompleteCalendarConnectionCommand(provider, code, state), ct);
        return result.IsSuccess
            ? Redirect(result.Value!)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
```

Confirm this controller's namespace/folder (`Controllers/Public/...`) matches an existing convention in this codebase for non-tenant-scoped controllers (grep for any other `[AllowAnonymous]` controller, e.g. `TenantSessionExchangeController` or similar, to match folder placement style) before finalizing the path — adjust the folder if the codebase already has an established `Public`/`Anonymous` controllers area under a different name.

- [ ] **Step 7: Write the architecture test proving this controller stays anonymous**

Following the per-controller architecture-test-file convention (`GitHubUserOAuthArchitectureTests.cs` etc.), add:

```csharp
// tests/ONEVO.Tests.Architecture/CalendarOAuthCallbackControllerArchitectureTests.cs
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using ONEVO.Api.Controllers.Public.Calendar;
using Xunit;

namespace ONEVO.Tests.Architecture;

public sealed class CalendarOAuthCallbackControllerArchitectureTests
{
    [Fact]
    public void CalendarOAuthCallbackController_IsAllowAnonymous_NotTenantPolicy()
    {
        var type = typeof(CalendarOAuthCallbackController);

        Assert.NotNull(type.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.Null(type.GetCustomAttribute<AuthorizeAttribute>());
    }
}
```

Run: `dotnet test tests/ONEVO.Tests.Architecture/ONEVO.Tests.Architecture.csproj --nologo`
Expected: all pass, including the new test.

- [ ] **Step 8: Commit**

```bash
git add src/ONEVO.Application/Features/Calendar/Commands/CompleteCalendarConnection/ src/ONEVO.Api/Controllers/Public/Calendar/ tests/ONEVO.Tests.Unit/Features/Calendar/CompleteCalendarConnectionCommandHandlerTests.cs tests/ONEVO.Tests.Architecture/CalendarOAuthCallbackControllerArchitectureTests.cs
git commit -m "feat(calendar): add CompleteCalendarConnectionCommand and OAuth callback controller"
```

---

### Task 7: Connection management commands (`GetMyCalendarConnectionsQuery`, `UpdateCalendarConnectionCommand`, `DisconnectCalendarConnectionCommand`, `TriggerCalendarSyncCommand`)

**Files:**
- Create: `src/ONEVO.Application/Features/Calendar/DTOs/Responses/CalendarConnectionResponse.cs`
- Create: `src/ONEVO.Application/Features/Calendar/Queries/GetMyCalendarConnections/GetMyCalendarConnectionsQuery.cs` + `GetMyCalendarConnectionsQueryHandler.cs`
- Create: `src/ONEVO.Application/Features/Calendar/Commands/UpdateCalendarConnection/UpdateCalendarConnectionCommand.cs` + `UpdateCalendarConnectionCommandHandler.cs`
- Create: `src/ONEVO.Application/Features/Calendar/Commands/DisconnectCalendarConnection/DisconnectCalendarConnectionCommand.cs` + `DisconnectCalendarConnectionCommandHandler.cs`
- Create: `src/ONEVO.Application/Features/Calendar/ServiceInterfaces/ICalendarSyncService.cs` — forward-declared interface, implemented in Task 10; only the one method `TriggerCalendarSyncCommand` needs is declared here.
- Create: `src/ONEVO.Application/Features/Calendar/Commands/TriggerCalendarSync/TriggerCalendarSyncCommand.cs` + `TriggerCalendarSyncCommandHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Calendar/GetMyCalendarConnectionsQueryHandlerTests.cs`, `UpdateCalendarConnectionCommandHandlerTests.cs`, `DisconnectCalendarConnectionCommandHandlerTests.cs`, `TriggerCalendarSyncCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `IExternalCalendarConnectionRepository`, `IExternalCalendarEventLinkRepository` (Task 4).
- Produces: `CalendarConnectionItem(Guid Id, string Provider, string ExternalAccountEmail, string? ExternalCalendarName, string SyncDirection, string Status, DateTimeOffset? LastSyncedAt, string? LastError)` (never includes token fields), `CalendarConnectionsResponse(IReadOnlyList<CalendarConnectionItem> Connections)`, `ICalendarSyncService.SyncConnectionAsync(Guid tenantId, Guid connectionId, CancellationToken ct)`.

- [ ] **Step 1: Write the response DTO and the sync-service forward interface**

```csharp
// src/ONEVO.Application/Features/Calendar/DTOs/Responses/CalendarConnectionResponse.cs
namespace ONEVO.Application.Features.Calendar.DTOs.Responses;

public sealed record CalendarConnectionItem(
    Guid Id,
    string Provider,
    string ExternalAccountEmail,
    string? ExternalCalendarName,
    string SyncDirection,
    string Status,
    DateTimeOffset? LastSyncedAt,
    string? LastError);

public sealed record CalendarConnectionsResponse(IReadOnlyList<CalendarConnectionItem> Connections);
```

```csharp
// src/ONEVO.Application/Features/Calendar/ServiceInterfaces/ICalendarSyncService.cs
namespace ONEVO.Application.Features.Calendar.ServiceInterfaces;

/// <summary>Implemented in Task 10 (CalendarSyncService). Declared here so
/// TriggerCalendarSyncCommandHandler can depend on it without a forward reference to
/// Infrastructure — the background job (also Task 10) calls the same method per connection.</summary>
public interface ICalendarSyncService
{
    Task SyncConnectionAsync(Guid tenantId, Guid connectionId, CancellationToken ct);
}
```

- [ ] **Step 2: Write `GetMyCalendarConnectionsQuery` and its failing test**

```csharp
// src/ONEVO.Application/Features/Calendar/Queries/GetMyCalendarConnections/GetMyCalendarConnectionsQuery.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Calendar.DTOs.Responses;

namespace ONEVO.Application.Features.Calendar.Queries.GetMyCalendarConnections;

public sealed record GetMyCalendarConnectionsQuery : IRequest<Result<CalendarConnectionsResponse>>;
```

```csharp
// tests/ONEVO.Tests.Unit/Features/Calendar/GetMyCalendarConnectionsQueryHandlerTests.cs
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Queries.GetMyCalendarConnections;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class GetMyCalendarConnectionsQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<IExternalCalendarConnectionRepository> _connections = new();

    private GetMyCalendarConnectionsQueryHandler BuildSut()
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        _currentUser.SetupGet(x => x.UserId).Returns(UserId);
        return new GetMyCalendarConnectionsQueryHandler(_currentUser.Object, _connections.Object);
    }

    [Fact]
    public async Task Handle_ReturnsCallersConnections_NeverIncludingTokenFields()
    {
        var sut = BuildSut();
        _connections.Setup(x => x.GetForUserAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new ExternalCalendarConnection
                {
                    Id = Guid.NewGuid(), TenantId = TenantId, UserId = UserId,
                    Provider = CalendarExternalSources.GoogleCalendar, ExternalAccountEmail = "me@acme.com",
                    ExternalCalendarName = "Primary", SyncDirection = CalendarSyncDirections.TwoWay,
                    Status = ExternalCalendarConnectionStatuses.Active,
                    RefreshTokenEncrypted = [9, 9, 9], AccessTokenEncrypted = [8, 8, 8]
                }
            ]);

        var result = await sut.Handle(new GetMyCalendarConnectionsQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Connections);
        Assert.Equal("me@acme.com", result.Value.Connections[0].ExternalAccountEmail);
        // CalendarConnectionItem's constructor has no token-field parameters at all - the type
        // system already guarantees they can't leak; this test documents that guarantee.
    }
}
```

- [ ] **Step 3: Run the test to verify it fails, then write the handler**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~GetMyCalendarConnectionsQueryHandlerTests" --nologo`
Expected: FAIL — handler does not exist yet.

```csharp
// src/ONEVO.Application/Features/Calendar/Queries/GetMyCalendarConnections/GetMyCalendarConnectionsQueryHandler.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.DTOs.Responses;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;

namespace ONEVO.Application.Features.Calendar.Queries.GetMyCalendarConnections;

public sealed class GetMyCalendarConnectionsQueryHandler(
    ICurrentUser currentUser,
    IExternalCalendarConnectionRepository connections)
    : IRequestHandler<GetMyCalendarConnectionsQuery, Result<CalendarConnectionsResponse>>
{
    public async Task<Result<CalendarConnectionsResponse>> Handle(GetMyCalendarConnectionsQuery request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result<CalendarConnectionsResponse>.Forbidden();

        var rows = await connections.GetForUserAsync(currentUser.TenantId, currentUser.UserId, ct);
        var items = rows.Select(c => new CalendarConnectionItem(
            c.Id, c.Provider, c.ExternalAccountEmail, c.ExternalCalendarName, c.SyncDirection, c.Status, c.LastSyncedAt, c.LastError)).ToList();

        return Result<CalendarConnectionsResponse>.Success(new CalendarConnectionsResponse(items));
    }
}
```

Run the test again — expected `Passed! - Failed: 0, Passed: 1`.

- [ ] **Step 4: Write `UpdateCalendarConnectionCommand` and its tests**

```csharp
// src/ONEVO.Application/Features/Calendar/Commands/UpdateCalendarConnection/UpdateCalendarConnectionCommand.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Calendar.DTOs.Responses;

namespace ONEVO.Application.Features.Calendar.Commands.UpdateCalendarConnection;

public sealed record UpdateCalendarConnectionCommand(Guid Id, string SyncDirection) : IRequest<Result<CalendarConnectionItem>>;
```

```csharp
// tests/ONEVO.Tests.Unit/Features/Calendar/UpdateCalendarConnectionCommandHandlerTests.cs
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Commands.UpdateCalendarConnection;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class UpdateCalendarConnectionCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid ConnectionId = Guid.NewGuid();

    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<IExternalCalendarConnectionRepository> _connections = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();

    private UpdateCalendarConnectionCommandHandler BuildSut()
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        _currentUser.SetupGet(x => x.UserId).Returns(UserId);
        _unitOfWork
            .Setup(x => x.ExecuteInTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<ONEVO.Application.Common.Models.Result<ONEVO.Application.Features.Calendar.DTOs.Responses.CalendarConnectionItem>>>>(),
                It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<ONEVO.Application.Common.Models.Result<ONEVO.Application.Features.Calendar.DTOs.Responses.CalendarConnectionItem>>>, CancellationToken>(
                (action, ct) => action(ct));
        return new UpdateCalendarConnectionCommandHandler(_currentUser.Object, _connections.Object, _unitOfWork.Object);
    }

    [Fact]
    public async Task Handle_ConnectionNotFound_ReturnsNotFound()
    {
        var sut = BuildSut();
        _connections.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, ConnectionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ExternalCalendarConnection?)null);

        var result = await sut.Handle(new UpdateCalendarConnectionCommand(ConnectionId, CalendarSyncDirections.PullOnly), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_NotOwner_ReturnsForbidden()
    {
        var sut = BuildSut();
        _connections.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, ConnectionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExternalCalendarConnection { Id = ConnectionId, TenantId = TenantId, UserId = Guid.NewGuid(), Provider = CalendarExternalSources.GoogleCalendar, ExternalAccountEmail = "x@y.com", RefreshTokenEncrypted = [1] });

        var result = await sut.Handle(new UpdateCalendarConnectionCommand(ConnectionId, CalendarSyncDirections.PullOnly), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_InvalidSyncDirection_ReturnsFailure400()
    {
        var sut = BuildSut();
        _connections.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, ConnectionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExternalCalendarConnection { Id = ConnectionId, TenantId = TenantId, UserId = UserId, Provider = CalendarExternalSources.GoogleCalendar, ExternalAccountEmail = "x@y.com", RefreshTokenEncrypted = [1] });

        var result = await sut.Handle(new UpdateCalendarConnectionCommand(ConnectionId, "not_a_real_direction"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task Handle_Owner_UpdatesSyncDirection()
    {
        var sut = BuildSut();
        var existing = new ExternalCalendarConnection { Id = ConnectionId, TenantId = TenantId, UserId = UserId, Provider = CalendarExternalSources.GoogleCalendar, ExternalAccountEmail = "x@y.com", RefreshTokenEncrypted = [1], SyncDirection = CalendarSyncDirections.TwoWay };
        _connections.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, ConnectionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var result = await sut.Handle(new UpdateCalendarConnectionCommand(ConnectionId, CalendarSyncDirections.PullOnly), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(CalendarSyncDirections.PullOnly, existing.SyncDirection);
        _connections.Verify(x => x.Update(existing), Times.Once);
    }
}
```

```csharp
// src/ONEVO.Application/Features/Calendar/Commands/UpdateCalendarConnection/UpdateCalendarConnectionCommandHandler.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.DTOs.Responses;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Application.Features.Calendar.Commands.UpdateCalendarConnection;

public sealed class UpdateCalendarConnectionCommandHandler(
    ICurrentUser currentUser,
    IExternalCalendarConnectionRepository connections,
    IUnitOfWork unitOfWork)
    : IRequestHandler<UpdateCalendarConnectionCommand, Result<CalendarConnectionItem>>
{
    private static readonly string[] ValidDirections =
        [CalendarSyncDirections.PullOnly, CalendarSyncDirections.PushOnly, CalendarSyncDirections.TwoWay, CalendarSyncDirections.Disabled];

    public async Task<Result<CalendarConnectionItem>> Handle(UpdateCalendarConnectionCommand request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result<CalendarConnectionItem>.Forbidden();

        if (!ValidDirections.Contains(request.SyncDirection))
            return Result<CalendarConnectionItem>.Failure("Invalid sync direction.", 400);

        var existing = await connections.GetTrackedByIdForTenantAsync(currentUser.TenantId, request.Id, ct);
        if (existing is null)
            return Result<CalendarConnectionItem>.NotFound("Calendar connection not found.");

        if (existing.UserId != currentUser.UserId)
            return Result<CalendarConnectionItem>.Forbidden("Only the connection owner can change its sync mode.");

        return await unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            existing.SyncDirection = request.SyncDirection;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
            connections.Update(existing);
            await unitOfWork.SaveChangesAsync(innerCt);

            return Result<CalendarConnectionItem>.Success(new CalendarConnectionItem(
                existing.Id, existing.Provider, existing.ExternalAccountEmail, existing.ExternalCalendarName,
                existing.SyncDirection, existing.Status, existing.LastSyncedAt, existing.LastError));
        }, ct);
    }
}
```

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~UpdateCalendarConnectionCommandHandlerTests" --nologo`
Expected: `Passed! - Failed: 0, Passed: 4`

- [ ] **Step 5: Write `DisconnectCalendarConnectionCommand` and its tests**

```csharp
// src/ONEVO.Application/Features/Calendar/Commands/DisconnectCalendarConnection/DisconnectCalendarConnectionCommand.cs
using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Calendar.Commands.DisconnectCalendarConnection;

public sealed record DisconnectCalendarConnectionCommand(Guid Id) : IRequest<Result<Unit>>;
```

```csharp
// tests/ONEVO.Tests.Unit/Features/Calendar/DisconnectCalendarConnectionCommandHandlerTests.cs
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Commands.DisconnectCalendarConnection;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class DisconnectCalendarConnectionCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid ConnectionId = Guid.NewGuid();

    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<IExternalCalendarConnectionRepository> _connections = new();
    private readonly Mock<IExternalCalendarEventLinkRepository> _links = new();
    private readonly Mock<ICalendarEventRepository> _events = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();

    private DisconnectCalendarConnectionCommandHandler BuildSut()
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        _currentUser.SetupGet(x => x.UserId).Returns(UserId);
        _unitOfWork
            .Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<ONEVO.Application.Common.Models.Result<MediatR.Unit>>>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<ONEVO.Application.Common.Models.Result<MediatR.Unit>>>, CancellationToken>((action, ct) => action(ct));
        return new DisconnectCalendarConnectionCommandHandler(_currentUser.Object, _connections.Object, _links.Object, _events.Object, _unitOfWork.Object);
    }

    private static ExternalCalendarConnection OwnedConnection() => new()
    {
        Id = ConnectionId, TenantId = TenantId, UserId = UserId,
        Provider = CalendarExternalSources.GoogleCalendar, ExternalAccountEmail = "x@y.com", RefreshTokenEncrypted = [1]
    };

    [Fact]
    public async Task Handle_NotOwner_ReturnsForbidden()
    {
        var sut = BuildSut();
        _connections.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, ConnectionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OwnedConnection() with { });
        var other = OwnedConnection();
        other.UserId = Guid.NewGuid();
        _connections.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, ConnectionId, It.IsAny<CancellationToken>())).ReturnsAsync(other);

        var result = await sut.Handle(new DisconnectCalendarConnectionCommand(ConnectionId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_Owner_RemovesConnectionAndOnlyExternallySourcedLinkedEvents()
    {
        var sut = BuildSut();
        _connections.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, ConnectionId, It.IsAny<CancellationToken>())).ReturnsAsync(OwnedConnection());
        var externalEvent = new CalendarEvent { Id = Guid.NewGuid(), TenantId = TenantId, Title = "Synced", SourceType = CalendarEventSourceTypes.ExternalSync, CreatedById = UserId };
        var convertedEvent = new CalendarEvent { Id = Guid.NewGuid(), TenantId = TenantId, Title = "Kept", SourceType = CalendarEventSourceTypes.Manual, CreatedById = UserId };
        var links = new List<ExternalCalendarEventLink>
        {
            new() { Id = Guid.NewGuid(), TenantId = TenantId, CalendarEventId = externalEvent.Id, ExternalCalendarConnectionId = ConnectionId },
            new() { Id = Guid.NewGuid(), TenantId = TenantId, CalendarEventId = convertedEvent.Id, ExternalCalendarConnectionId = ConnectionId }
        };
        _links.Setup(x => x.GetByConnectionIdAsync(TenantId, ConnectionId, It.IsAny<CancellationToken>())).ReturnsAsync(links);
        _events.Setup(x => x.GetByIdForTenantAsync(TenantId, externalEvent.Id, It.IsAny<CancellationToken>())).ReturnsAsync(externalEvent);
        _events.Setup(x => x.GetByIdForTenantAsync(TenantId, convertedEvent.Id, It.IsAny<CancellationToken>())).ReturnsAsync(convertedEvent);
        _events.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, externalEvent.Id, It.IsAny<CancellationToken>())).ReturnsAsync(externalEvent);

        var result = await sut.Handle(new DisconnectCalendarConnectionCommand(ConnectionId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _events.Verify(x => x.Remove(It.Is<CalendarEvent>(e => e.Id == externalEvent.Id)), Times.Once);
        _events.Verify(x => x.Remove(It.Is<CalendarEvent>(e => e.Id == convertedEvent.Id)), Times.Never);
        _links.Verify(x => x.RemoveRange(links), Times.Once);
        _connections.Verify(x => x.Remove(It.Is<ExternalCalendarConnection>(c => c.Id == ConnectionId)), Times.Once);
    }
}
```

```csharp
// src/ONEVO.Application/Features/Calendar/Commands/DisconnectCalendarConnection/DisconnectCalendarConnectionCommandHandler.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Application.Features.Calendar.Commands.DisconnectCalendarConnection;

public sealed class DisconnectCalendarConnectionCommandHandler(
    ICurrentUser currentUser,
    IExternalCalendarConnectionRepository connections,
    IExternalCalendarEventLinkRepository links,
    ICalendarEventRepository events,
    IUnitOfWork unitOfWork)
    : IRequestHandler<DisconnectCalendarConnectionCommand, Result<Unit>>
{
    public async Task<Result<Unit>> Handle(DisconnectCalendarConnectionCommand request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result<Unit>.Forbidden();

        var tenantId = currentUser.TenantId;
        var existing = await connections.GetTrackedByIdForTenantAsync(tenantId, request.Id, ct);
        if (existing is null)
            return Result<Unit>.NotFound("Calendar connection not found.");

        if (existing.UserId != currentUser.UserId)
            return Result<Unit>.Forbidden("Only the connection owner can disconnect it.");

        return await unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var connectionLinks = await links.GetByConnectionIdAsync(tenantId, existing.Id, innerCt);

            // Only delete events still owned by the sync (SourceType == ExternalSync). An event that
            // was later "converted" to a OneVo-owned event keeps existing after disconnect - there is
            // no convert command yet in this codebase, so this branch is presently unreachable, but the
            // check stays correct for when a later spec adds one (per the design spec's explicit note).
            foreach (var link in connectionLinks)
            {
                var linkedEvent = await events.GetTrackedByIdForTenantAsync(tenantId, link.CalendarEventId, innerCt);
                if (linkedEvent is not null && linkedEvent.SourceType == CalendarEventSourceTypes.ExternalSync)
                    events.Remove(linkedEvent);
            }

            links.RemoveRange(connectionLinks);
            connections.Remove(existing);
            await unitOfWork.SaveChangesAsync(innerCt);
            return Result<Unit>.Success(Unit.Value);
        }, ct);
    }
}
```

The test's `GetByIdForTenantAsync` stub setups are unused by this exact handler implementation (it calls `GetTrackedByIdForTenantAsync` for both events) — simplify the test to only stub `GetTrackedByIdForTenantAsync` for both `externalEvent.Id` and `convertedEvent.Id` before running it; this is a test-authoring detail to fix while implementing, not a handler defect.

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~DisconnectCalendarConnectionCommandHandlerTests" --nologo`
Expected: `Passed! - Failed: 0, Passed: 2`

- [ ] **Step 6: Write `TriggerCalendarSyncCommand` and its tests**

```csharp
// src/ONEVO.Application/Features/Calendar/Commands/TriggerCalendarSync/TriggerCalendarSyncCommand.cs
using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Calendar.Commands.TriggerCalendarSync;

public sealed record TriggerCalendarSyncCommand(Guid Id) : IRequest<Result<Unit>>;
```

```csharp
// tests/ONEVO.Tests.Unit/Features/Calendar/TriggerCalendarSyncCommandHandlerTests.cs
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Commands.TriggerCalendarSync;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class TriggerCalendarSyncCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid ConnectionId = Guid.NewGuid();

    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<IExternalCalendarConnectionRepository> _connections = new();
    private readonly Mock<ICalendarSyncService> _syncService = new();

    private TriggerCalendarSyncCommandHandler BuildSut()
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        _currentUser.SetupGet(x => x.UserId).Returns(UserId);
        return new TriggerCalendarSyncCommandHandler(_currentUser.Object, _connections.Object, _syncService.Object);
    }

    [Fact]
    public async Task Handle_NotOwner_ReturnsForbidden()
    {
        var sut = BuildSut();
        _connections.Setup(x => x.GetByIdForTenantAsync(TenantId, ConnectionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExternalCalendarConnection { Id = ConnectionId, TenantId = TenantId, UserId = Guid.NewGuid(), Provider = CalendarExternalSources.GoogleCalendar, ExternalAccountEmail = "x@y.com", RefreshTokenEncrypted = [1] });

        var result = await sut.Handle(new TriggerCalendarSyncCommand(ConnectionId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        _syncService.Verify(x => x.SyncConnectionAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Owner_InvokesSyncService()
    {
        var sut = BuildSut();
        _connections.Setup(x => x.GetByIdForTenantAsync(TenantId, ConnectionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExternalCalendarConnection { Id = ConnectionId, TenantId = TenantId, UserId = UserId, Provider = CalendarExternalSources.GoogleCalendar, ExternalAccountEmail = "x@y.com", RefreshTokenEncrypted = [1] });

        var result = await sut.Handle(new TriggerCalendarSyncCommand(ConnectionId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _syncService.Verify(x => x.SyncConnectionAsync(TenantId, ConnectionId, It.IsAny<CancellationToken>()), Times.Once);
    }
}
```

```csharp
// src/ONEVO.Application/Features/Calendar/Commands/TriggerCalendarSync/TriggerCalendarSyncCommandHandler.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;

namespace ONEVO.Application.Features.Calendar.Commands.TriggerCalendarSync;

public sealed class TriggerCalendarSyncCommandHandler(
    ICurrentUser currentUser,
    IExternalCalendarConnectionRepository connections,
    ICalendarSyncService syncService)
    : IRequestHandler<TriggerCalendarSyncCommand, Result<Unit>>
{
    public async Task<Result<Unit>> Handle(TriggerCalendarSyncCommand request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result<Unit>.Forbidden();

        var tenantId = currentUser.TenantId;
        var existing = await connections.GetByIdForTenantAsync(tenantId, request.Id, ct);
        if (existing is null)
            return Result<Unit>.NotFound("Calendar connection not found.");

        if (existing.UserId != currentUser.UserId)
            return Result<Unit>.Forbidden("Only the connection owner can trigger a sync.");

        await syncService.SyncConnectionAsync(tenantId, existing.Id, ct);
        return Result<Unit>.Success(Unit.Value);
    }
}
```

This command does not run inside `IUnitOfWork.ExecuteInTransactionAsync` — it has no direct writes of its own; `ICalendarSyncService.SyncConnectionAsync` (Task 10) owns its own transactional boundaries per connection.

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~TriggerCalendarSyncCommandHandlerTests" --nologo`
Expected: `Passed! - Failed: 0, Passed: 2`

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Application/Features/Calendar/DTOs/Responses/CalendarConnectionResponse.cs src/ONEVO.Application/Features/Calendar/Queries/GetMyCalendarConnections/ src/ONEVO.Application/Features/Calendar/Commands/UpdateCalendarConnection/ src/ONEVO.Application/Features/Calendar/Commands/DisconnectCalendarConnection/ src/ONEVO.Application/Features/Calendar/Commands/TriggerCalendarSync/ src/ONEVO.Application/Features/Calendar/ServiceInterfaces/ICalendarSyncService.cs tests/ONEVO.Tests.Unit/Features/Calendar/GetMyCalendarConnectionsQueryHandlerTests.cs tests/ONEVO.Tests.Unit/Features/Calendar/UpdateCalendarConnectionCommandHandlerTests.cs tests/ONEVO.Tests.Unit/Features/Calendar/DisconnectCalendarConnectionCommandHandlerTests.cs tests/ONEVO.Tests.Unit/Features/Calendar/TriggerCalendarSyncCommandHandlerTests.cs
git commit -m "feat(calendar): add connection management commands (list/update/disconnect/trigger-sync)"
```

---

### Task 8: Wire connection routes into `CalendarController`

**Files:**
- Modify: `src/ONEVO.Api/Contracts/Calendar/CalendarContracts.cs` — add connection view-models/mapper, alongside the existing event view-models (do not touch the existing ones)
- Modify: `src/ONEVO.Api/Controllers/Tenant/Calendar/CalendarController.cs` — add 5 new actions under a `connections` sub-route, alongside the existing 9 actions (do not touch them)
- Test: `tests/ONEVO.Tests.Architecture` — run the existing suite; this task adds no new architecture rule (the new actions live on the same already-`TenantPolicy` controller)

**Interfaces:**
- Consumes: `StartCalendarConnectionCommand` (Task 3), `GetMyCalendarConnectionsQuery`, `UpdateCalendarConnectionCommand`, `DisconnectCalendarConnectionCommand`, `TriggerCalendarSyncCommand` (Task 7).
- Produces: the 5 `connections` routes from the spec's endpoint table (`connect`/list/update/disconnect/sync — the 6th, `callback`, is `CalendarOAuthCallbackController` from Task 6, a different controller entirely).

- [ ] **Step 1: Read the current `CalendarController.cs` and `CalendarContracts.cs`**

Open both files in full before editing. Confirm the existing 9 actions' exact attribute style (`[HttpGet]`, `[RequirePermission("...")]` placement, constructor-injection vs primary-constructor style) so the 5 new actions match exactly — do not introduce a different style within the same file.

- [ ] **Step 2: Add the connection contracts**

Append to `src/ONEVO.Api/Contracts/Calendar/CalendarContracts.cs` (do not remove or reorder anything already in the file):

```csharp
public sealed record StartCalendarConnectionRequest(); // provider comes from the route, body is empty

public sealed record StartCalendarConnectionResponseModel(string AuthorizeUrl);

public sealed record UpdateCalendarConnectionRequest(string SyncDirection);

public sealed record CalendarConnectionViewModel(
    Guid Id, string Provider, string ExternalAccountEmail, string? ExternalCalendarName,
    string SyncDirection, string Status, DateTimeOffset? LastSyncedAt, string? LastError);

public sealed record CalendarConnectionsViewModel(IReadOnlyList<CalendarConnectionViewModel> Connections);

public static class CalendarConnectionViewModelMapper
{
    public static CalendarConnectionViewModel ToViewModel(this ONEVO.Application.Features.Calendar.DTOs.Responses.CalendarConnectionItem dto) => new(
        dto.Id, dto.Provider, dto.ExternalAccountEmail, dto.ExternalCalendarName, dto.SyncDirection, dto.Status, dto.LastSyncedAt, dto.LastError);

    public static CalendarConnectionsViewModel ToViewModel(this ONEVO.Application.Features.Calendar.DTOs.Responses.CalendarConnectionsResponse dto) =>
        new(dto.Connections.Select(c => c.ToViewModel()).ToList());
}
```

If the file already has a `using ONEVO.Application.Features.Calendar.DTOs.Responses;` import, use the short type names (`CalendarConnectionItem`, `CalendarConnectionsResponse`) instead of the fully-qualified ones above to match the file's existing style.

- [ ] **Step 3: Add the 5 actions to `CalendarController`**

Append inside the existing `CalendarController` class (after its current last action, before the closing brace):

```csharp
[HttpGet("connections")]
[RequirePermission("calendar:read")]
public async Task<IActionResult> GetConnections(CancellationToken ct)
{
    var result = await _mediator.Send(new GetMyCalendarConnectionsQuery(), ct);
    return result.IsSuccess
        ? Ok(result.Value!.ToViewModel())
        : Problem(result.Error, statusCode: result.StatusCode ?? 400);
}

[HttpPost("connections/{provider}/connect")]
[RequirePermission("calendar:read")]
public async Task<IActionResult> Connect(string provider, CancellationToken ct)
{
    var result = await _mediator.Send(new StartCalendarConnectionCommand(provider), ct);
    return result.IsSuccess
        ? Ok(new StartCalendarConnectionResponseModel(result.Value!.AuthorizeUrl))
        : Problem(result.Error, statusCode: result.StatusCode ?? 400);
}

[HttpPut("connections/{id:guid}")]
[RequirePermission("calendar:write")]
public async Task<IActionResult> UpdateConnection(Guid id, [FromBody] UpdateCalendarConnectionRequest request, CancellationToken ct)
{
    var result = await _mediator.Send(new UpdateCalendarConnectionCommand(id, request.SyncDirection), ct);
    return result.IsSuccess
        ? Ok(result.Value!.ToViewModel())
        : Problem(result.Error, statusCode: result.StatusCode ?? 400);
}

[HttpDelete("connections/{id:guid}")]
[RequirePermission("calendar:write")]
public async Task<IActionResult> DisconnectConnection(Guid id, CancellationToken ct)
{
    var result = await _mediator.Send(new DisconnectCalendarConnectionCommand(id), ct);
    return result.IsSuccess
        ? NoContent()
        : Problem(result.Error, statusCode: result.StatusCode ?? 400);
}

[HttpPost("connections/{id:guid}/sync")]
[RequirePermission("calendar:write")]
public async Task<IActionResult> TriggerSync(Guid id, CancellationToken ct)
{
    var result = await _mediator.Send(new TriggerCalendarSyncCommand(id), ct);
    return result.IsSuccess
        ? Ok()
        : Problem(result.Error, statusCode: result.StatusCode ?? 400);
}
```

Add the four new `using` statements this needs (`ONEVO.Application.Features.Calendar.Commands.StartCalendarConnection`, `...UpdateCalendarConnection`, `...DisconnectCalendarConnection`, `...TriggerCalendarSync`, `...Queries.GetMyCalendarConnections`) to the top of the file, matching however the existing 9 actions' `using` block is organized (one block vs. per-feature grouping).

`calendar:read` on `Connect` matches the spec's endpoint table (`POST .../connect` → "Authenticated", not `calendar:write` — connecting your own calendar is a read-adjacent self-service action, consistent with `calendar:read` already being a `ModuleAutoGrants` self-service entry per the Global Constraints).

- [ ] **Step 4: Build and run the full unit + architecture suites**

Stop the running backend dev server first (it locks the DLLs). Then:

Run: `dotnet build tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --nologo`
Expected: `Build succeeded. 0 Error(s)`

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --nologo`
Expected: all pre-existing tests still pass, plus every Calendar external-sync test from Tasks 1-7; zero new failures anywhere else.

Run: `dotnet test tests/ONEVO.Tests.Architecture/ONEVO.Tests.Architecture.csproj --nologo`
Expected: all pass — `CalendarController` already satisfies every existing `TenantPolicy` architecture rule for its 9 pre-existing actions, and the 5 new actions live on the same controller class, so nothing new to assert here.

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Api/Contracts/Calendar/CalendarContracts.cs src/ONEVO.Api/Controllers/Tenant/Calendar/CalendarController.cs
git commit -m "feat(calendar): wire connection management routes into CalendarController"
```

---

### Task 9: Provider event clients (`IGoogleCalendarClient`, `IMicrosoftGraphCalendarClient`)

These handle recurring event CRUD against the provider APIs during sync (Task 10) — a separate concern from Task 5's token-exchange/account-lookup client.

**Files:**
- Create: `src/ONEVO.Application/Features/Calendar/ServiceInterfaces/IGoogleCalendarClient.cs`
- Create: `src/ONEVO.Application/Features/Calendar/ServiceInterfaces/IMicrosoftGraphCalendarClient.cs`
- Create: `src/ONEVO.Infrastructure/ExternalServices/Calendar/GoogleCalendarClient.cs`
- Create: `src/ONEVO.Infrastructure/ExternalServices/Calendar/MicrosoftGraphCalendarClient.cs`
- Modify: `src/ONEVO.Infrastructure/DependencyInjection.cs` — register both as typed `HttpClient`s
- Test: `tests/ONEVO.Tests.Unit/Features/Calendar/GoogleCalendarClientTests.cs`, `MicrosoftGraphCalendarClientTests.cs`

**Interfaces:**
- Produces: `GoogleCalendarEventDto`, `GoogleCalendarPage(IReadOnlyList<GoogleCalendarEventDto> Events, string? NextSyncToken)`, `IGoogleCalendarClient`; `GraphEventDto`, `GraphCalendarPage(IReadOnlyList<GraphEventDto> Events, string? NextDeltaLink)`, `IMicrosoftGraphCalendarClient`. Both event DTOs carry only the fields Task 10's sync logic actually reads/writes (title, description, start/end, all-day, timezone, location, external id, etag, cancelled flag) — no unused fields.

- [ ] **Step 1: Write the Google client interface, DTOs, and implementation**

```csharp
// src/ONEVO.Application/Features/Calendar/ServiceInterfaces/IGoogleCalendarClient.cs
namespace ONEVO.Application.Features.Calendar.ServiceInterfaces;

public sealed record GoogleCalendarEventDto(
    string Id,
    string? Etag,
    string Title,
    string? Description,
    DateTimeOffset Start,
    DateTimeOffset End,
    bool IsAllDay,
    string? Timezone,
    string? Location,
    bool IsCancelled);

public sealed record GoogleCalendarPage(IReadOnlyList<GoogleCalendarEventDto> Events, string? NextSyncToken);

public interface IGoogleCalendarClient
{
    Task<GoogleCalendarPage> ListEventsAsync(string accessToken, string calendarId, string? syncToken, DateTimeOffset windowStart, DateTimeOffset windowEnd, CancellationToken ct);
    Task<GoogleCalendarEventDto> InsertEventAsync(string accessToken, string calendarId, GoogleCalendarEventDto @event, CancellationToken ct);
    Task<GoogleCalendarEventDto> PatchEventAsync(string accessToken, string calendarId, string eventId, GoogleCalendarEventDto @event, CancellationToken ct);
    Task DeleteEventAsync(string accessToken, string calendarId, string eventId, CancellationToken ct);
}
```

```csharp
// src/ONEVO.Infrastructure/ExternalServices/Calendar/GoogleCalendarClient.cs
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;

namespace ONEVO.Infrastructure.ExternalServices.Calendar;

public sealed class GoogleCalendarClient(HttpClient httpClient) : IGoogleCalendarClient
{
    private const string BaseUrl = "https://www.googleapis.com/calendar/v3";

    public async Task<GoogleCalendarPage> ListEventsAsync(string accessToken, string calendarId, string? syncToken, DateTimeOffset windowStart, DateTimeOffset windowEnd, CancellationToken ct)
    {
        var url = syncToken is not null
            ? $"{BaseUrl}/calendars/{Uri.EscapeDataString(calendarId)}/events?syncToken={Uri.EscapeDataString(syncToken)}&maxResults=200"
            : $"{BaseUrl}/calendars/{Uri.EscapeDataString(calendarId)}/events?timeMin={Uri.EscapeDataString(windowStart.ToString("O"))}&timeMax={Uri.EscapeDataString(windowEnd.ToString("O"))}&maxResults=200&singleEvents=true";

        using var response = await SendAsync(HttpMethod.Get, url, accessToken, body: null, ct);
        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        var events = new List<GoogleCalendarEventDto>();
        foreach (var item in doc.RootElement.GetProperty("items").EnumerateArray())
            events.Add(ParseEvent(item));

        var nextSyncToken = doc.RootElement.TryGetProperty("nextSyncToken", out var t) ? t.GetString() : null;
        return new GoogleCalendarPage(events, nextSyncToken);
    }

    public async Task<GoogleCalendarEventDto> InsertEventAsync(string accessToken, string calendarId, GoogleCalendarEventDto @event, CancellationToken ct)
    {
        var url = $"{BaseUrl}/calendars/{Uri.EscapeDataString(calendarId)}/events";
        using var response = await SendAsync(HttpMethod.Post, url, accessToken, ToJsonBody(@event), ct);
        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        return ParseEvent(doc.RootElement);
    }

    public async Task<GoogleCalendarEventDto> PatchEventAsync(string accessToken, string calendarId, string eventId, GoogleCalendarEventDto @event, CancellationToken ct)
    {
        var url = $"{BaseUrl}/calendars/{Uri.EscapeDataString(calendarId)}/events/{Uri.EscapeDataString(eventId)}";
        using var response = await SendAsync(HttpMethod.Patch, url, accessToken, ToJsonBody(@event), ct);
        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        return ParseEvent(doc.RootElement);
    }

    public async Task DeleteEventAsync(string accessToken, string calendarId, string eventId, CancellationToken ct)
    {
        var url = $"{BaseUrl}/calendars/{Uri.EscapeDataString(calendarId)}/events/{Uri.EscapeDataString(eventId)}";
        using var response = await SendAsync(HttpMethod.Delete, url, accessToken, body: null, ct);
        response.Dispose();
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string accessToken, string? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (body is not null)
            request.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return response;
    }

    private static string ToJsonBody(GoogleCalendarEventDto e)
    {
        var payload = new Dictionary<string, object?>
        {
            ["summary"] = e.Title,
            ["description"] = e.Description,
            ["location"] = e.Location,
            ["start"] = e.IsAllDay
                ? new Dictionary<string, object?> { ["date"] = e.Start.ToString("yyyy-MM-dd") }
                : new Dictionary<string, object?> { ["dateTime"] = e.Start.ToString("O"), ["timeZone"] = e.Timezone },
            ["end"] = e.IsAllDay
                ? new Dictionary<string, object?> { ["date"] = e.End.ToString("yyyy-MM-dd") }
                : new Dictionary<string, object?> { ["dateTime"] = e.End.ToString("O"), ["timeZone"] = e.Timezone }
        };
        return JsonSerializer.Serialize(payload);
    }

    private static GoogleCalendarEventDto ParseEvent(JsonElement item)
    {
        var status = item.TryGetProperty("status", out var s) ? s.GetString() : null;
        var start = item.GetProperty("start");
        var end = item.GetProperty("end");
        var isAllDay = start.TryGetProperty("date", out _);

        DateTimeOffset ParseWhen(JsonElement whenElement) => isAllDay
            ? DateTimeOffset.Parse(whenElement.GetProperty("date").GetString()!)
            : DateTimeOffset.Parse(whenElement.GetProperty("dateTime").GetString()!);

        return new GoogleCalendarEventDto(
            Id: item.GetProperty("id").GetString()!,
            Etag: item.TryGetProperty("etag", out var etag) ? etag.GetString() : null,
            Title: item.TryGetProperty("summary", out var summary) ? summary.GetString() ?? string.Empty : string.Empty,
            Description: item.TryGetProperty("description", out var desc) ? desc.GetString() : null,
            Start: ParseWhen(start),
            End: ParseWhen(end),
            IsAllDay: isAllDay,
            Timezone: !isAllDay && start.TryGetProperty("timeZone", out var tz) ? tz.GetString() : null,
            Location: item.TryGetProperty("location", out var loc) ? loc.GetString() : null,
            IsCancelled: status == "cancelled");
    }
}
```

- [ ] **Step 2: Write the Microsoft Graph client interface, DTOs, and implementation**

```csharp
// src/ONEVO.Application/Features/Calendar/ServiceInterfaces/IMicrosoftGraphCalendarClient.cs
namespace ONEVO.Application.Features.Calendar.ServiceInterfaces;

public sealed record GraphEventDto(
    string Id,
    string? Etag,
    string Title,
    string? Description,
    DateTimeOffset Start,
    DateTimeOffset End,
    bool IsAllDay,
    string? Timezone,
    string? Location,
    bool IsCancelled);

public sealed record GraphCalendarPage(IReadOnlyList<GraphEventDto> Events, string? NextDeltaLink);

public interface IMicrosoftGraphCalendarClient
{
    Task<GraphCalendarPage> ListEventsAsync(string accessToken, string? deltaLink, DateTimeOffset windowStart, DateTimeOffset windowEnd, CancellationToken ct);
    Task<GraphEventDto> CreateEventAsync(string accessToken, GraphEventDto @event, CancellationToken ct);
    Task<GraphEventDto> UpdateEventAsync(string accessToken, string eventId, GraphEventDto @event, CancellationToken ct);
    Task DeleteEventAsync(string accessToken, string eventId, CancellationToken ct);
}
```

```csharp
// src/ONEVO.Infrastructure/ExternalServices/Calendar/MicrosoftGraphCalendarClient.cs
using System.Net.Http.Headers;
using System.Text.Json;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;

namespace ONEVO.Infrastructure.ExternalServices.Calendar;

public sealed class MicrosoftGraphCalendarClient(HttpClient httpClient) : IMicrosoftGraphCalendarClient
{
    private const string BaseUrl = "https://graph.microsoft.com/v1.0";

    public async Task<GraphCalendarPage> ListEventsAsync(string accessToken, string? deltaLink, DateTimeOffset windowStart, DateTimeOffset windowEnd, CancellationToken ct)
    {
        var url = deltaLink ?? $"{BaseUrl}/me/calendarView/delta?startDateTime={Uri.EscapeDataString(windowStart.ToString("O"))}&endDateTime={Uri.EscapeDataString(windowEnd.ToString("O"))}";

        using var response = await SendAsync(HttpMethod.Get, url, accessToken, body: null, ct);
        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        var events = new List<GraphEventDto>();
        foreach (var item in doc.RootElement.GetProperty("value").EnumerateArray())
            events.Add(ParseEvent(item));

        var nextDeltaLink = doc.RootElement.TryGetProperty("@odata.deltaLink", out var d) ? d.GetString() : null;
        return new GraphCalendarPage(events, nextDeltaLink);
    }

    public async Task<GraphEventDto> CreateEventAsync(string accessToken, GraphEventDto @event, CancellationToken ct)
    {
        using var response = await SendAsync(HttpMethod.Post, $"{BaseUrl}/me/events", accessToken, ToJsonBody(@event), ct);
        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        return ParseEvent(doc.RootElement);
    }

    public async Task<GraphEventDto> UpdateEventAsync(string accessToken, string eventId, GraphEventDto @event, CancellationToken ct)
    {
        using var response = await SendAsync(HttpMethod.Patch, $"{BaseUrl}/me/events/{Uri.EscapeDataString(eventId)}", accessToken, ToJsonBody(@event), ct);
        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        return ParseEvent(doc.RootElement);
    }

    public async Task DeleteEventAsync(string accessToken, string eventId, CancellationToken ct)
    {
        using var response = await SendAsync(HttpMethod.Delete, $"{BaseUrl}/me/events/{Uri.EscapeDataString(eventId)}", accessToken, body: null, ct);
        response.Dispose();
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string accessToken, string? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (body is not null)
            request.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return response;
    }

    private static string ToJsonBody(GraphEventDto e)
    {
        var payload = new Dictionary<string, object?>
        {
            ["subject"] = e.Title,
            ["body"] = new Dictionary<string, object?> { ["contentType"] = "text", ["content"] = e.Description ?? string.Empty },
            ["isAllDay"] = e.IsAllDay,
            ["location"] = new Dictionary<string, object?> { ["displayName"] = e.Location },
            ["start"] = new Dictionary<string, object?> { ["dateTime"] = e.Start.ToString("s"), ["timeZone"] = e.Timezone ?? "UTC" },
            ["end"] = new Dictionary<string, object?> { ["dateTime"] = e.End.ToString("s"), ["timeZone"] = e.Timezone ?? "UTC" }
        };
        return JsonSerializer.Serialize(payload);
    }

    private static GraphEventDto ParseEvent(JsonElement item)
    {
        var start = item.GetProperty("start");
        var end = item.GetProperty("end");
        var timezone = start.TryGetProperty("timeZone", out var tz) ? tz.GetString() : null;

        return new GraphEventDto(
            Id: item.GetProperty("id").GetString()!,
            Etag: item.TryGetProperty("@odata.etag", out var etag) ? etag.GetString() : null,
            Title: item.TryGetProperty("subject", out var subject) ? subject.GetString() ?? string.Empty : string.Empty,
            Description: item.TryGetProperty("body", out var body) && body.TryGetProperty("content", out var content) ? content.GetString() : null,
            Start: DateTimeOffset.Parse(start.GetProperty("dateTime").GetString()!),
            End: DateTimeOffset.Parse(end.GetProperty("dateTime").GetString()!),
            IsAllDay: item.TryGetProperty("isAllDay", out var allDay) && allDay.GetBoolean(),
            Timezone: timezone,
            Location: item.TryGetProperty("location", out var loc) && loc.TryGetProperty("displayName", out var name) ? name.GetString() : null,
            IsCancelled: item.TryGetProperty("isCancelled", out var cancelled) && cancelled.GetBoolean());
    }
}
```

Per the spec's "Open Items" note, confirm the delta-query response envelope (`@odata.deltaLink` field name, `value` array) and event field names against current Microsoft Graph v1.0 docs at build time — stable, long-standing fields, but verify rather than trust this plan's memory.

- [ ] **Step 3: Register both as typed HttpClients**

Open `src/ONEVO.Infrastructure/DependencyInjection.cs`, add next to the `ICalendarOAuthTokenExchangeClient` registration from Task 5:

```csharp
services.AddHttpClient<IGoogleCalendarClient, GoogleCalendarClient>(client => { client.Timeout = TimeSpan.FromSeconds(30); });
services.AddHttpClient<IMicrosoftGraphCalendarClient, MicrosoftGraphCalendarClient>(client => { client.Timeout = TimeSpan.FromSeconds(30); });
```

- [ ] **Step 4: Write and run the tests**

Mirror `CalendarOAuthTokenExchangeClientTests`' `StubHandler` pattern (Task 5, Step 4) for both clients. For `GoogleCalendarClientTests.cs`: one test proving `ListEventsAsync` parses a timed event and an all-day event correctly (`IsAllDay` true/false, `Start`/`End` parsed from the right JSON shape per branch) and captures `nextSyncToken`; one test proving `InsertEventAsync` posts to the right URL and parses the response back into a `GoogleCalendarEventDto`. For `MicrosoftGraphCalendarClientTests.cs`: the equivalent two tests using Graph's response shapes (`@odata.deltaLink`, `value`, `subject`/`body.content`).

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~GoogleCalendarClientTests|FullyQualifiedName~MicrosoftGraphCalendarClientTests" --nologo`
Expected: `Passed! - Failed: 0, Passed: 4`

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Features/Calendar/ServiceInterfaces/IGoogleCalendarClient.cs src/ONEVO.Application/Features/Calendar/ServiceInterfaces/IMicrosoftGraphCalendarClient.cs src/ONEVO.Infrastructure/ExternalServices/Calendar/GoogleCalendarClient.cs src/ONEVO.Infrastructure/ExternalServices/Calendar/MicrosoftGraphCalendarClient.cs src/ONEVO.Infrastructure/DependencyInjection.cs tests/ONEVO.Tests.Unit/Features/Calendar/GoogleCalendarClientTests.cs tests/ONEVO.Tests.Unit/Features/Calendar/MicrosoftGraphCalendarClientTests.cs
git commit -m "feat(calendar): add IGoogleCalendarClient and IMicrosoftGraphCalendarClient"
```

---

### Task 10: `CalendarSyncService` + `CalendarSyncJob` (background sync)

**Files:**
- Modify: `src/ONEVO.Application/Features/Calendar/ServiceInterfaces/IGoogleCalendarClient.cs` — add `bool IsPrivate` to `GoogleCalendarEventDto` (Google's `visibility` field: `"private"`/`"confidential"` → `true`)
- Modify: `src/ONEVO.Application/Features/Calendar/ServiceInterfaces/IMicrosoftGraphCalendarClient.cs` — add `bool IsPrivate` to `GraphEventDto` (Graph's `sensitivity` field: `"private"`/`"confidential"` → `true`)
- Modify: `src/ONEVO.Infrastructure/ExternalServices/Calendar/GoogleCalendarClient.cs` — parse/serialize the new field
- Modify: `src/ONEVO.Infrastructure/ExternalServices/Calendar/MicrosoftGraphCalendarClient.cs` — parse/serialize the new field
- Modify: `src/ONEVO.Application/Features/Calendar/RepositoryInterfaces/ICalendarEventRepository.cs` — add one method for finding manually-created events eligible to push
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/Calendar/EfCalendarEventRepository.cs` — implement it
- Create: `src/ONEVO.Application/Features/Calendar/ServiceInterfaces/ICalendarSyncService.cs` is already declared (Task 7) — do not recreate it, only implement it
- Create: `src/ONEVO.Infrastructure/Services/Calendar/CalendarSyncService.cs`
- Create: `src/ONEVO.Infrastructure/Services/Calendar/CalendarSyncJob.cs`
- Modify: `src/ONEVO.Infrastructure/DependencyInjection.cs` — register `ICalendarSyncService` (scoped) and `CalendarSyncJob` (hosted service)
- Modify: `src/ONEVO.Api/appsettings.json` and `appsettings.Development.json` — add `Urls:CalendarOAuthCallbackBaseUrl` (see Global Constraints — do this now if not already done in an earlier task)
- Test: `tests/ONEVO.Tests.Unit/Features/Calendar/CalendarSyncServiceTests.cs`

**Interfaces:**
- Consumes: everything from Tasks 4, 5, 9 (repositories, token exchange client, provider event clients).
- Produces: `CalendarSyncService : ICalendarSyncService` with `SyncConnectionAsync(Guid tenantId, Guid connectionId, CancellationToken ct)` (used directly by `TriggerCalendarSyncCommandHandler`, Task 7, and by `CalendarSyncJob` below), `CalendarSyncJob : BackgroundService` (runs every 15 minutes across all tenants).

- [ ] **Step 1: Add `IsPrivate` to both provider event DTOs**

In `GoogleCalendarEventDto`, add `bool IsPrivate` as the last positional parameter. In `GoogleCalendarClient.ParseEvent`, add: `IsPrivate: item.TryGetProperty("visibility", out var vis) && (vis.GetString() == "private" || vis.GetString() == "confidential")`.

In `GraphEventDto`, add `bool IsPrivate` as the last positional parameter. In `MicrosoftGraphCalendarClient.ParseEvent`, add: `IsPrivate: item.TryGetProperty("sensitivity", out var sens) && (sens.GetString() == "private" || sens.GetString() == "confidential")`.

Neither `ToJsonBody` method needs to change — this pass never pushes a OneVo event's own `IsPrivate` flag as `visibility`/`sensitivity` to the provider (out of scope; only inbound redaction is required by the spec).

- [ ] **Step 2: Add the push-candidates repository method**

Add to `ICalendarEventRepository` (`src/ONEVO.Application/Features/Calendar/RepositoryInterfaces/ICalendarEventRepository.cs`):

```csharp
/// <summary>Manual events the caller created that changed after `since` and are not soft-deleted -
/// candidates for CalendarSyncService's push direction. Recurring masters/children are excluded
/// (Recurrence != None) - pushing recurring events to external providers is a follow-on, not this pass.</summary>
Task<IReadOnlyList<CalendarEvent>> GetManualEventsUpdatedSinceForUserAsync(
    Guid tenantId, Guid userId, DateTimeOffset since, CancellationToken ct = default);
```

Add to `EfCalendarEventRepository`:

```csharp
public async Task<IReadOnlyList<CalendarEvent>> GetManualEventsUpdatedSinceForUserAsync(
    Guid tenantId, Guid userId, DateTimeOffset since, CancellationToken ct = default)
    => await _db.CalendarEvents.AsNoTracking()
        .Where(e => e.TenantId == tenantId && e.CreatedById == userId
                    && e.SourceType == CalendarEventSourceTypes.Manual
                    && e.Recurrence == CalendarRecurrences.None
                    && (e.UpdatedAt ?? e.CreatedAt) > since)
        .ToListAsync(ct);
```

(Match the field name EF repository constructor actually uses for the injected `ApplicationDbContext` — Task 1/2's `EfCalendarEventRepository` used a private `_db` field via primary constructor; confirm and match exactly, don't introduce a second style in the same class.)

- [ ] **Step 3: Write the sync service's failing tests**

```csharp
// tests/ONEVO.Tests.Unit/Features/Calendar/CalendarSyncServiceTests.cs
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.ServiceInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Infrastructure.Services.Calendar;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class CalendarSyncServiceTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ConnectionId = Guid.NewGuid();

    private readonly Mock<IExternalCalendarConnectionRepository> _connections = new();
    private readonly Mock<IExternalCalendarEventLinkRepository> _links = new();
    private readonly Mock<ICalendarEventRepository> _events = new();
    private readonly Mock<ICalendarOAuthTokenExchangeClient> _tokenClient = new();
    private readonly Mock<IGoogleCalendarClient> _googleClient = new();
    private readonly Mock<IMicrosoftGraphCalendarClient> _msClient = new();
    private readonly Mock<IPlatformOAuthAppResolver> _appResolver = new();
    private readonly Mock<IEncryptionService> _encryption = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();

    private CalendarSyncService BuildSut()
    {
        _unitOfWork
            .Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<bool>>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<bool>>, CancellationToken>((action, ct) => action(ct));
        _encryption.Setup(x => x.DecryptBytes(It.IsAny<byte[]>())).Returns("decrypted-token");
        _encryption.Setup(x => x.EncryptBytes(It.IsAny<string>())).Returns<string>(s => System.Text.Encoding.UTF8.GetBytes(s));
        return new CalendarSyncService(
            _connections.Object, _links.Object, _events.Object, _tokenClient.Object,
            _googleClient.Object, _msClient.Object, _appResolver.Object, _encryption.Object,
            _unitOfWork.Object, NullLogger<CalendarSyncService>.Instance);
    }

    private static ExternalCalendarConnection MakeConnection(string syncDirection, DateTimeOffset? expiresAt = null) => new()
    {
        Id = ConnectionId, TenantId = TenantId, UserId = Guid.NewGuid(),
        Provider = CalendarExternalSources.GoogleCalendar, ExternalAccountEmail = "me@acme.com",
        ExternalCalendarId = "me@acme.com", AccessTokenEncrypted = [1], RefreshTokenEncrypted = [2],
        SyncDirection = syncDirection, Status = ExternalCalendarConnectionStatuses.Active,
        ExpiresAt = expiresAt ?? DateTimeOffset.UtcNow.AddHours(1)
    };

    [Fact]
    public async Task SyncConnectionAsync_ConnectionNotFound_DoesNothing()
    {
        var sut = BuildSut();
        _connections.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, ConnectionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ExternalCalendarConnection?)null);

        await sut.SyncConnectionAsync(TenantId, ConnectionId, CancellationToken.None);

        _googleClient.Verify(x => x.ListEventsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SyncConnectionAsync_DisabledDirection_SkipsSync()
    {
        var sut = BuildSut();
        var connection = MakeConnection(CalendarSyncDirections.Disabled);
        _connections.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, ConnectionId, It.IsAny<CancellationToken>())).ReturnsAsync(connection);

        await sut.SyncConnectionAsync(TenantId, ConnectionId, CancellationToken.None);

        _googleClient.Verify(x => x.ListEventsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SyncConnectionAsync_PullOnly_UpsertsNewEventAsCalendarEvent()
    {
        var sut = BuildSut();
        var connection = MakeConnection(CalendarSyncDirections.PullOnly);
        _connections.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, ConnectionId, It.IsAny<CancellationToken>())).ReturnsAsync(connection);
        _googleClient.Setup(x => x.ListEventsAsync("decrypted-token", "me@acme.com", null, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GoogleCalendarPage(
                [new GoogleCalendarEventDto("ext-1", "etag-1", "Standup", null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), false, "UTC", null, false, false)],
                "sync-token-1"));
        _links.Setup(x => x.GetTrackedByConnectionAndExternalEventAsync(TenantId, ConnectionId, "ext-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ExternalCalendarEventLink?)null);

        await sut.SyncConnectionAsync(TenantId, ConnectionId, CancellationToken.None);

        _events.Verify(x => x.AddAsync(It.Is<CalendarEvent>(e => e.Title == "Standup" && e.SourceType == CalendarEventSourceTypes.ExternalSync), It.IsAny<CancellationToken>()), Times.Once);
        _links.Verify(x => x.AddAsync(It.Is<ExternalCalendarEventLink>(l => l.ExternalEventId == "ext-1" && l.SyncStatus == ExternalCalendarSyncStatuses.Synced), It.IsAny<CancellationToken>()), Times.Once);
        _connections.Verify(x => x.Update(It.Is<ExternalCalendarConnection>(c => c.FailureCount == 0 && c.LastSyncedAt != null)), Times.Once);
    }

    [Fact]
    public async Task SyncConnectionAsync_PullOnly_PrivateEvent_RedactsTitleAndDescription()
    {
        var sut = BuildSut();
        var connection = MakeConnection(CalendarSyncDirections.PullOnly);
        _connections.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, ConnectionId, It.IsAny<CancellationToken>())).ReturnsAsync(connection);
        _googleClient.Setup(x => x.ListEventsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GoogleCalendarPage(
                [new GoogleCalendarEventDto("ext-2", "etag-2", "Doctor appointment", "sensitive details", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), false, "UTC", "Clinic", false, IsPrivate: true)],
                null));
        _links.Setup(x => x.GetTrackedByConnectionAndExternalEventAsync(TenantId, ConnectionId, "ext-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ExternalCalendarEventLink?)null);

        await sut.SyncConnectionAsync(TenantId, ConnectionId, CancellationToken.None);

        _events.Verify(x => x.AddAsync(It.Is<CalendarEvent>(e =>
            e.Title == "Busy" && e.Description == null && e.Location == null && e.IsPrivate), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SyncConnectionAsync_TokenRefreshFails_SetsReauthRequiredAndSkipsSync()
    {
        var sut = BuildSut();
        var connection = MakeConnection(CalendarSyncDirections.PullOnly, expiresAt: DateTimeOffset.UtcNow.AddMinutes(2));
        _connections.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, ConnectionId, It.IsAny<CancellationToken>())).ReturnsAsync(connection);
        _appResolver.Setup(x => x.GetActiveCredentialForProviderAsync("google", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.ServiceInterfaces.ResolvedPlatformOAuthAppCredential("google", "client", "secret", null, 1));
        _appResolver.Setup(x => x.GetActiveAppForProviderAsync("google", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.ServiceInterfaces.ResolvedPlatformOAuthApp("google", "client", "https://accounts.google.com/o/oauth2/v2/auth", "https://oauth2.googleapis.com/token", ["scope"]));
        _tokenClient.Setup(x => x.RefreshTokenAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("invalid_grant"));

        await sut.SyncConnectionAsync(TenantId, ConnectionId, CancellationToken.None);

        _connections.Verify(x => x.Update(It.Is<ExternalCalendarConnection>(c => c.Status == ExternalCalendarConnectionStatuses.ReauthRequired)), Times.Once);
        _googleClient.Verify(x => x.ListEventsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SyncConnectionAsync_ListEventsThrows_IncrementsFailureCount()
    {
        var sut = BuildSut();
        var connection = MakeConnection(CalendarSyncDirections.PullOnly);
        connection.FailureCount = 2;
        _connections.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, ConnectionId, It.IsAny<CancellationToken>())).ReturnsAsync(connection);
        _googleClient.Setup(x => x.ListEventsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("rate limited"));

        await sut.SyncConnectionAsync(TenantId, ConnectionId, CancellationToken.None);

        _connections.Verify(x => x.Update(It.Is<ExternalCalendarConnection>(c => c.FailureCount == 3 && c.Status == ExternalCalendarConnectionStatuses.Failed)), Times.Once);
    }
}
```

`ExecuteInTransactionAsync`'s generic type in the test setup (`Func<CancellationToken, Task<bool>>`) is a placeholder for whatever the real handler's transactional return type ends up being — adjust the mock setup's generic type argument to match exactly what `CalendarSyncService`'s internal transaction call actually returns once Step 4 is written (Moq's strict generic matching means a mismatched type argument silently never triggers the setup, causing a null-reference rather than a clear test failure — watch for this while making the test pass).

- [ ] **Step 4: Run the tests to verify they fail, then write `CalendarSyncService`**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~CalendarSyncServiceTests" --nologo`
Expected: FAIL — `CalendarSyncService` does not exist yet.

```csharp
// src/ONEVO.Infrastructure/Services/Calendar/CalendarSyncService.cs
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.ServiceInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Infrastructure.Services.Calendar;

public sealed class CalendarSyncService(
    IExternalCalendarConnectionRepository connections,
    IExternalCalendarEventLinkRepository links,
    ICalendarEventRepository events,
    ICalendarOAuthTokenExchangeClient tokenExchangeClient,
    IGoogleCalendarClient googleClient,
    IMicrosoftGraphCalendarClient msClient,
    IPlatformOAuthAppResolver appResolver,
    IEncryptionService encryption,
    IUnitOfWork unitOfWork,
    ILogger<CalendarSyncService> logger)
    : ICalendarSyncService
{
    private static readonly TimeSpan SyncWindowPast = TimeSpan.FromDays(30);
    private static readonly TimeSpan SyncWindowFuture = TimeSpan.FromDays(180);
    private const int BatchLimitPerConnection = 200;
    private const int MaxConsecutiveFailures = 3;

    public async Task SyncConnectionAsync(Guid tenantId, Guid connectionId, CancellationToken ct)
    {
        var connection = await connections.GetTrackedByIdForTenantAsync(tenantId, connectionId, ct);
        if (connection is null || connection.SyncDirection == CalendarSyncDirections.Disabled)
            return;

        var oauthProvider = connection.Provider == CalendarExternalSources.GoogleCalendar ? "google" : "microsoft";

        var accessToken = await EnsureFreshAccessTokenAsync(connection, oauthProvider, ct);
        if (accessToken is null)
            return; // refresh failed - EnsureFreshAccessTokenAsync already marked ReauthRequired and saved.

        try
        {
            if (connection.SyncDirection is CalendarSyncDirections.PullOnly or CalendarSyncDirections.TwoWay)
                await PullAsync(connection, accessToken, ct);

            if (connection.SyncDirection is CalendarSyncDirections.PushOnly or CalendarSyncDirections.TwoWay)
                await PushAsync(connection, accessToken, ct);

            connection.LastSyncedAt = DateTimeOffset.UtcNow;
            connection.LastSuccessfulSyncAt = DateTimeOffset.UtcNow;
            connection.FailureCount = 0;
            connection.LastError = null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Calendar sync failed for connection {ConnectionId}.", connection.Id);
            connection.FailureCount++;
            connection.LastError = ex.Message;
            connection.LastSyncedAt = DateTimeOffset.UtcNow;
            if (connection.FailureCount >= MaxConsecutiveFailures)
                connection.Status = ExternalCalendarConnectionStatuses.Failed;
        }

        connections.Update(connection);
        await unitOfWork.SaveChangesAsync(ct);
    }

    private async Task<string?> EnsureFreshAccessTokenAsync(ExternalCalendarConnection connection, string oauthProvider, CancellationToken ct)
    {
        var needsRefresh = connection.ExpiresAt is null || connection.ExpiresAt <= DateTimeOffset.UtcNow.AddMinutes(5);
        if (!needsRefresh && connection.AccessTokenEncrypted is not null)
            return encryption.DecryptBytes(connection.AccessTokenEncrypted);

        try
        {
            var app = await appResolver.GetActiveAppForProviderAsync(oauthProvider, ct);
            var credential = await appResolver.GetActiveCredentialForProviderAsync(oauthProvider, ct);
            if (app is null || credential is null)
                throw new InvalidOperationException($"No active OAuth app configured for provider '{oauthProvider}'.");

            var refreshToken = encryption.DecryptBytes(connection.RefreshTokenEncrypted);
            var tokens = await tokenExchangeClient.RefreshTokenAsync(app.TokenUrl, credential.ClientId, credential.ClientSecret, refreshToken, ct);

            connection.AccessTokenEncrypted = encryption.EncryptBytes(tokens.AccessToken);
            connection.RefreshTokenEncrypted = encryption.EncryptBytes(tokens.RefreshToken ?? refreshToken);
            connection.ExpiresAt = tokens.ExpiresAt;
            return tokens.AccessToken;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Token refresh failed for connection {ConnectionId}; marking reauth_required.", connection.Id);
            connection.Status = ExternalCalendarConnectionStatuses.ReauthRequired;
            connection.LastError = ex.Message;
            connections.Update(connection);
            await unitOfWork.SaveChangesAsync(ct);
            return null;
        }
    }

    private async Task PullAsync(ExternalCalendarConnection connection, string accessToken, CancellationToken ct)
    {
        var calendarId = connection.ExternalCalendarId ?? connection.ExternalAccountEmail;
        var windowStart = DateTimeOffset.UtcNow.Subtract(SyncWindowPast);
        var windowEnd = DateTimeOffset.UtcNow.Add(SyncWindowFuture);
        var syncToken = connection.SyncTokenEncrypted is not null ? encryption.DecryptBytes(connection.SyncTokenEncrypted) : null;

        if (connection.Provider == CalendarExternalSources.GoogleCalendar)
        {
            var page = await googleClient.ListEventsAsync(accessToken, calendarId, syncToken, windowStart, windowEnd, ct);
            foreach (var item in page.Events.Take(BatchLimitPerConnection))
                await UpsertPulledEventAsync(connection, item.Id, item.Etag, item.Title, item.Description, item.Start, item.End, item.IsAllDay, item.Timezone, item.Location, item.IsCancelled, item.IsPrivate, ct);
            if (page.NextSyncToken is not null)
                connection.SyncTokenEncrypted = encryption.EncryptBytes(page.NextSyncToken);
        }
        else
        {
            var deltaLink = connection.DeltaLinkEncrypted is not null ? encryption.DecryptBytes(connection.DeltaLinkEncrypted) : null;
            var page = await msClient.ListEventsAsync(accessToken, deltaLink, windowStart, windowEnd, ct);
            foreach (var item in page.Events.Take(BatchLimitPerConnection))
                await UpsertPulledEventAsync(connection, item.Id, item.Etag, item.Title, item.Description, item.Start, item.End, item.IsAllDay, item.Timezone, item.Location, item.IsCancelled, item.IsPrivate, ct);
            if (page.NextDeltaLink is not null)
                connection.DeltaLinkEncrypted = encryption.EncryptBytes(page.NextDeltaLink);
        }
    }

    private async Task UpsertPulledEventAsync(
        ExternalCalendarConnection connection, string externalEventId, string? etag, string title, string? description,
        DateTimeOffset start, DateTimeOffset end, bool isAllDay, string? timezone, string? location, bool isCancelled, bool isPrivate, CancellationToken ct)
    {
        var link = await links.GetTrackedByConnectionAndExternalEventAsync(connection.TenantId, connection.Id, externalEventId, ct);

        if (isCancelled)
        {
            if (link is not null)
            {
                var existingEvent = await events.GetTrackedByIdForTenantAsync(connection.TenantId, link.CalendarEventId, ct);
                if (existingEvent is not null)
                    events.Remove(existingEvent);
                links.Remove(link);
            }
            return;
        }

        var displayTitle = isPrivate ? "Busy" : title;
        var displayDescription = isPrivate ? null : description;
        var displayLocation = isPrivate ? null : location;

        if (link is null)
        {
            var calendarEvent = new CalendarEvent
            {
                Id = Guid.NewGuid(), TenantId = connection.TenantId, Title = displayTitle, Description = displayDescription,
                StartDate = start, EndDate = end, SourceType = CalendarEventSourceTypes.ExternalSync,
                ExternalId = externalEventId, ExternalSource = connection.Provider, IsAllDay = isAllDay,
                Timezone = timezone, IsPrivate = isPrivate, Location = displayLocation,
                CreatedById = connection.UserId, CreatedAt = DateTimeOffset.UtcNow, ExternalUpdatedAt = DateTimeOffset.UtcNow
            };
            await events.AddAsync(calendarEvent, ct);
            await links.AddAsync(new ExternalCalendarEventLink
            {
                Id = Guid.NewGuid(), TenantId = connection.TenantId, CalendarEventId = calendarEvent.Id,
                ExternalCalendarConnectionId = connection.Id, Provider = connection.Provider,
                ExternalCalendarId = connection.ExternalCalendarId ?? connection.ExternalAccountEmail, ExternalEventId = externalEventId,
                ExternalEtag = etag, SyncDirection = ExternalCalendarLinkDirections.Inbound,
                SyncStatus = ExternalCalendarSyncStatuses.Synced, LastSyncedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow
            }, ct);
            return;
        }

        var localEvent = await events.GetTrackedByIdForTenantAsync(connection.TenantId, link.CalendarEventId, ct);
        if (localEvent is null)
            return;

        // Two-way conflict check: both sides changed since the last successful sync -> pull wins,
        // flag the link as conflict for later admin visibility (no resolution UI in this pass).
        var bothSidesChanged = connection.SyncDirection == CalendarSyncDirections.TwoWay
            && link.ExternalEtag != etag
            && connection.LastSyncedAt is not null
            && localEvent.UpdatedAt is not null && localEvent.UpdatedAt > connection.LastSyncedAt;

        localEvent.Title = displayTitle;
        localEvent.Description = displayDescription;
        localEvent.StartDate = start;
        localEvent.EndDate = end;
        localEvent.IsAllDay = isAllDay;
        localEvent.Timezone = timezone;
        localEvent.Location = displayLocation;
        localEvent.IsPrivate = isPrivate;
        localEvent.ExternalUpdatedAt = DateTimeOffset.UtcNow;
        localEvent.UpdatedAt = DateTimeOffset.UtcNow;
        events.Update(localEvent);

        link.ExternalEtag = etag;
        link.LastSyncedAt = DateTimeOffset.UtcNow;
        link.SyncStatus = bothSidesChanged ? ExternalCalendarSyncStatuses.Conflict : ExternalCalendarSyncStatuses.Synced;
        if (bothSidesChanged)
            link.LastError = "Both the local event and the external event changed since the last sync; the external version was kept.";
        links.Update(link);
    }

    private async Task PushAsync(ExternalCalendarConnection connection, string accessToken, CancellationToken ct)
    {
        var since = connection.LastSyncedAt ?? DateTimeOffset.UtcNow.Subtract(SyncWindowPast);
        var candidates = await events.GetManualEventsUpdatedSinceForUserAsync(connection.TenantId, connection.UserId, since, ct);
        var calendarId = connection.ExternalCalendarId ?? connection.ExternalAccountEmail;

        foreach (var localEvent in candidates.Take(BatchLimitPerConnection))
        {
            var existingLink = await links.GetTrackedByCalendarEventAndConnectionAsync(connection.TenantId, localEvent.Id, connection.Id, ct);

            if (connection.Provider == CalendarExternalSources.GoogleCalendar)
            {
                var dto = new GoogleCalendarEventDto(existingLink?.ExternalEventId ?? string.Empty, existingLink?.ExternalEtag, localEvent.Title,
                    localEvent.Description, localEvent.StartDate, localEvent.EndDate, localEvent.IsAllDay, localEvent.Timezone, localEvent.Location, false, false);
                var pushed = existingLink is null
                    ? await googleClient.InsertEventAsync(accessToken, calendarId, dto, ct)
                    : await googleClient.PatchEventAsync(accessToken, calendarId, existingLink.ExternalEventId, dto, ct);
                UpsertOutboundLink(connection, localEvent.Id, existingLink, pushed.Id, pushed.Etag, calendarId);
            }
            else
            {
                var dto = new GraphEventDto(existingLink?.ExternalEventId ?? string.Empty, existingLink?.ExternalEtag, localEvent.Title,
                    localEvent.Description, localEvent.StartDate, localEvent.EndDate, localEvent.IsAllDay, localEvent.Timezone, localEvent.Location, false, false);
                var pushed = existingLink is null
                    ? await msClient.CreateEventAsync(accessToken, dto, ct)
                    : await msClient.UpdateEventAsync(accessToken, existingLink.ExternalEventId, dto, ct);
                UpsertOutboundLink(connection, localEvent.Id, existingLink, pushed.Id, pushed.Etag, calendarId);
            }
        }
    }

    private void UpsertOutboundLink(ExternalCalendarConnection connection, Guid calendarEventId, ExternalCalendarEventLink? existingLink, string externalEventId, string? etag, string calendarId)
    {
        if (existingLink is not null)
        {
            existingLink.ExternalEventId = externalEventId;
            existingLink.ExternalEtag = etag;
            existingLink.LastSyncedAt = DateTimeOffset.UtcNow;
            existingLink.SyncStatus = ExternalCalendarSyncStatuses.Synced;
            links.Update(existingLink);
            return;
        }

        links.AddAsync(new ExternalCalendarEventLink
        {
            Id = Guid.NewGuid(), TenantId = connection.TenantId, CalendarEventId = calendarEventId,
            ExternalCalendarConnectionId = connection.Id, Provider = connection.Provider, ExternalCalendarId = calendarId,
            ExternalEventId = externalEventId, ExternalEtag = etag, SyncDirection = ExternalCalendarLinkDirections.Outbound,
            SyncStatus = ExternalCalendarSyncStatuses.Synced, LastSyncedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow
        }, CancellationToken.None).GetAwaiter().GetResult();
    }
}
```

`GoogleCalendarEventDto`/`GraphEventDto` constructor calls above use 11 positional arguments matching the order after Step 1's `IsPrivate` addition (`Id, Etag, Title, Description, Start, End, IsAllDay, Timezone, Location, IsCancelled, IsPrivate`) — if Step 1 placed `IsPrivate` anywhere other than last, update every construction site here (and in Task 9's tests) to match the real order.

`UpsertOutboundLink`'s `.GetAwaiter().GetResult()` on `links.AddAsync(...)` is a deliberate simplification only because `EfExternalCalendarEventLinkRepository.AddAsync` (Task 4) just calls `DbSet.AddAsync` internally, which is synchronous in practice for a non-persisted, non-value-generated-on-add entity — if this trips a "never block on async" concern in review, make `UpsertOutboundLink` itself `async Task` and `await` it from both call sites in `PushAsync` instead; either is correct, prefer whichever the task reviewer flags as cleaner.

This handler does not wrap its writes in `unitOfWork.ExecuteInTransactionAsync` the way every CQRS command handler does — `SyncConnectionAsync` runs one connection's whole pull+push cycle as a single unit of work bounded by the single `SaveChangesAsync` call at the end of the outer method, which is the correct granularity here (a background job syncing one connection, not a user-facing request/response). This is a deliberate deviation from the Global Constraint for CQRS command handlers, not an oversight.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~CalendarSyncServiceTests" --nologo`
Expected: `Passed! - Failed: 0, Passed: 6`

- [ ] **Step 6: Write `CalendarSyncJob`**

```csharp
// src/ONEVO.Infrastructure/Services/Calendar/CalendarSyncJob.cs
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Tenancy.Entities;

namespace ONEVO.Infrastructure.Services.Calendar;

public sealed class CalendarSyncJob(IServiceProvider services, ILogger<CalendarSyncJob> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);
    private const int TenantPageSize = 100;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try { await RunOnceAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "CalendarSyncJob run failed."); }
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var tenants = scope.ServiceProvider.GetRequiredService<ITenantRepository>();
        var switcher = scope.ServiceProvider.GetRequiredService<ITenantContextSwitcher>();

        var skip = 0;
        while (true)
        {
            var page = await tenants.ListAsync(TenantStatus.Active, searchTerm: null, skip, TenantPageSize, ct);
            if (page.Count == 0) break;

            foreach (var tenant in page)
            {
                await switcher.SwitchToTenantAsync(new TenantRegistryEntry(tenant.Id, tenant.Slug, tenant.Status, null), ct);

                var connections = scope.ServiceProvider.GetRequiredService<IExternalCalendarConnectionRepository>();
                var syncService = scope.ServiceProvider.GetRequiredService<ICalendarSyncService>();

                foreach (var connection in await connections.GetActiveAsync(ct))
                {
                    if (connection.SyncDirection == CalendarSyncDirections.Disabled) continue;
                    await syncService.SyncConnectionAsync(tenant.Id, connection.Id, ct);
                }
            }

            skip += TenantPageSize;
        }
    }
}
```

One `IServiceScope` for the whole run (not one per tenant) — `ICalendarSyncService`/`IGoogleCalendarClient`/`IMicrosoftGraphCalendarClient` are stateless, but `IExternalCalendarConnectionRepository`/`ICalendarSyncService` are still resolved fresh inside the loop via `scope.ServiceProvider.GetRequiredService` on each tenant iteration so they see the just-switched tenant context (they query the DB on-demand, not at scope-creation time) — do not hoist those two resolutions above the `foreach (var tenant in page)` loop.

- [ ] **Step 7: Register in DI**

Open `src/ONEVO.Infrastructure/DependencyInjection.cs`:

```csharp
services.AddScoped<ICalendarSyncService, CalendarSyncService>();
services.AddHostedService<Services.Calendar.CalendarSyncJob>();
```

Add the second line next to the existing `services.AddHostedService<...AgentCommandExpiryJob>();`/`services.AddHostedService<Services.WorkManagement.SprintLifecycleJob>();` lines.

- [ ] **Step 8: Add the callback base URL config (if not already added in Task 6)**

In `src/ONEVO.Api/appsettings.json`, under `"Urls"`, add: `"CalendarOAuthCallbackBaseUrl": ""`.
In `src/ONEVO.Api/appsettings.Development.json`, under `"Urls"`, add: `"CalendarOAuthCallbackBaseUrl": "https://localhost:7229"`.

- [ ] **Step 9: Build everything and run the full suites**

Stop the running backend dev server first (it locks the DLLs). Then:

Run: `dotnet build src/ONEVO.Api/ONEVO.Api.csproj --nologo`
Expected: `Build succeeded. 0 Error(s)`

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --nologo`
Expected: all pre-existing tests plus every Calendar external-sync test across all 10 tasks pass; zero new failures anywhere else.

Run: `dotnet test tests/ONEVO.Tests.Architecture/ONEVO.Tests.Architecture.csproj --nologo`
Expected: all pass.

- [ ] **Step 10: Manually verify end-to-end (requires real Google/Microsoft OAuth app credentials configured in `PlatformOAuthApps` for at least one provider)**

Restart the backend dev server. Authenticated as a tenant user: `POST /api/v1/calendar/connections/google/connect`, confirm `{authorizeUrl}` back, open it in a browser, complete Google's consent screen, confirm the browser lands back on `.../calendar?connected=google`. `GET /api/v1/calendar/connections` shows the new connection with `status: "active"`. Create an event in the connected Google Calendar directly (outside the app); wait up to 15 minutes (or `POST /api/v1/calendar/connections/{id}/sync` to force it immediately); confirm `GET /api/v1/calendar?from=&to=` now includes it with `sourceType: "external_sync"`. `DELETE /api/v1/calendar/connections/{id}`; confirm the synced event disappears from a following `GET`.

If no real OAuth app credentials are available in this environment, skip this step and note it as a known gap in the final report — the automated test suite (Steps 5 and 9) is the primary verification for this task.

- [ ] **Step 11: Commit**

```bash
git add src/ONEVO.Application/Features/Calendar/ServiceInterfaces/IGoogleCalendarClient.cs src/ONEVO.Application/Features/Calendar/ServiceInterfaces/IMicrosoftGraphCalendarClient.cs src/ONEVO.Infrastructure/ExternalServices/Calendar/GoogleCalendarClient.cs src/ONEVO.Infrastructure/ExternalServices/Calendar/MicrosoftGraphCalendarClient.cs src/ONEVO.Application/Features/Calendar/RepositoryInterfaces/ICalendarEventRepository.cs src/ONEVO.Infrastructure/Persistence/Repositories/Calendar/EfCalendarEventRepository.cs src/ONEVO.Infrastructure/Services/Calendar/ src/ONEVO.Infrastructure/DependencyInjection.cs src/ONEVO.Api/appsettings.json src/ONEVO.Api/appsettings.Development.json tests/ONEVO.Tests.Unit/Features/Calendar/CalendarSyncServiceTests.cs
git commit -m "feat(calendar): add CalendarSyncService and CalendarSyncJob"
```

