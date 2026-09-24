# Remove Legacy Employee JobTitleId/ManagerId Fields Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Retire `Employee.JobTitleId`/`Employee.ManagerId` (and their `employees.job_title_id`/`employees.manager_id` columns) so Core HR cannot accidentally build on the deprecated job-title/manager surface, leaving reporting exclusively to the future `positions`/`position_assignments` model.

**Architecture:** Two-file domain/EF-config edit (`Employee.cs`, `EmployeeConfiguration.cs`), one EF Core migration generated via `dotnet ef migrations add` (not hand-written) that drops the self-referencing FK/index and the two columns, plus architecture/unit test coverage proving the fields are gone and the dev smoke seeder never references them.

**Tech Stack:** .NET 10 / EF Core 10 (Npgsql), xUnit + FluentAssertions + Moq, SQLite in-memory for seeder unit tests, PostgreSQL via Docker for integration tests.

## Global Constraints

- Work only inside `C:\onevoNew\HRMS-Backend-v1`. Do not touch OneVo-HR docs, frontend, Postman, or unrelated auth/system-config/legal-entity/department/position behavior.
- Do not edit any existing migration file. Only add a new one.
- Do not create a `job_titles` table. Do not add any replacement "position" field on `Employee`.
- Do not touch `employees.user_id`, `tenant_id`, `legal_entity_id`, `department_id`, `employee_number`, employment type/status/work mode fields, or the `employees` table itself.
- Preserve table name `employees`, primary key, `TenantId`, `UserId` unique index, `(TenantId, EmployeeNumber)` unique index, all other required-field configuration.
- Do not commit or push.

## Audit Findings (already confirmed — do not re-run unless something looks off)

- Non-migration references to `JobTitleId`/`ManagerId`/`job_title_id`/`manager_id` exist in exactly two production files: `src/ONEVO.Domain/Features/CoreHr/Employee/Entities/Employee.cs` and `src/ONEVO.Infrastructure/Persistence/Configurations/CoreHr/Employee/EmployeeConfiguration.cs`. No Application-layer feature, controller, or test references either field.
- `src/ONEVO.Infrastructure/Persistence/Seeders/DevSmokeTestTenantSeeder.cs` already never sets `JobTitleId` or `ManagerId` (confirmed by reading the current file, which already has uncommitted changes from a prior session unrelated to this task — do not revert those). It already seeds exactly 4 employees (3 Acme + 1 Dapi), one per smoke user, and leaves `DepartmentId` unset (null) — no change needed there. Task 3 below only *adds test coverage* proving this, it does not change seeder behavior.
- Latest migration on disk is `20260804102821_AddPositionFoundationSchema`. The new migration's timestamp must sort after it.
- Docker is available locally, so the Docker-gated integration test step in Task 5 must actually run, not be skipped.

---

### Task 1: Remove the fields from the domain entity and EF configuration

**Files:**
- Modify: `src/ONEVO.Domain/Features/CoreHr/Employee/Entities/Employee.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Configurations/CoreHr/Employee/EmployeeConfiguration.cs`
- Test: `tests/ONEVO.Tests.Architecture/EmployeeLegacyFieldRetirementArchitectureTests.cs` (new)

**Interfaces:**
- Produces: `Employee` (in `ONEVO.Domain.Features.CoreHr.Entities`) with no `JobTitleId`/`ManagerId` properties — later tasks (migration generation, seeder tests) rely on the model no longer declaring these properties.

- [ ] **Step 1: Write the failing architecture test**

Create `tests/ONEVO.Tests.Architecture/EmployeeLegacyFieldRetirementArchitectureTests.cs`:

```csharp
using System.Reflection;
using Xunit;

namespace ONEVO.Tests.Architecture;

/// <summary>
/// Guards the retirement of Employee.JobTitleId/Employee.ManagerId documented in
/// EMPLOYEE_POSITION_MODEL_RECONCILIATION_REPORT.md. Reporting belongs to
/// positions/position_assignments (deferred), never to employees.manager_id, and job titles
/// are not modeled on Employee at all (no job_titles table exists).
/// </summary>
public sealed class EmployeeLegacyFieldRetirementArchitectureTests
{
    private static readonly Assembly DomainAssembly =
        typeof(ONEVO.Domain.Features.CoreHr.Entities.Employee).Assembly;

    private static readonly Assembly ApplicationAssembly =
        typeof(ONEVO.Application.Common.Models.Result).Assembly;

    [Fact]
    public void EmployeeEntity_HasNoJobTitleIdProperty()
    {
        var property = typeof(ONEVO.Domain.Features.CoreHr.Entities.Employee)
            .GetProperty("JobTitleId");

        Assert.Null(property);
    }

    [Fact]
    public void EmployeeEntity_HasNoManagerIdProperty()
    {
        var property = typeof(ONEVO.Domain.Features.CoreHr.Entities.Employee)
            .GetProperty("ManagerId");

        Assert.Null(property);
    }

    [Fact]
    public void EmployeeConfiguration_NoLongerConfiguresManagerSelfForeignKey()
    {
        var source = ReadSourceRelativeToRepoRoot(
            "src", "ONEVO.Infrastructure", "Persistence", "Configurations",
            "CoreHr", "Employee", "EmployeeConfiguration.cs");

        Assert.DoesNotContain("ManagerId", source, StringComparison.Ordinal);
        Assert.DoesNotContain("JobTitleId", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DomainAssembly_HasNoJobTitleType()
    {
        var offenders = DomainAssembly.GetTypes()
            .Where(t => t.Name.Contains("JobTitle", StringComparison.Ordinal))
            .Select(t => t.FullName)
            .ToList();

        Assert.True(offenders.Count == 0,
            "No JobTitle entity/type may exist (job_titles table is intentionally not modeled): " +
            string.Join(", ", offenders));
    }

    [Fact]
    public void NoApplicationFeature_ReferencesEmployeeManagerIdOrJobTitleId()
    {
        var sourceRoot = FindRepositoryPath("src", "ONEVO.Application");
        var offenders = Directory.GetFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path =>
            {
                var text = File.ReadAllText(path);
                return text.Contains("ManagerId", StringComparison.Ordinal) ||
                       text.Contains("JobTitleId", StringComparison.Ordinal);
            })
            .ToList();

        Assert.True(offenders.Count == 0,
            "No Application feature may reference Employee.ManagerId/JobTitleId (retired): " +
            string.Join("; ", offenders));

        // Touch the assembly reference so the field stays exercised if this test is ever trimmed.
        Assert.NotNull(ApplicationAssembly);
    }

    private static string ReadSourceRelativeToRepoRoot(params string[] segments) =>
        File.ReadAllText(FindRepositoryPath(segments));

    private static string FindRepositoryPath(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var repositoryMarker = Path.Combine(directory.FullName, "src", "ONEVO.Api");
            if (Directory.Exists(repositoryMarker))
            {
                return Path.Combine([directory.FullName, .. segments]);
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
```

- [ ] **Step 2: Run the test to confirm it currently fails**

Run: `dotnet test tests\ONEVO.Tests.Architecture\ONEVO.Tests.Architecture.csproj --no-restore --filter EmployeeLegacyFieldRetirementArchitectureTests --verbosity minimal`
Expected: FAIL — `EmployeeEntity_HasNoJobTitleIdProperty`, `EmployeeEntity_HasNoManagerIdProperty`, and `EmployeeConfiguration_NoLongerConfiguresManagerSelfForeignKey` fail because the properties/config still exist.

- [ ] **Step 3: Remove the properties from `Employee.cs`**

In `src/ONEVO.Domain/Features/CoreHr/Employee/Entities/Employee.cs`, delete these two lines (currently lines 17-18):

```csharp
    public Guid? JobTitleId { get; set; }
    public Guid? ManagerId { get; set; }
```

Leave every other property untouched (`DepartmentId`, `LegalEntityId`, employment type/status/work-mode ids, etc. all stay).

- [ ] **Step 4: Remove the self-referencing FK from `EmployeeConfiguration.cs`**

In `src/ONEVO.Infrastructure/Persistence/Configurations/CoreHr/Employee/EmployeeConfiguration.cs`, delete this block (currently lines 23-28, including the blank line before it):

```csharp

        builder.HasOne<EmployeeEntity>()
            .WithMany()
            .HasForeignKey(e => e.ManagerId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);
```

The file should read straight from the `WorkModeId` property config to the two `HasIndex` calls, with no `JobTitleId`/`ManagerId` mapping anywhere.

- [ ] **Step 5: Run the test again to verify it passes**

Run: `dotnet test tests\ONEVO.Tests.Architecture\ONEVO.Tests.Architecture.csproj --no-restore --filter EmployeeLegacyFieldRetirementArchitectureTests --verbosity minimal`
Expected: PASS (all 5 facts)

- [ ] **Step 6: Confirm the solution still builds**

Run: `dotnet build src\ONEVO.Api\ONEVO.Api.csproj --no-restore --verbosity minimal`
Expected: Build succeeds. If it doesn't restore cleanly, run `dotnet build src\ONEVO.Api\ONEVO.Api.csproj --verbosity minimal` once (with restore) first, then re-run with `--no-restore` for the rest of the plan.

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Domain/Features/CoreHr/Employee/Entities/Employee.cs src/ONEVO.Infrastructure/Persistence/Configurations/CoreHr/Employee/EmployeeConfiguration.cs tests/ONEVO.Tests.Architecture/EmployeeLegacyFieldRetirementArchitectureTests.cs
git commit -m "refactor: retire Employee.JobTitleId and Employee.ManagerId from domain model"
```

---

### Task 2: Generate and verify the EF Core migration

**Files:**
- Create (via `dotnet ef migrations add`, not by hand): `src/ONEVO.Infrastructure/Migrations/<timestamp>_RemoveLegacyEmployeeJobTitleAndManagerFields.cs`
- Create (via tooling): `src/ONEVO.Infrastructure/Migrations/<timestamp>_RemoveLegacyEmployeeJobTitleAndManagerFields.Designer.cs`
- Modify (via tooling): `src/ONEVO.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs`

**Interfaces:**
- Consumes: the updated `Employee` entity/`EmployeeConfiguration` from Task 1 — EF diffs the new model against the snapshot to produce this migration.
- Produces: a migration whose `Up()` drops `employees.job_title_id`, `employees.manager_id`, and the manager self-FK/index, runnable standalone against a fresh database via `dotnet ef database update` or `dotnet ef migrations script`.

- [ ] **Step 1: Generate the migration with EF tooling**

Run from the repo root (`C:\onevoNew\HRMS-Backend-v1`):

```bash
dotnet ef migrations add RemoveLegacyEmployeeJobTitleAndManagerFields --project src\ONEVO.Infrastructure --startup-project src\ONEVO.Api
```

Expected: Command succeeds and reports the new migration files created under `src/ONEVO.Infrastructure/Migrations/`.

- [ ] **Step 2: Read the generated migration and confirm its shape**

Open the new `<timestamp>_RemoveLegacyEmployeeJobTitleAndManagerFields.cs`. Confirm:
- `Up()` contains a `migrationBuilder.DropForeignKey(...)` for the manager self-FK (only if EF detected an index/FK on `manager_id` — confirm by reading the generated code, since the exact generated member/constraint names come from EF's own naming, not something to guess up front).
- `Up()` contains `migrationBuilder.DropColumn(name: "job_title_id", table: "employees")` and `migrationBuilder.DropColumn(name: "manager_id", table: "employees")`.
- `Down()` re-adds both columns (and the FK/index, if one was dropped), so the migration is reversible.
- No other table's columns are touched.

If the shape doesn't match (e.g., it tries to touch other columns, or is missing a drop), stop and re-examine Task 1's edits before proceeding — do not hand-edit the generated migration to force it into shape.

- [ ] **Step 3: Confirm `ApplicationDbContextModelSnapshot.cs` no longer declares the two properties**

Run: `grep -n "JobTitleId\|ManagerId\|job_title_id\|manager_id" src\ONEVO.Infrastructure\Migrations\ApplicationDbContextModelSnapshot.cs`
Expected: No output (EF tooling already regenerated the snapshot in Step 1; this just confirms it).

- [ ] **Step 4: Confirm no old migration file was touched**

Run: `git status --short src\ONEVO.Infrastructure\Migrations\`
Expected: Only the two new migration files show as untracked (`??`), and `ApplicationDbContextModelSnapshot.cs` shows as modified (`M`). No previously-existing `*.cs`/`*.Designer.cs` migration file appears as modified.

- [ ] **Step 5: Generate and sanity-check the full migration script**

Run: `dotnet ef migrations script --project src\ONEVO.Infrastructure --startup-project src\ONEVO.Api --idempotent -o out-migrations-script.sql`

Then inspect the tail of `out-migrations-script.sql` for the `RemoveLegacyEmployeeJobTitleAndManagerFields` block and confirm it contains `DROP COLUMN` statements for `job_title_id` and `manager_id` on `employees`. Delete `out-migrations-script.sql` afterward (it's a scratch artifact, not a repo file to commit).

- [ ] **Step 6: Rebuild to confirm everything still compiles with the new migration in the tree**

Run: `dotnet build src\ONEVO.Api\ONEVO.Api.csproj --no-restore --verbosity minimal`
Expected: Build succeeds.

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Infrastructure/Migrations/
git commit -m "feat: add RemoveLegacyEmployeeJobTitleAndManagerFields migration"
```

---

### Task 3: Add seeder test coverage proving no legacy field usage

**Files:**
- Modify: `tests/ONEVO.Tests.Architecture/DevSmokeTestTenantSeederArchitectureTests.cs`
- Modify: `tests/ONEVO.Tests.Unit/Features/DevPlatform/Tenancy/DevSmokeTestTenantSeederTests.cs`

**Interfaces:**
- Consumes: `DevSmokeTestTenantSeeder.SeedAsync(...)` (unchanged signature) and `Employee` (no `JobTitleId`/`ManagerId` per Task 1).

No production behavior changes in this task — the seeder already seeds exactly 4 employees (one per smoke user: `AcmeOwnerUserId`, `AcmeHrManagerUserId`, `AcmeWorkManagerUserId`, `DapiOwnerUserId`), already leaves `DepartmentId` null, and already never sets `JobTitleId`/`ManagerId`. This task only adds the assertions the parent task asked for; `SeedAsync_CreatesExactlyOneEmployeeRowPerSeededUser`, `SeedAsync_EmployeeNumbersAreUniquePerTenant`, and `SeedAsync_IsIdempotentAcrossRepeatedRunsForEmployees` in the existing test file already cover "exactly 4 smoke employees", "no duplicate employee rows for same UserId", and "employee_number remains tenant-unique" — do not duplicate those, just add what's missing (source-text guard + department-null assertion).

- [ ] **Step 1: Write the failing architecture-level source guard**

Add this fact to `tests/ONEVO.Tests.Architecture/DevSmokeTestTenantSeederArchitectureTests.cs` (inside the existing `DevSmokeTestTenantSeederArchitectureTests` class, after `Seeder_RemainsDevelopmentOrTestOnly`):

```csharp
    [Fact]
    public void Seeder_NeverReferencesRetiredEmployeeJobTitleOrManagerFields()
    {
        var source = ReadSeederSource();

        Assert.DoesNotContain("JobTitleId", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ManagerId", source, StringComparison.Ordinal);
        Assert.DoesNotContain("job_title_id", source, StringComparison.Ordinal);
        Assert.DoesNotContain("manager_id", source, StringComparison.Ordinal);
    }
```

- [ ] **Step 2: Write the failing unit test for department nullability**

Add this fact to `tests/ONEVO.Tests.Unit/Features/DevPlatform/Tenancy/DevSmokeTestTenantSeederTests.cs` (after `SeedAsync_EmployeeNumbersAreUniquePerTenant`):

```csharp
    [Fact]
    public async Task SeedAsync_LeavesDepartmentIdNullForEverySmokeEmployee()
    {
        using var db = CreateContext();
        await RunSeederAsync(db);

        using var verify = CreateContext();
        var departmentIds = await verify.Set<Employee>().Select(e => e.DepartmentId).ToListAsync();

        departmentIds.Should().AllSatisfy(id => id.Should().BeNull());
    }
```

- [ ] **Step 3: Run both to confirm they currently pass** (this is verification-of-existing-behavior, not a bug fix — both should already be green since the seeder never touched these fields)

Run: `dotnet test tests\ONEVO.Tests.Architecture\ONEVO.Tests.Architecture.csproj --no-restore --filter Seeder_NeverReferencesRetiredEmployeeJobTitleOrManagerFields --verbosity minimal`
Run: `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --no-restore --filter SeedAsync_LeavesDepartmentIdNullForEverySmokeEmployee --verbosity minimal`
Expected: Both PASS immediately (confirming no seeder change was needed, per the audit).

- [ ] **Step 4: Commit**

```bash
git add tests/ONEVO.Tests.Architecture/DevSmokeTestTenantSeederArchitectureTests.cs tests/ONEVO.Tests.Unit/Features/DevPlatform/Tenancy/DevSmokeTestTenantSeederTests.cs
git commit -m "test: guard dev smoke seeder against retired Employee.JobTitleId/ManagerId fields"
```

---

### Task 4: Full verification sweep

**Files:** none (verification only)

- [ ] **Step 1: Full build**

Run: `dotnet build src\ONEVO.Api\ONEVO.Api.csproj --no-restore --verbosity minimal`
Expected: `Build succeeded.`

- [ ] **Step 2: Full unit test suite**

Run: `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --no-restore --verbosity minimal`
Expected: All tests pass, 0 failures.

- [ ] **Step 3: Full architecture test suite**

Run: `dotnet test tests\ONEVO.Tests.Architecture\ONEVO.Tests.Architecture.csproj --no-restore --verbosity minimal`
Expected: All tests pass, 0 failures.

- [ ] **Step 4: Migration script regenerates cleanly**

Run: `dotnet ef migrations script --project src\ONEVO.Infrastructure --startup-project src\ONEVO.Api`
Expected: Prints full SQL script with no errors, includes the `RemoveLegacyEmployeeJobTitleAndManagerFields` section.

- [ ] **Step 5: Whitespace check**

Run: `git diff --check`
Expected: No output (no trailing-whitespace/conflict-marker issues).

- [ ] **Step 6: Docker-backed integration tests (Docker confirmed available)**

Run: `dotnet test tests\ONEVO.Tests.Integration\ONEVO.Tests.Integration.csproj --no-restore --verbosity minimal --filter "FullyQualifiedName~CoreHr|FullyQualifiedName~Employee"`

If no tests match that filter (Employee has no dedicated integration test class today), instead run the full integration suite so migrations actually apply to real PostgreSQL:

Run: `dotnet test tests\ONEVO.Tests.Integration\ONEVO.Tests.Integration.csproj --no-restore --verbosity minimal`

Expected: All tests pass, confirming the new migration applies cleanly to a real PostgreSQL database via Testcontainers/Docker.

- [ ] **Step 7: Record results** (no commit — this task only gathers evidence for Task 5's report)

---

### Task 5: Write the reconciliation report

**Files:**
- Create: `EMPLOYEE_POSITION_MODEL_RECONCILIATION_REPORT.md` (repo root, alongside the other `*_REPORT.md` files already there)

- [ ] **Step 1: Write the report**

Create `EMPLOYEE_POSITION_MODEL_RECONCILIATION_REPORT.md` with these sections (fill in real values from Tasks 1-4's actual output — migration timestamp, exact test counts, exact command output — do not leave placeholders):

```markdown
# Employee/Position Model Reconciliation Report

## Why users and employees are separate

`users` is the login/security account (credentials, MFA, session). `employees` is the HR
person profile (name, employment dates, legal entity, department). A user can exist without
an employee profile being fully built out yet, and the two rows are joined 1:1 via
`employees.user_id` (unique index) rather than being the same table, so identity/auth concerns
never leak into HR profile concerns or vice versa.

## Why job_title_id and manager_id were removed

Both were placeholders from before the Position model existed. `job_title_id` implied a
`job_titles` lookup table that Phase 1 explicitly does not build (job title/family/level was
already removed from position setup - see the Phase 1 Position Fields Decision). `manager_id`
hard-coded a single flat reporting line directly on the employee row, which cannot represent
multiple concurrent assignments, position-based authority, or org changes over time.

## Proof that Phase 1 reporting belongs to positions/position_assignments, not employees.manager_id

- `positions` already carries `reports_to_position_id` (added in the
  `AddPositionFoundationSchema` migration) - reporting is modeled position-to-position, not
  person-to-person.
- No `position_assignments` or `employee_hierarchy_closure` table exists yet (deferred, listed
  below) - until they land, there is no supported reporting-line feature at all, which is why
  removing `employee.manager_id` does not regress any working feature (confirmed by the Task 1
  audit: no Application-layer code referenced it).

## Exact files changed

- `src/ONEVO.Domain/Features/CoreHr/Employee/Entities/Employee.cs` - removed `JobTitleId`,
  `ManagerId`.
- `src/ONEVO.Infrastructure/Persistence/Configurations/CoreHr/Employee/EmployeeConfiguration.cs`
  - removed the `ManagerId` self-referencing FK configuration.
- `src/ONEVO.Infrastructure/Migrations/<timestamp>_RemoveLegacyEmployeeJobTitleAndManagerFields.cs`
  and `.Designer.cs` (new) - drops `employees.job_title_id`, `employees.manager_id`, and the
  manager FK/index.
- `src/ONEVO.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs` - regenerated via
  EF tooling.
- `tests/ONEVO.Tests.Architecture/EmployeeLegacyFieldRetirementArchitectureTests.cs` (new).
- `tests/ONEVO.Tests.Architecture/DevSmokeTestTenantSeederArchitectureTests.cs` - added a
  source-text guard.
- `tests/ONEVO.Tests.Unit/Features/DevPlatform/Tenancy/DevSmokeTestTenantSeederTests.cs` -
  added a department-nullability assertion.

## Migration safety notes

- [fill in: was a FK/index actually present and dropped, per Task 2 Step 2's reading of the
  generated migration]
- `Down()` is reversible: re-adds both columns [and FK/index, if applicable].
- No other `employees` column, and no other table, is touched.
- No existing migration file was edited (`git status --short` confirmed in Task 2 Step 4).

## Test results

- [fill in real counts]: `dotnet test tests\ONEVO.Tests.Unit\...` - N passed, 0 failed.
- [fill in real counts]: `dotnet test tests\ONEVO.Tests.Architecture\...` - N passed, 0 failed.
- `dotnet ef migrations script` - succeeded, includes the new migration.
- `git diff --check` - clean.
- [fill in]: Docker-backed integration test run - N passed, 0 failed.

## Remaining deferred work

- `position_assignments` (employee-to-position seat assignment over time).
- `employee_hierarchy_closure` (materialized reporting-line closure table).
- Employee-to-position assignment APIs.
- Employee management/profile APIs.
```

- [ ] **Step 2: Commit**

```bash
git add EMPLOYEE_POSITION_MODEL_RECONCILIATION_REPORT.md
git commit -m "docs: add Employee/Position model reconciliation report"
```

(Per the parent task's explicit instruction, do not push any of these commits.)
