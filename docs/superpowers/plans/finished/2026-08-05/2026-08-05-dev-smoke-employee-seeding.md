# Dev Smoke Employee Seeding Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `DevSmokeTestTenantSeeder` create exactly one `Employee` row per seeded dev-smoke user, tenant- and legal-entity-scoped correctly, idempotently, without touching auth/login, Department/Position/LegalEntity schema, or any production bootstrap path.

**Architecture:** Add a `SmokeEmployeeDefinition` record (`UserId`, `LegalEntityId`, `EmployeeNumber`) to the existing per-tenant definition model in `DevSmokeTestTenantSeeder.cs`, populate it for the 4 seeded users, and add a `SeedTenantEmployeeAsync` step that runs inside the existing per-tenant/per-user loop (same iteration where `SeedTenantUserAsync`/`SeedTenantRoleAsync` already run, so it inherits the already-resolved tenant RLS context). Reference-data existence (employment type/status/work mode) is verified once per `SeedAsync` call before any employee row is written, using the standing guarantee that `LookupDataSeeder` (`DependencyInjection.cs:313`) runs before `DevSmokeTestTenantSeeder` (`DependencyInjection.cs:316`) in the hosted-service startup order, seeding fixed `Id = 1` rows for `full_time` / `active` / `on_site`.

**Tech Stack:** .NET 8 / EF Core (Npgsql provider in production, Sqlite in-memory in `ONEVO.Tests.Unit`), xUnit + FluentAssertions + Moq.

## Global Constraints

- Work only inside `HRMS-Backend-v1`. Do not touch OneVo-HR docs, frontend, Postman, unrelated migrations, auth/login logic, Department/Position schema, LegalEntity schema, or payment/system-config/OAuth code.
- No new migrations — `employees.user_id` unique index (`ix_employees_user_id`) and `(tenant_id, employee_number)` unique index (`ix_employees_tenant_id_employee_number`) already exist (confirmed in `ApplicationDbContextModelSnapshot.cs:1334-1341`). No FK constraints exist from `employees.employment_type_id/employment_status_id/work_mode_id` to the lookup tables (confirmed absent from `20260519061316_AddLookupTables.cs` and `EmployeeConfiguration.cs`) — safe to assign int lookup ids without an FK lookup round-trip, but a value-existence check is still required per spec.
- Exactly one `Employee` row per seeded smoke user — never one per legal entity.
- Employee number format: `ACME-0001`, `ACME-0002`, `ACME-0003`, `DAPI-0001`.
- Must remain Development/Test-only (`StartAsync` environment guard is untouched) — this is a seeding-only change, no API/controllers/invite-flow/department-assignment/position-assignment/hierarchy work.
- Do not commit or push. Do not run the full multi-hour integration suite.

---

### Task 1: Add employee seeding to DevSmokeTestTenantSeeder

**Files:**
- Modify: `src/ONEVO.Infrastructure/Persistence/Seeders/DevSmokeTestTenantSeeder.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/DevPlatform/Tenancy/DevSmokeTestTenantSeederTests.cs`

**Interfaces:**
- Consumes: `ApplicationDbContext.Employees` (`DbSet<Employee>`, already registered at `ApplicationDbContext.cs:165`), `ApplicationDbContext.EmploymentTypes/EmploymentStatuses/WorkModes` (already registered, seeded by `LookupDataSeeder`), the existing `User` objects created by `SeedTenantUserAsync` (has `Id`, `FirstName`, `LastName`, `Email`), the existing `SmokeLegalEntityDefinition.Id` values (`AcmeLegalEntityTechnologiesId`, `AcmeLegalEntitySolutionsId`, `DapiLegalEntityId`).
- Produces: one `Employee` row per seeded user, matched on `UserId` for idempotency. No other task depends on this — it's the full change.

- [ ] **Step 1: Write the failing tests first**

Open `tests/ONEVO.Tests.Unit/Features/DevPlatform/Tenancy/DevSmokeTestTenantSeederTests.cs`.

First, add a lookup-data seeding helper (mirrors the existing `SeedPermissionsAsync` pattern — the real host runs `LookupDataSeeder` before `DevSmokeTestTenantSeeder`, so the test must reproduce that ordering) and update `RunSeederAsync` to call it:

```csharp
using ONEVO.Domain.Lookups;
```

```csharp
private static async Task SeedLookupDataAsync(ApplicationDbContext db)
{
    if (!await db.EmploymentTypes.AnyAsync())
    {
        db.EmploymentTypes.Add(new EmploymentType { Id = 1, Code = "full_time", Label = "Full-Time" });
    }
    if (!await db.EmploymentStatuses.AnyAsync())
    {
        db.EmploymentStatuses.Add(new EmploymentStatus { Id = 1, Code = "active", Label = "Active" });
    }
    if (!await db.WorkModes.AnyAsync())
    {
        db.WorkModes.Add(new WorkMode { Id = 1, Code = "on_site", Label = "On-Site" });
    }
    await db.SaveChangesAsync();
}

private static async Task RunSeederAsync(ApplicationDbContext db)
{
    await SeedPermissionsAsync(db);
    await SeedLookupDataAsync(db);
    var tenantContext = new TenantContextAccessor();
    await DevSmokeTestTenantSeeder.SeedAsync(
        db,
        tenantContext,
        CreatePasswordHasher().Object,
        new Mock<IEncryptionService>().Object,
        new ConfigurationBuilder().Build(),
        CancellationToken.None);
}
```

Replace the existing `SeedAsync_DoesNotCreateAnyEmployeeRowForWorkManager` test (it asserts the old, now-incorrect behavior) with the new employee-seeding test block below. Delete the old test entirely and add these in its place:

```csharp
[Fact]
public async Task SeedAsync_CreatesExactlyOneEmployeeRowPerSeededUser()
{
    using (var first = CreateContext())
    {
        await RunSeederAsync(first);
    }

    using var verify = CreateContext();
    var acmeTenant = await verify.Tenants.SingleAsync(t => t.Slug == "acme");
    var dapiTenant = await verify.Tenants.SingleAsync(t => t.Slug == "dapi");
    var seededUserIds = await verify.Users
        .Where(u => u.Email == AcmeOwnerEmail || u.Email == AcmeHrManagerEmail ||
                    u.Email == AcmeWorkManagerEmail || u.Email == DapiOwnerEmail)
        .Select(u => u.Id)
        .ToListAsync();

    var employees = await verify.Set<Employee>()
        .Where(e => seededUserIds.Contains(e.UserId))
        .ToListAsync();

    employees.Should().HaveCount(4);
    employees.Select(e => e.UserId).Should().OnlyHaveUniqueItems();
}

[Fact]
public async Task SeedAsync_AcmeOwnerGetsAcmeTechnologiesEmployeeNumberAcme0001()
{
    using var db = CreateContext();
    await RunSeederAsync(db);

    using var verify = CreateContext();
    var acmeTenant = await verify.Tenants.SingleAsync(t => t.Slug == "acme");
    var technologies = await verify.LegalEntities.SingleAsync(l => l.Name == "Acme Technologies");
    var owner = await verify.Users.SingleAsync(u => u.Email == AcmeOwnerEmail);
    var employee = await verify.Set<Employee>().SingleAsync(e => e.UserId == owner.Id);

    employee.TenantId.Should().Be(acmeTenant.Id);
    employee.LegalEntityId.Should().Be(technologies.Id);
    employee.EmployeeNumber.Should().Be("ACME-0001");
    employee.FirstName.Should().Be(owner.FirstName);
    employee.LastName.Should().Be(owner.LastName);
    employee.Email.Should().Be(owner.Email);
}

[Fact]
public async Task SeedAsync_AcmeHrManagerGetsAcmeTechnologiesEmployeeNumberAcme0002()
{
    using var db = CreateContext();
    await RunSeederAsync(db);

    using var verify = CreateContext();
    var technologies = await verify.LegalEntities.SingleAsync(l => l.Name == "Acme Technologies");
    var user = await verify.Users.SingleAsync(u => u.Email == AcmeHrManagerEmail);
    var employee = await verify.Set<Employee>().SingleAsync(e => e.UserId == user.Id);

    employee.LegalEntityId.Should().Be(technologies.Id);
    employee.EmployeeNumber.Should().Be("ACME-0002");
}

[Fact]
public async Task SeedAsync_AcmeWorkManagerHasExactlyOneEmployeeRowUnderAcmeSolutions()
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
    var solutions = await verify.LegalEntities.SingleAsync(l => l.Name == "Acme Solutions");
    var user = await verify.Users.SingleAsync(u => u.Email == AcmeWorkManagerEmail);
    var employees = await verify.Set<Employee>().Where(e => e.UserId == user.Id).ToListAsync();

    employees.Should().ContainSingle();
    employees[0].LegalEntityId.Should().Be(solutions.Id);
    employees[0].EmployeeNumber.Should().Be("ACME-0003");
}

[Fact]
public async Task SeedAsync_DapiOwnerGetsDapiLegalEntityEmployeeNumberDapi0001()
{
    using var db = CreateContext();
    await RunSeederAsync(db);

    using var verify = CreateContext();
    var dapiTenant = await verify.Tenants.SingleAsync(t => t.Slug == "dapi");
    var dapiLegalEntity = await verify.LegalEntities.SingleAsync(l => l.TenantId == dapiTenant.Id);
    var owner = await verify.Users.SingleAsync(u => u.Email == DapiOwnerEmail);
    var employee = await verify.Set<Employee>().SingleAsync(e => e.UserId == owner.Id);

    employee.TenantId.Should().Be(dapiTenant.Id);
    employee.LegalEntityId.Should().Be(dapiLegalEntity.Id);
    employee.EmployeeNumber.Should().Be("DAPI-0001");
}

[Fact]
public async Task SeedAsync_NoEmployeeRowIsCreatedForAUserInTheWrongTenant()
{
    using var db = CreateContext();
    await RunSeederAsync(db);

    using var verify = CreateContext();
    var dapiTenant = await verify.Tenants.SingleAsync(t => t.Slug == "dapi");
    var acmeOwner = await verify.Users.SingleAsync(u => u.Email == AcmeOwnerEmail);
    var employee = await verify.Set<Employee>().SingleAsync(e => e.UserId == acmeOwner.Id);

    employee.TenantId.Should().NotBe(dapiTenant.Id);
}

[Fact]
public async Task SeedAsync_IsIdempotentAcrossRepeatedRunsForEmployees()
{
    using (var first = CreateContext())
    {
        await RunSeederAsync(first);
    }
    using (var second = CreateContext())
    {
        await RunSeederAsync(second);
    }
    using (var third = CreateContext())
    {
        await RunSeederAsync(third);
    }

    using var verify = CreateContext();
    (await verify.Set<Employee>().CountAsync()).Should().Be(4);
}

[Fact]
public async Task SeedAsync_EmployeeNumbersAreUniquePerTenant()
{
    using var db = CreateContext();
    await RunSeederAsync(db);

    using var verify = CreateContext();
    var acmeTenant = await verify.Tenants.SingleAsync(t => t.Slug == "acme");
    var numbers = await verify.Set<Employee>()
        .Where(e => e.TenantId == acmeTenant.Id)
        .Select(e => e.EmployeeNumber)
        .ToListAsync();

    numbers.Should().OnlyHaveUniqueItems();
    numbers.Should().BeEquivalentTo(["ACME-0001", "ACME-0002", "ACME-0003"]);
}

[Fact]
public async Task SeedAsync_ReusesExistingEmployeeRowInsteadOfDuplicatingOnRerun()
{
    using (var first = CreateContext())
    {
        await RunSeederAsync(first);
    }

    Guid employeeId;
    using (var mid = CreateContext())
    {
        var owner = await mid.Users.SingleAsync(u => u.Email == AcmeOwnerEmail);
        employeeId = (await mid.Set<Employee>().SingleAsync(e => e.UserId == owner.Id)).Id;
    }

    using (var second = CreateContext())
    {
        await RunSeederAsync(second);
    }

    using var verify = CreateContext();
    var ownerAfter = await verify.Users.SingleAsync(u => u.Email == AcmeOwnerEmail);
    var employeeAfter = await verify.Set<Employee>().SingleAsync(e => e.UserId == ownerAfter.Id);

    employeeAfter.Id.Should().Be(employeeId);
}

[Fact]
public async Task SeedAsync_ThrowsWhenEmployeeNumberCollidesWithADifferentUserInTheSameTenant()
{
    using var db = CreateContext();
    await SeedPermissionsAsync(db);
    await SeedLookupDataAsync(db);

    var tenantContext = new TenantContextAccessor();

    // Seed tenants/users/legal entities first via one normal pass so the tenant + user rows
    // exist, then hand-plant a conflicting employee_number under a foreign UserId before the
    // *next* pass tries to seed the real ACME-0001 row for the real owner - this simulates a
    // dirty dev database rather than a fresh one.
    await DevSmokeTestTenantSeeder.SeedAsync(
        db, tenantContext, CreatePasswordHasher().Object,
        new Mock<IEncryptionService>().Object, new ConfigurationBuilder().Build(), CancellationToken.None);

    var acmeTenant = await db.Tenants.SingleAsync(t => t.Slug == "acme");
    var owner = await db.Users.SingleAsync(u => u.Email == AcmeOwnerEmail);
    var conflictingEmployee = await db.Set<Employee>().SingleAsync(e => e.UserId == owner.Id);

    // Repoint the existing ACME-0001 row onto a different, unrelated user id so the next
    // seeder pass sees "employee_number ACME-0001 belongs to someone else."
    conflictingEmployee.UserId = Guid.NewGuid();
    await db.SaveChangesAsync();

    var act = () => DevSmokeTestTenantSeeder.SeedAsync(
        db, tenantContext, CreatePasswordHasher().Object,
        new Mock<IEncryptionService>().Object, new ConfigurationBuilder().Build(), CancellationToken.None);

    await act.Should().ThrowAsync<InvalidOperationException>()
        .WithMessage("*ACME-0001*");
}

[Fact]
public void EmployeeEntity_UserIdIndex_IsUnique()
{
    using var db = CreateContext();
    var entityType = db.Model.FindEntityType(typeof(Employee))!;
    var index = entityType.GetIndexes()
        .Single(i => i.Properties.Select(p => p.Name).SequenceEqual(["UserId"]));

    index.IsUnique.Should().BeTrue();
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --no-restore --filter "FullyQualifiedName~DevSmokeTestTenantSeederTests" --verbosity minimal`

Expected: Compile error or failures — `Employee` rows don't exist yet, `SeedAsync_DoesNotCreateAnyEmployeeRowForWorkManager` no longer exists (already removed), new assertions fail because no employees are created.

- [ ] **Step 3: Implement the minimal seeder change**

In `src/ONEVO.Infrastructure/Persistence/Seeders/DevSmokeTestTenantSeeder.cs`:

Add the using statement near the top (alongside the other `ONEVO.Domain.Features.*` usings):

```csharp
using ONEVO.Domain.Features.CoreHr.Entities;
```

Add a new record next to `SmokeLegalEntityDefinition` (around line 44):

```csharp
    private sealed record SmokeEmployeeDefinition(
        Guid UserId,
        Guid LegalEntityId,
        string EmployeeNumber);
```

Add `Employees` to `SmokeTenantDefinition` (around line 46-52):

```csharp
    private sealed record SmokeTenantDefinition(
        Guid TenantId,
        string Slug,
        string Name,
        Guid SubscriptionId,
        IReadOnlyList<SmokeUserDefinition> Users,
        IReadOnlyList<SmokeLegalEntityDefinition> LegalEntities,
        IReadOnlyList<SmokeEmployeeDefinition> Employees);
```

Add three constants near the other `private const` declarations (around line 76-77):

```csharp
    private const int SmokeDefaultEmploymentTypeId = 1;   // "full_time" — seeded by LookupDataSeeder
    private const int SmokeDefaultEmploymentStatusId = 1; // "active"    — seeded by LookupDataSeeder
    private const int SmokeDefaultWorkModeId = 1;          // "on_site"   — seeded by LookupDataSeeder
```

And a stable hire date next to `SmokeUserPassword`:

```csharp
    private static readonly DateOnly SmokeEmployeeHireDate = new(2025, 1, 1);
```

Add employee numbers as constants next to the email constants (around line 79-82):

```csharp
    private const string AcmeOwnerEmployeeNumber = "ACME-0001";
    private const string AcmeHrManagerEmployeeNumber = "ACME-0002";
    private const string AcmeWorkManagerEmployeeNumber = "ACME-0003";
    private const string DapiOwnerEmployeeNumber = "DAPI-0001";
```

Update `BuildTenantDefinitions()` to pass the new `Employees` list for each tenant (add the 7th positional argument to both `SmokeTenantDefinition` constructions):

```csharp
    private static IReadOnlyList<SmokeTenantDefinition> BuildTenantDefinitions()
    {
        return
        [
            new SmokeTenantDefinition(
                AcmeTenantId,
                AcmeSlug,
                AcmeTenantName,
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
                ],
                [
                    new SmokeEmployeeDefinition(AcmeOwnerUserId, AcmeLegalEntityTechnologiesId, AcmeOwnerEmployeeNumber),
                    new SmokeEmployeeDefinition(AcmeHrManagerUserId, AcmeLegalEntityTechnologiesId, AcmeHrManagerEmployeeNumber),
                    new SmokeEmployeeDefinition(AcmeWorkManagerUserId, AcmeLegalEntitySolutionsId, AcmeWorkManagerEmployeeNumber)
                ]),
            new SmokeTenantDefinition(
                DapiTenantId,
                DapiSlug,
                DapiTenantName,
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
                ],
                [
                    new SmokeEmployeeDefinition(DapiOwnerUserId, DapiLegalEntityId, DapiOwnerEmployeeNumber)
                ])
        ];
    }
```

Wire employee seeding into the main per-tenant loop in `SeedAsync` (around line 176-182). Replace:

```csharp
            User? firstUser = null;
            foreach (var userDefinition in tenantDefinition.Users)
            {
                var user = await SeedTenantUserAsync(db, tenant.Id, userDefinition, passwordHasher, now, ct);
                firstUser ??= user;
                await SeedTenantRoleAsync(db, tenant.Id, user.Id, userDefinition, now, ct);
            }
```

with:

```csharp
            await EnsureSmokeEmployeeReferenceDataAsync(db, ct);

            User? firstUser = null;
            foreach (var userDefinition in tenantDefinition.Users)
            {
                var user = await SeedTenantUserAsync(db, tenant.Id, userDefinition, passwordHasher, now, ct);
                firstUser ??= user;
                await SeedTenantRoleAsync(db, tenant.Id, user.Id, userDefinition, now, ct);

                var employeeDefinition = tenantDefinition.Employees
                    .FirstOrDefault(e => e.UserId == user.Id);
                if (employeeDefinition is not null)
                {
                    await SeedTenantEmployeeAsync(db, tenant.Id, user, employeeDefinition, now, ct);
                }
            }
```

Add the two new private static methods (place them after `SeedTenantRoleAsync`/`ResolveRolePermissionsAsync`, before `SeedTenantLegalEntitiesAsync` — i.e. after line 545):

```csharp
    private static async Task EnsureSmokeEmployeeReferenceDataAsync(
        ApplicationDbContext db,
        CancellationToken ct)
    {
        // LookupDataSeeder (DependencyInjection.cs) is registered and runs before
        // DevSmokeTestTenantSeeder in the hosted-service startup order, seeding fixed
        // Id=1 rows for employment_types("full_time"), employment_statuses("active"), and
        // work_modes("on_site"). This check turns a broken startup order into a clear failure
        // instead of silently writing Employee rows with dangling lookup ids.
        var typeOk = await db.EmploymentTypes.AnyAsync(t => t.Id == SmokeDefaultEmploymentTypeId, ct);
        var statusOk = await db.EmploymentStatuses.AnyAsync(s => s.Id == SmokeDefaultEmploymentStatusId, ct);
        var workModeOk = await db.WorkModes.AnyAsync(w => w.Id == SmokeDefaultWorkModeId, ct);

        if (!typeOk || !statusOk || !workModeOk)
        {
            throw new InvalidOperationException(
                "Development smoke-test seeder requires employment_types/employment_statuses/work_modes " +
                $"to already contain Id={SmokeDefaultEmploymentTypeId} rows (LookupDataSeeder must run " +
                "before DevSmokeTestTenantSeeder). Refusing to seed Employee rows with dangling lookup ids.");
        }
    }

    private static async Task SeedTenantEmployeeAsync(
        ApplicationDbContext db,
        Guid tenantId,
        User user,
        SmokeEmployeeDefinition definition,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var numberConflict = await db.Employees.FirstOrDefaultAsync(
            e => e.TenantId == tenantId &&
                 e.EmployeeNumber == definition.EmployeeNumber &&
                 e.UserId != user.Id,
            ct);
        if (numberConflict is not null)
        {
            throw new InvalidOperationException(
                $"Development smoke-test seeder found employee_number '{definition.EmployeeNumber}' " +
                $"in tenant {tenantId} already assigned to user {numberConflict.UserId}, but it is " +
                $"expected for user {user.Id}. The development database is in an inconsistent state " +
                "and must be reconciled manually before re-seeding.");
        }

        var employee = await db.Employees.FirstOrDefaultAsync(e => e.UserId == user.Id, ct);
        if (employee is null)
        {
            db.Employees.Add(new Employee
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                UserId = user.Id,
                EmployeeNumber = definition.EmployeeNumber,
                FirstName = user.FirstName,
                LastName = user.LastName,
                Email = user.Email,
                LegalEntityId = definition.LegalEntityId,
                EmploymentTypeId = SmokeDefaultEmploymentTypeId,
                EmploymentStatusId = SmokeDefaultEmploymentStatusId,
                WorkModeId = SmokeDefaultWorkModeId,
                HireDate = SmokeEmployeeHireDate,
                CreatedAt = now,
                CreatedById = user.Id
            });
            return;
        }

        employee.EmployeeNumber = definition.EmployeeNumber;
        employee.FirstName = user.FirstName;
        employee.LastName = user.LastName;
        employee.Email = user.Email;
        employee.LegalEntityId = definition.LegalEntityId;
        employee.EmploymentTypeId = SmokeDefaultEmploymentTypeId;
        employee.EmploymentStatusId = SmokeDefaultEmploymentStatusId;
        employee.WorkModeId = SmokeDefaultWorkModeId;
        employee.UpdatedAt = now;
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --no-restore --filter "FullyQualifiedName~DevSmokeTestTenantSeederTests" --verbosity minimal`

Expected: All tests pass, including the new ones and all pre-existing ones (legal entities, permissions, idempotency, global email directory).

- [ ] **Step 5: Run the full unit and architecture suites**

Run:
```
dotnet build src\ONEVO.Api\ONEVO.Api.csproj --no-restore --verbosity minimal
dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --no-restore --no-build --verbosity minimal
dotnet test tests\ONEVO.Tests.Architecture\ONEVO.Tests.Architecture.csproj --no-restore --no-build --verbosity minimal
```

Expected: build succeeds; both suites are fully green (no regressions in unrelated tests). The architecture tests in `DevSmokeTestTenantSeederArchitectureTests.cs` must still pass unmodified — the substring-ordering assertions they check (`SetAdminMode` before `SeedAsync`, `ResolveSmokeTenantContext` before `SeedTenantUserAsync`, no RLS-bypass strings) are untouched by this change since employee seeding is inserted *after* those checkpoints, inside the already-tenant-resolved loop.

- [ ] **Step 6: `git diff --check` and ASCII scan on touched files**

Run:
```
git diff --check
```

For the ASCII scan, run this PowerShell one-liner against the two touched files and confirm no output (no non-ASCII bytes):
```powershell
Get-Content -Raw "src\ONEVO.Infrastructure\Persistence\Seeders\DevSmokeTestTenantSeeder.cs" | Select-String -Pattern '[^\x00-\x7F]'
Get-Content -Raw "tests\ONEVO.Tests.Unit\Features\DevPlatform\Tenancy\DevSmokeTestTenantSeederTests.cs" | Select-String -Pattern '[^\x00-\x7F]'
```

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Infrastructure/Persistence/Seeders/DevSmokeTestTenantSeeder.cs tests/ONEVO.Tests.Unit/Features/DevPlatform/Tenancy/DevSmokeTestTenantSeederTests.cs
git commit -m "feat: seed one Employee row per dev-smoke user"
```

(Only run this step if the user has asked for a commit — the task instructions say not to commit or push.)

---

### Task 2: Manual DB verification query (if Docker/local Postgres available)

**Files:** none (verification only, no code changes)

**Interfaces:** none

- [ ] **Step 1: Start the app locally against a migrated dev database**

Follow the repo's existing local-db setup script if present (`setup-local-db.ps1`), applying migrations, then run the API in Development so `DevSmokeTestTenantSeeder` executes.

- [ ] **Step 2: Run the verification query**

```sql
SELECT
    t.slug,
    u.email,
    e.employee_number,
    e.first_name,
    e.last_name,
    le.name AS legal_entity_name
FROM employees e
JOIN users u ON u.id = e.user_id
JOIN tenants t ON t.id = e.tenant_id
LEFT JOIN legal_entities le ON le.id = e.legal_entity_id
ORDER BY t.slug, e.employee_number;
```

Expected 4 rows (order may vary by `employee_number` ascending within each tenant):
```
acme | siyasiyamala932@gmail.com      | ACME-0001 | Acme | Owner        | Acme Technologies
acme | paramanathanmuthaiya@gmail.com | ACME-0002 | Acme | HR Manager   | Acme Technologies
acme | mrt15473@gmail.com             | ACME-0003 | Acme | Work Manager | Acme Solutions
dapi | dapiyshanth1908@gmail.com      | DAPI-0001 | Dapi | Owner        | Dapi Technologies
```

If Docker/local Postgres is unavailable, skip this task and note it as unverified in the report (Task 3 covers documenting this explicitly).

---

### Task 3: Write the reconciliation report

**Files:**
- Create: `DEV_SMOKE_EMPLOYEE_SEED_RECONCILIATION_REPORT.md` (repo root of `HRMS-Backend-v1`)

**Interfaces:** none — pure documentation, written after Task 1 (and Task 2 if it ran) are complete so the report reflects actual verified results, not planned ones.

- [ ] **Step 1: Write the report**

Include these sections, filled in with the *actual* results from Task 1 Step 5 and Task 2 (or their absence):

- **Why users and employees are separate** — `User` is an auth/identity row (login credentials, tenant membership); `Employee` is an HR-domain profile row (`employee_number`, `hire_date`, legal entity, employment type/status/work mode) referenced by later Core HR features (department/position assignment, leave, attendance). One user can exist without ever becoming an employee (e.g. a pure platform/API integration account); `employees.user_id` is unique so a user can have at most one employee row.
- **Confirmation `employees` is Phase 1** — cite the schema evidence found (unique indexes already present in `ApplicationDbContextModelSnapshot.cs`, `employees` already RLS-protected in `20260515022320_AddRlsPolicies.cs:17`).
- **Exact rows seeded** — the 4 rows and their employee numbers.
- **Exact legal entity mapping** — Acme Owner/HR Manager → Acme Technologies; Acme Work Manager → Acme Solutions; Dapi Owner → Dapi Technologies (Dapi's only/primary legal entity).
- **RLS/tenant-context approach** — employee seeding runs inside the existing per-tenant loop, after `ResolveSmokeTenantContext(tenantContext, tenant)` has already switched out of admin mode into the target tenant's resolved context (same context under which `SeedTenantUserAsync`/`SeedTenantRoleAsync` already write), so RLS sees writes under the correct tenant.
- **Idempotency behavior** — matched by `UserId` first; smoke-controlled fields (name/email/employee number/legal entity/employment type/status/work mode) are refreshed on rerun; a `TenantId`+`EmployeeNumber` collision against a *different* `UserId` throws `InvalidOperationException` instead of silently corrupting data.
- **What was intentionally not built** — Employee API/controllers, invite flow, department assignment UI/API, position assignment, employee hierarchy closure, multi-legal-entity authority, new tables, new auth behavior, and no production bootstrap changes.
- **Verification commands and results** — paste the actual `dotnet build`/`dotnet test` output summary (pass/fail counts) from Task 1 Step 5, the `git diff --check` result, the ASCII scan result, and the Task 2 query result if it ran (or "not run — Docker/Postgres unavailable in this environment" if it didn't).
- **Remaining gaps** — `position_assignments` and multi-legal-entity authority are deferred; a user can only ever get exactly one `Employee` row via this seeder, tied to a single legal entity, by design (per requirement #6 of the task).

- [ ] **Step 2: Confirm the file is ASCII-clean**

```powershell
Get-Content -Raw "DEV_SMOKE_EMPLOYEE_SEED_RECONCILIATION_REPORT.md" | Select-String -Pattern '[^\x00-\x7F]'
```

Expected: no output.

---

## Self-Review Notes

- Spec coverage: A (definitions + 4 rows) → Task 1 Step 3; B (reference-data safety, documented Id=1 guarantee) → Task 1 Step 3 (`EnsureSmokeEmployeeReferenceDataAsync`) + Task 3; C (idempotency incl. number-collision failure) → Task 1 Step 3 (`SeedTenantEmployeeAsync`) + tests; D (RLS correctness) → Task 1 Step 3 (wired inside existing resolved-context loop) + Task 3; E (no overbuild) → Global Constraints + Task 3; F (tests) → Task 1 Steps 1-2, covers every bullet including the EF metadata unique-index assertion; G (verification commands) → Task 1 Steps 5-6; H (manual query) → Task 2; I (report) → Task 3.
- No placeholders: every step has literal code or literal commands.
- Type consistency: `SeedTenantEmployeeAsync` signature (`ApplicationDbContext db, Guid tenantId, User user, SmokeEmployeeDefinition definition, DateTimeOffset now, CancellationToken ct`) matches its call site in the modified loop; `SmokeEmployeeDefinition` fields (`UserId`, `LegalEntityId`, `EmployeeNumber`) match both the `BuildTenantDefinitions()` construction and the lookup (`tenantDefinition.Employees.FirstOrDefault(e => e.UserId == user.Id)`) and the entity assignment (`definition.LegalEntityId`, `definition.EmployeeNumber`).
