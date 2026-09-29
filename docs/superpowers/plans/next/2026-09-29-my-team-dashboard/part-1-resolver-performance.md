# My Team — Part 1: Visibility Resolver Performance

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `EmployeeAuthorityResolver.ResolveVisibilityAsync` issue a constant number of DB
round trips however much coverage a manager has, and expand coverage at most once per request.
Behavior does not change.

**Architecture:**
1. Replace the per-covered-position holder loop with the existing batch
   `GetActiveHoldersByPositionIdsAsync`.
2. Replace the per-covered-department recursive CTE loop with one multi-root CTE.
3. Add a request-scoped memo inside the scoped resolver.
   - It holds the actor employee and the permission-independent coverage expansion.
   - It is keyed by `(TenantId, ActorUserId, LegalEntityId)`.
   - The permission check is never memoized.

This part also adds the shared integration-test helpers every later backend part uses.

**Tech Stack:** .NET 10, EF Core 10 / Npgsql, xUnit, FluentAssertions, Testcontainers.

**Spec:** `docs/superpowers/specs/next/2026-09-29-my-team-dashboard-design.md` §13.1, §13.2, AC-12.

## Global Constraints

- No behavior change. The existing `EmployeeAuthorityResolverTests` (1,713 lines) must pass
  unmodified.
- `EmployeeAuthorityResolverArchitectureTests.EmployeeAuthorityResolver_NeverHardcodesAPermissionCode`
  must stay green. Never write a `"resource:action"` string literal in `EmployeeAuthorityResolver.cs`.
- Never memoize `UserHasPermissionCodeAsync`. Every call checks its own `RequiredPermission`.
- No cross-request cache. The memo is a private field of the scoped resolver instance
  (`AddScoped` in `src/ONEVO.Application/DependencyInjection.cs` L100).

---

### Task 1: Integration-test support: command counter and My Team DB helper

**Files:**
- Create: `tests/ONEVO.Tests.Integration/Support/CountingDbCommandInterceptor.cs`
- Create: `tests/ONEVO.Tests.Integration/MyTeam/MyTeamDb.cs`
- Test: `tests/ONEVO.Tests.Integration/MyTeam/MyTeamDbSmokeTests.cs`

**Interfaces:**
- Produces (used by Parts 1, 3, 4, 5, 6):
  - `CountingDbCommandInterceptor` with `int Count`, `void Reset()`.
  - `MyTeamDb`:
    - `static Task<MyTeamDb> CreateAsync()`
    - `Guid TenantId`, `Guid LegalEntityId`
    - `ApplicationDbContext NewContext(CountingDbCommandInterceptor? counter = null)`
    - seeding helpers:
      - `Employee AddEmployee(ApplicationDbContext db, Guid? departmentId = null, Guid? legalEntityId = null, string? lastName = null)`
      - `Guid AddDepartment(ApplicationDbContext db, Guid? parentId = null, bool active = true, Guid? legalEntityId = null)`
      - `Guid AddPositionHeldBy(ApplicationDbContext db, Guid employeeId)`
      - `void AddCoverage(ApplicationDbContext db, Guid ownerPositionId, string targetType, Guid? coveredPositionId = null, Guid? coveredDepartmentId = null, bool active = true)`
      - `void GrantPermission(ApplicationDbContext db, Guid userId, string permissionCode)`

- [ ] **Step 1: Write the counter**

```csharp
// tests/ONEVO.Tests.Integration/Support/CountingDbCommandInterceptor.cs
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ONEVO.Tests.Integration.Support;

/// <summary>Counts every SQL command EF Core sends through a context. Used by the My Team
/// query-budget and "no population-size N+1" tests (spec §13.2).</summary>
public sealed class CountingDbCommandInterceptor : DbCommandInterceptor
{
    private int _count;

    public int Count => Volatile.Read(ref _count);

    public void Reset() => Interlocked.Exchange(ref _count, 0);

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        Interlocked.Increment(ref _count);
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _count);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        Interlocked.Increment(ref _count);
        return result;
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _count);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        Interlocked.Increment(ref _count);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _count);
        return ValueTask.FromResult(result);
    }
}
```

- [ ] **Step 2: Write the DB helper**

It uses a superuser connection with no tenant context: RLS is not under test here; the repository
queries under test filter by `TenantId` explicitly. Entity shapes are copied from
`tests/ONEVO.Tests.Integration/CoreHr/Employee/EmployeesListIntegrationTests.cs` (`NewEmployee`,
`SeedCompanyWideCaller`).

```csharp
// tests/ONEVO.Tests.Integration/MyTeam/MyTeamDb.cs
using Microsoft.EntityFrameworkCore;
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
using ONEVO.Tests.Integration.Support;
using ManagementCoverageRecord = ONEVO.Domain.Features.OrgStructure.Entities.ManagementCoverageRecord;

namespace ONEVO.Tests.Integration.MyTeam;

/// <summary>One cloned PostgreSQL database per test class, with a tenant, a legal entity, the
/// "active" employment status, and small seeding helpers shared by every My Team integration
/// test (spec §18.2).</summary>
public sealed class MyTeamDb
{
    private readonly SystemDateTimeProvider _clock = new();
    private readonly Dictionary<string, Guid> _permissionIds = new(StringComparer.Ordinal);
    private string _connectionString = string.Empty;

    public Guid TenantId { get; private set; }
    public Guid LegalEntityId { get; private set; }

    public static async Task<MyTeamDb> CreateAsync()
    {
        var helper = new MyTeamDb { _connectionString = await SharedPostgresTemplate.CreateDatabaseAsync() };
        await using var db = helper.NewContext();
        if (!await db.EmploymentStatuses.AnyAsync(s => s.Id == 1))
            db.EmploymentStatuses.Add(new EmploymentStatus { Id = 1, Code = "active", Label = "Active" });
        var tenant = new Tenant
        {
            Id = Guid.NewGuid(), Name = "My Team Tenant", Slug = $"my-team-{Guid.NewGuid():N}"[..20],
            CompanySizeRange = "51-200", Status = TenantStatus.Active,
        };
        helper.TenantId = tenant.Id;
        db.Tenants.Add(tenant);
        var legalEntity = new LegalEntity
        {
            Id = Guid.NewGuid(), TenantId = tenant.Id, Name = "My Team Co",
            Timezone = "UTC", WorkStartTime = new TimeOnly(9, 0), WorkEndTime = new TimeOnly(17, 0),
            StandardWorkingDays = "[1,2,3,4,5,6,7]", BreakDurationMinutes = 60,
        };
        helper.LegalEntityId = legalEntity.Id;
        db.LegalEntities.Add(legalEntity);
        await db.SaveChangesAsync();
        return helper;
    }

    public ApplicationDbContext NewContext(CountingDbCommandInterceptor? counter = null)
    {
        var tenantContext = new TenantContextAccessor();
        var builder = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_connectionString)
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new TenantRlsInterceptor(tenantContext));
        if (counter is not null)
            builder.AddInterceptors(counter);

        return new ApplicationDbContext(
            builder.Options,
            new AuditableEntityInterceptor(new AnonymousCurrentUser(), _clock),
            new SoftDeleteInterceptor(_clock),
            new DomainEventDispatchInterceptor(new NoOpPublisher()),
            tenantContext);
    }

    public Employee AddEmployee(ApplicationDbContext db, Guid? departmentId = null, Guid? legalEntityId = null, string? lastName = null)
    {
        var employee = new Employee
        {
            Id = Guid.NewGuid(), TenantId = TenantId, UserId = Guid.NewGuid(),
            EmployeeNumber = $"MT-{Guid.NewGuid():N}"[..12], FirstName = "Team",
            LastName = lastName ?? $"Member{Guid.NewGuid():N}"[..14],
            Email = $"{Guid.NewGuid():N}@my-team-test.onevo.dev",
            HireDate = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-1),
            LegalEntityId = legalEntityId ?? LegalEntityId, DepartmentId = departmentId, EmploymentStatusId = 1,
        };
        db.Employees.Add(employee);
        return employee;
    }

    public Guid AddDepartment(ApplicationDbContext db, Guid? parentId = null, bool active = true, Guid? legalEntityId = null)
    {
        var department = new Department
        {
            Id = Guid.NewGuid(), TenantId = TenantId, LegalEntityId = legalEntityId ?? LegalEntityId,
            Name = $"Dept {Guid.NewGuid():N}"[..14], ParentDepartmentId = parentId, IsActive = active,
        };
        db.Departments.Add(department);
        return department.Id;
    }

    public Guid AddPositionHeldBy(ApplicationDbContext db, Guid employeeId)
    {
        var position = new Position
        {
            Id = Guid.NewGuid(), TenantId = TenantId, LegalEntityId = LegalEntityId,
            Name = $"Pos {Guid.NewGuid():N}"[..13], PositionType = Position.TypeUnique, MaxOccupancy = 1, IsActive = true,
        };
        db.Positions.Add(position);
        db.PositionAssignments.Add(new PositionAssignment
        {
            Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = employeeId, PositionId = position.Id,
            AssignmentKind = PositionAssignmentKind.PrimaryEmployment,
            AssignmentStatus = PositionAssignmentStatus.Active,
            EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-1),
        });
        return position.Id;
    }

    public void AddCoverage(ApplicationDbContext db, Guid ownerPositionId, string targetType,
        Guid? coveredPositionId = null, Guid? coveredDepartmentId = null, bool active = true)
    {
        db.ManagementCoverageRecords.Add(new ManagementCoverageRecord
        {
            Id = Guid.NewGuid(), TenantId = TenantId, LegalEntityId = LegalEntityId,
            OwnerPositionId = ownerPositionId, CoveredTargetType = targetType,
            CoveredPositionId = coveredPositionId, CoveredDepartmentId = coveredDepartmentId,
            OwnerOrder = 1, Source = ManagementCoverageRecord.SourceManual, IsLocked = false,
            Status = active ? ManagementCoverageRecord.StatusActive : ManagementCoverageRecord.StatusInactive,
        });
    }

    public void GrantPermission(ApplicationDbContext db, Guid userId, string permissionCode)
    {
        if (!_permissionIds.TryGetValue(permissionCode, out var permissionId))
        {
            permissionId = Guid.NewGuid();
            _permissionIds[permissionCode] = permissionId;
            db.Permissions.Add(new Permission { Id = permissionId, Code = permissionCode, Module = "core_hr", Description = permissionCode });
        }

        var roleId = Guid.NewGuid();
        db.Roles.Add(new Role { Id = roleId, TenantId = TenantId, Name = $"R{Guid.NewGuid():N}"[..20], CreatedById = userId });
        db.RolePermissions.Add(new RolePermission { TenantId = TenantId, RoleId = roleId, PermissionId = permissionId });
        db.UserRoles.Add(new UserRole { TenantId = TenantId, UserId = userId, RoleId = roleId, AssignedBy = userId });
    }
}
```

Before running Step 3, check the entity property names against these files. If one differs,
adjust the helper; do not change the entity.
- `src/ONEVO.Domain/Features/OrgStructure/Entities/LegalEntity.cs`: `Timezone`,
  `WorkStartTime`, `WorkEndTime`, `StandardWorkingDays` (the format parsed by
  `AttendanceScheduleResolver.ParseWorkingDays`) and `BreakDurationMinutes`.
- `Department.cs`: `ParentDepartmentId`, `IsActive`.

```bash
grep -n "ParseWorkingDays" -A12 src/ONEVO.Application/Features/TimeAttendance/Services/AttendanceScheduleResolver.cs
grep -n -E "public .* (Timezone|WorkStartTime|WorkEndTime|StandardWorkingDays|BreakDurationMinutes) " src/ONEVO.Domain/Features/OrgStructure/Entities/LegalEntity.cs
```

If `ParseWorkingDays` expects a format other than a JSON array of ISO day numbers, set
`StandardWorkingDays` to the format it parses, with all 7 days, so every test day is a working
day.

- [ ] **Step 3: Smoke test**

```csharp
// tests/ONEVO.Tests.Integration/MyTeam/MyTeamDbSmokeTests.cs
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ONEVO.Tests.Integration.Support;
using Xunit;

namespace ONEVO.Tests.Integration.MyTeam;

public sealed class MyTeamDbSmokeTests
{
    [Fact]
    public async Task Seeds_and_counts_commands()
    {
        var helper = await MyTeamDb.CreateAsync();
        await using (var db = helper.NewContext())
        {
            helper.AddEmployee(db);
            await db.SaveChangesAsync();
        }

        var counter = new CountingDbCommandInterceptor();
        await using var read = helper.NewContext(counter);
        (await read.Employees.CountAsync(e => e.TenantId == helper.TenantId)).Should().Be(1);
        counter.Count.Should().Be(1);
    }
}
```

- [ ] **Step 4: Run it**

Run: `dotnet test tests/ONEVO.Tests.Integration --filter "FullyQualifiedName~MyTeamDbSmokeTests"`
Expected: PASS (Docker running).

- [ ] **Step 5: Commit**

```bash
git add tests/ONEVO.Tests.Integration/Support/CountingDbCommandInterceptor.cs tests/ONEVO.Tests.Integration/MyTeam
git commit -m "test(my-team): command-counting interceptor and shared My Team DB helper"
```

---

### Task 2: Multi-root department descendant query

**Files:**
- Modify: `src/ONEVO.Application/Features/OrgStructure/Department/RepositoryInterfaces/IDepartmentRepository.cs:64-72`
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/OrgStructure/Department/EfDepartmentRepository.cs:247-263`
- Modify: `src/ONEVO.Application/Features/CoreHr/EmployeeAuthority/Services/EmployeeAuthorityResolver.cs:146-155`, so the build stays green before Task 3.
- Modify: `tests/ONEVO.Tests.Unit/Features/CoreHr/EmployeeAuthority/EmployeeAuthorityTestGraph.cs:580-583` (fake)
- Test: `tests/ONEVO.Tests.Integration/MyTeam/DepartmentDescendantsIntegrationTests.cs`

**Interfaces:**
- Produces: `IDepartmentRepository.GetDescendantDepartmentIdsAsync(Guid tenantId, Guid legalEntityId, IReadOnlyCollection<Guid> departmentIds, CancellationToken ct = default)`.
  - It **replaces** the single-root overload. The only caller is `EmployeeAuthorityResolver`;
    verify with the command below.
  - Returns the union of the transitive active descendants of every root, excluding the roots
    themselves.

- [ ] **Step 1: Confirm the single caller**

Run: `grep -rn "GetDescendantDepartmentIdsAsync(" src --include=*.cs`

Expected: exactly the interface, `EfDepartmentRepository.cs`, and `EmployeeAuthorityResolver.cs`.
If there are more callers, stop and add the multi-root method as a **new** overload instead of
replacing the old one.

- [ ] **Step 2: Write the failing integration test**

```csharp
// tests/ONEVO.Tests.Integration/MyTeam/DepartmentDescendantsIntegrationTests.cs
using FluentAssertions;
using ONEVO.Infrastructure.Persistence.Repositories.OrgStructure;
using ONEVO.Tests.Integration.Support;
using Xunit;

namespace ONEVO.Tests.Integration.MyTeam;

public sealed class DepartmentDescendantsIntegrationTests
{
    [Fact]
    public async Task Multi_root_query_returns_union_of_active_descendants_in_one_command()
    {
        var helper = await MyTeamDb.CreateAsync();
        Guid a, a1, a11, b, b1, b11, c;
        await using (var db = helper.NewContext())
        {
            a = helper.AddDepartment(db);
            a1 = helper.AddDepartment(db, a);
            a11 = helper.AddDepartment(db, a1);
            b = helper.AddDepartment(db);
            b1 = helper.AddDepartment(db, b, active: false); // inactive intermediate truncates the walk
            b11 = helper.AddDepartment(db, b1);
            c = helper.AddDepartment(db);
            await db.SaveChangesAsync();
        }

        var counter = new CountingDbCommandInterceptor();
        await using var read = helper.NewContext(counter);
        var repository = new EfDepartmentRepository(read);

        var result = await repository.GetDescendantDepartmentIdsAsync(
            helper.TenantId, helper.LegalEntityId, new[] { a, b, c });

        result.Should().BeEquivalentTo(new[] { a1, a11 });
        result.Should().NotContain(new[] { b1, b11, a, b, c });
        counter.Count.Should().Be(1);
    }

    [Fact]
    public async Task Empty_root_set_returns_empty_without_querying()
    {
        var helper = await MyTeamDb.CreateAsync();
        var counter = new CountingDbCommandInterceptor();
        await using var read = helper.NewContext(counter);

        var result = await new EfDepartmentRepository(read)
            .GetDescendantDepartmentIdsAsync(helper.TenantId, helper.LegalEntityId, Array.Empty<Guid>());

        result.Should().BeEmpty();
        counter.Count.Should().Be(0);
    }
}
```

Check the namespace of `EfDepartmentRepository`:
`grep -n namespace src/ONEVO.Infrastructure/Persistence/Repositories/OrgStructure/Department/EfDepartmentRepository.cs`.
Fix the `using` if it differs.

- [ ] **Step 3: Run it to see it fail**

Run: `dotnet test tests/ONEVO.Tests.Integration --filter "FullyQualifiedName~DepartmentDescendantsIntegrationTests"`
Expected: compile error; no overload takes a `Guid[]`.

- [ ] **Step 4: Change the interface**

Replace the method at `IDepartmentRepository.cs:64-72` with:

```csharp
    /// <summary>Transitive active descendant department ids (any depth, the roots themselves
    /// excluded) of every department in <paramref name="departmentIds"/>, in ONE recursive CTE.
    /// Used by IEmployeeAuthorityResolver to expand covered departments into their sub-trees for
    /// visibility. Filtered to is_active = true at every level, so an inactive intermediate
    /// department truncates the walk there and excludes its active children too, not just
    /// itself.</summary>
    Task<IReadOnlyList<Guid>> GetDescendantDepartmentIdsAsync(
        Guid tenantId, Guid legalEntityId, IReadOnlyCollection<Guid> departmentIds, CancellationToken ct = default);
```

- [ ] **Step 5: Implement it**

Replace the method at `EfDepartmentRepository.cs:247-263` with:

```csharp
    public async Task<IReadOnlyList<Guid>> GetDescendantDepartmentIdsAsync(
        Guid tenantId, Guid legalEntityId, IReadOnlyCollection<Guid> departmentIds, CancellationToken ct = default)
    {
        if (departmentIds.Count == 0)
            return Array.Empty<Guid>();

        var roots = departmentIds.Distinct().ToArray();
        var descendantIds = _db.Database.SqlQuery<Guid>($@"
            WITH RECURSIVE descendants AS (
                SELECT id FROM departments
                WHERE tenant_id = {tenantId} AND legal_entity_id = {legalEntityId}
                    AND parent_department_id = ANY({roots}) AND is_active = true
                UNION
                SELECT d.id FROM departments d
                INNER JOIN descendants ON d.parent_department_id = descendants.id
                WHERE d.tenant_id = {tenantId} AND d.legal_entity_id = {legalEntityId} AND d.is_active = true
            )
            SELECT id AS ""Value"" FROM descendants
        ");

        var ids = await descendantIds.ToListAsync(ct);
        var rootSet = roots.ToHashSet();
        return ids.Where(id => !rootSet.Contains(id)).Distinct().ToList();
    }
```

Two differences from the old single-root query, and why:
- `UNION` replaces `UNION ALL`, and roots are excluded in C#. With several roots, one root can
  be a descendant of another. `UNION` removes duplicates and prevents re-walking a shared
  subtree.
- The old single-root query never returned its own root. Excluding every root keeps that exact
  contract.

- [ ] **Step 6: Update the resolver call site**

In `EmployeeAuthorityResolver.cs` lines 146-155, replace the department loop with:

```csharp
        var expandedDepartmentIds = new HashSet<Guid>(coveredDepartmentIds);
        if (coveredDepartmentIds.Count > 0)
        {
            var descendantDeptIds = await _departmentRepository.GetDescendantDepartmentIdsAsync(
                tenantId, legalEntityId, coveredDepartmentIds, ct);
            expandedDepartmentIds.UnionWith(descendantDeptIds);
        }
```

- [ ] **Step 7: Update the unit-test fake**

In `EmployeeAuthorityTestGraph.cs`, replace `FakeDepartmentRepository.GetDescendantDepartmentIdsAsync`
(lines 580-583) with:

```csharp
        public Task<IReadOnlyList<Guid>> GetDescendantDepartmentIdsAsync(
            Guid tenantId, Guid legalEntityId, IReadOnlyCollection<Guid> departmentIds, CancellationToken ct = default)
        {
            _graph.RecordCall("Department.GetDescendantDepartmentIdsAsync");
            var roots = departmentIds.ToHashSet();
            var result = departmentIds
                .SelectMany(id => _graph.DescendantDepartmentsOf(id))
                .Where(id => !roots.Contains(id))
                .Distinct()
                .ToList();
            return Task.FromResult<IReadOnlyList<Guid>>(result);
        }
```

- [ ] **Step 8: Run all affected tests**

Run:
- `dotnet test tests/ONEVO.Tests.Integration --filter "FullyQualifiedName~DepartmentDescendantsIntegrationTests"` → PASS
- `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~EmployeeAuthority"` → PASS (all existing tests)

- [ ] **Step 9: Commit**

```bash
git add src/ONEVO.Application/Features/OrgStructure/Department/RepositoryInterfaces/IDepartmentRepository.cs src/ONEVO.Infrastructure/Persistence/Repositories/OrgStructure/Department/EfDepartmentRepository.cs src/ONEVO.Application/Features/CoreHr/EmployeeAuthority/Services/EmployeeAuthorityResolver.cs tests/ONEVO.Tests.Unit/Features/CoreHr/EmployeeAuthority/EmployeeAuthorityTestGraph.cs tests/ONEVO.Tests.Integration/MyTeam/DepartmentDescendantsIntegrationTests.cs
git commit -m "perf(authority): expand covered departments with one multi-root CTE"
```

---

### Task 3: Batch covered-position holders

**Files:**
- Modify: `src/ONEVO.Application/Features/CoreHr/EmployeeAuthority/Services/EmployeeAuthorityResolver.cs:128-143`
- Modify: `tests/ONEVO.Tests.Unit/Features/CoreHr/EmployeeAuthority/EmployeeAuthorityTestGraph.cs` (record calls in `GetActiveHoldersAsync`)
- Test: `tests/ONEVO.Tests.Unit/Features/CoreHr/EmployeeAuthority/EmployeeAuthorityResolverBatchingTests.cs`

**Interfaces:**
- Consumes: `IPositionAssignmentRepository.GetActiveHoldersByPositionIdsAsync(Guid tenantId, IReadOnlyCollection<Guid> positionIds, CancellationToken ct)`
  returns `IReadOnlyDictionary<Guid, IReadOnlyList<PositionActiveHolder>>`. Its join is identical
  to `GetActiveHoldersAsync` (`EfPositionAssignmentRepository.cs` L93-L155).

- [ ] **Step 1: Make the fake count single-position holder calls**

In `FakePositionAssignmentRepository.GetActiveHoldersAsync`, add
`_graph.RecordCall("PositionAssignment.GetActiveHoldersAsync");` as the first statement. The
batch fake already records `"PositionAssignment.GetActiveHoldersByPositionIdsAsync"`.

- [ ] **Step 2: Write the failing test**

```csharp
// tests/ONEVO.Tests.Unit/Features/CoreHr/EmployeeAuthority/EmployeeAuthorityResolverBatchingTests.cs
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.Models;
using Xunit;

namespace ONEVO.Tests.Unit.Features.CoreHr.EmployeeAuthority;

public sealed class EmployeeAuthorityResolverBatchingTests
{
    private const string AttendanceRead = "attendance:read";

    [Fact]
    public async Task Position_coverage_resolves_all_holders_with_one_batch_call()
    {
        var graph = new EmployeeAuthorityTestGraph();
        var le = Guid.NewGuid();
        var actor = graph.AddEmployee(le);
        var actorPosition = graph.AddPosition(le);
        graph.AddPrimaryAssignment(actor.Id, actorPosition.Id);
        var covered = new List<Guid>();
        for (var i = 0; i < 4; i++)
        {
            var position = graph.AddPosition(le);
            var holder = graph.AddEmployee(le);
            graph.AddPrimaryAssignment(holder.Id, position.Id);
            graph.AddCoverage(le, actorPosition.Id, "Position", position.Id, null, ownerOrder: i + 1);
            covered.Add(holder.Id);
        }
        graph.GrantPermission(actor.UserId, AttendanceRead);

        var scope = await graph.BuildResolver().ResolveVisibilityAsync(new EmployeeAuthorityVisibilityRequest(
            actor.UserId, le, AttendanceRead, IncludeSelf: false, EmployeeAuthorityPurpose.TimeTrackingRead));

        Assert.Equal(covered.OrderBy(x => x), scope.EmployeeIds.OrderBy(x => x));
        Assert.Equal(1, graph.CallCounts.GetValueOrDefault("PositionAssignment.GetActiveHoldersByPositionIdsAsync"));
        Assert.Equal(0, graph.CallCounts.GetValueOrDefault("PositionAssignment.GetActiveHoldersAsync"));
    }

    [Fact]
    public async Task Department_coverage_expands_all_roots_with_one_call()
    {
        var graph = new EmployeeAuthorityTestGraph();
        var le = Guid.NewGuid();
        var actor = graph.AddEmployee(le);
        var actorPosition = graph.AddPosition(le);
        graph.AddPrimaryAssignment(actor.Id, actorPosition.Id);
        var expected = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            var root = graph.AddDepartment();
            var child = graph.AddDepartment(parentDepartmentId: root);
            expected.Add(graph.AddEmployee(le, departmentId: child).Id);
            graph.AddCoverage(le, actorPosition.Id, "Department", null, root, ownerOrder: i + 1);
        }
        graph.GrantPermission(actor.UserId, AttendanceRead);

        var scope = await graph.BuildResolver().ResolveVisibilityAsync(new EmployeeAuthorityVisibilityRequest(
            actor.UserId, le, AttendanceRead, IncludeSelf: false, EmployeeAuthorityPurpose.TimeTrackingRead));

        Assert.Equal(expected.OrderBy(x => x), scope.EmployeeIds.OrderBy(x => x));
        Assert.Equal(1, graph.CallCounts.GetValueOrDefault("Department.GetDescendantDepartmentIdsAsync"));
    }
}
```

- [ ] **Step 3: Run it to see it fail**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~EmployeeAuthorityResolverBatchingTests"`
Expected: `Position_coverage_...` FAILS. The batch call count is 0 and the single-call count is 4.
The department test already passes after Task 2.

- [ ] **Step 4: Implement**

In `AddManagedVisibilityAsync`, replace the `foreach (var positionId in coveredPositionIds)` block
(lines 128-136) with:

```csharp
        // Position coverage: direct holder(s) of each covered position (one batched lookup for
        // every covered position), plus their full reporting-line subtree.
        var positionHolderIds = new HashSet<Guid>();
        if (coveredPositionIds.Count > 0)
        {
            var holdersByPosition = await _positionAssignmentRepository.GetActiveHoldersByPositionIdsAsync(
                tenantId, coveredPositionIds, ct);
            foreach (var holders in holdersByPosition.Values)
            {
                foreach (var holder in holders)
                    positionHolderIds.Add(holder.EmployeeId);
            }
        }
```

Leave the `if (positionHolderIds.Count > 0)` closure block that follows unchanged.

- [ ] **Step 5: Run the tests**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~EmployeeAuthority"`
Expected: PASS, including every pre-existing resolver test.

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Application/Features/CoreHr/EmployeeAuthority/Services/EmployeeAuthorityResolver.cs tests/ONEVO.Tests.Unit/Features/CoreHr/EmployeeAuthority
git commit -m "perf(authority): resolve covered position holders in one batch"
```

---

### Task 4: Request-scoped memo of the coverage expansion

**Files:**
- Modify: `src/ONEVO.Application/Features/CoreHr/EmployeeAuthority/Services/EmployeeAuthorityResolver.cs:56-103` (`ResolveVisibilityAsync`) plus new private members
- Modify: `tests/ONEVO.Tests.Unit/Features/CoreHr/EmployeeAuthority/EmployeeAuthorityTestGraph.cs` (record calls in `ListCoverageByOwnerPositionAsync`, `GetByUserAndLegalEntityAsync`, `UserHasPermissionCodeAsync`)
- Test: `tests/ONEVO.Tests.Unit/Features/CoreHr/EmployeeAuthority/EmployeeAuthorityResolverMemoTests.cs`

**Interfaces:**
- Produces: no public surface change. Later parts rely only on this behavior:
  - in one DI scope, repeated `ResolveVisibilityAsync` calls for the same (actor, legal entity)
    expand coverage once;
  - each call still checks its own `RequiredPermission`.

- [ ] **Step 1: Make the fakes record calls**

Add each of these as the first statement of the named fake method:
- `_graph.RecordCall("Position.ListCoverageByOwnerPositionAsync");` in `FakePositionRepository.ListCoverageByOwnerPositionAsync`;
- `_graph.RecordCall("Employee.GetByUserAndLegalEntityAsync");` in `FakeEmployeeRepository.GetByUserAndLegalEntityAsync`;
- `_graph.RecordCall("Permission.UserHasPermissionCodeAsync");` in `FakePermissionRepository.UserHasPermissionCodeAsync`.

Expression-bodied members become block bodies ending in the original expression as a `return`.

- [ ] **Step 2: Write the failing tests**

```csharp
// tests/ONEVO.Tests.Unit/Features/CoreHr/EmployeeAuthority/EmployeeAuthorityResolverMemoTests.cs
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.Models;
using Xunit;

namespace ONEVO.Tests.Unit.Features.CoreHr.EmployeeAuthority;

public sealed class EmployeeAuthorityResolverMemoTests
{
    private const string AttendanceRead = "attendance:read";
    private const string AttendanceApprove = "attendance:approve";

    private static (EmployeeAuthorityTestGraph Graph, Guid Le, ONEVO.Domain.Features.CoreHr.Entities.Employee Actor, Guid Covered) Build()
    {
        var graph = new EmployeeAuthorityTestGraph();
        var le = Guid.NewGuid();
        var actor = graph.AddEmployee(le);
        var actorPosition = graph.AddPosition(le);
        graph.AddPrimaryAssignment(actor.Id, actorPosition.Id);
        var coveredPosition = graph.AddPosition(le);
        var covered = graph.AddEmployee(le);
        graph.AddPrimaryAssignment(covered.Id, coveredPosition.Id);
        graph.AddCoverage(le, actorPosition.Id, "Position", coveredPosition.Id, null, ownerOrder: 1);
        return (graph, le, actor, covered.Id);
    }

    [Fact]
    public async Task Second_call_in_same_scope_reuses_expansion_but_rechecks_permission()
    {
        var (graph, le, actor, covered) = Build();
        graph.GrantPermission(actor.UserId, AttendanceRead);
        graph.GrantPermission(actor.UserId, AttendanceApprove);
        var resolver = graph.BuildResolver();

        var first = await resolver.ResolveVisibilityAsync(new EmployeeAuthorityVisibilityRequest(
            actor.UserId, le, AttendanceRead, IncludeSelf: false, EmployeeAuthorityPurpose.TimeTrackingRead));
        var second = await resolver.ResolveVisibilityAsync(new EmployeeAuthorityVisibilityRequest(
            actor.UserId, le, AttendanceApprove, IncludeSelf: false, EmployeeAuthorityPurpose.AttendanceCorrectionApproval));

        Assert.Contains(covered, first.EmployeeIds);
        Assert.Contains(covered, second.EmployeeIds);
        Assert.Equal(1, graph.CallCounts.GetValueOrDefault("Position.ListCoverageByOwnerPositionAsync"));
        Assert.Equal(1, graph.CallCounts.GetValueOrDefault("Employee.GetByUserAndLegalEntityAsync"));
        Assert.Equal(2, graph.CallCounts.GetValueOrDefault("Permission.UserHasPermissionCodeAsync"));
    }

    [Fact]
    public async Task Memo_never_grants_coverage_to_a_permission_the_actor_lacks()
    {
        var (graph, le, actor, covered) = Build();
        graph.GrantPermission(actor.UserId, AttendanceRead); // NOT attendance:approve
        var resolver = graph.BuildResolver();

        await resolver.ResolveVisibilityAsync(new EmployeeAuthorityVisibilityRequest(
            actor.UserId, le, AttendanceRead, IncludeSelf: false, EmployeeAuthorityPurpose.TimeTrackingRead));
        var denied = await resolver.ResolveVisibilityAsync(new EmployeeAuthorityVisibilityRequest(
            actor.UserId, le, AttendanceApprove, IncludeSelf: false, EmployeeAuthorityPurpose.AttendanceCorrectionApproval));

        Assert.DoesNotContain(covered, denied.EmployeeIds);
        Assert.Empty(denied.EmployeeIds);
    }

    [Fact]
    public async Task Memo_is_per_legal_entity()
    {
        var (graph, le, actor, _) = Build();
        graph.GrantPermission(actor.UserId, AttendanceRead);
        var otherLe = Guid.NewGuid();
        var resolver = graph.BuildResolver();

        await resolver.ResolveVisibilityAsync(new EmployeeAuthorityVisibilityRequest(
            actor.UserId, le, AttendanceRead, IncludeSelf: false, EmployeeAuthorityPurpose.TimeTrackingRead));
        var other = await resolver.ResolveVisibilityAsync(new EmployeeAuthorityVisibilityRequest(
            actor.UserId, otherLe, AttendanceRead, IncludeSelf: false, EmployeeAuthorityPurpose.TimeTrackingRead));

        Assert.Empty(other.EmployeeIds);
        Assert.Equal(2, graph.CallCounts.GetValueOrDefault("Employee.GetByUserAndLegalEntityAsync"));
    }

    [Fact]
    public async Task A_new_resolver_instance_starts_with_an_empty_memo()
    {
        var (graph, le, actor, _) = Build();
        graph.GrantPermission(actor.UserId, AttendanceRead);
        var request = new EmployeeAuthorityVisibilityRequest(
            actor.UserId, le, AttendanceRead, IncludeSelf: false, EmployeeAuthorityPurpose.TimeTrackingRead);

        await graph.BuildResolver().ResolveVisibilityAsync(request);
        await graph.BuildResolver().ResolveVisibilityAsync(request);

        Assert.Equal(2, graph.CallCounts.GetValueOrDefault("Position.ListCoverageByOwnerPositionAsync"));
    }
}
```

- [ ] **Step 3: Run them to see them fail**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~EmployeeAuthorityResolverMemoTests"`
Expected: `Second_call_...` FAILS; the coverage listing count is 2. The others pass or fail only
on counts.

- [ ] **Step 4: Implement the memo**

Add these private members inside `EmployeeAuthorityResolver`, after the constructor:

```csharp
    // Request-scoped memo of the permission-INDEPENDENT half of visibility resolution: the actor's
    // employee record in a legal entity and the expanded management-coverage candidate set. This
    // resolver is registered AddScoped, so the memo lives exactly one HTTP request / one DI scope
    // and never crosses users or requests (spec §13.1). The permission check is deliberately NOT
    // memoized: every call re-checks its own RequiredPermission, so the memo can never grant
    // coverage for a permission the actor lacks.
    private readonly Dictionary<(Guid TenantId, Guid ActorUserId, Guid LegalEntityId), ActorCoverage> _coverageMemo = new();

    private sealed class ActorCoverage
    {
        public ActorCoverage(ONEVO.Domain.Features.CoreHr.Entities.Employee? actor) => Actor = actor;

        public ONEVO.Domain.Features.CoreHr.Entities.Employee? Actor { get; }

        /// <summary>Null until first expanded; expansion only happens once a caller has passed the
        /// permission check.</summary>
        public IReadOnlySet<Guid>? ManagedCandidateIds { get; set; }
    }

    private async Task<ActorCoverage> GetActorCoverageAsync(
        Guid tenantId, Guid actorUserId, Guid legalEntityId, CancellationToken ct)
    {
        var key = (tenantId, actorUserId, legalEntityId);
        if (_coverageMemo.TryGetValue(key, out var cached))
            return cached;

        var actor = await _employeeRepository.GetByUserAndLegalEntityAsync(tenantId, actorUserId, legalEntityId, ct);
        var entry = new ActorCoverage(actor);
        _coverageMemo[key] = entry;
        return entry;
    }

    private async Task<IReadOnlySet<Guid>> ExpandManagedVisibilityAsync(
        Guid tenantId, Guid legalEntityId, Guid actorEmployeeId, CancellationToken ct)
    {
        var managed = new HashSet<Guid>();
        var actorAssignment = await _positionAssignmentRepository.GetActivePrimaryAsync(tenantId, actorEmployeeId, ct);
        if (actorAssignment is not null)
            await AddManagedVisibilityAsync(tenantId, legalEntityId, actorAssignment.PositionId, managed, ct);
        return managed;
    }
```

Replace the body of `ResolveVisibilityAsync` (lines 56-103) with:

```csharp
    public async Task<EmployeeAuthorityVisibilityScope> ResolveVisibilityAsync(
        EmployeeAuthorityVisibilityRequest request, CancellationToken cancellationToken = default)
    {
        var tenantId = _currentUser.TenantId;
        var now = _clock.UtcNow;

        var coverage = await GetActorCoverageAsync(tenantId, request.ActorUserId, request.LegalEntityId, cancellationToken);
        var actorEmployee = coverage.Actor;

        var candidateIds = new HashSet<Guid>();
        var includesSelf = false;

        if (request.IncludeSelf && actorEmployee is not null)
        {
            candidateIds.Add(actorEmployee.Id);
            includesSelf = true;
        }

        var hasPermission = await _permissionRepository.UserHasPermissionCodeAsync(
            request.ActorUserId, request.RequiredPermission, now, cancellationToken);

        if (hasPermission && actorEmployee is not null)
        {
            coverage.ManagedCandidateIds ??= await ExpandManagedVisibilityAsync(
                tenantId, request.LegalEntityId, actorEmployee.Id, cancellationToken);
            candidateIds.UnionWith(coverage.ManagedCandidateIds);
        }

        if (candidateIds.Count == 0)
        {
            return new EmployeeAuthorityVisibilityScope(
                request.ActorUserId, request.LegalEntityId, includesSelf, Array.Empty<Guid>());
        }

        var finalIds = await _employeeRepository.ListActiveEmployeeIdsByIdsAsync(
            tenantId, request.LegalEntityId, candidateIds, cancellationToken);

        // The self channel is not permission-gated, but must still survive the same
        // tenant/legal-entity/active-status chokepoint every other candidate does.
        includesSelf = includesSelf && actorEmployee is not null && finalIds.Contains(actorEmployee.Id);

        return new EmployeeAuthorityVisibilityScope(request.ActorUserId, request.LegalEntityId, includesSelf, finalIds);
    }
```

`AddManagedVisibilityAsync` keeps its signature (`HashSet<Guid> candidateIds` output parameter).

- [ ] **Step 5: Run the resolver tests and the architecture test**

Run:
- `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~EmployeeAuthority"` → PASS
- `dotnet test tests/ONEVO.Tests.Architecture --filter "FullyQualifiedName~EmployeeAuthorityResolver"` → PASS
  (no `"x:y"` literal was added).

- [ ] **Step 6: Verify the memo-safety precondition (spec §13.1)**

No handler may call `ResolveVisibilityAsync` after mutating coverage in the same DI scope.

Run: `grep -rln "ResolveVisibilityAsync(" src --include=*.cs`

Expected, exactly these callers, all of them read paths or list methods that never write
`ManagementCoverageRecord`:
- `ListEmployeesQueryHandler.cs`
- `ExceptionScopeResolver.cs`
- `DeviceChangeRequestWorkflow.cs`
- `AttendanceCorrectionWorkflow.cs`
- `LocationChangeRequestWorkflow.cs`
- `WorkAreaChangeRequestWorkflow.cs`
- `AttendanceReadHandlers.cs`
- `AttendanceTodayStateService.cs`

Then: `grep -rln "ManagementCoverageRecords\b.*Add\|UpdateCoverage\|coverage" src/ONEVO.Application/Features/OrgStructure --include=*Command*Handler.cs`
and confirm that none of those files references `IEmployeeAuthorityResolver`.

If any new caller appears that writes coverage and then resolves, stop and report. A reset method
would be needed, and it is not part of this plan.

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Application/Features/CoreHr/EmployeeAuthority/Services/EmployeeAuthorityResolver.cs tests/ONEVO.Tests.Unit/Features/CoreHr/EmployeeAuthority
git commit -m "perf(authority): request-scoped memo of the coverage expansion"
```

---

### Task 5: Resolver round-trip budget and equivalence (integration)

**Files:**
- Test: `tests/ONEVO.Tests.Integration/MyTeam/EmployeeAuthorityResolverBudgetIntegrationTests.cs`

**Interfaces:**
- Consumes: `MyTeamDb`, `CountingDbCommandInterceptor` (Task 1). Also the real
  `EmployeeAuthorityResolver` constructor (unchanged since Task 4):
  `(ICurrentUser, IDateTimeProvider, IEmployeeRepository, IPositionAssignmentRepository, IPositionRepository, IEmployeeHierarchyClosureRepository, IDepartmentRepository, IPermissionRepository)`.

- [ ] **Step 1: Write the test**

The budget is: at most 7 commands on the first call however much coverage there is, and at most
2 on a second call with a different permission.

```csharp
// tests/ONEVO.Tests.Integration/MyTeam/EmployeeAuthorityResolverBudgetIntegrationTests.cs
using FluentAssertions;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.Models;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.Services;
using ONEVO.Infrastructure.Identity.Time;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Repositories.Auth.Login;
using ONEVO.Infrastructure.Persistence.Repositories.CoreHr;
using ONEVO.Infrastructure.Persistence.Repositories.OrgStructure;
using ONEVO.Tests.Integration.Support;
using Xunit;
using ManagementCoverageRecord = ONEVO.Domain.Features.OrgStructure.Entities.ManagementCoverageRecord;

namespace ONEVO.Tests.Integration.MyTeam;

public sealed class EmployeeAuthorityResolverBudgetIntegrationTests
{
    private sealed class StubCurrentUser(Guid tenantId, Guid userId) : ICurrentUser
    {
        public Guid UserId => userId;
        public Guid TenantId => tenantId;
        public string Email => string.Empty;
        public IReadOnlyList<string> Permissions => [];
        public bool HasPermission(string permission) => false;
        public bool IsAuthenticated => true;
    }

    private static EmployeeAuthorityResolver Build(ApplicationDbContext db, Guid tenantId, Guid userId) => new(
        new StubCurrentUser(tenantId, userId),
        new SystemDateTimeProvider(),
        new EfEmployeeRepository(db),
        PositionAssignmentRepositoryTestSupport.CreateRepository(db),
        new EfPositionRepository(db),
        PositionAssignmentRepositoryTestSupport.CreateClosureRepository(db),
        new EfDepartmentRepository(db),
        new EfPermissionRepository(db));

    [Theory]
    [InlineData(1, 1)]
    [InlineData(6, 4)]
    public async Task First_call_is_constant_and_second_call_reuses_the_expansion(int coveredPositions, int coveredDepartments)
    {
        var helper = await MyTeamDb.CreateAsync();
        Guid actorUserId;
        await using (var db = helper.NewContext())
        {
            var actor = helper.AddEmployee(db);
            actorUserId = actor.UserId;
            var actorPosition = helper.AddPositionHeldBy(db, actor.Id);
            for (var i = 0; i < coveredPositions; i++)
            {
                var member = helper.AddEmployee(db);
                helper.AddCoverage(db, actorPosition, ManagementCoverageRecord.TargetPosition,
                    coveredPositionId: helper.AddPositionHeldBy(db, member.Id));
            }
            for (var i = 0; i < coveredDepartments; i++)
            {
                var root = helper.AddDepartment(db);
                helper.AddEmployee(db, departmentId: helper.AddDepartment(db, root));
                helper.AddCoverage(db, actorPosition, ManagementCoverageRecord.TargetDepartment, coveredDepartmentId: root);
            }
            helper.GrantPermission(db, actorUserId, "attendance:read");
            helper.GrantPermission(db, actorUserId, "attendance:approve");
            await db.SaveChangesAsync();
        }

        var counter = new CountingDbCommandInterceptor();
        await using var read = helper.NewContext(counter);
        var resolver = Build(read, helper.TenantId, actorUserId);

        var first = await resolver.ResolveVisibilityAsync(new EmployeeAuthorityVisibilityRequest(
            actorUserId, helper.LegalEntityId, "attendance:read", false, EmployeeAuthorityPurpose.TimeTrackingRead));
        var firstCommands = counter.Count;
        counter.Reset();
        var second = await resolver.ResolveVisibilityAsync(new EmployeeAuthorityVisibilityRequest(
            actorUserId, helper.LegalEntityId, "attendance:approve", false, EmployeeAuthorityPurpose.AttendanceCorrectionApproval));

        first.EmployeeIds.Should().HaveCount(coveredPositions + coveredDepartments);
        second.EmployeeIds.Should().BeEquivalentTo(first.EmployeeIds);
        firstCommands.Should().BeLessThanOrEqualTo(7);
        counter.Count.Should().BeLessThanOrEqualTo(2);
    }
}
```

Before running, confirm the Infrastructure namespaces of `EfPositionRepository`, `EfPermissionRepository`
and `EfEmployeeRepository`, then fix the `using` lines:
`grep -rn "class EfPositionRepository\|class EfPermissionRepository\|class EfEmployeeRepository" -B30 src/ONEVO.Infrastructure | grep namespace`

- [ ] **Step 2: Run it**

Run: `dotnet test tests/ONEVO.Tests.Integration --filter "FullyQualifiedName~EmployeeAuthorityResolverBudgetIntegrationTests"`
Expected: PASS for both rows. The first-call count must be the same for (1,1) and (6,4).

If the first call exceeds 7, print the SQL by temporarily adding
`.LogTo(Console.WriteLine, Microsoft.Extensions.Logging.LogLevel.Information)` to `NewContext`,
find the extra per-item query, and batch it. Do not raise the budget.

- [ ] **Step 3: Commit**

```bash
git add tests/ONEVO.Tests.Integration/MyTeam/EmployeeAuthorityResolverBudgetIntegrationTests.cs
git commit -m "test(authority): constant round-trip budget and memo reuse against PostgreSQL"
```

---

## Part 1 done when

- Every existing `EmployeeAuthority*` unit test and architecture test is green, unmodified.
- The new batching, memo and budget tests are green.
- `git log` shows the five commits above.
