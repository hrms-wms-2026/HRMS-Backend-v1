# Dev Smoke Multi-Tenant Seed Expansion Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Expand `DevSmokeTestTenantSeeder` (Development/Test only) so local manual testing has two tenants (acme, dapi), three Acme users with distinct roles/permissions, one Dapi owner, three Acme legal entities, and one Dapi legal entity — all idempotent, with no Department/Position/position_assignments/legal-entity-membership modeling.

**Architecture:** Replace the seeder's single hardcoded tenant/user/role with small immutable definition records (`SmokeTenantDefinition`, `SmokeUserDefinition`, `SmokeLegalEntityDefinition`) built once in a static factory method, then loop over tenants seeding tenant → legal entities → users → roles/permissions → auth policy → subscription → global_email_directory (scoped delete + upsert per tenant). The existing GitHub OAuth / legal-document bootstrap block stays tied to Acme only (tenant-incidental, out of scope for this task). No Employee/Department/Position rows are created — Employee.LegalEntityId already models "one employee = one legal entity" and is intentionally left for a later task.

**Tech Stack:** EF Core (Npgsql provider in production, InMemory + SQLite in unit tests), xUnit, FluentAssertions/Moq (existing test conventions).

## Global Constraints

- Work only in `C:\onevoNew\HRMS-Backend-v1`.
- Seeder remains Development/Test only (`IHostedService.StartAsync` environment guard unchanged).
- Idempotent: rerunning must not duplicate tenants, users, roles, role_permissions, user_roles, legal_entities, global_email_directory rows, subscriptions, tenant_auth_policies.
- No Employee, Department, Position, position_assignments, or legal-entity-membership table/rows.
- No LegalEntityId added to UserRole.
- Every RolePermission/UserRole row has non-empty TenantId.
- If a requested permission code is missing, throw (fail loud) — do not silently skip.
- Do not touch Postman files, migrations (unless proven required), production bootstrap, auth/login handlers, Department/Position APIs.
- No commits/pushes.
- Block-bodied methods only (no expression-bodied members) in the seeder.
- Password `Password123!` only inside this Development/Test seeder.

---

## Task 1: Rewrite DevSmokeTestTenantSeeder for multi-tenant, multi-user, multi-legal-entity seeding

**Files:**
- Modify: `src/ONEVO.Infrastructure/Persistence/Seeders/DevSmokeTestTenantSeeder.cs`

**Interfaces:**
- Consumes: `ApplicationDbContext` (`Tenants`, `Users`, `Roles`, `RolePermissions`, `UserRoles`, `Permissions`, `LegalEntities`, `TenantAuthPolicies`, `TenantSubscriptions`, `SubscriptionPlans`, `PlatformUsers`, `PlatformOAuthApps`, `PlatformOAuthAppCredentials`, `IntegrationCatalogEntries`, `ModuleIntegrationLinks`, `TenantIntegrationCredentials`, `LegalDocumentVersions`), `IWritableTenantContext`, `IPasswordHasher.Hash(string)`, `IEncryptionService.Encrypt(string)`, `IConfiguration`.
- Produces: `public static Task SeedAsync(ApplicationDbContext, IWritableTenantContext, IPasswordHasher, IEncryptionService, IConfiguration, CancellationToken)` — unchanged signature, so existing callers (and future unit tests) keep working.

Fixed IDs (already generated, do not regenerate):

```
Acme tenant   = da810816-3fed-4e71-9a44-f93e9b509bc7 (existing, keep)
Acme owner user = c468afc2-967a-4b9a-beae-6bce6652ffc1 (existing, keep)
Acme owner role = 70a8c52d-d8d8-4be2-b377-33e62088dfc4 (existing, keep)
Acme subscription = be53e2b6-b1c5-4765-b4f3-c73ef5387908 (existing, keep)
Acme HR manager user = 1f02b6b1-3699-476e-bcb7-079172a3ede8
Acme HR manager role = b59678fe-6295-4b89-8140-aed5b59e4f4c
Acme work manager user = bf868643-c87a-4a57-a84f-ab2005659650
Acme work manager role = 269883a4-4d4e-49e6-bc69-dcacb079168b
Acme Technologies (primary) = 2addcd1b-e3d3-4930-b66f-e53329fa7f55
Acme Solutions = 04372560-2487-44ba-ac47-c41a6fc42ceb
Acme Global Services = 675710d1-2b10-4594-8c99-0c22183d2fd9
Dapi tenant = 6b0874ab-71db-401f-859f-bdd50c1317fb
Dapi owner user = cd49a0c2-e978-4055-b8be-7d46a3727e94
Dapi owner role = 722d06e9-23fd-403c-b34c-e0b12f81e974
Dapi subscription = 23a3a903-3690-4cad-8927-4e6c221b7465
Dapi Technologies (primary) = 57fecfe8-1c1e-4a82-be4b-2c8451436420
```

- [ ] **Step 1: Add definition records and fixed IDs/constants**

Add near the top of the class (replacing the old single-tenant consts):

```csharp
private sealed record SmokeUserDefinition(
    Guid UserId,
    string Email,
    string FirstName,
    string LastName,
    Guid RoleId,
    string RoleName,
    string RoleDescription,
    IReadOnlyList<string>? PermissionCodes);

private sealed record SmokeLegalEntityDefinition(
    Guid Id,
    string Name,
    string CompanyCode,
    string CountryCode,
    string CurrencyCode,
    string Timezone,
    bool IsPrimary);

private sealed record SmokeTenantDefinition(
    Guid TenantId,
    string Slug,
    string Name,
    Guid SubscriptionId,
    IReadOnlyList<SmokeUserDefinition> Users,
    IReadOnlyList<SmokeLegalEntityDefinition> LegalEntities);
```

Keep `GitHubProvider`, `GitHubIntegrationKey`, `GitHubAuthorizationUrl`, `GitHubTokenUrl` consts as-is. Replace `TenantId`/`UserId`/`RoleId`/`SubscriptionId`/`TenantSlug`/`TenantName`/`UserEmail`/`UserPassword`/`RoleName` consts with the full set of fixed GUIDs and email/slug constants listed above, plus:

```csharp
private const string AcmeSlug = "acme";
private const string DapiSlug = "dapi";
private const string SmokeUserPassword = "Password123!";
private const string FullAccessPermissionCode = "*";

private const string AcmeOwnerEmail = "siyasiyamala932@gmail.com";
private const string AcmeHrManagerEmail = "paramanathanmuthaiya@gmail.com";
private const string AcmeWorkManagerEmail = "mrt15473@gmail.com";
private const string DapiOwnerEmail = "dapiyshanth1908@gmail.com";

private static readonly IReadOnlyList<string> HrManagerPermissionCodes =
[
    "org:read", "org:manage", "employees:read", "employees:write", "roles:read"
];

private static readonly IReadOnlyList<string> WorkManagerPermissionCodes =
[
    "org:read", "employees:read", "projects:read", "tasks:read", "tasks:write"
];
```

- [ ] **Step 2: Add `BuildTenantDefinitions()`**

```csharp
private static IReadOnlyList<SmokeTenantDefinition> BuildTenantDefinitions()
{
    return
    [
        new SmokeTenantDefinition(
            AcmeTenantId,
            AcmeSlug,
            "Acme Test",
            AcmeSubscriptionId,
            [
                new SmokeUserDefinition(
                    AcmeOwnerUserId, AcmeOwnerEmail, "Acme", "Owner",
                    AcmeOwnerRoleId, "Tenant Owner",
                    "Development smoke-test tenant owner.", null),
                new SmokeUserDefinition(
                    AcmeHrManagerUserId, AcmeHrManagerEmail, "Acme", "HR Manager",
                    AcmeHrManagerRoleId, "HR Manager",
                    "Development smoke-test HR/org manager role.", HrManagerPermissionCodes),
                new SmokeUserDefinition(
                    AcmeWorkManagerUserId, AcmeWorkManagerEmail, "Acme", "Work Manager",
                    AcmeWorkManagerRoleId, "Work Manager",
                    "Development smoke-test work/limited manager role.", WorkManagerPermissionCodes)
            ],
            [
                new SmokeLegalEntityDefinition(
                    AcmeLegalEntityTechnologiesId, "Acme Technologies", "ACME", "LK", "LKR", "Asia/Colombo", true),
                new SmokeLegalEntityDefinition(
                    AcmeLegalEntitySolutionsId, "Acme Solutions", "ACMESOL", "LK", "LKR", "Asia/Colombo", false),
                new SmokeLegalEntityDefinition(
                    AcmeLegalEntityGlobalServicesId, "Acme Global Services", "ACMEGS", "LK", "LKR", "Asia/Colombo", false)
            ]),
        new SmokeTenantDefinition(
            DapiTenantId,
            DapiSlug,
            "Dapi Test",
            DapiSubscriptionId,
            [
                new SmokeUserDefinition(
                    DapiOwnerUserId, DapiOwnerEmail, "Dapi", "Owner",
                    DapiOwnerRoleId, "Tenant Owner",
                    "Development smoke-test tenant owner.", null)
            ],
            [
                new SmokeLegalEntityDefinition(
                    DapiLegalEntityId, "Dapi Technologies", "DAPI", "LK", "LKR", "Asia/Colombo", true)
            ])
    ];
}
```

- [ ] **Step 3: Rewrite `SeedAsync` to loop over tenant definitions**

Preserve this exact call ordering inside the loop (the architecture test `Seeder_EstablishesAdminAndTenantContextsBeforeCorrespondingWrites` asserts the literal source-text order of `SeedTenantAsync(db`, `ResolveSmokeTenantContext(tenantContext, tenant);`, `SeedTenantUserAsync(db`):

```csharp
public static async Task SeedAsync(
    ApplicationDbContext db,
    IWritableTenantContext tenantContext,
    IPasswordHasher passwordHasher,
    IEncryptionService encryption,
    IConfiguration configuration,
    CancellationToken ct)
{
    var now = DateTimeOffset.UtcNow;
    tenantContext.SetAdminMode();

    var tenantDefinitions = BuildTenantDefinitions();
    Tenant? acmeTenant = null;
    User? acmeOwnerUser = null;

    foreach (var tenantDefinition in tenantDefinitions)
    {
        tenantContext.SetAdminMode();
        var tenant = await SeedTenantAsync(db, tenantDefinition, now, ct);
        await db.SaveChangesAsync(ct);

        ResolveSmokeTenantContext(tenantContext, tenant);
        await SeedTenantLegalEntitiesAsync(db, tenant.Id, tenantDefinition.LegalEntities, now, ct);

        User? firstUser = null;
        foreach (var userDefinition in tenantDefinition.Users)
        {
            var user = await SeedTenantUserAsync(db, tenant.Id, userDefinition, passwordHasher, now, ct);
            firstUser ??= user;
            await SeedTenantRoleAsync(db, tenant.Id, user.Id, userDefinition, now, ct);
        }

        await SeedTenantAuthPolicyAsync(db, tenant.Id, now, ct);
        await SeedTenantSubscriptionAsync(db, tenant.Id, firstUser!.Id, tenantDefinition.SubscriptionId, now, ct);
        await db.SaveChangesAsync(ct);

        tenantContext.SetAdminMode();
        var seededEmails = tenantDefinition.Users.Select(u => u.Email).ToArray();
        await SeedGlobalEmailDirectoryAsync(db, tenant.Id, seededEmails, ct);

        if (tenantDefinition.Slug == AcmeSlug)
        {
            acmeTenant = tenant;
            acmeOwnerUser = firstUser;
        }
    }

    await SeedDevelopmentLegalVersionsAsync(db, now, ct);

    var platformUser = await GetPlatformBootstrapUserAsync(db, ct);
    if (platformUser is null)
    {
        await db.SaveChangesAsync(ct);
        return;
    }

    var oauthApp = await SeedGitHubPlatformOAuthAppAsync(
        db,
        configuration,
        platformUser.Id,
        now,
        ct);
    if (oauthApp is not null)
    {
        await SeedGitHubPlatformOAuthCredentialAsync(
            db,
            configuration,
            encryption,
            oauthApp.Id,
            platformUser.Id,
            now,
            ct);

        await SeedGitHubIntegrationCatalogAsync(db, platformUser.Id, now, ct);
        await SeedGitHubModuleIntegrationLinkAsync(db, platformUser.Id, now, ct);
    }

    await db.SaveChangesAsync(ct);

    if (oauthApp is null)
    {
        return;
    }

    ResolveSmokeTenantContext(tenantContext, acmeTenant!);
    await SeedGitHubTenantApprovalAsync(db, acmeTenant!.Id, acmeOwnerUser!.Id, now, ct);
    await db.SaveChangesAsync(ct);
}
```

Note: the GitHub OAuth / legal-document bootstrap block is deliberately left tied to Acme only — it is tenant-incidental demo data, out of scope for the dapi expansion.

- [ ] **Step 4: Generalize `SeedTenantAsync`**

```csharp
private static async Task<Tenant> SeedTenantAsync(
    ApplicationDbContext db,
    SmokeTenantDefinition definition,
    DateTimeOffset now,
    CancellationToken ct)
{
    var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == definition.TenantId, ct);
    if (tenant is null)
    {
        tenant = new Tenant
        {
            Id = definition.TenantId,
            Name = definition.Name,
            Slug = definition.Slug,
            IndustryProfile = "office_it",
            CompanySizeRange = "51-200",
            Status = TenantStatus.Active,
            CreatedAt = now
        };
        db.Tenants.Add(tenant);
        return tenant;
    }

    tenant.Name = definition.Name;
    tenant.Slug = definition.Slug;
    tenant.Status = TenantStatus.Active;
    tenant.UpdatedAt = now;
    return tenant;
}
```

- [ ] **Step 5: Generalize `SeedTenantUserAsync`**

```csharp
private static async Task<User> SeedTenantUserAsync(
    ApplicationDbContext db,
    Guid tenantId,
    SmokeUserDefinition definition,
    IPasswordHasher passwordHasher,
    DateTimeOffset now,
    CancellationToken ct)
{
    // Matched by the definition's fixed UserId, not by email: an existing dev/test database
    // seeded before an address changed still has the row under the old email, and looking it
    // up by email would fall through to Add() with the same hardcoded Id - a primary-key
    // violation. Id is the stable anchor; email is just a field on the row it updates.
    var user = await db.Users.FirstOrDefaultAsync(u => u.Id == definition.UserId, ct);
    if (user is null)
    {
        user = new User
        {
            Id = definition.UserId,
            TenantId = tenantId,
            Email = definition.Email,
            FirstName = definition.FirstName,
            LastName = definition.LastName,
            PasswordHash = passwordHasher.Hash(SmokeUserPassword),
            IsActive = true,
            EmailVerified = true,
            MustChangePassword = false,
            PasswordSetByAdmin = false,
            CreatedAt = now,
            CreatedById = definition.UserId
        };
        db.Users.Add(user);
        return user;
    }

    user.Email = definition.Email;
    user.FirstName = definition.FirstName;
    user.LastName = definition.LastName;
    user.IsActive = true;
    user.EmailVerified = true;
    user.MustChangePassword = false;
    user.PasswordSetByAdmin = false;
    if (string.IsNullOrWhiteSpace(user.PasswordHash))
    {
        user.PasswordHash = passwordHasher.Hash(SmokeUserPassword);
    }
    user.UpdatedAt = now;
    return user;
}
```

- [ ] **Step 6: Replace `SeedTenantOwnerRoleAsync` with `SeedTenantRoleAsync` + `ResolveRolePermissionsAsync`**

```csharp
private static async Task SeedTenantRoleAsync(
    ApplicationDbContext db,
    Guid tenantId,
    Guid userId,
    SmokeUserDefinition userDefinition,
    DateTimeOffset now,
    CancellationToken ct)
{
    var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == userDefinition.RoleId, ct);
    if (role is null)
    {
        role = new Role
        {
            Id = userDefinition.RoleId,
            TenantId = tenantId,
            Name = userDefinition.RoleName,
            Description = userDefinition.RoleDescription,
            IsSystem = true,
            CreatedAt = now,
            CreatedById = userId
        };
        db.Roles.Add(role);
    }
    else
    {
        role.Name = userDefinition.RoleName;
        role.Description = userDefinition.RoleDescription;
        role.IsSystem = true;
        role.UpdatedAt = now;
    }

    var permissions = await ResolveRolePermissionsAsync(db, userDefinition.PermissionCodes, ct);
    foreach (var permission in permissions)
    {
        var exists = await db.RolePermissions.AnyAsync(
            rp => rp.TenantId == tenantId &&
                  rp.RoleId == role.Id &&
                  rp.PermissionId == permission.Id,
            ct);
        if (exists)
        {
            continue;
        }

        db.RolePermissions.Add(new RolePermission
        {
            TenantId = tenantId,
            RoleId = role.Id,
            PermissionId = permission.Id
        });
    }

    var assignmentExists = await db.UserRoles.AnyAsync(
        ur => ur.TenantId == tenantId &&
              ur.UserId == userId &&
              ur.RoleId == role.Id,
        ct);
    if (!assignmentExists)
    {
        db.UserRoles.Add(new UserRole
        {
            TenantId = tenantId,
            UserId = userId,
            RoleId = role.Id,
            AssignedAt = now,
            AssignedBy = userId
        });
    }
}

private static async Task<List<Permission>> ResolveRolePermissionsAsync(
    ApplicationDbContext db,
    IReadOnlyList<string>? explicitCodes,
    CancellationToken ct)
{
    if (explicitCodes is null)
    {
        // Tenant Owner: every currently seeded permission except the "*" bypass row - the
        // codebase already treats "*" as excluded from explicit tenant role grants
        // (DefaultRoleSeeder.SeedDefaultRolesAsync applies the same exclusion for Owner roles).
        return await db.Permissions
            .Where(p => p.Code != FullAccessPermissionCode)
            .ToListAsync(ct);
    }

    var permissions = new List<Permission>(explicitCodes.Count);
    foreach (var code in explicitCodes)
    {
        var permission = await db.Permissions.FirstOrDefaultAsync(p => p.Code == code, ct);
        if (permission is null)
        {
            throw new InvalidOperationException(
                $"Development smoke-test seeder requires permission code '{code}' but it does not " +
                "exist in the Permissions table. Add it to PermissionSeeder before seeding smoke-test roles.");
        }

        permissions.Add(permission);
    }

    return permissions;
}
```

- [ ] **Step 7: Add `SeedTenantLegalEntitiesAsync`**

```csharp
private static async Task SeedTenantLegalEntitiesAsync(
    ApplicationDbContext db,
    Guid tenantId,
    IReadOnlyList<SmokeLegalEntityDefinition> definitions,
    DateTimeOffset now,
    CancellationToken ct)
{
    foreach (var definition in definitions)
    {
        var legalEntity = await db.LegalEntities.FirstOrDefaultAsync(l => l.Id == definition.Id, ct);
        if (legalEntity is null)
        {
            legalEntity = new LegalEntity
            {
                Id = definition.Id,
                TenantId = tenantId,
                Name = definition.Name,
                CompanyCode = definition.CompanyCode,
                CountryCode = definition.CountryCode,
                CurrencyCode = definition.CurrencyCode,
                Timezone = definition.Timezone,
                IsPrimary = definition.IsPrimary,
                IsActive = true,
                CreatedAt = now
            };
            db.LegalEntities.Add(legalEntity);
            continue;
        }

        legalEntity.Name = definition.Name;
        legalEntity.CompanyCode = definition.CompanyCode;
        legalEntity.CountryCode = definition.CountryCode;
        legalEntity.CurrencyCode = definition.CurrencyCode;
        legalEntity.Timezone = definition.Timezone;
        legalEntity.IsPrimary = definition.IsPrimary;
        legalEntity.IsActive = true;
        legalEntity.UpdatedAt = now;
    }
}
```

- [ ] **Step 8: Fix `SeedGlobalEmailDirectoryAsync` to scope cleanup across all seeded emails for the tenant**

The current implementation deletes every row for the tenant except a single hardcoded email — with three Acme users this would delete two of them every run. Replace with:

```csharp
private static async Task SeedGlobalEmailDirectoryAsync(
    ApplicationDbContext db,
    Guid tenantId,
    IReadOnlyList<string> emails,
    CancellationToken ct)
{
    // Scoped cleanup: remove only rows for THIS tenant whose email is not one of the emails
    // this seeder currently seeds for THIS tenant (e.g. an address retired between seeder
    // versions). Never touches rows belonging to other tenants. Uses positional ExecuteSqlRaw
    // parameters (not string interpolation) to build the NOT IN list safely.
    var placeholders = string.Join(", ", emails.Select((_, index) => $"{{{index + 1}}}"));
    var parameters = new object[] { tenantId }.Concat(emails).ToArray();
    await db.Database.ExecuteSqlRawAsync(
        $"DELETE FROM global_email_directory WHERE tenant_id = {{0}} AND email NOT IN ({placeholders})",
        parameters,
        ct);

    foreach (var email in emails)
    {
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO global_email_directory (email, tenant_id)
            VALUES ({email}, {tenantId})
            ON CONFLICT (email, tenant_id) DO NOTHING
            """,
            ct);
    }
}
```

- [ ] **Step 9: Generalize `SeedTenantSubscriptionAsync` to accept a subscription ID parameter**

Same body as today, but replace the hardcoded `SubscriptionId` const with a `Guid subscriptionId` parameter, and update the call site in `SeedAsync` to pass `tenantDefinition.SubscriptionId`. `SeedTenantAuthPolicyAsync` is unchanged except it is now called once per tenant in the loop.

- [ ] **Step 10: Update the `StartAsync` log line**

Replace the removed `UserEmail` const reference:

```csharp
_logger.LogInformation(
    "Development smoke-test tenants seeded: {Slugs}",
    string.Join(", ", new[] { AcmeSlug, DapiSlug }));
```

- [ ] **Step 11: Build and run architecture tests**

Run:
```
dotnet build src\ONEVO.Api\ONEVO.Api.csproj --no-restore --verbosity minimal
dotnet test tests\ONEVO.Tests.Architecture\ONEVO.Tests.Architecture.csproj --no-restore --verbosity minimal
```
Expected: build succeeds; all `DevSmokeTestTenantSeederArchitectureTests` pass (string-ordering assertions must still hold given the loop body layout in Step 3).

- [ ] **Step 12: Do not commit.** Leave changes staged/unstaged per user instruction.

---

## Task 2: Unit tests for the expanded seeder

**Files:**
- Create: `tests/ONEVO.Tests.Unit/Features/DevPlatform/Tenancy/DevSmokeTestTenantSeederTests.cs`

**Interfaces:**
- Consumes: `DevSmokeTestTenantSeeder.SeedAsync(...)` (public static, Task 1 Step 3 signature), `PermissionSeeder` (reflection-invoke `SeedPermissionsAsync` exactly as `PermissionSeederTests.cs` already does, to populate real permission rows first), `ONEVO.Tests.Unit.Features.Auth.SqliteTestApplicationDbContext` (internal, same assembly, reused via `using ONEVO.Tests.Unit.Features.Auth;`), `ONEVO.Infrastructure.Identity.Tenancy.TenantContextAccessor` (real `IWritableTenantContext` impl), `ONEVO.Infrastructure.Identity.CurrentUser.AnonymousCurrentUser`, `ONEVO.Infrastructure.ExternalServices.Messaging.NoOpPublisher`.
- Produces: nothing consumed elsewhere — this is the terminal test file for the task.

Why SQLite instead of EF InMemory: `SeedGlobalEmailDirectoryAsync` uses `ExecuteSqlRawAsync`/`ExecuteSqlInterpolatedAsync`, which the EF InMemory provider cannot execute at all. `PostgresMfaChallengeStoreTests.cs` establishes the exact pattern for this: build `ApplicationDbContext` on a shared-cache SQLite in-memory connection via `SqliteTestApplicationDbContext`, call `EnsureCreated()` to build every EF-mapped table from the production model. `global_email_directory` is NOT part of the EF model (it only exists via a raw-SQL migration), so it must be created manually after `EnsureCreated()` with equivalent DDL, scoped to the test file only — this does not touch the real migration.

- [ ] **Step 1: Write the test file skeleton with SQLite fixture and permission bootstrap**

```csharp
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Infrastructure.ExternalServices.Messaging;
using ONEVO.Infrastructure.Identity.CurrentUser;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Seeders;
using ONEVO.Tests.Unit.Features.Auth;
using Xunit;

namespace ONEVO.Tests.Unit.Features.DevPlatform.Tenancy;

public sealed class DevSmokeTestTenantSeederTests : IDisposable
{
    private const string AcmeOwnerEmail = "siyasiyamala932@gmail.com";
    private const string AcmeHrManagerEmail = "paramanathanmuthaiya@gmail.com";
    private const string AcmeWorkManagerEmail = "mrt15473@gmail.com";
    private const string DapiOwnerEmail = "dapiyshanth1908@gmail.com";

    private readonly string _connectionString;
    private readonly SqliteConnection _masterConnection;
    private readonly TestClock _clock = new();

    public DevSmokeTestTenantSeederTests()
    {
        var databaseName = $"dev_smoke_seed_tests_{Guid.NewGuid():N}";
        _connectionString = $"Data Source={databaseName};Mode=Memory;Cache=Shared;Foreign Keys=False";
        _masterConnection = new SqliteConnection(_connectionString);
        _masterConnection.Open();

        using var schemaContext = CreateContext();
        schemaContext.Database.EnsureCreated();

        // global_email_directory has no EF entity mapping in production (it is created only via
        // a raw-SQL migration and accessed only via raw SQL), so EnsureCreated() does not create
        // it. Re-create the same shape here, test-only, so the seeder's raw INSERT/DELETE
        // statements have a table to operate on.
        schemaContext.Database.ExecuteSqlRaw(
            """
            CREATE TABLE IF NOT EXISTS global_email_directory (
                email TEXT NOT NULL,
                tenant_id TEXT NOT NULL,
                created_at TEXT NOT NULL DEFAULT '',
                PRIMARY KEY (email, tenant_id)
            );
            """);
    }

    public void Dispose()
    {
        _masterConnection.Dispose();
    }

    private ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        return new SqliteTestApplicationDbContext(
            options,
            new AuditableEntityInterceptor(new AnonymousCurrentUser(), _clock),
            new SoftDeleteInterceptor(_clock),
            new DomainEventDispatchInterceptor(new NoOpPublisher()),
            new TenantContextAccessor());
    }

    private async Task SeedPermissionsAsync(ApplicationDbContext db)
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddScoped(_ => db);
        var sp = services.BuildServiceProvider();
        var seeder = new PermissionSeeder(sp, NullLogger<PermissionSeeder>.Instance);
        var method = typeof(PermissionSeeder).GetMethod(
            "SeedPermissionsAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        await (Task)method.Invoke(seeder, [db, CancellationToken.None])!;
    }

    private static Mock<IPasswordHasher> CreatePasswordHasher()
    {
        var hasher = new Mock<IPasswordHasher>();
        hasher.Setup(h => h.Hash(It.IsAny<string>())).Returns("hashed-password");
        return hasher;
    }

    private async Task RunSeederAsync(ApplicationDbContext db)
    {
        await SeedPermissionsAsync(db);
        var tenantContext = new TenantContextAccessor();
        await DevSmokeTestTenantSeeder.SeedAsync(
            db,
            tenantContext,
            CreatePasswordHasher().Object,
            new Mock<IEncryptionService>().Object,
            new ConfigurationBuilder().Build(),
            CancellationToken.None);
    }

    private sealed class TestClock : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);
        public DateOnly Today => DateOnly.FromDateTime(UtcNow.UtcDateTime);
    }
}
```

- [ ] **Step 2: Run it to confirm the fixture compiles and boots**

Run: `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --filter "DevSmokeTestTenantSeederTests" --no-restore --verbosity minimal`
Expected: 0 tests collected yet (no `[Fact]` methods) but the project builds clean.

- [ ] **Step 3: Add the 14 required test facts**

Add each as a `[Fact]` (or `[Theory]` where natural) method on the class from Step 1. Use `using var db = CreateContext();` for isolated per-assertion contexts against the same shared-cache SQLite database, matching the `PostgresMfaChallengeStoreTests` pattern.

```csharp
[Fact]
public async Task SeedAsync_CreatesBothAcmeAndDapiTenants()
{
    using var db = CreateContext();
    await RunSeederAsync(db);

    using var verify = CreateContext();
    var slugs = await verify.Tenants.Select(t => t.Slug).ToListAsync();
    slugs.Should().Contain(["acme", "dapi"]);
}

[Fact]
public async Task SeedAsync_DapiOwnerBelongsOnlyToDapi()
{
    using var db = CreateContext();
    await RunSeederAsync(db);

    using var verify = CreateContext();
    var dapiTenant = await verify.Tenants.SingleAsync(t => t.Slug == "dapi");
    var acmeTenant = await verify.Tenants.SingleAsync(t => t.Slug == "acme");
    var dapiOwner = await verify.Users.SingleAsync(u => u.Email == DapiOwnerEmail);

    dapiOwner.TenantId.Should().Be(dapiTenant.Id);
    (await verify.Users.AnyAsync(u => u.Email == DapiOwnerEmail && u.TenantId == acmeTenant.Id))
        .Should().BeFalse();
}

[Fact]
public async Task SeedAsync_AcmeOwnerHasFullPermissionsExceptWildcard()
{
    using var db = CreateContext();
    await RunSeederAsync(db);

    using var verify = CreateContext();
    var acmeTenant = await verify.Tenants.SingleAsync(t => t.Slug == "acme");
    var owner = await verify.Users.SingleAsync(u => u.Email == AcmeOwnerEmail);
    var allPermissionCount = await verify.Permissions.CountAsync(p => p.Code != "*");

    var ownerRolePermissionCodes = await (
        from ur in verify.UserRoles
        join rp in verify.RolePermissions on ur.RoleId equals rp.RoleId
        join p in verify.Permissions on rp.PermissionId equals p.Id
        where ur.TenantId == acmeTenant.Id && ur.UserId == owner.Id
        select p.Code).ToListAsync();

    ownerRolePermissionCodes.Should().HaveCount(allPermissionCount);
    ownerRolePermissionCodes.Should().NotContain("*");
}

[Fact]
public async Task SeedAsync_AcmeHrManagerHasExactlyItsRequiredPermissions()
{
    using var db = CreateContext();
    await RunSeederAsync(db);

    using var verify = CreateContext();
    var acmeTenant = await verify.Tenants.SingleAsync(t => t.Slug == "acme");
    var user = await verify.Users.SingleAsync(u => u.Email == AcmeHrManagerEmail);

    var codes = await (
        from ur in verify.UserRoles
        join rp in verify.RolePermissions on ur.RoleId equals rp.RoleId
        join p in verify.Permissions on rp.PermissionId equals p.Id
        where ur.TenantId == acmeTenant.Id && ur.UserId == user.Id
        select p.Code).ToListAsync();

    codes.Should().BeEquivalentTo(["org:read", "org:manage", "employees:read", "employees:write", "roles:read"]);
}

[Fact]
public async Task SeedAsync_AcmeWorkManagerHasExactlyItsRequiredPermissions()
{
    using var db = CreateContext();
    await RunSeederAsync(db);

    using var verify = CreateContext();
    var acmeTenant = await verify.Tenants.SingleAsync(t => t.Slug == "acme");
    var user = await verify.Users.SingleAsync(u => u.Email == AcmeWorkManagerEmail);

    var codes = await (
        from ur in verify.UserRoles
        join rp in verify.RolePermissions on ur.RoleId equals rp.RoleId
        join p in verify.Permissions on rp.PermissionId equals p.Id
        where ur.TenantId == acmeTenant.Id && ur.UserId == user.Id
        select p.Code).ToListAsync();

    codes.Should().BeEquivalentTo(["org:read", "employees:read", "projects:read", "tasks:read", "tasks:write"]);
    codes.Should().NotContain("org:manage");
}

[Fact]
public async Task SeedAsync_TheThreeAcmeUsersHaveDifferentPermissionSets()
{
    using var db = CreateContext();
    await RunSeederAsync(db);

    using var verify = CreateContext();
    var acmeTenant = await verify.Tenants.SingleAsync(t => t.Slug == "acme");

    async Task<HashSet<string>> CodesFor(string email)
    {
        var user = await verify.Users.SingleAsync(u => u.Email == email);
        var codes = await (
            from ur in verify.UserRoles
            join rp in verify.RolePermissions on ur.RoleId equals rp.RoleId
            join p in verify.Permissions on rp.PermissionId equals p.Id
            where ur.TenantId == acmeTenant.Id && ur.UserId == user.Id
            select p.Code).ToListAsync();
        return codes.ToHashSet();
    }

    var owner = await CodesFor(AcmeOwnerEmail);
    var hrManager = await CodesFor(AcmeHrManagerEmail);
    var workManager = await CodesFor(AcmeWorkManagerEmail);

    owner.Should().NotBeEquivalentTo(hrManager);
    owner.Should().NotBeEquivalentTo(workManager);
    hrManager.Should().NotBeEquivalentTo(workManager);
}

[Fact]
public async Task SeedAsync_AcmeHasExactlyThreeLegalEntitiesAfterRepeatedSeeding()
{
    using var db = CreateContext();
    await RunSeederAsync(db);
    using (var second = CreateContext())
    {
        await RunSeederAsync(second);
    }

    using var verify = CreateContext();
    var acmeTenant = await verify.Tenants.SingleAsync(t => t.Slug == "acme");
    var legalEntities = await verify.LegalEntities.Where(l => l.TenantId == acmeTenant.Id).ToListAsync();

    legalEntities.Should().HaveCount(3);
    legalEntities.Select(l => l.Name).Should().BeEquivalentTo(
        ["Acme Technologies", "Acme Solutions", "Acme Global Services"]);
    legalEntities.Count(l => l.IsPrimary).Should().Be(1);
    legalEntities.Single(l => l.IsPrimary).Name.Should().Be("Acme Technologies");
}

[Fact]
public async Task SeedAsync_DapiHasExactlyOneLegalEntityAfterRepeatedSeeding()
{
    using var db = CreateContext();
    await RunSeederAsync(db);
    using (var second = CreateContext())
    {
        await RunSeederAsync(second);
    }

    using var verify = CreateContext();
    var dapiTenant = await verify.Tenants.SingleAsync(t => t.Slug == "dapi");
    var legalEntities = await verify.LegalEntities.Where(l => l.TenantId == dapiTenant.Id).ToListAsync();

    legalEntities.Should().ContainSingle();
    legalEntities[0].Name.Should().Be("Dapi Technologies");
    legalEntities[0].IsPrimary.Should().BeTrue();
}

[Fact]
public async Task SeedAsync_IsIdempotentAcrossTenantsUsersRolesAndAssignments()
{
    using (var first = CreateContext())
    {
        await RunSeederAsync(first);
    }
    using (var second = CreateContext())
    {
        await RunSeederAsync(second);
    }

    using var verify = CreateContext();
    (await verify.Tenants.CountAsync(t => t.Slug == "acme" || t.Slug == "dapi")).Should().Be(2);
    (await verify.Users.CountAsync(u =>
        u.Email == AcmeOwnerEmail || u.Email == AcmeHrManagerEmail ||
        u.Email == AcmeWorkManagerEmail || u.Email == DapiOwnerEmail)).Should().Be(4);
    (await verify.Roles.CountAsync(r => r.Name == "Tenant Owner")).Should().Be(2);
    (await verify.Roles.CountAsync(r => r.Name == "HR Manager" || r.Name == "Work Manager")).Should().Be(2);
}

[Fact]
public async Task SeedAsync_DoesNotCreateAnyEmployeeRowForWorkManager()
{
    using var db = CreateContext();
    await RunSeederAsync(db);
    using (var second = CreateContext())
    {
        await RunSeederAsync(second);
    }

    using var verify = CreateContext();
    var user = await verify.Users.SingleAsync(u => u.Email == AcmeWorkManagerEmail);
    (await verify.Set<ONEVO.Domain.Features.CoreHr.Entities.Employee>().AnyAsync(e => e.UserId == user.Id))
        .Should().BeFalse();
}

[Fact]
public async Task SeedAsync_AllSeededRolePermissionAndUserRoleRowsHaveNonEmptyTenantId()
{
    using var db = CreateContext();
    await RunSeederAsync(db);

    using var verify = CreateContext();
    (await verify.RolePermissions.AnyAsync(rp => rp.TenantId == Guid.Empty)).Should().BeFalse();
    (await verify.UserRoles.AnyAsync(ur => ur.TenantId == Guid.Empty)).Should().BeFalse();
}

[Fact]
public async Task SeedAsync_EverySeededUserHasAGlobalEmailDirectoryRowForItsTenant()
{
    using var db = CreateContext();
    await RunSeederAsync(db);

    using var verify = CreateContext();
    var acmeTenant = await verify.Tenants.SingleAsync(t => t.Slug == "acme");
    var dapiTenant = await verify.Tenants.SingleAsync(t => t.Slug == "dapi");

    var rows = await verify.Database.SqlQueryRaw<string>(
        "SELECT email FROM global_email_directory WHERE tenant_id = {0}", acmeTenant.Id.ToString())
        .ToListAsync();
    rows.Should().BeEquivalentTo([AcmeOwnerEmail, AcmeHrManagerEmail, AcmeWorkManagerEmail]);

    var dapiRows = await verify.Database.SqlQueryRaw<string>(
        "SELECT email FROM global_email_directory WHERE tenant_id = {0}", dapiTenant.Id.ToString())
        .ToListAsync();
    dapiRows.Should().BeEquivalentTo([DapiOwnerEmail]);
}
```

Note on requirement #14 ("no test depends on tenant-host password login"): satisfied structurally — none of the tests above call any login endpoint or handler; they assert directly against `ApplicationDbContext` rows. No additional test is needed for this requirement; call it out explicitly in the report instead.

If `Database.SqlQueryRaw<string>` on SQLite returns the tenant_id column type mismatched (SQLite stores it as TEXT here vs `Guid` in Postgres), adjust the raw table DDL/query in Step 1/this step so the comparison works under SQLite specifically (test-only DDL, not the production migration) — verify by running the test before considering this step done.

- [ ] **Step 4: Run the full filtered unit test suite**

Run: `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --filter "DevSmokeTestTenantSeeder|SmokeTenant|Seed" --no-restore --verbosity minimal`
Expected: all new facts pass, plus pre-existing `PermissionSeederTests`, `PlatformAccessSeederTests`, `PlatformOAuthProviderMetadataSeederTests`, `ModuleCatalogSeederTests` (matched by "Seed") still pass unchanged.

- [ ] **Step 5: Re-run architecture tests to confirm Task 1's refactor didn't break source-ordering assertions**

Run: `dotnet test tests\ONEVO.Tests.Architecture\ONEVO.Tests.Architecture.csproj --no-restore --verbosity minimal`

---

## Task 3: Required searches, full build/test verification, and report

**Files:**
- Create: `DEV_SMOKE_MULTI_TENANT_SEED_EXPANSION_REPORT.md` (repo root: `C:\onevoNew\HRMS-Backend-v1\DEV_SMOKE_MULTI_TENANT_SEED_EXPANSION_REPORT.md`)

- [ ] **Step 1: Run required searches**

```bash
grep -rn "owner@acme.test" src tests
grep -rn "Guid.Empty" src/ONEVO.Infrastructure/Persistence/Seeders/DevSmokeTestTenantSeeder.cs
grep -rn "LegalEntityId" src/ONEVO.Domain/Features/Auth/Roles/Entities/UserRole.cs
grep -rln "legal_entity_membership\|company_membership" src tests
grep -rln "position_assignments" src tests
```
Expected: no `owner@acme.test` hits; no `Guid.Empty` literal assigned to RolePermission/UserRole TenantId in the seeder; no `LegalEntityId` in `UserRole.cs`; no membership table files; any `position_assignments` hits (if the phrase already exists elsewhere in the codebase from earlier work) must show zero new occurrences introduced by this task's diff.

- [ ] **Step 2: Full build**

Run: `dotnet build src\ONEVO.Api\ONEVO.Api.csproj --no-restore --verbosity minimal`
Expected: 0 errors.

- [ ] **Step 3: Full targeted test runs**

```
dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --filter "DevSmokeTestTenantSeeder|SmokeTenant|Seed" --no-restore --verbosity minimal
dotnet test tests\ONEVO.Tests.Architecture\ONEVO.Tests.Architecture.csproj --no-restore --verbosity minimal
```
Record pass counts for the report. Attempt the Docker-based integration checks described in the task only if Docker is confirmed available; otherwise state explicitly in the report that they were skipped and why.

- [ ] **Step 4: Write the report**

Include: files changed; final seeded tenants/users/legal entities/roles; exact permission codes per role; idempotency proof (test names + what they assert); tests run and counts; required search results (verbatim or "no matches"); confirmation no production bootstrap/runtime/auth/Department/Position/position_assignments/membership-table code was touched; explicit note that Employee rows are intentionally not created (Employee.LegalEntityId already provides one-employee-one-legal-entity modeling, left for the deferred phase); and the exact required sentence:

"Multi-legal-entity access for mrt15473@gmail.com is intentionally deferred until Department APIs, Position APIs, and then the Phase 1 position_assignments / authority assignment model are implemented in that order."

- [ ] **Step 5: Do not commit or push.** Confirm `git status` shows only the intended files changed, and stop there.
