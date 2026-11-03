# Meetings as Tasks — Phase 1 (Foundations) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Every project gets a protected "Meeting" task category, and every tenant gets an owner-led Office project that actually works under row-level security.

**Architecture:** A new `task_categories.system_key` column marks protected categories. One migration adds it and backfills a Meeting category into every project using frozen, set-based SQL. Category handlers reject deleting or renaming system categories and reject look-alike names. A new `OfficeProjectReconciler` creates or aligns each tenant's Office project (lead = tenant owner's employee). It runs once at startup, and again whenever the onboarding checklist path needs the Office project. On the way, it fixes a live defect: `OfficeProjectProvisioner` currently runs under System mode, which RLS fails closed. The frontend category editor hides delete and rename on system categories.

**Tech Stack:** .NET 10, EF Core 10 + Npgsql (snake_case), xUnit, Moq, Testcontainers PostgreSQL; Angular 21 (standalone, signals), Vitest via `ng test`.

**Spec:** `docs/superpowers/specs/2026-10-13-meeting-task-phase0-1-design.md` (§4, §7 Q1). Phase 0 has its own plan.

## Global Constraints

- Worktrees: backend `C:\Users\User\onexso-worktrees\be-meeting-task`, frontend `C:\Users\User\onexso-worktrees\fe-meeting-task`, both on branch `feature/meeting-task-phase0-1`. Never edit the main checkouts.
- Migration data steps run as `onevo_migrator` (NOSUPERUSER NOBYPASSRLS) against FORCE-RLS tables, so every data step starts with `SET LOCAL app.tenant_context_mode = 'admin';`.
- System category rule: **delete ✗, rename ✗, reorder ✓**. The reserved name is `meeting` in **any casing**, after trimming.
- Office project = `projects.system_purpose = 'office'` (never matched by name). One per tenant (existing filtered unique index).
- Tenant owner = holder of the tenant's system role named `Owner`. Eligible owners have a non-expired `UserRole` **and** an active Employee (`EmploymentStatusId = 1`). The earliest `UserRole.AssignedAt` wins; among that owner's employees, the one in a primary legal entity wins, then the earliest created.
- No new periodic background job (SCL-01). Only the one-off startup run, plus on-demand reconciliation.
- Production code has no mocks, stubs or placeholders. Tests may mock.
- Commit and push to the branch only, **no PR**. Every commit message ends with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Integration tests need Docker running (Testcontainers).

## Review Focus

1. **A user category named `" MEETING "` (spaces, upper case)** must be rejected on create and on rename, just like `meeting`. Test in Task 4.
2. **Renaming a system category to its own name with different surrounding whitespace** (`" meeting "`), for example when the client re-sends the name during a reorder, must be accepted, not rejected as a rename. Test in Task 4.
3. **A project with no categories at all** must get Meeting at `display_order = 0`, not fail on `max()` of nothing. Test in Task 2.
4. **An owner whose only employee record is Terminated** is not eligible. The next owner (or none) is used. Test in Task 6.
5. **More than one page of tenants at startup**: every tenant on every page is reconciled. Test in Task 7.

## File Structure

| File | Responsibility |
|---|---|
| `src/ONEVO.Infrastructure/Services/CoreHr/OfficeProjectProvisioner.cs` (modify) | Switch its own scope to the target tenant before touching data (RLS fix); add the Meeting system category |
| `src/ONEVO.Domain/Features/WorkManagement/Tasks/Entities/TaskCategory.cs` (modify) | `SystemKey` property + `TaskCategorySystemKeys` (constant + reserved-name check) |
| `src/ONEVO.Infrastructure/Persistence/Configurations/WorkManagement/TaskCategoryConfiguration.cs` (modify) | Column length + unique filtered index |
| `src/ONEVO.Infrastructure/Persistence/DataMigrations/MeetingCategoryBackfill.cs` (create) | Frozen backfill SQL, shared by the migration and its integration test |
| `src/ONEVO.Infrastructure/Migrations/<ts>_AddTaskCategorySystemKey.cs` (generated + edited) | Schema + backfill |
| `src/ONEVO.Application/Features/WorkManagement/Tasks/Services/DefaultTaskCategoryTemplate.cs` (modify) | Meeting in every new project |
| `src/ONEVO.Infrastructure/Persistence/Seeders/WorkManagementDapiDemoSeeder.Tasks.cs` (modify) | Demo projects get the system Meeting category |
| `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/{Create,Edit,Delete}TaskCategory/*Handler.cs` (modify) | Guards |
| `src/ONEVO.Application/Features/WorkManagement/Tasks/DTOs/Responses/TaskCategoryResponse.cs` + 3 construction sites + `ONEVO.Api/Contracts/WorkManagement/Tasks/{TaskContracts,WorkTaskViewModelMapper}.cs` (modify) | Expose `systemKey` / `isSystem` |
| `src/ONEVO.Application/Features/CoreHr/Onboarding/Services/IOfficeProjectReconciler.cs` (create) | Reconciler contract + outcome enum |
| `src/ONEVO.Infrastructure/Services/CoreHr/OfficeProjectReconciler.cs` (create) | Owner selection, create/align, owner membership |
| `src/ONEVO.Infrastructure/Services/CoreHr/OfficeProjectReconciliationStartupService.cs` (create) | One-off startup run over all tenants |
| `src/ONEVO.Application/Features/CoreHr/Onboarding/Services/EmployeeChecklistWorkTaskProvisioner.cs` (modify) | Reconcile before ensuring the Office project |
| `src/ONEVO.Infrastructure/DependencyInjection.cs` (modify) | Registrations |
| `tests/ONEVO.Tests.Integration/WorkManagement/OfficeProjectDb.cs` (create) | Test harness: seeds as superuser, runs code under `onevo_app` |
| frontend `src/app/modules/work/models/{task-category.model.ts,dto/task-category.dto.ts}`, `utils/task-category.mapper.ts`, `ui/category-list-editor/category-list-editor.component.ts` (modify) | Lock UI |

---

### Task 0: Worktree setup

- [ ] **Step 1: Confirm both worktrees are on the branch and clean (spec commits only)**

```bash
git -C "C:/Users/User/onexso-worktrees/be-meeting-task" status --short --branch
git -C "C:/Users/User/onexso-worktrees/fe-meeting-task" status --short --branch
```
Expected: `## feature/meeting-task-phase0-1`, nothing else listed.

- [ ] **Step 2: Give the backend worktree a local `.env` (gitignored) so `dotnet ef` can start the design-time host**

```bash
cp "C:/Users/User/OneDrive/Desktop/onexso/HRMS-Backend-v1/.env" "C:/Users/User/onexso-worktrees/be-meeting-task/.env"
git -C "C:/Users/User/onexso-worktrees/be-meeting-task" status --short
```
Expected: no output (`.env` is ignored).

- [ ] **Step 3: Install frontend dependencies in the frontend worktree**

```bash
cd "C:/Users/User/onexso-worktrees/fe-meeting-task" && npm ci --no-audit --no-fund
```
Expected: exits 0.

---

### Task 1: Office provisioner runs under the target tenant (RLS fix) + test harness

**Context.** `OfficeProjectProvisioner.EnsureAsync` opens a fresh DI scope. `TenantContextAccessor` is scoped and starts in System mode. `TenantRlsInterceptor` sets `app.tenant_context_mode = 'system'` for that scope's connection, and the `tenant_isolation` policy matches no rows in System mode. The app connects as `onevo_app` (`rolbypassrls = false`, verified 2026-10-13). So under the real role, the provisioner can neither see an existing Office project nor insert one.

**Files:**
- Create: `tests/ONEVO.Tests.Integration/WorkManagement/OfficeProjectDb.cs`
- Create: `tests/ONEVO.Tests.Integration/WorkManagement/OfficeProjectProvisionerRlsIntegrationTests.cs`
- Modify: `src/ONEVO.Infrastructure/Services/CoreHr/OfficeProjectProvisioner.cs`

**Interfaces:**
- Produces: `OfficeProjectDb` with `CreateAsync()`, `TenantId`, `LegalEntityId`, `AdminContext()`, `BuildAppScopeFactory()`, `SeedEmployeeAsync(Guid? legalEntityId = null, int employmentStatusId = 1, bool primaryEntity = true)`, `SeedOwnerRoleAsync()`, `AssignRoleAsync(Guid userId, Guid roleId, DateTimeOffset assignedAt, DateTimeOffset? expiresAt = null)`, `SeedTenantStatusAsync(TenantStatus status)`. Tasks 3 and 6 use these.

- [ ] **Step 1: Create the harness**

`tests/ONEVO.Tests.Integration/WorkManagement/OfficeProjectDb.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.Services;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Domain.Lookups;
using ONEVO.Infrastructure.ExternalServices.Messaging;
using ONEVO.Infrastructure.Identity.CurrentUser;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Identity.Time;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.DevPlatform.Tenancy;
using ONEVO.Infrastructure.Services.CoreHr;
using ONEVO.Tests.Integration.Support;

namespace ONEVO.Tests.Integration.WorkManagement;

/// <summary>One cloned database per test. Seeds through a superuser context (RLS bypassed) and
/// runs the code under test as onevo_app (NOBYPASSRLS), so RLS defects surface.</summary>
public sealed class OfficeProjectDb : IAsyncDisposable
{
    private readonly SystemDateTimeProvider _clock = new();
    private string _adminConnectionString = string.Empty;
    private string _appConnectionString = string.Empty;
    private IntegrationTestEnvironmentScope _environmentScope = null!;

    public Guid TenantId { get; private set; }
    public Guid LegalEntityId { get; private set; }

    public static async Task<OfficeProjectDb> CreateAsync()
    {
        var harness = new OfficeProjectDb();
        harness._adminConnectionString = await SharedPostgresTemplate.CreateDatabaseAsync();
        harness._environmentScope = new IntegrationTestEnvironmentScope(harness._adminConnectionString);
        harness._appConnectionString = harness._environmentScope.DefaultConnectionString;

        await using (var connection = new NpgsqlConnection(harness._adminConnectionString))
        {
            await connection.OpenAsync();
            await using var grant = connection.CreateCommand();
            grant.CommandText = """
                GRANT USAGE ON SCHEMA public TO onevo_app;
                GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO onevo_app;
                GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO onevo_app;
                """;
            await grant.ExecuteNonQueryAsync();
        }

        await using var db = harness.AdminContext();
        if (!await db.EmploymentStatuses.AnyAsync(s => s.Id == EmploymentStatusIds.Active))
            db.EmploymentStatuses.Add(new EmploymentStatus { Id = EmploymentStatusIds.Active, Code = "active", Label = "Active" });
        if (!await db.EmploymentStatuses.AnyAsync(s => s.Id == EmploymentStatusIds.Terminated))
            db.EmploymentStatuses.Add(new EmploymentStatus { Id = EmploymentStatusIds.Terminated, Code = "terminated", Label = "Terminated" });

        var tenant = new Tenant
        {
            Id = Guid.NewGuid(), Name = "Office Tenant", Slug = $"office-{Guid.NewGuid():N}"[..20],
            CompanySizeRange = "51-200", Status = TenantStatus.Active,
        };
        harness.TenantId = tenant.Id;
        db.Tenants.Add(tenant);

        var legalEntity = NewLegalEntity(tenant.Id, isPrimary: true);
        harness.LegalEntityId = legalEntity.Id;
        db.LegalEntities.Add(legalEntity);
        await db.SaveChangesAsync();
        return harness;
    }

    private static LegalEntity NewLegalEntity(Guid tenantId, bool isPrimary) => new()
    {
        Id = Guid.NewGuid(), TenantId = tenantId, Name = $"Company {Guid.NewGuid():N}"[..16], IsPrimary = isPrimary,
        Timezone = "UTC", WorkStartTime = new TimeOnly(9, 0), WorkEndTime = new TimeOnly(17, 0),
        StandardWorkingDays = "[1,2,3,4,5]", BreakDurationMinutes = 60,
    };

    public ApplicationDbContext AdminContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_adminConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        return new ApplicationDbContext(
            options,
            new AuditableEntityInterceptor(new AnonymousCurrentUser(), _clock),
            new SoftDeleteInterceptor(_clock),
            new DomainEventDispatchInterceptor(new NoOpPublisher()),
            new TenantContextAccessor());
    }

    /// <summary>Production-like wiring under onevo_app: scoped tenant context, RLS interceptor,
    /// tenant switcher, the real provisioner and reconciler.</summary>
    public IServiceScopeFactory BuildAppScopeFactory()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IDateTimeProvider>(_clock);
        services.AddScoped<TenantContextAccessor>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContextAccessor>());
        services.AddScoped<IWritableTenantContext>(sp => sp.GetRequiredService<TenantContextAccessor>());
        services.AddScoped<TenantRlsInterceptor>();
        services.AddScoped(_ => new AuditableEntityInterceptor(new AnonymousCurrentUser(), _clock));
        services.AddScoped(_ => new SoftDeleteInterceptor(_clock));
        services.AddScoped(_ => new DomainEventDispatchInterceptor(new NoOpPublisher()));
        services.AddDbContext<ApplicationDbContext>((sp, options) =>
            options.UseNpgsql(_appConnectionString)
                   .UseSnakeCaseNamingConvention()
                   .AddInterceptors(sp.GetRequiredService<TenantRlsInterceptor>()));
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<ITenantRepository, EfTenantRepository>();
        services.AddScoped<ITenantContextSwitcher, TenantContextSwitcher>();
        services.AddScoped<IOfficeProjectProvisioner, OfficeProjectProvisioner>();
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    public async Task<Employee> SeedEmployeeAsync(Guid? legalEntityId = null, int employmentStatusId = EmploymentStatusIds.Active, Guid? userId = null)
    {
        await using var db = AdminContext();
        var employee = new Employee
        {
            Id = Guid.NewGuid(), TenantId = TenantId, UserId = userId ?? Guid.NewGuid(),
            EmployeeNumber = $"OF-{Guid.NewGuid():N}"[..12], FirstName = "Office",
            LastName = $"Person{Guid.NewGuid():N}"[..14], Email = $"{Guid.NewGuid():N}@office-test.onevo.dev",
            HireDate = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-1),
            LegalEntityId = legalEntityId, EmploymentStatusId = employmentStatusId,
        };
        db.Employees.Add(employee);
        await db.SaveChangesAsync();
        return employee;
    }

    public async Task<Guid> SeedSecondaryLegalEntityAsync()
    {
        await using var db = AdminContext();
        var entity = NewLegalEntity(TenantId, isPrimary: false);
        db.LegalEntities.Add(entity);
        await db.SaveChangesAsync();
        return entity.Id;
    }

    public async Task<Guid> SeedOwnerRoleAsync()
    {
        await using var db = AdminContext();
        var role = new Role { Id = Guid.NewGuid(), TenantId = TenantId, Name = "Owner", Description = "Full access", IsSystem = true };
        db.Roles.Add(role);
        await db.SaveChangesAsync();
        return role.Id;
    }

    public async Task AssignRoleAsync(Guid userId, Guid roleId, DateTimeOffset assignedAt, DateTimeOffset? expiresAt = null)
    {
        await using var db = AdminContext();
        db.UserRoles.Add(new UserRole
        {
            TenantId = TenantId, UserId = userId, RoleId = roleId,
            AssignedAt = assignedAt, AssignedBy = userId, ExpiresAt = expiresAt,
        });
        await db.SaveChangesAsync();
    }

    public async Task SeedTenantStatusAsync(TenantStatus status)
    {
        await using var db = AdminContext();
        var tenant = await db.Tenants.SingleAsync(t => t.Id == TenantId);
        tenant.Status = status;
        await db.SaveChangesAsync();
    }

    public async ValueTask DisposeAsync() => await _environmentScope.DisposeAsync();
}
```

- [ ] **Step 2: Write the failing RLS tests**

`tests/ONEVO.Tests.Integration/WorkManagement/OfficeProjectProvisionerRlsIntegrationTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ONEVO.Application.Features.CoreHr.Onboarding.Services;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using Xunit;

namespace ONEVO.Tests.Integration.WorkManagement;

public sealed class OfficeProjectProvisionerRlsIntegrationTests
{
    [Fact]
    public async Task EnsureAsync_AsOnevoApp_CreatesTheTenantsOfficeProject()
    {
        await using var harness = await OfficeProjectDb.CreateAsync();
        var hr = await harness.SeedEmployeeAsync(harness.LegalEntityId);
        var scopes = harness.BuildAppScopeFactory();

        await using var scope = scopes.CreateAsyncScope();
        var context = await scope.ServiceProvider.GetRequiredService<IOfficeProjectProvisioner>()
            .EnsureAsync(harness.TenantId, harness.LegalEntityId, hr.UserId, hr.Id, DateOnly.FromDateTime(DateTime.UtcNow));

        await using var admin = harness.AdminContext();
        var office = await admin.Projects.SingleAsync(p => p.TenantId == harness.TenantId && p.SystemPurpose == ProjectSystemPurposes.Office);
        Assert.Equal(context.ProjectId, office.Id);
        Assert.Equal(hr.Id, office.LeadId);
    }

    [Fact]
    public async Task EnsureAsync_AsOnevoApp_CalledTwice_ReturnsTheSameProject()
    {
        await using var harness = await OfficeProjectDb.CreateAsync();
        var hr = await harness.SeedEmployeeAsync(harness.LegalEntityId);
        var scopes = harness.BuildAppScopeFactory();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        Guid first, second;
        await using (var scope = scopes.CreateAsyncScope())
            first = (await scope.ServiceProvider.GetRequiredService<IOfficeProjectProvisioner>()
                .EnsureAsync(harness.TenantId, harness.LegalEntityId, hr.UserId, hr.Id, today)).ProjectId;
        await using (var scope = scopes.CreateAsyncScope())
            second = (await scope.ServiceProvider.GetRequiredService<IOfficeProjectProvisioner>()
                .EnsureAsync(harness.TenantId, harness.LegalEntityId, hr.UserId, hr.Id, today)).ProjectId;

        Assert.Equal(first, second);
        await using var admin = harness.AdminContext();
        Assert.Equal(1, await admin.Projects.CountAsync(p => p.TenantId == harness.TenantId && p.SystemPurpose == ProjectSystemPurposes.Office));
    }
}
```

- [ ] **Step 3: Run them and confirm they fail for the RLS reason**

Run: `dotnet test tests/ONEVO.Tests.Integration/ONEVO.Tests.Integration.csproj --filter "FullyQualifiedName~OfficeProjectProvisionerRlsIntegrationTests"`
Expected: both FAIL with `Npgsql.PostgresException` SqlState `42501` ("new row violates row-level security policy"). **If they pass instead, stop and report.** That would mean the RLS diagnosis in this plan is wrong, and the reconciler design must be revisited.

- [ ] **Step 4: Switch the provisioner's scope to the tenant before any data access**

In `src/ONEVO.Infrastructure/Services/CoreHr/OfficeProjectProvisioner.cs`, add usings:

```csharp
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
```

Replace the first three lines of `EnsureAsync`'s body:

```csharp
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
```

with:

```csharp
        await using var scope = scopeFactory.CreateAsyncScope();
        // A fresh scope's tenant context starts in System mode, which the tenant_isolation RLS
        // policy fails closed, so every read and write below would see or affect nothing.
        await SwitchToTenantAsync(scope.ServiceProvider, tenantId, ct);
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
```

and add this method to the class (after `EnsureAsync`):

```csharp
    private static async Task SwitchToTenantAsync(IServiceProvider services, Guid tenantId, CancellationToken ct)
    {
        var tenant = await services.GetRequiredService<ITenantRepository>().GetByIdAsync(tenantId, ct)
            ?? throw new InvalidOperationException($"Tenant {tenantId} does not exist.");
        await services.GetRequiredService<ITenantContextSwitcher>().SwitchToTenantAsync(
            new TenantRegistryEntry(tenant.Id, tenant.Slug, tenant.Status, PlanCode: null), ct);
    }
```

- [ ] **Step 5: Run the RLS tests again**

Run: `dotnet test tests/ONEVO.Tests.Integration/ONEVO.Tests.Integration.csproj --filter "FullyQualifiedName~OfficeProjectProvisionerRlsIntegrationTests"`
Expected: 2 passed.

- [ ] **Step 6: Run the onboarding provisioner unit tests (unchanged behaviour)**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~EmployeeChecklistWorkTaskProvisioner"`
Expected: all pass.

- [ ] **Step 7: Commit**

```bash
git add tests/ONEVO.Tests.Integration/WorkManagement/OfficeProjectDb.cs tests/ONEVO.Tests.Integration/WorkManagement/OfficeProjectProvisionerRlsIntegrationTests.cs src/ONEVO.Infrastructure/Services/CoreHr/OfficeProjectProvisioner.cs
git commit -m "fix(office-project): provision under the target tenant's RLS context

OfficeProjectProvisioner opened a fresh scope whose tenant context starts in
System mode, which the tenant_isolation policy fails closed, so under the real
onevo_app role it could neither find nor create the Office project.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: `system_key` column, unique index and Meeting backfill

**Files:**
- Modify: `src/ONEVO.Domain/Features/WorkManagement/Tasks/Entities/TaskCategory.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Configurations/WorkManagement/TaskCategoryConfiguration.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/DataMigrations/MeetingCategoryBackfill.cs`
- Create (generated, then edited): `src/ONEVO.Infrastructure/Migrations/<timestamp>_AddTaskCategorySystemKey.cs`
- Create: `tests/ONEVO.Tests.Integration/WorkManagement/MeetingCategoryBackfillIntegrationTests.cs`

**Interfaces:**
- Produces: `TaskCategory.SystemKey` (`string?`); `TaskCategorySystemKeys.Meeting` (`"meeting"`); `TaskCategorySystemKeys.IsReservedName(string name)` → `bool`; `MeetingCategoryBackfill.Sql` (`string` const).

- [ ] **Step 1: Add the property and constants**

Replace the body of `src/ONEVO.Domain/Features/WorkManagement/Tasks/Entities/TaskCategory.cs` with:

```csharp
using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

public static class TaskCategorySystemKeys
{
    public const string Meeting = "meeting";

    /// <summary>True for any casing of a system category's name, after trimming. Users can't
    /// create or rename a category to one of these.</summary>
    public static bool IsReservedName(string name) =>
        string.Equals(name.Trim(), Meeting, StringComparison.OrdinalIgnoreCase);
}

public class TaskCategory : BaseEntity
{
    public Guid ProjectId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
    /// <summary>Set only on system-managed categories (can't be deleted or renamed).</summary>
    public string? SystemKey { get; set; }
}
```

- [ ] **Step 2: Configure the column and index**

In `TaskCategoryConfiguration.Configure`, add after the existing name index:

```csharp
        builder.Property(c => c.SystemKey).HasMaxLength(32);

        builder.HasIndex(c => new { c.TenantId, c.ProjectId, c.SystemKey })
            .IsUnique()
            .HasFilter("system_key IS NOT NULL")
            .HasDatabaseName("ix_task_categories_one_system_key_per_project");
```

- [ ] **Step 3: Write the frozen backfill SQL**

`src/ONEVO.Infrastructure/Persistence/DataMigrations/MeetingCategoryBackfill.cs`:

```csharp
namespace ONEVO.Infrastructure.Persistence.DataMigrations;

/// <summary>
/// Data step of migration AddTaskCategorySystemKey. FROZEN: the migration and its integration
/// test both run this exact text. Never edit it; add a new migration instead.
///
/// ix_task_categories_one_name_per_project is case-sensitive and not filtered by is_deleted, so:
/// (1) promote one live case-variant of "meeting" per project; (2) otherwise restore a
/// soft-deleted exact "meeting" (it still owns the name); (3) otherwise insert one.
/// Idempotent: every step skips projects that already have the system row.
/// </summary>
public static class MeetingCategoryBackfill
{
    public const string Sql = """
        SET LOCAL app.tenant_context_mode = 'admin';

        UPDATE task_categories c
        SET system_key = 'meeting', updated_at = now()
        FROM (
            SELECT DISTINCT ON (tc.tenant_id, tc.project_id) tc.id
            FROM task_categories tc
            WHERE tc.is_deleted = false
              AND lower(tc.name) = 'meeting'
              AND NOT EXISTS (
                  SELECT 1 FROM task_categories s
                  WHERE s.tenant_id = tc.tenant_id AND s.project_id = tc.project_id AND s.system_key = 'meeting')
            ORDER BY tc.tenant_id, tc.project_id, tc.display_order, tc.created_at, tc.id
        ) pick
        WHERE c.id = pick.id;

        UPDATE task_categories c
        SET is_deleted = false, deleted_at = NULL, system_key = 'meeting', updated_at = now()
        FROM (
            SELECT DISTINCT ON (tc.tenant_id, tc.project_id) tc.id
            FROM task_categories tc
            WHERE tc.is_deleted = true
              AND tc.name = 'meeting'
              AND NOT EXISTS (
                  SELECT 1 FROM task_categories s
                  WHERE s.tenant_id = tc.tenant_id AND s.project_id = tc.project_id AND s.system_key = 'meeting')
            ORDER BY tc.tenant_id, tc.project_id, tc.created_at, tc.id
        ) pick
        WHERE c.id = pick.id;

        INSERT INTO task_categories (id, tenant_id, project_id, name, display_order, system_key, created_at, created_by_id, is_deleted)
        SELECT gen_random_uuid(), p.tenant_id, p.id,
               CASE WHEN p.system_purpose = 'office' THEN 'Meeting' ELSE 'meeting' END,
               COALESCE((SELECT max(tc.display_order) FROM task_categories tc
                         WHERE tc.tenant_id = p.tenant_id AND tc.project_id = p.id AND tc.is_deleted = false), -1) + 1,
               'meeting', now(), p.created_by_id, false
        FROM projects p
        WHERE p.is_deleted = false
          AND NOT EXISTS (
              SELECT 1 FROM task_categories s
              WHERE s.tenant_id = p.tenant_id AND s.project_id = p.id AND s.system_key = 'meeting');
        """;
}
```

- [ ] **Step 4: Generate the migration**

```bash
cd "C:/Users/User/onexso-worktrees/be-meeting-task"
dotnet ef migrations add AddTaskCategorySystemKey --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api --output-dir Migrations
```
Expected: creates `src/ONEVO.Infrastructure/Migrations/<timestamp>_AddTaskCategorySystemKey.cs` (+ `.Designer.cs`) and updates the model snapshot. `Up` contains `AddColumn` for `system_key` and `CreateIndex` for `ix_task_categories_one_system_key_per_project`.

- [ ] **Step 5: Insert the data step between the column and the index**

In the generated migration's `Up`, add `using ONEVO.Infrastructure.Persistence.DataMigrations;` at the top. Then place this line **after** `migrationBuilder.AddColumn<string>(name: "system_key", ...)` and **before** `migrationBuilder.CreateIndex(...)`:

```csharp
            migrationBuilder.Sql(MeetingCategoryBackfill.Sql);
```

Leave `Down` as generated (drop index, drop column).

- [ ] **Step 6: Write the backfill integration tests**

`tests/ONEVO.Tests.Integration/WorkManagement/MeetingCategoryBackfillIntegrationTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.DataMigrations;
using ONEVO.Tests.Integration.MyTeam;
using Xunit;

namespace ONEVO.Tests.Integration.WorkManagement;

/// <summary>The template database already ran the migration (on no projects). Each test seeds
/// projects without a system row and runs the frozen SQL itself.</summary>
public sealed class MeetingCategoryBackfillIntegrationTests
{
    private static async Task RunBackfillAsync(ApplicationDbContext db)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync(MeetingCategoryBackfill.Sql);
        await tx.CommitAsync();
    }

    private static Task<List<TaskCategory>> CategoriesAsync(ApplicationDbContext db, Guid projectId) =>
        db.TaskCategories.IgnoreQueryFilters().Where(c => c.ProjectId == projectId).OrderBy(c => c.DisplayOrder).ToListAsync();

    private static TaskCategory Category(MyTeamDb h, Guid projectId, string name, int order, bool deleted = false) => new()
    {
        Id = Guid.NewGuid(), TenantId = h.TenantId, ProjectId = projectId, Name = name, DisplayOrder = order,
        IsDeleted = deleted, DeletedAt = deleted ? DateTimeOffset.UtcNow : null,
    };

    [Fact]
    public async Task ProjectWithoutMeeting_GetsOneInsertedAfterTheLastCategory()
    {
        var h = await MyTeamDb.CreateAsync();
        await using var db = h.NewContext();
        var lead = h.AddEmployee(db);
        var (projectId, _) = h.AddProject(db, lead.Id); // seeds "General" at 0
        await db.SaveChangesAsync();

        await RunBackfillAsync(db);

        var meeting = Assert.Single((await CategoriesAsync(db, projectId)).Where(c => c.SystemKey == TaskCategorySystemKeys.Meeting));
        Assert.Equal("meeting", meeting.Name);
        Assert.Equal(1, meeting.DisplayOrder);
        Assert.False(meeting.IsDeleted);
    }

    [Fact]
    public async Task ProjectWithNoCategoriesAtAll_GetsMeetingAtZero()
    {
        var h = await MyTeamDb.CreateAsync();
        await using var db = h.NewContext();
        var lead = h.AddEmployee(db);
        var (projectId, _) = h.AddProject(db, lead.Id);
        await db.SaveChangesAsync();
        await db.TaskCategories.Where(c => c.ProjectId == projectId).ExecuteDeleteAsync();

        await RunBackfillAsync(db);

        var meeting = Assert.Single(await CategoriesAsync(db, projectId));
        Assert.Equal(0, meeting.DisplayOrder);
        Assert.Equal(TaskCategorySystemKeys.Meeting, meeting.SystemKey);
    }

    [Fact]
    public async Task ExistingLiveMeeting_IsPromotedInPlace_AndItsTasksStay()
    {
        var h = await MyTeamDb.CreateAsync();
        await using var db = h.NewContext();
        var lead = h.AddEmployee(db);
        var (projectId, rootId) = h.AddProject(db, lead.Id);
        var existing = Category(h, projectId, "Meeting", 3);
        db.TaskCategories.Add(existing);
        var statusId = h.AddStatus(db, projectId, marksComplete: false);
        var task = h.AddTask(db, projectId, rootId, statusId, progress: 0, due: null);
        task.CategoryId = existing.Id;
        await db.SaveChangesAsync();

        await RunBackfillAsync(db);

        var categories = await CategoriesAsync(db, projectId);
        var system = Assert.Single(categories.Where(c => c.SystemKey == TaskCategorySystemKeys.Meeting));
        Assert.Equal(existing.Id, system.Id);
        Assert.Equal("Meeting", system.Name);
        Assert.Equal(2, categories.Count);
        Assert.Equal(existing.Id, (await db.WorkTasks.SingleAsync(t => t.Id == task.Id)).CategoryId);
    }

    [Fact]
    public async Task TwoLiveCaseVariants_PromotesTheLowestDisplayOrder_LeavesTheOther()
    {
        var h = await MyTeamDb.CreateAsync();
        await using var db = h.NewContext();
        var lead = h.AddEmployee(db);
        var (projectId, _) = h.AddProject(db, lead.Id);
        var later = Category(h, projectId, "meeting", 5);
        var earlier = Category(h, projectId, "MEETING", 2);
        db.TaskCategories.AddRange(later, earlier);
        await db.SaveChangesAsync();

        await RunBackfillAsync(db);

        var categories = await CategoriesAsync(db, projectId);
        Assert.Equal(TaskCategorySystemKeys.Meeting, categories.Single(c => c.Id == earlier.Id).SystemKey);
        Assert.Null(categories.Single(c => c.Id == later.Id).SystemKey);
        Assert.Equal(3, categories.Count);
    }

    [Fact]
    public async Task OnlySoftDeletedExactMeeting_IsRestoredAndPromoted()
    {
        var h = await MyTeamDb.CreateAsync();
        await using var db = h.NewContext();
        var lead = h.AddEmployee(db);
        var (projectId, _) = h.AddProject(db, lead.Id);
        var deleted = Category(h, projectId, "meeting", 4, deleted: true);
        db.TaskCategories.Add(deleted);
        await db.SaveChangesAsync();

        await RunBackfillAsync(db);

        var restored = (await CategoriesAsync(db, projectId)).Single(c => c.Id == deleted.Id);
        Assert.False(restored.IsDeleted);
        Assert.Null(restored.DeletedAt);
        Assert.Equal(TaskCategorySystemKeys.Meeting, restored.SystemKey);
        Assert.Equal(2, (await CategoriesAsync(db, projectId)).Count);
    }

    [Fact]
    public async Task SoftDeletedExactMeetingPlusLiveVariant_PromotesTheLiveOne()
    {
        var h = await MyTeamDb.CreateAsync();
        await using var db = h.NewContext();
        var lead = h.AddEmployee(db);
        var (projectId, _) = h.AddProject(db, lead.Id);
        var deleted = Category(h, projectId, "meeting", 4, deleted: true);
        var live = Category(h, projectId, "Meeting", 6);
        db.TaskCategories.AddRange(deleted, live);
        await db.SaveChangesAsync();

        await RunBackfillAsync(db);

        var categories = await CategoriesAsync(db, projectId);
        Assert.Equal(TaskCategorySystemKeys.Meeting, categories.Single(c => c.Id == live.Id).SystemKey);
        Assert.True(categories.Single(c => c.Id == deleted.Id).IsDeleted);
    }

    [Fact]
    public async Task OfficeProject_GetsCapitalisedMeeting()
    {
        var h = await MyTeamDb.CreateAsync();
        await using var db = h.NewContext();
        var lead = h.AddEmployee(db);
        var (projectId, _) = h.AddProject(db, lead.Id);
        await db.SaveChangesAsync();
        await db.Projects.Where(p => p.Id == projectId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.SystemPurpose, ProjectSystemPurposes.Office));

        await RunBackfillAsync(db);

        var meeting = (await CategoriesAsync(db, projectId)).Single(c => c.SystemKey == TaskCategorySystemKeys.Meeting);
        Assert.Equal("Meeting", meeting.Name);
    }

    [Fact]
    public async Task SecondRun_ChangesNothing()
    {
        var h = await MyTeamDb.CreateAsync();
        await using var db = h.NewContext();
        var lead = h.AddEmployee(db);
        var (projectId, _) = h.AddProject(db, lead.Id);
        await db.SaveChangesAsync();

        await RunBackfillAsync(db);
        var afterFirst = (await CategoriesAsync(db, projectId)).Select(c => (c.Id, c.Name, c.SystemKey, c.IsDeleted)).ToList();
        await RunBackfillAsync(db);
        var afterSecond = (await CategoriesAsync(db, projectId)).Select(c => (c.Id, c.Name, c.SystemKey, c.IsDeleted)).ToList();

        Assert.Equal(afterFirst, afterSecond);
    }
}
```

- [ ] **Step 7: Run the backfill tests**

Run: `dotnet test tests/ONEVO.Tests.Integration/ONEVO.Tests.Integration.csproj --filter "FullyQualifiedName~MeetingCategoryBackfillIntegrationTests"`
Expected: 8 passed. (The shared template database applies all migrations, including this one, so the new migration is also exercised end to end.)

- [ ] **Step 8: Build and run the architecture tests (model snapshot and RLS conventions)**

Run: `dotnet build src/ONEVO.Api/ONEVO.Api.csproj -nologo && dotnet test tests/ONEVO.Tests.Architecture/ONEVO.Tests.Architecture.csproj -nologo`
Expected: build succeeds; all architecture tests pass.

- [ ] **Step 9: Commit**

```bash
git add src/ONEVO.Domain/Features/WorkManagement/Tasks/Entities/TaskCategory.cs src/ONEVO.Infrastructure/Persistence/Configurations/WorkManagement/TaskCategoryConfiguration.cs src/ONEVO.Infrastructure/Persistence/DataMigrations/MeetingCategoryBackfill.cs src/ONEVO.Infrastructure/Migrations tests/ONEVO.Tests.Integration/WorkManagement/MeetingCategoryBackfillIntegrationTests.cs
git commit -m "feat(task-categories): system_key column and Meeting backfill for every project

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: New projects, the Office project and demo data get the Meeting category

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/Tasks/Services/DefaultTaskCategoryTemplate.cs`
- Modify: `src/ONEVO.Infrastructure/Services/CoreHr/OfficeProjectProvisioner.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Seeders/WorkManagementDapiDemoSeeder.Tasks.cs`
- Create: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/DefaultTaskCategoryTemplateTests.cs`
- Modify: `tests/ONEVO.Tests.Integration/WorkManagement/OfficeProjectProvisionerRlsIntegrationTests.cs`

- [ ] **Step 1: Write the failing template test**

`tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/DefaultTaskCategoryTemplateTests.cs`:

```csharp
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class DefaultTaskCategoryTemplateTests
{
    [Fact]
    public void BuildRows_IncludesExactlyOneSystemMeetingCategoryLast()
    {
        var rows = DefaultTaskCategoryTemplate.BuildRows(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow);

        Assert.Equal(new[] { "task", "bug", "story", "feature", "meeting" }, rows.OrderBy(r => r.DisplayOrder).Select(r => r.Name));
        var meeting = Assert.Single(rows, r => r.SystemKey is not null);
        Assert.Equal(TaskCategorySystemKeys.Meeting, meeting.SystemKey);
        Assert.Equal(4, meeting.DisplayOrder);
    }
}
```

- [ ] **Step 2: Run it**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~DefaultTaskCategoryTemplateTests"`
Expected: FAIL (4 names returned, no system row).

- [ ] **Step 3: Add Meeting to the template**

In `DefaultTaskCategoryTemplate.BuildRows`, add a fifth element to the list:

```csharp
            new() { Id = Guid.NewGuid(), TenantId = tenantId, ProjectId = projectId, Name = TaskCategorySystemKeys.Meeting, DisplayOrder = 4, SystemKey = TaskCategorySystemKeys.Meeting, CreatedById = createdById, CreatedAt = now }
```

- [ ] **Step 4: Run it**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~DefaultTaskCategoryTemplateTests|FullyQualifiedName~CreateProjectCommandHandlerTests"`
Expected: all pass.

- [ ] **Step 5: Add a failing assertion for the Office project's Meeting category**

Append this test to `OfficeProjectProvisionerRlsIntegrationTests`:

```csharp
    [Fact]
    public async Task EnsureAsync_NewOfficeProject_HasTheSystemMeetingCategory()
    {
        await using var harness = await OfficeProjectDb.CreateAsync();
        var hr = await harness.SeedEmployeeAsync(harness.LegalEntityId);
        var scopes = harness.BuildAppScopeFactory();

        await using var scope = scopes.CreateAsyncScope();
        var context = await scope.ServiceProvider.GetRequiredService<IOfficeProjectProvisioner>()
            .EnsureAsync(harness.TenantId, harness.LegalEntityId, hr.UserId, hr.Id, DateOnly.FromDateTime(DateTime.UtcNow));

        await using var admin = harness.AdminContext();
        var meeting = await admin.TaskCategories.SingleAsync(c => c.ProjectId == context.ProjectId && c.SystemKey == "meeting");
        Assert.Equal("Meeting", meeting.Name);
        Assert.True(context.CategoryIds.ContainsKey("meeting"));
    }
```

Run: `dotnet test tests/ONEVO.Tests.Integration/ONEVO.Tests.Integration.csproj --filter "FullyQualifiedName~EnsureAsync_NewOfficeProject_HasTheSystemMeetingCategory"`
Expected: FAIL (`Sequence contains no elements`).

- [ ] **Step 6: Add Meeting to the provisioner**

In `OfficeProjectProvisioner.EnsureAsync`, replace:

```csharp
        var categories = new[]
        {
            TaskCategory("Onboarding", 0),
            TaskCategory("Offboarding", 1)
        };
```

with:

```csharp
        var categories = new[]
        {
            TaskCategory("Onboarding", 0),
            TaskCategory("Offboarding", 1),
            TaskCategory("Meeting", 2, ONEVO.Domain.Features.WorkManagement.Tasks.Entities.TaskCategorySystemKeys.Meeting)
        };
```

and replace the local factory:

```csharp
        ONEVO.Domain.Features.WorkManagement.Tasks.Entities.TaskCategory TaskCategory(string name, int order) => new()
        {
            Id = Guid.NewGuid(), TenantId = tenantId, ProjectId = project.Id,
            Name = name, DisplayOrder = order, CreatedById = actingUserId, CreatedAt = now
        };
```

with:

```csharp
        ONEVO.Domain.Features.WorkManagement.Tasks.Entities.TaskCategory TaskCategory(string name, int order, string? systemKey = null) => new()
        {
            Id = Guid.NewGuid(), TenantId = tenantId, ProjectId = project.Id,
            Name = name, DisplayOrder = order, SystemKey = systemKey, CreatedById = actingUserId, CreatedAt = now
        };
```

- [ ] **Step 7: Give demo projects the system Meeting category**

In `WorkManagementDapiDemoSeeder.Tasks.cs`, change:

```csharp
    private static readonly string[] CanonicalCategoryNames = ["task", "bug", "story", "feature"];
```

to:

```csharp
    private static readonly string[] CanonicalCategoryNames = ["task", "bug", "story", "feature", TaskCategorySystemKeys.Meeting];
```

and in `SeedProjectTaskCategoriesAsync`, in the `new TaskCategory { ... }` initializer, add after `DisplayOrder = order,`:

```csharp
                SystemKey = name == TaskCategorySystemKeys.Meeting ? TaskCategorySystemKeys.Meeting : null,
```

- [ ] **Step 8: Run the affected tests**

Run: `dotnet test tests/ONEVO.Tests.Integration/ONEVO.Tests.Integration.csproj --filter "FullyQualifiedName~OfficeProjectProvisionerRlsIntegrationTests"` → Expected: 3 passed.
Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~WorkManagementDapiDemoSeeder"` → Expected: all pass.

- [ ] **Step 9: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Tasks/Services/DefaultTaskCategoryTemplate.cs src/ONEVO.Infrastructure/Services/CoreHr/OfficeProjectProvisioner.cs src/ONEVO.Infrastructure/Persistence/Seeders/WorkManagementDapiDemoSeeder.Tasks.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/DefaultTaskCategoryTemplateTests.cs tests/ONEVO.Tests.Integration/WorkManagement/OfficeProjectProvisionerRlsIntegrationTests.cs
git commit -m "feat(task-categories): seed the system Meeting category in new, Office and demo projects

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: Handler guards — no delete or rename of system categories, no look-alikes

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/DeleteTaskCategory/DeleteTaskCategoryCommandHandler.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/EditTaskCategory/EditTaskCategoryCommandHandler.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/CreateTaskCategory/CreateTaskCategoryCommandHandler.cs`
- Modify: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/{Delete,Edit,Create}TaskCategoryCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `TaskCategory.SystemKey`, `TaskCategorySystemKeys.IsReservedName` (Task 2).

- [ ] **Step 1: Let the existing test builders create system categories**

In `DeleteTaskCategoryCommandHandlerTests.Build`, add a parameter `string? systemKey = null` (after `bool? callerIsEffectiveManager = null`) and change the category line to:

```csharp
        var category = new TaskCategory { Id = CategoryId, TenantId = TenantId, ProjectId = ProjectId, Name = systemKey ?? "Bug", SystemKey = systemKey, CreatedAt = DateTimeOffset.UtcNow };
```

In `EditTaskCategoryCommandHandlerTests.Build`, add the same parameter `string? systemKey = null`, and in the `new TaskCategory { ... }` initializer set `Name = systemKey ?? "Bug",` and add `SystemKey = systemKey,`.

- [ ] **Step 2: Write the failing guard tests**

Append to `DeleteTaskCategoryCommandHandlerTests`:

```csharp
    [Fact]
    public async Task Handle_SystemCategory_ReturnsConflictAndDoesNotRemove()
    {
        var (handler, categories) = Build(anyTasksInCategory: false, systemKey: TaskCategorySystemKeys.Meeting);

        var result = await handler.Handle(new DeleteTaskCategoryCommand(CategoryId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        categories.Verify(x => x.Remove(It.IsAny<TaskCategory>()), Times.Never);
    }
```

Append to `EditTaskCategoryCommandHandlerTests`:

```csharp
    [Fact]
    public async Task Handle_SystemCategoryRename_ReturnsConflict()
    {
        var (handler, categories) = Build(systemKey: TaskCategorySystemKeys.Meeting);

        var result = await handler.Handle(new EditTaskCategoryCommand(CategoryId, "Calls", 3), CancellationToken.None);

        Assert.Equal(409, result.StatusCode);
        categories.Verify(x => x.Update(It.IsAny<TaskCategory>()), Times.Never);
    }

    [Theory]
    [InlineData("meeting")]
    [InlineData(" meeting ")]
    public async Task Handle_SystemCategoryOrderOnlyEdit_IsAllowed(string sameName)
    {
        var (handler, categories) = Build(systemKey: TaskCategorySystemKeys.Meeting);

        var result = await handler.Handle(new EditTaskCategoryCommand(CategoryId, sameName, 3), CancellationToken.None);

        Assert.True(result.IsSuccess);
        categories.Verify(x => x.Update(It.Is<TaskCategory>(c => c.DisplayOrder == 3 && c.Name == "meeting")), Times.Once);
    }

    [Theory]
    [InlineData("Meeting")]
    [InlineData(" MEETING ")]
    public async Task Handle_RenameUserCategoryToReservedName_ReturnsConflict(string name)
    {
        var (handler, categories) = Build();

        var result = await handler.Handle(new EditTaskCategoryCommand(CategoryId, name, 0), CancellationToken.None);

        Assert.Equal(409, result.StatusCode);
        categories.Verify(x => x.Update(It.IsAny<TaskCategory>()), Times.Never);
    }
```

Append to `CreateTaskCategoryCommandHandlerTests` (and add `using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;` if missing):

```csharp
    [Theory]
    [InlineData("meeting")]
    [InlineData("Meeting")]
    [InlineData(" MEETING ")]
    public async Task Handle_ReservedName_ReturnsConflict(string name)
    {
        var (handler, categories) = Build(OwnerEmployeeId);

        var result = await handler.Handle(new CreateTaskCategoryCommand(ProjectId, name, 9), CancellationToken.None);

        Assert.Equal(409, result.StatusCode);
        categories.Verify(x => x.AddAsync(It.IsAny<TaskCategory>(), It.IsAny<CancellationToken>()), Times.Never);
    }
```

- [ ] **Step 3: Run them**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~TaskCategoryCommandHandlerTests"`
Expected: the 8 new cases FAIL; existing cases pass.

- [ ] **Step 4: Implement the guards**

`DeleteTaskCategoryCommandHandler.Handle`: directly after

```csharp
        if (category is null)
            return Result.NotFound("Task category not found.");
```

insert:

```csharp
        if (category.SystemKey is not null)
            return Result.Conflict("System categories can't be deleted.");
```

`EditTaskCategoryCommandHandler.Handle`: directly after the membership check (`... can edit task categories.");`), insert:

```csharp
        var newName = request.Name.Trim();
        if (category.SystemKey is not null && !string.Equals(category.Name, newName, StringComparison.Ordinal))
            return Result.Conflict("System categories can't be renamed.");
        if (category.SystemKey is null && TaskCategorySystemKeys.IsReservedName(newName))
            return Result.Conflict("This project already has a Meeting category.");
```

and inside the transaction change `category.Name = request.Name.Trim();` to `category.Name = newName;`. Add `using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;`.

`CreateTaskCategoryCommandHandler.Handle`: directly after the Office check (`... system-managed.");`), insert:

```csharp
        if (TaskCategorySystemKeys.IsReservedName(request.Name))
            return Result<TaskCategoryResponse>.Conflict("This project already has a Meeting category.");
```

- [ ] **Step 5: Run them**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~TaskCategoryCommandHandlerTests"`
Expected: all pass.

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Tasks/Commands tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks
git commit -m "feat(task-categories): block deleting/renaming system categories and Meeting look-alikes

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: Expose `systemKey` / `isSystem` through the API

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/Tasks/DTOs/Responses/TaskCategoryResponse.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/CreateTaskCategory/CreateTaskCategoryCommandHandler.cs:76-77`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/ReorderTaskCategories/ReorderTaskCategoriesCommandHandler.cs:91`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Tasks/Queries/GetProjectTaskCategories/GetProjectTaskCategoriesQueryHandler.cs:37`
- Modify: `src/ONEVO.Api/Contracts/WorkManagement/Tasks/TaskContracts.cs:55`, `WorkTaskViewModelMapper.cs:29`
- Modify: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/GetProjectTaskCategoriesQueryHandlerTests.cs`

**Interfaces:**
- Produces: `TaskCategoryResponse(Guid Id, string Name, int DisplayOrder, string? SystemKey)` with `bool IsSystem`. `TaskCategoryViewModel(Guid Id, string Name, int DisplayOrder, string? SystemKey, bool IsSystem)`. JSON: `systemKey`, `isSystem`. The frontend (Task 9) relies on these names.

- [ ] **Step 1: Write the failing test**

In `GetProjectTaskCategoriesQueryHandlerTests`, find the happy-path test that asserts `new[] { "Feature", "Bug" }`. In the same file's setup, give the repository mock one more category with `SystemKey = TaskCategorySystemKeys.Meeting`. Then add a test (reuse that file's existing setup helper, adding the Meeting row to the categories it returns):

```csharp
    [Fact]
    public async Task Handle_ReturnsSystemKeyAndIsSystem()
    {
        // Arrange with the same setup as the happy-path test, plus:
        // new TaskCategory { Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, Name = "meeting", DisplayOrder = 2, SystemKey = TaskCategorySystemKeys.Meeting, CreatedAt = DateTimeOffset.UtcNow }
        var result = await handler.Handle(new GetProjectTaskCategoriesQuery(ProjectId), CancellationToken.None);

        var meeting = Assert.Single(result.Value!, c => c.IsSystem);
        Assert.Equal(TaskCategorySystemKeys.Meeting, meeting.SystemKey);
        Assert.All(result.Value!.Where(c => c.Name != "meeting"), c => Assert.False(c.IsSystem));
    }
```

(The executor writes the Arrange block by copying that file's happy-path arrangement and appending the Meeting row shown in the comment. Keep the existing tests' expectations: the meeting row sorts after the others, so the existing `"Feature", "Bug"` assertion must be changed to `"Feature", "Bug", "meeting"` only if that test shares the modified setup.)

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~GetProjectTaskCategoriesQueryHandlerTests"`
Expected: compile error / FAIL (`SystemKey` not on the response).

- [ ] **Step 2: Extend the response and its construction sites**

`TaskCategoryResponse.cs`:

```csharp
namespace ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;

public record TaskCategoryResponse(Guid Id, string Name, int DisplayOrder, string? SystemKey)
{
    public bool IsSystem => SystemKey is not null;
}
```

`CreateTaskCategoryCommandHandler.cs` (the success return):

```csharp
            return Result<TaskCategoryResponse>.Success(new TaskCategoryResponse(
                category.Id, category.Name, category.DisplayOrder, category.SystemKey));
```

`ReorderTaskCategoriesCommandHandler.cs:91` and `GetProjectTaskCategoriesQueryHandler.cs:37`:

```csharp
                .Select(c => new TaskCategoryResponse(c.Id, c.Name, c.DisplayOrder, c.SystemKey))
```

`TaskContracts.cs:55`:

```csharp
public sealed record TaskCategoryViewModel(Guid Id, string Name, int DisplayOrder, string? SystemKey, bool IsSystem);
```

`WorkTaskViewModelMapper.cs:29`:

```csharp
    public static TaskCategoryViewModel ToViewModel(this TaskCategoryResponse dto) =>
        new(dto.Id, dto.Name, dto.DisplayOrder, dto.SystemKey, dto.IsSystem);
```

- [ ] **Step 3: Build and fix any other compile breaks**

Run: `dotnet build src/ONEVO.Api/ONEVO.Api.csproj -nologo 2>&1 | grep -E "error|Build succeeded"`
Expected: `Build succeeded`. If a test project constructs `TaskCategoryResponse` with three arguments, add `null` as the fourth (search: `grep -rn "new TaskCategoryResponse(" tests`).

- [ ] **Step 4: Run the category tests**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~TaskCategor"`
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Tasks src/ONEVO.Api/Contracts/WorkManagement/Tasks tests/ONEVO.Tests.Unit
git commit -m "feat(task-categories): expose systemKey and isSystem in category responses

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: `OfficeProjectReconciler`

**Files:**
- Create: `src/ONEVO.Application/Features/CoreHr/Onboarding/Services/IOfficeProjectReconciler.cs`
- Create: `src/ONEVO.Infrastructure/Services/CoreHr/OfficeProjectReconciler.cs`
- Modify: `src/ONEVO.Infrastructure/DependencyInjection.cs` (next to line 320)
- Modify: `tests/ONEVO.Tests.Integration/WorkManagement/OfficeProjectDb.cs` (register the reconciler)
- Create: `tests/ONEVO.Tests.Integration/WorkManagement/OfficeProjectReconcilerIntegrationTests.cs`

**Interfaces:**
- Consumes: `IOfficeProjectProvisioner.EnsureAsync` (Task 1 fix), `ITenantRepository`, `ITenantContextSwitcher`.
- Produces:
  ```csharp
  public enum OfficeProjectReconcileOutcome { Created, LeadSwitched, AlreadyAligned, NoOwnerEmployee, TenantInactive }
  public interface IOfficeProjectReconciler
  { Task<OfficeProjectReconcileOutcome> ReconcileAsync(Guid tenantId, CancellationToken ct = default); }
  ```
  Tasks 7 and 8 call `ReconcileAsync`.

- [ ] **Step 1: Write the contract**

`src/ONEVO.Application/Features/CoreHr/Onboarding/Services/IOfficeProjectReconciler.cs`:

```csharp
namespace ONEVO.Application.Features.CoreHr.Onboarding.Services;

public enum OfficeProjectReconcileOutcome
{
    Created,
    LeadSwitched,
    AlreadyAligned,
    NoOwnerEmployee,
    TenantInactive
}

/// <summary>Makes a tenant's Office project exist and be led by the tenant owner's employee.
/// Idempotent; safe to call on every startup and whenever the Office project is needed.</summary>
public interface IOfficeProjectReconciler
{
    Task<OfficeProjectReconcileOutcome> ReconcileAsync(Guid tenantId, CancellationToken ct = default);
}
```

- [ ] **Step 2: Register it in the harness (so tests compile against it)**

In `OfficeProjectDb.BuildAppScopeFactory`, after the provisioner registration, add:

```csharp
        services.AddScoped<IOfficeProjectReconciler, OfficeProjectReconciler>();
```

- [ ] **Step 3: Write the failing integration tests**

`tests/ONEVO.Tests.Integration/WorkManagement/OfficeProjectReconcilerIntegrationTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ONEVO.Application.Features.CoreHr.Onboarding.Services;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using ONEVO.Domain.Lookups;
using Xunit;

namespace ONEVO.Tests.Integration.WorkManagement;

public sealed class OfficeProjectReconcilerIntegrationTests
{
    private static async Task<OfficeProjectReconcileOutcome> ReconcileAsync(OfficeProjectDb harness)
    {
        await using var scope = harness.BuildAppScopeFactory().CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IOfficeProjectReconciler>().ReconcileAsync(harness.TenantId);
    }

    private static async Task<Project?> OfficeAsync(OfficeProjectDb harness)
    {
        await using var admin = harness.AdminContext();
        return await admin.Projects.SingleOrDefaultAsync(p => p.TenantId == harness.TenantId && p.SystemPurpose == ProjectSystemPurposes.Office);
    }

    private static async Task<bool> IsActiveMemberAsync(OfficeProjectDb harness, Guid projectId, Guid employeeId)
    {
        await using var admin = harness.AdminContext();
        return await admin.ProjectMembers.AnyAsync(m => m.ProjectId == projectId && m.EmployeeId == employeeId && m.IsActive);
    }

    [Fact]
    public async Task NoOwnerEmployee_CreatesNothing()
    {
        await using var harness = await OfficeProjectDb.CreateAsync();
        var role = await harness.SeedOwnerRoleAsync();
        await harness.AssignRoleAsync(Guid.NewGuid(), role, DateTimeOffset.UtcNow.AddDays(-5));

        Assert.Equal(OfficeProjectReconcileOutcome.NoOwnerEmployee, await ReconcileAsync(harness));
        Assert.Null(await OfficeAsync(harness));
    }

    [Fact]
    public async Task OwnerWithEmployee_CreatesOfficeLedByOwner()
    {
        await using var harness = await OfficeProjectDb.CreateAsync();
        var role = await harness.SeedOwnerRoleAsync();
        var owner = await harness.SeedEmployeeAsync(harness.LegalEntityId);
        await harness.AssignRoleAsync(owner.UserId, role, DateTimeOffset.UtcNow.AddDays(-5));

        Assert.Equal(OfficeProjectReconcileOutcome.Created, await ReconcileAsync(harness));
        var office = await OfficeAsync(harness);
        Assert.NotNull(office);
        Assert.Equal(owner.Id, office!.LeadId);
        Assert.True(await IsActiveMemberAsync(harness, office.Id, owner.Id));
    }

    [Fact]
    public async Task ExistingOfficeLedByHr_SwitchesLeadToOwner_KeepsHrMembership()
    {
        await using var harness = await OfficeProjectDb.CreateAsync();
        var hr = await harness.SeedEmployeeAsync(harness.LegalEntityId);
        await using (var scope = harness.BuildAppScopeFactory().CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<IOfficeProjectProvisioner>()
                .EnsureAsync(harness.TenantId, harness.LegalEntityId, hr.UserId, hr.Id, DateOnly.FromDateTime(DateTime.UtcNow));
        var role = await harness.SeedOwnerRoleAsync();
        var owner = await harness.SeedEmployeeAsync(harness.LegalEntityId);
        await harness.AssignRoleAsync(owner.UserId, role, DateTimeOffset.UtcNow.AddDays(-5));

        Assert.Equal(OfficeProjectReconcileOutcome.LeadSwitched, await ReconcileAsync(harness));
        var office = (await OfficeAsync(harness))!;
        Assert.Equal(owner.Id, office.LeadId);
        Assert.True(await IsActiveMemberAsync(harness, office.Id, owner.Id));
        Assert.True(await IsActiveMemberAsync(harness, office.Id, hr.Id));
    }

    [Fact]
    public async Task SecondRun_IsAlreadyAligned_WithoutDuplicateMembership()
    {
        await using var harness = await OfficeProjectDb.CreateAsync();
        var role = await harness.SeedOwnerRoleAsync();
        var owner = await harness.SeedEmployeeAsync(harness.LegalEntityId);
        await harness.AssignRoleAsync(owner.UserId, role, DateTimeOffset.UtcNow.AddDays(-5));

        await ReconcileAsync(harness);
        Assert.Equal(OfficeProjectReconcileOutcome.AlreadyAligned, await ReconcileAsync(harness));

        var office = (await OfficeAsync(harness))!;
        await using var admin = harness.AdminContext();
        Assert.Equal(1, await admin.ProjectMembers.CountAsync(m => m.ProjectId == office.Id && m.EmployeeId == owner.Id));
    }

    [Fact]
    public async Task ExpiredOwnerAssignment_IsIgnored()
    {
        await using var harness = await OfficeProjectDb.CreateAsync();
        var role = await harness.SeedOwnerRoleAsync();
        var expired = await harness.SeedEmployeeAsync(harness.LegalEntityId);
        await harness.AssignRoleAsync(expired.UserId, role, DateTimeOffset.UtcNow.AddDays(-30), expiresAt: DateTimeOffset.UtcNow.AddDays(-1));
        var current = await harness.SeedEmployeeAsync(harness.LegalEntityId);
        await harness.AssignRoleAsync(current.UserId, role, DateTimeOffset.UtcNow.AddDays(-2));

        await ReconcileAsync(harness);

        Assert.Equal(current.Id, (await OfficeAsync(harness))!.LeadId);
    }

    [Fact]
    public async Task EarliestOwnerWithoutEmployee_FallsThroughToNextOwnerWithEmployee()
    {
        await using var harness = await OfficeProjectDb.CreateAsync();
        var role = await harness.SeedOwnerRoleAsync();
        await harness.AssignRoleAsync(Guid.NewGuid(), role, DateTimeOffset.UtcNow.AddDays(-30));
        var later = await harness.SeedEmployeeAsync(harness.LegalEntityId);
        await harness.AssignRoleAsync(later.UserId, role, DateTimeOffset.UtcNow.AddDays(-2));

        await ReconcileAsync(harness);

        Assert.Equal(later.Id, (await OfficeAsync(harness))!.LeadId);
    }

    [Fact]
    public async Task OwnerWhoseOnlyEmployeeIsTerminated_IsNotEligible()
    {
        await using var harness = await OfficeProjectDb.CreateAsync();
        var role = await harness.SeedOwnerRoleAsync();
        var terminated = await harness.SeedEmployeeAsync(harness.LegalEntityId, EmploymentStatusIds.Terminated);
        await harness.AssignRoleAsync(terminated.UserId, role, DateTimeOffset.UtcNow.AddDays(-5));

        Assert.Equal(OfficeProjectReconcileOutcome.NoOwnerEmployee, await ReconcileAsync(harness));
        Assert.Null(await OfficeAsync(harness));
    }

    [Fact]
    public async Task OwnerEmployeeWithoutLegalEntity_UsesThePrimaryLegalEntity()
    {
        await using var harness = await OfficeProjectDb.CreateAsync();
        var role = await harness.SeedOwnerRoleAsync();
        var owner = await harness.SeedEmployeeAsync(legalEntityId: null);
        await harness.AssignRoleAsync(owner.UserId, role, DateTimeOffset.UtcNow.AddDays(-5));

        await ReconcileAsync(harness);

        Assert.Equal(harness.LegalEntityId, (await OfficeAsync(harness))!.OwningLegalEntityId);
    }

    [Fact]
    public async Task OwnerWithTwoEmployees_PrefersThePrimaryLegalEntity()
    {
        await using var harness = await OfficeProjectDb.CreateAsync();
        var role = await harness.SeedOwnerRoleAsync();
        var userId = Guid.NewGuid();
        var secondary = await harness.SeedSecondaryLegalEntityAsync();
        await harness.SeedEmployeeAsync(secondary, userId: userId);
        var primaryEmployee = await harness.SeedEmployeeAsync(harness.LegalEntityId, userId: userId);
        await harness.AssignRoleAsync(userId, role, DateTimeOffset.UtcNow.AddDays(-5));

        await ReconcileAsync(harness);

        Assert.Equal(primaryEmployee.Id, (await OfficeAsync(harness))!.LeadId);
    }

    [Fact]
    public async Task SuspendedTenant_IsSkipped()
    {
        await using var harness = await OfficeProjectDb.CreateAsync();
        await harness.SeedTenantStatusAsync(TenantStatus.Suspended);

        Assert.Equal(OfficeProjectReconcileOutcome.TenantInactive, await ReconcileAsync(harness));
    }
}
```

Run: `dotnet test tests/ONEVO.Tests.Integration/ONEVO.Tests.Integration.csproj --filter "FullyQualifiedName~OfficeProjectReconcilerIntegrationTests"`
Expected: compile error (`OfficeProjectReconciler` does not exist).

- [ ] **Step 4: Implement the reconciler**

`src/ONEVO.Infrastructure/Services/CoreHr/OfficeProjectReconciler.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.Services;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.WorkManagement.ProjectMembers.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using ONEVO.Domain.Lookups;
using ONEVO.Infrastructure.Persistence;

namespace ONEVO.Infrastructure.Services.CoreHr;

public sealed class OfficeProjectReconciler(
    IServiceScopeFactory scopeFactory,
    IOfficeProjectProvisioner provisioner,
    ILogger<OfficeProjectReconciler> logger) : IOfficeProjectReconciler
{
    private const string OwnerRoleName = "Owner";

    private sealed record OwnerEmployee(Guid EmployeeId, Guid UserId, Guid LegalEntityId);

    public async Task<OfficeProjectReconcileOutcome> ReconcileAsync(Guid tenantId, CancellationToken ct = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var services = scope.ServiceProvider;

        var tenant = await services.GetRequiredService<ITenantRepository>().GetByIdAsync(tenantId, ct);
        if (tenant is null || tenant.Status is TenantStatus.Suspended or TenantStatus.Cancelled)
            return OfficeProjectReconcileOutcome.TenantInactive;

        await services.GetRequiredService<ITenantContextSwitcher>().SwitchToTenantAsync(
            new TenantRegistryEntry(tenant.Id, tenant.Slug, tenant.Status, PlanCode: null), ct);
        var db = services.GetRequiredService<ApplicationDbContext>();
        var now = DateTimeOffset.UtcNow;

        var owner = await FindOwnerEmployeeAsync(db, tenantId, now, ct);
        if (owner is null)
            return OfficeProjectReconcileOutcome.NoOwnerEmployee;

        var office = await db.Projects.AsNoTracking()
            .Where(p => p.TenantId == tenantId && p.SystemPurpose == ProjectSystemPurposes.Office)
            .Select(p => new { p.Id, p.LeadId })
            .FirstOrDefaultAsync(ct);

        if (office is null)
        {
            await provisioner.EnsureAsync(
                tenantId, owner.LegalEntityId, owner.UserId, owner.EmployeeId, DateOnly.FromDateTime(now.UtcDateTime), ct);
            logger.LogInformation("Created the Office project for tenant {TenantId}, led by the tenant owner.", tenantId);
            return OfficeProjectReconcileOutcome.Created;
        }

        var outcome = OfficeProjectReconcileOutcome.AlreadyAligned;
        if (office.LeadId != owner.EmployeeId)
        {
            // Guarded on the lead we read, so concurrent runs converge instead of flip-flopping.
            var updated = await db.Projects
                .Where(p => p.Id == office.Id && p.LeadId == office.LeadId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(p => p.LeadId, owner.EmployeeId)
                    .SetProperty(p => p.UpdatedAt, now), ct);
            if (updated == 1)
            {
                outcome = OfficeProjectReconcileOutcome.LeadSwitched;
                logger.LogInformation(
                    "Switched the Office project lead for tenant {TenantId} to the tenant owner (previous lead kept as member).",
                    tenantId);
            }
        }

        await EnsureOwnerMembershipAsync(db, tenantId, office.Id, owner, now, ct);
        return outcome;
    }

    private static async Task<OwnerEmployee?> FindOwnerEmployeeAsync(
        ApplicationDbContext db, Guid tenantId, DateTimeOffset now, CancellationToken ct)
    {
        var ownerUserIds = await db.UserRoles.AsNoTracking()
            .Where(ur => ur.TenantId == tenantId
                && ur.Role.IsSystem && ur.Role.Name == OwnerRoleName
                && (ur.ExpiresAt == null || ur.ExpiresAt > now))
            .OrderBy(ur => ur.AssignedAt).ThenBy(ur => ur.UserId)
            .Select(ur => ur.UserId)
            .ToListAsync(ct);
        if (ownerUserIds.Count == 0)
            return null;

        var candidates = await db.Employees.AsNoTracking()
            .Where(e => e.TenantId == tenantId
                && ownerUserIds.Contains(e.UserId)
                && e.EmploymentStatusId == EmploymentStatusIds.Active)
            .Select(e => new
            {
                e.Id, e.UserId, e.LegalEntityId, e.CreatedAt,
                InPrimaryEntity = e.LegalEntityId != null
                    && db.LegalEntities.Any(le => le.Id == e.LegalEntityId && le.IsPrimary)
            })
            .ToListAsync(ct);
        if (candidates.Count == 0)
            return null;

        var primaryLegalEntityId = await db.LegalEntities.AsNoTracking()
            .Where(le => le.TenantId == tenantId && le.IsPrimary && le.IsActive)
            .OrderBy(le => le.CreatedAt)
            .Select(le => (Guid?)le.Id)
            .FirstOrDefaultAsync(ct);

        foreach (var userId in ownerUserIds)
        {
            var chosen = candidates
                .Where(c => c.UserId == userId)
                .OrderByDescending(c => c.InPrimaryEntity).ThenBy(c => c.CreatedAt).ThenBy(c => c.Id)
                .FirstOrDefault();
            if (chosen is null)
                continue;

            var legalEntityId = chosen.LegalEntityId ?? primaryLegalEntityId;
            return legalEntityId is null ? null : new OwnerEmployee(chosen.Id, chosen.UserId, legalEntityId.Value);
        }

        return null;
    }

    private async Task EnsureOwnerMembershipAsync(
        ApplicationDbContext db, Guid tenantId, Guid projectId, OwnerEmployee owner, DateTimeOffset now, CancellationToken ct)
    {
        var rootObjectiveId = await db.Objectives.AsNoTracking()
            .Where(o => o.TenantId == tenantId && o.ProjectId == projectId && o.IsDefault)
            .Select(o => o.Id)
            .SingleAsync(ct);

        var membership = await db.ProjectMembers.FirstOrDefaultAsync(m =>
            m.TenantId == tenantId && m.ProjectId == projectId
            && m.ObjectiveId == rootObjectiveId && m.EmployeeId == owner.EmployeeId, ct);

        if (membership is null)
        {
            db.ProjectMembers.Add(new ProjectMember
            {
                Id = Guid.NewGuid(), TenantId = tenantId, ProjectId = projectId, ObjectiveId = rootObjectiveId,
                EmployeeId = owner.EmployeeId, MembershipSource = ProjectMembershipSources.System,
                IsActive = true, JoinedAt = now, CreatedById = owner.UserId, CreatedAt = now
            });
        }
        else if (!membership.IsActive)
        {
            membership.IsActive = true;
            membership.RemovedAt = null;
            membership.JoinedAt = now;
        }
        else
        {
            return;
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            logger.LogInformation("Owner membership for the Office project of tenant {TenantId} was added concurrently.", tenantId);
        }
    }
}
```

- [ ] **Step 5: Register it in the app**

In `src/ONEVO.Infrastructure/DependencyInjection.cs`, after line 320 (`AddScoped<IOfficeProjectProvisioner, ...>`), add:

```csharp
        services.AddScoped<IOfficeProjectReconciler, ONEVO.Infrastructure.Services.CoreHr.OfficeProjectReconciler>();
```

(add `using ONEVO.Application.Features.CoreHr.Onboarding.Services;` at the top if it isn't already imported).

- [ ] **Step 6: Run the reconciler tests**

Run: `dotnet test tests/ONEVO.Tests.Integration/ONEVO.Tests.Integration.csproj --filter "FullyQualifiedName~OfficeProjectReconcilerIntegrationTests"`
Expected: 10 passed.

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Application/Features/CoreHr/Onboarding/Services/IOfficeProjectReconciler.cs src/ONEVO.Infrastructure/Services/CoreHr/OfficeProjectReconciler.cs src/ONEVO.Infrastructure/DependencyInjection.cs tests/ONEVO.Tests.Integration/WorkManagement
git commit -m "feat(office-project): reconciler creates or aligns the Office project to the tenant owner

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 7: One-off reconciliation at startup

**Files:**
- Create: `src/ONEVO.Infrastructure/Services/CoreHr/OfficeProjectReconciliationStartupService.cs`
- Modify: `src/ONEVO.Infrastructure/DependencyInjection.cs`
- Create: `tests/ONEVO.Tests.Unit/Features/CoreHr/Onboarding/OfficeProjectReconciliationStartupServiceTests.cs`

**Interfaces:**
- Consumes: `IOfficeProjectReconciler.ReconcileAsync` (Task 6), `ITenantRepository.ListAsync(TenantStatus?, string?, int skip, int take, CancellationToken)` (ordered by `CreatedAt` desc).
- Produces: `OfficeProjectReconciliationStartupService.RunOnceAsync(CancellationToken)` (public, for tests).

- [ ] **Step 1: Write the failing tests**

`tests/ONEVO.Tests.Unit/Features/CoreHr/Onboarding/OfficeProjectReconciliationStartupServiceTests.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ONEVO.Application.Features.CoreHr.Onboarding.Services;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Infrastructure.Services.CoreHr;
using Xunit;

namespace ONEVO.Tests.Unit.Features.CoreHr.Onboarding;

public class OfficeProjectReconciliationStartupServiceTests
{
    private static (OfficeProjectReconciliationStartupService Service, Mock<IOfficeProjectReconciler> Reconciler) Build(
        IReadOnlyList<Tenant> tenants, string environment = "Development")
    {
        var repository = new Mock<ITenantRepository>();
        repository
            .Setup(r => r.ListAsync(null, null, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TenantStatus? _, string? _, int skip, int take, CancellationToken _) =>
                tenants.Skip(skip).Take(take).ToList());
        var reconciler = new Mock<IOfficeProjectReconciler>();
        var hostEnvironment = new Mock<IHostEnvironment>();
        hostEnvironment.SetupGet(e => e.EnvironmentName).Returns(environment);

        var services = new ServiceCollection();
        services.AddSingleton(repository.Object);
        services.AddSingleton(reconciler.Object);
        var provider = services.BuildServiceProvider();

        return (new OfficeProjectReconciliationStartupService(
            provider, hostEnvironment.Object, NullLogger<OfficeProjectReconciliationStartupService>.Instance), reconciler);
    }

    private static Tenant TenantWith(TenantStatus status) => new()
    {
        Id = Guid.NewGuid(), Name = "T", Slug = $"t-{Guid.NewGuid():N}"[..12], Status = status
    };

    [Fact]
    public async Task RunOnceAsync_ReconcilesLiveTenants_SkipsSuspendedAndCancelled()
    {
        var live = new[] { TenantWith(TenantStatus.Active), TenantWith(TenantStatus.Trial), TenantWith(TenantStatus.Provisioning) };
        var dead = new[] { TenantWith(TenantStatus.Suspended), TenantWith(TenantStatus.Cancelled) };
        var (service, reconciler) = Build(live.Concat(dead).ToList());

        await service.RunOnceAsync(CancellationToken.None);

        foreach (var tenant in live)
            reconciler.Verify(r => r.ReconcileAsync(tenant.Id, It.IsAny<CancellationToken>()), Times.Once);
        foreach (var tenant in dead)
            reconciler.Verify(r => r.ReconcileAsync(tenant.Id, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunOnceAsync_OneTenantThrows_OthersStillReconciled()
    {
        var tenants = new[] { TenantWith(TenantStatus.Active), TenantWith(TenantStatus.Active) };
        var (service, reconciler) = Build(tenants);
        reconciler.Setup(r => r.ReconcileAsync(tenants[0].Id, It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("boom"));

        await service.RunOnceAsync(CancellationToken.None);

        reconciler.Verify(r => r.ReconcileAsync(tenants[1].Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunOnceAsync_MoreThanOnePage_ReconcilesEveryTenant()
    {
        var tenants = Enumerable.Range(0, OfficeProjectReconciliationStartupService.PageSize + 3)
            .Select(_ => TenantWith(TenantStatus.Active)).ToList();
        var (service, reconciler) = Build(tenants);

        await service.RunOnceAsync(CancellationToken.None);

        reconciler.Verify(r => r.ReconcileAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Exactly(tenants.Count));
    }

    [Fact]
    public async Task StartAsync_InTestEnvironment_DoesNothing()
    {
        var (service, reconciler) = Build(new[] { TenantWith(TenantStatus.Active) }, environment: "Test");

        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        reconciler.Verify(r => r.ReconcileAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
```

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~OfficeProjectReconciliationStartupServiceTests"`
Expected: compile error (type doesn't exist).

- [ ] **Step 2: Implement the service**

`src/ONEVO.Infrastructure/Services/CoreHr/OfficeProjectReconciliationStartupService.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Features.CoreHr.Onboarding.Services;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Domain.Features.InfrastructureModule.Entities;

namespace ONEVO.Infrastructure.Services.CoreHr;

/// <summary>Runs <see cref="IOfficeProjectReconciler"/> once for every live tenant shortly after
/// startup: the backfill for existing tenants and the recovery path for anything missed. Not a
/// periodic job (SCL-01: background jobs have no multi-instance locking). Skipped in the Test
/// environment so integration-test hosts don't create Office projects behind the tests' backs.</summary>
public sealed class OfficeProjectReconciliationStartupService(
    IServiceProvider services,
    IHostEnvironment environment,
    ILogger<OfficeProjectReconciliationStartupService> logger) : BackgroundService
{
    public const int PageSize = 200;
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (environment.IsEnvironment("Test"))
            return;

        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
            await RunOnceAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host shutting down before or during the run.
        }
    }

    public async Task RunOnceAsync(CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var tenants = scope.ServiceProvider.GetRequiredService<ITenantRepository>();
        var reconciler = scope.ServiceProvider.GetRequiredService<IOfficeProjectReconciler>();

        var skip = 0;
        while (true)
        {
            var page = await tenants.ListAsync(statusFilter: null, searchTerm: null, skip, PageSize, ct);
            foreach (var tenant in page)
            {
                if (tenant.Status is TenantStatus.Suspended or TenantStatus.Cancelled)
                    continue;
                try
                {
                    var outcome = await reconciler.ReconcileAsync(tenant.Id, ct);
                    logger.LogDebug("Office project reconciliation for tenant {TenantId}: {Outcome}.", tenant.Id, outcome);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Office project reconciliation failed for tenant {TenantId}.", tenant.Id);
                }
            }

            if (page.Count < PageSize)
                break;
            skip += PageSize;
        }
    }
}
```

- [ ] **Step 3: Register it**

In `src/ONEVO.Infrastructure/DependencyInjection.cs`, directly after the reconciler registration from Task 6, add:

```csharp
        services.AddHostedService<ONEVO.Infrastructure.Services.CoreHr.OfficeProjectReconciliationStartupService>();
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~OfficeProjectReconciliationStartupServiceTests"`
Expected: 4 passed.

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Infrastructure/Services/CoreHr/OfficeProjectReconciliationStartupService.cs src/ONEVO.Infrastructure/DependencyInjection.cs tests/ONEVO.Tests.Unit/Features/CoreHr/Onboarding/OfficeProjectReconciliationStartupServiceTests.cs
git commit -m "feat(office-project): reconcile every live tenant once at startup

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 8: Reconcile whenever the onboarding path needs the Office project

**Files:**
- Modify: `src/ONEVO.Application/Features/CoreHr/Onboarding/Services/EmployeeChecklistWorkTaskProvisioner.cs`
- Modify: `tests/ONEVO.Tests.Unit/Features/CoreHr/Onboarding/EmployeeChecklistWorkTaskProvisionerTests.cs`

**Interfaces:**
- Consumes: `IOfficeProjectReconciler.ReconcileAsync` (Task 6).

- [ ] **Step 1: Write the failing test**

In `EmployeeChecklistWorkTaskProvisionerTests`, change the constructor call at line 68 to pass a reconciler mock as the second argument. Declare `var reconciler = new Mock<IOfficeProjectReconciler>();` next to `officeProjects` at line 42:

```csharp
        var sut = new EmployeeChecklistWorkTaskProvisioner(
            officeProjects.Object, reconciler.Object, employees.Object, projects.Object, workTasks.Object,
```

(keep the remaining arguments as they are). Then add this test, which reuses the file's existing arrangement for an active acting employee:

```csharp
    [Fact]
    public async Task EnsureOfficeProjectAsync_ReconcilesBeforeEnsuring()
    {
        // Arrange as in the existing EnsureOfficeProjectAsync happy-path test, then:
        var calls = new List<string>();
        reconciler.Setup(r => r.ReconcileAsync(TenantId, It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("reconcile"))
            .ReturnsAsync(OfficeProjectReconcileOutcome.AlreadyAligned);
        officeProjects.Setup(o => o.EnsureAsync(TenantId, It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("ensure"))
            .ReturnsAsync(new OfficeProjectContext(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new Dictionary<string, Guid>()));

        var result = await sut.EnsureOfficeProjectAsync(TenantId, LegalEntityId, ActingUserId, DateOnly.FromDateTime(DateTime.UtcNow));

        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { "reconcile", "ensure" }, calls);
    }
```

(The executor uses the file's existing field names for the tenant, legal entity and acting user ids if they differ from `TenantId`, `LegalEntityId`, `ActingUserId`.)

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~EmployeeChecklistWorkTaskProvisionerTests"`
Expected: compile error (constructor has no reconciler parameter).

- [ ] **Step 2: Inject and call the reconciler**

In `EmployeeChecklistWorkTaskProvisioner`, change the primary constructor to:

```csharp
public sealed class EmployeeChecklistWorkTaskProvisioner(
    IOfficeProjectProvisioner officeProjects,
    IOfficeProjectReconciler officeReconciler,
    IEmployeeRepository employees,
    IProjectRepository projects,
    IWorkTaskRepository workTasks,
    ITaskAssignmentRepository assignments,
    IProjectMemberRepository members,
    IWorkNotificationEngine notifications) : IEmployeeChecklistWorkTaskProvisioner
```

and in `EnsureOfficeProjectAsync`, insert before `var office = await officeProjects.EnsureAsync(`:

```csharp
        // Create it owner-led when an owner employee exists, and align an existing one's lead.
        // EnsureAsync below then only extends the target date or, when no owner employee exists
        // yet, creates it with the acting HR employee as lead (accepted exception, spec §7 Q1).
        await officeReconciler.ReconcileAsync(tenantId, ct);
```

- [ ] **Step 3: Run the tests**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~EmployeeChecklistWorkTaskProvisionerTests|FullyQualifiedName~ApproveAccessGrantRequest|FullyQualifiedName~OnboardingDraft"`
Expected: all pass.

- [ ] **Step 4: Commit**

```bash
git add src/ONEVO.Application/Features/CoreHr/Onboarding/Services/EmployeeChecklistWorkTaskProvisioner.cs tests/ONEVO.Tests.Unit/Features/CoreHr/Onboarding/EmployeeChecklistWorkTaskProvisionerTests.cs
git commit -m "feat(office-project): reconcile before ensuring the Office project for onboarding

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 9: Frontend — lock system categories in the editor

Work in `C:\Users\User\onexso-worktrees\fe-meeting-task`.

**Files:**
- Modify: `src/app/modules/work/models/dto/task-category.dto.ts`, `src/app/modules/work/models/task-category.model.ts`, `src/app/modules/work/utils/task-category.mapper.ts`
- Create: `src/app/modules/work/utils/task-category.mapper.spec.ts`
- Modify: `src/app/modules/work/ui/category-list-editor/category-list-editor.component.ts` and its `.spec.ts`

**Interfaces:**
- Consumes: API fields `systemKey` (`string | null`) and `isSystem` (`boolean`) from Task 5.

- [ ] **Step 1: Write the failing tests**

`src/app/modules/work/utils/task-category.mapper.spec.ts`:

```ts
import { toTaskCategoryColumn } from './task-category.mapper';

describe('toTaskCategoryColumn', () => {
  it('maps the system flags', () => {
    expect(toTaskCategoryColumn({ id: 'm', name: 'meeting', displayOrder: 4, systemKey: 'meeting', isSystem: true }))
      .toEqual({ id: 'm', name: 'meeting', displayOrder: 4, systemKey: 'meeting', isSystem: true });
  });

  it('defaults the flags when the API omits them', () => {
    expect(toTaskCategoryColumn({ id: 't', name: 'task', displayOrder: 0 }))
      .toEqual({ id: 't', name: 'task', displayOrder: 0, systemKey: null, isSystem: false });
  });
});
```

In `category-list-editor.component.spec.ts`, add a Meeting system row to the `categories` fixture:

```ts
    { id: 'c9', name: 'meeting', displayOrder: 3, systemKey: 'meeting', isSystem: true }
```

and change the first test's expected names to `['Task', 'Bug', 'Story', 'meeting']` and its row count to `4`. Then add:

```ts
  it('shows a lock badge and no edit or delete buttons on system categories', () => {
    const { fixture } = setup();
    fixture.detectChanges();

    const rows = Array.from(fixture.nativeElement.querySelectorAll('.category-list__row') as NodeListOf<HTMLElement>);
    const meetingRow = rows.find((row) => row.querySelector('.category-list__name')?.textContent?.trim() === 'meeting')!;
    expect(meetingRow.querySelector('.category-list__system-badge')).not.toBeNull();
    expect(meetingRow.querySelector('.category-list__edit-icon')).toBeNull();
    expect(meetingRow.querySelector('.category-list__delete-icon')).toBeNull();
  });

  it('keeps system categories in the saved order', async () => {
    const { fixture, reorderTaskCategories } = setup();
    fixture.detectChanges();

    Array.from(fixture.nativeElement.querySelectorAll('button') as NodeListOf<HTMLButtonElement>)
      .find((button) => button.textContent?.includes('Update'))!.click();
    await fixture.whenStable();

    expect(reorderTaskCategories).toHaveBeenCalledWith('project-1', expect.arrayContaining([{ categoryId: 'c9', displayOrder: 3 }]));
  });
```

The existing delete test removes `c1` and expects `['Bug', 'Story']`. Update its expectation to `['Bug', 'Story', 'meeting']`.

Run: `npx ng test --watch=false --include src/app/modules/work/utils/task-category.mapper.spec.ts --include src/app/modules/work/ui/category-list-editor/category-list-editor.component.spec.ts`
Expected: FAIL (type errors on `systemKey`/`isSystem`, badge missing).

- [ ] **Step 2: Extend the DTO, model and mapper**

`task-category.dto.ts`, in `TaskCategoryDto`:

```ts
export interface TaskCategoryDto {
  id: string;
  name: string;
  displayOrder: number;
  systemKey?: string | null;
  isSystem?: boolean;
}
```

`task-category.model.ts`:

```ts
export interface TaskCategoryColumn {
  id: string;
  name: string;
  displayOrder: number;
  systemKey?: string | null;
  isSystem?: boolean;
}
```

`task-category.mapper.ts`:

```ts
export function toTaskCategoryColumn(dto: TaskCategoryDto): TaskCategoryColumn {
  return {
    id: dto.id,
    name: dto.name,
    displayOrder: dto.displayOrder,
    systemKey: dto.systemKey ?? null,
    isSystem: dto.isSystem ?? false
  };
}
```

- [ ] **Step 3: Lock system rows in the editor**

In `category-list-editor.component.ts`:

1. `DraftRow` becomes:

```ts
interface DraftRow {
  id: string;
  name: string;
  isSystem: boolean;
}
```

2. In the constructor `effect`, map rows with the flag:

```ts
            .map((category) => ({ id: category.id, name: category.name, isSystem: category.isSystem ?? false }))
```

3. In `confirmAdd`, the appended row:

```ts
      this.rows.set([...this.rows(), { id: created.id, name: created.name, isSystem: created.isSystem ?? false }]);
```

4. Replace the `<div class="category-list__row-actions"> ... </div>` block (lines 62–77) with:

```html
            <div class="category-list__row-actions">
              @if (row.isSystem) {
                <span class="category-list__system-badge" title="System category: can't be renamed or deleted." aria-label="System category: can't be renamed or deleted.">
                  <svg viewBox="0 0 24 24" width="12" height="12" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
                    <rect x="4" y="11" width="16" height="10" rx="2" />
                    <path d="M8 11V7a4 4 0 0 1 8 0v4" />
                  </svg>
                  <span>System</span>
                </span>
              } @else {
                <button type="button" class="category-list__edit-icon" aria-label="Rename category" title="Rename" (click)="openRename(row)">
                  <svg viewBox="0 0 24 24" width="13" height="13" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
                    <path d="M17 3a2.85 2.83 0 1 1 4 4L7.5 20.5 2 22l1.5-5.5Z" />
                  </svg>
                  <span>Edit</span>
                </button>
                <button type="button" class="category-list__delete-icon" aria-label="Delete category" title="Delete" (click)="deleteRow(row.id)">
                  <svg viewBox="0 0 24 24" width="13" height="13" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
                    <path d="M3 6h18" />
                    <path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6" />
                    <path d="M8 6V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2" />
                  </svg>
                  <span>Delete</span>
                </button>
              }
            </div>
```

5. Add to `styles` (after the `.category-list__delete-icon:hover` rule):

```css
    .category-list__system-badge {
      display: inline-flex;
      align-items: center;
      gap: 4px;
      padding: 3px 8px;
      border-radius: 999px;
      font-size: 11.5px;
      font-weight: 500;
      color: var(--color-text-secondary);
      background: color-mix(in srgb, var(--color-border) 30%, transparent);
    }
```

6. Defence in depth, so nothing can call the endpoints for a system row. At the top of `openRename(row: DraftRow)` add `if (row.isSystem) return;`. At the top of `deleteRow(id: string)` add:

```ts
    if (this.rows().find((row) => row.id === id)?.isSystem) return;
```

- [ ] **Step 4: Run the tests**

Run: `npx ng test --watch=false --include src/app/modules/work/utils/task-category.mapper.spec.ts --include src/app/modules/work/ui/category-list-editor/category-list-editor.component.spec.ts`
Expected: all pass.

- [ ] **Step 5: Type-check the whole app**

Run: `npx ng build --configuration development`
Expected: build succeeds (any pre-existing NG8107 warnings are unchanged).

- [ ] **Step 6: Commit**

```bash
git add src/app/modules/work/models src/app/modules/work/utils src/app/modules/work/ui/category-list-editor
git commit -m "feat(task-categories): lock system categories in the category editor

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 10: Full verification and push

- [ ] **Step 1: Backend build, unit and architecture suites**

```bash
cd "C:/Users/User/onexso-worktrees/be-meeting-task"
dotnet build src/ONEVO.Api/ONEVO.Api.csproj -nologo 2>&1 | tail -3
dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj -nologo 2>&1 | tail -3
dotnet test tests/ONEVO.Tests.Architecture/ONEVO.Tests.Architecture.csproj -nologo 2>&1 | tail -3
```
Expected: build succeeded with 0 errors; both suites `Failed: 0`.

- [ ] **Step 2: Integration tests for this phase, plus the categories and onboarding areas it touches**

```bash
dotnet test tests/ONEVO.Tests.Integration/ONEVO.Tests.Integration.csproj -nologo --filter "FullyQualifiedName~OfficeProject|FullyQualifiedName~MeetingCategoryBackfill|FullyQualifiedName~Onboarding|FullyQualifiedName~TaskCategor" 2>&1 | tail -3
```
Expected: `Failed: 0`.

- [ ] **Step 3: Frontend full unit suite**

```bash
cd "C:/Users/User/onexso-worktrees/fe-meeting-task" && npx ng test --watch=false 2>&1 | tail -5
```
Expected: no failures.

- [ ] **Step 4: Push both branches (no PR)**

```bash
git -C "C:/Users/User/onexso-worktrees/be-meeting-task" push -u origin feature/meeting-task-phase0-1
git -C "C:/Users/User/onexso-worktrees/fe-meeting-task" push -u origin feature/meeting-task-phase0-1
```
Expected: both pushes succeed.
