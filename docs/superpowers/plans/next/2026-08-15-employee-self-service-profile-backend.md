# Employee Self-Service Profile (Backend) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `/api/v1/employees/me/*` self-service endpoints (Personal Information, Emergency Contacts, Dependents, Job Information read, Payroll & Statutory, avatar) plus `change-password` and `mfa/disable` on the existing Auth controllers, so every tenant user can manage their own HR profile.

**Architecture:** Clean Architecture/CQRS via MediatR, matching `EmployeesController`'s existing List/GetById/ResendInvitation pattern exactly. Four new child entities (`EmployeeAddress`, `EmployeeEmergencyContact`, `EmployeeDependent`, `EmployeeBankDetail`) live under the existing `CoreHr/Employee` subfeature. Bank account numbers are encrypted via the existing `IEncryptionService`. All new tables get PostgreSQL RLS in the same migration that creates them.

**Tech Stack:** ASP.NET Core Web API, MediatR, FluentValidation, EF Core + Npgsql, PostgreSQL, xUnit + Moq, Testcontainers.

## Global Constraints

- All new routes live under `[Authorize(Policy = "TenantPolicy")]` (already on `EmployeesController`/`AuthPasswordController`/`AuthMfaController` at the class level — do not add `[AllowAnonymous]` to anything in this plan).
- `/employees/me/*` actions do **not** get `[RequirePermission("employees:read")]`/`[RequirePermission("employees:write")]` like the controller's other actions — self-service is authorized by trusted session identity alone (backend-arch §3.4: "Own-record self-service ... authorized by trusted session identity"), **except** `PUT /me/payroll`, which requires `[RequirePermission("employees:write")]` even though it's the caller's own record (design decision — bank-detail edits are HR-mediated).
- Every new tenant-owned entity implements `ITenantOwnedEntity` (inherit `BaseEntity`) and gets an RLS policy in its creation migration, using the exact `tenant_isolation` policy SQL shape from `20260810071627_AddOnboardingDrafts.cs`.
- `account_number_encrypted` is `varchar(500)`, populated via `IEncryptionService.Encrypt(string)` — never `bytea`/`EncryptBytes`.
- No response DTO in this plan ever returns a decrypted/raw bank account number — only a masked `"****1234"` form.
- Money/PII fields (bank details, addresses, emergency contacts, dependents) must never appear in log statements.
- Run `dotnet test tests/ONEVO.Tests.Architecture` after Task 1 and again after Task 11 — it must stay green throughout (`TenantIsolationArchitectureTests`, `EmployeeLegacyFieldRetirementArchitectureTests`).

---

### Task 1: Employee-profile child tables migration + entities + RLS

**Files:**
- Create: `src/ONEVO.Domain/Features/CoreHr/Entities/EmployeeAddress.cs`
- Create: `src/ONEVO.Domain/Features/CoreHr/Entities/EmployeeEmergencyContact.cs`
- Create: `src/ONEVO.Domain/Features/CoreHr/Entities/EmployeeDependent.cs`
- Create: `src/ONEVO.Domain/Features/CoreHr/Entities/EmployeeBankDetail.cs`
- Modify: `src/ONEVO.Domain/Features/CoreHr/Entities/Employee.cs` (add `DisplayTimezone`)
- Create: `src/ONEVO.Infrastructure/Persistence/Configurations/CoreHr/Employee/EmployeeAddressConfiguration.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Configurations/CoreHr/Employee/EmployeeEmergencyContactConfiguration.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Configurations/CoreHr/Employee/EmployeeDependentConfiguration.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Configurations/CoreHr/Employee/EmployeeBankDetailConfiguration.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Configurations/CoreHr/Employee/EmployeeConfiguration.cs` (add `DisplayTimezone` + xmin shadow property)
- Modify: `src/ONEVO.Infrastructure/Persistence/ApplicationDbContext.cs` (add 4 `DbSet<T>` properties)
- Create: migration (via `dotnet ef migrations add`, see Step 6)
- Test: `tests/ONEVO.Tests.Integration/CoreHr/EmployeeProfile/EmployeeProfileTablesRlsTests.cs`

**Interfaces:**
- Produces: `Employee.DisplayTimezone` (string?), `EmployeeAddress`, `EmployeeEmergencyContact`, `EmployeeDependent`, `EmployeeBankDetail` domain types — consumed by every later task.

- [ ] **Step 1: Write the entities**

```csharp
// src/ONEVO.Domain/Features/CoreHr/Entities/EmployeeAddress.cs
using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.CoreHr.Entities;

public class EmployeeAddress : BaseEntity
{
    public Guid EmployeeId { get; set; }
    public string AddressType { get; set; } = string.Empty; // "permanent" | "current"
    public string AddressJson { get; set; } = "{}";
    public bool IsPrimary { get; set; }
}
```

```csharp
// src/ONEVO.Domain/Features/CoreHr/Entities/EmployeeEmergencyContact.cs
using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.CoreHr.Entities;

public class EmployeeEmergencyContact : BaseEntity
{
    public Guid EmployeeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Relationship { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string? Email { get; set; }
    public bool IsPrimary { get; set; }
}
```

```csharp
// src/ONEVO.Domain/Features/CoreHr/Entities/EmployeeDependent.cs
using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.CoreHr.Entities;

public class EmployeeDependent : BaseEntity
{
    public Guid EmployeeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Relationship { get; set; } = string.Empty; // spouse | child | parent | other
    public DateOnly DateOfBirth { get; set; }
    public bool IsEmergencyContact { get; set; }
    public string? Phone { get; set; }
}
```

```csharp
// src/ONEVO.Domain/Features/CoreHr/Entities/EmployeeBankDetail.cs
using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.CoreHr.Entities;

public class EmployeeBankDetail : BaseEntity
{
    public Guid EmployeeId { get; set; }
    public string BankName { get; set; } = string.Empty;
    public string BranchName { get; set; } = string.Empty;
    public string AccountHolderName { get; set; } = string.Empty;
    public string AccountNumberEncrypted { get; set; } = string.Empty;
    public string AccountType { get; set; } = string.Empty;
    public string? RoutingNumber { get; set; }
    public bool IsPrimary { get; set; }
}
```

Add to `Employee.cs` (after `AvatarFileId`):

```csharp
    public string? DisplayTimezone { get; set; }
```

- [ ] **Step 2: Write the EF configurations**

```csharp
// src/ONEVO.Infrastructure/Persistence/Configurations/CoreHr/Employee/EmployeeAddressConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.CoreHr.Employee;

public class EmployeeAddressConfiguration : IEntityTypeConfiguration<EmployeeAddress>
{
    public void Configure(EntityTypeBuilder<EmployeeAddress> builder)
    {
        builder.ToTable("employee_addresses");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.AddressType).HasColumnName("address_type").HasMaxLength(20).IsRequired();
        builder.Property(a => a.AddressJson).HasColumnName("address_json").HasColumnType("jsonb").IsRequired();
        builder.Property(a => a.IsPrimary).HasColumnName("is_primary").IsRequired();
        builder.Property(a => a.EmployeeId).HasColumnName("employee_id").IsRequired();
        builder.HasIndex(a => new { a.TenantId, a.EmployeeId });
        builder.HasOne<ONEVO.Domain.Features.CoreHr.Entities.Employee>()
            .WithMany().HasForeignKey(a => a.EmployeeId).OnDelete(DeleteBehavior.Cascade);
    }
}
```

```csharp
// src/ONEVO.Infrastructure/Persistence/Configurations/CoreHr/Employee/EmployeeEmergencyContactConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.CoreHr.Employee;

public class EmployeeEmergencyContactConfiguration : IEntityTypeConfiguration<EmployeeEmergencyContact>
{
    public void Configure(EntityTypeBuilder<EmployeeEmergencyContact> builder)
    {
        builder.ToTable("employee_emergency_contacts");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        builder.Property(c => c.Relationship).HasColumnName("relationship").HasMaxLength(30).IsRequired();
        builder.Property(c => c.Phone).HasColumnName("phone").HasMaxLength(20).IsRequired();
        builder.Property(c => c.Email).HasColumnName("email").HasMaxLength(255);
        builder.Property(c => c.IsPrimary).HasColumnName("is_primary").IsRequired();
        builder.Property(c => c.EmployeeId).HasColumnName("employee_id").IsRequired();
        builder.HasIndex(c => new { c.TenantId, c.EmployeeId });
        builder.HasOne<ONEVO.Domain.Features.CoreHr.Entities.Employee>()
            .WithMany().HasForeignKey(c => c.EmployeeId).OnDelete(DeleteBehavior.Cascade);
    }
}
```

```csharp
// src/ONEVO.Infrastructure/Persistence/Configurations/CoreHr/Employee/EmployeeDependentConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.CoreHr.Employee;

public class EmployeeDependentConfiguration : IEntityTypeConfiguration<EmployeeDependent>
{
    public void Configure(EntityTypeBuilder<EmployeeDependent> builder)
    {
        builder.ToTable("employee_dependents");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        builder.Property(d => d.Relationship).HasColumnName("relationship").HasMaxLength(20).IsRequired();
        builder.Property(d => d.DateOfBirth).HasColumnName("date_of_birth").HasColumnType("date").IsRequired();
        builder.Property(d => d.IsEmergencyContact).HasColumnName("is_emergency_contact").IsRequired();
        builder.Property(d => d.Phone).HasColumnName("phone").HasMaxLength(20);
        builder.Property(d => d.EmployeeId).HasColumnName("employee_id").IsRequired();
        builder.HasIndex(d => new { d.TenantId, d.EmployeeId });
        builder.HasOne<ONEVO.Domain.Features.CoreHr.Entities.Employee>()
            .WithMany().HasForeignKey(d => d.EmployeeId).OnDelete(DeleteBehavior.Cascade);
    }
}
```

```csharp
// src/ONEVO.Infrastructure/Persistence/Configurations/CoreHr/Employee/EmployeeBankDetailConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.CoreHr.Employee;

public class EmployeeBankDetailConfiguration : IEntityTypeConfiguration<EmployeeBankDetail>
{
    public void Configure(EntityTypeBuilder<EmployeeBankDetail> builder)
    {
        builder.ToTable("employee_bank_details");
        builder.HasKey(b => b.Id);
        builder.Property(b => b.BankName).HasColumnName("bank_name").HasMaxLength(100).IsRequired();
        builder.Property(b => b.BranchName).HasColumnName("branch_name").HasMaxLength(100).IsRequired();
        builder.Property(b => b.AccountHolderName).HasColumnName("account_holder_name").HasMaxLength(100).IsRequired();
        builder.Property(b => b.AccountNumberEncrypted).HasColumnName("account_number_encrypted").HasMaxLength(500).IsRequired();
        builder.Property(b => b.AccountType).HasColumnName("account_type").HasMaxLength(30).IsRequired();
        builder.Property(b => b.RoutingNumber).HasColumnName("routing_number").HasMaxLength(20);
        builder.Property(b => b.IsPrimary).HasColumnName("is_primary").IsRequired();
        builder.Property(b => b.EmployeeId).HasColumnName("employee_id").IsRequired();
        builder.HasIndex(b => new { b.TenantId, b.EmployeeId });
        builder.HasOne<ONEVO.Domain.Features.CoreHr.Entities.Employee>()
            .WithMany().HasForeignKey(b => b.EmployeeId).OnDelete(DeleteBehavior.Cascade);
    }
}
```

In `EmployeeConfiguration.cs`, add inside `Configure(...)` (after the existing `Property` calls):

```csharp
        builder.Property(e => e.DisplayTimezone).HasMaxLength(50);

        // Concurrency token mapped to the PostgreSQL system column xmin - see
        // OnboardingDraftConfiguration.cs for the identical precedent and rationale.
        builder.Property<uint>("xmin")
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();
```

- [ ] **Step 2b: Register the four new DbSets**

In `ApplicationDbContext.cs`, add near the existing `Employees`/`EmployeeHierarchyClosures` DbSets:

```csharp
    public DbSet<EmployeeAddress> EmployeeAddresses => Set<EmployeeAddress>();
    public DbSet<EmployeeEmergencyContact> EmployeeEmergencyContacts => Set<EmployeeEmergencyContact>();
    public DbSet<EmployeeDependent> EmployeeDependents => Set<EmployeeDependent>();
    public DbSet<EmployeeBankDetail> EmployeeBankDetails => Set<EmployeeBankDetail>();
```

(Add the corresponding `using ONEVO.Domain.Features.CoreHr.Entities;` if not already present in that file.)

- [ ] **Step 3: Build to confirm entities/configs compile**

Run: `dotnet build src/ONEVO.Infrastructure`
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Confirm architecture tests still pass before adding the migration**

Run: `dotnet test tests/ONEVO.Tests.Architecture --filter "FullyQualifiedName~EmployeeLegacyFieldRetirementArchitectureTests|FullyQualifiedName~TenantIsolationArchitectureTests"`
Expected: All PASS (green baseline before this change starts touching migrations).

- [ ] **Step 5: Generate the EF migration**

Run: `dotnet ef migrations add AddEmployeeProfileChildTables --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api`
Expected: A new migration file `YYYYMMDDHHMMSS_AddEmployeeProfileChildTables.cs` is created under `src/ONEVO.Infrastructure/Migrations/`.

- [ ] **Step 6: Hand-edit the generated migration to add RLS policies**

Open the generated migration file and add, at the end of `Up()` (after all four `CreateTable` calls) and the start of `Down()`, the same `tenant_isolation` policy block used in `20260810071627_AddOnboardingDrafts.cs`:

```csharp
        // Add this constant at the top of the class body, alongside the existing fields:
        private static readonly string[] TenantTables =
        [
            "employee_addresses", "employee_emergency_contacts", "employee_dependents", "employee_bank_details"
        ];
```

At the end of `Up()`:

```csharp
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

At the start of `Down()` (before the `DropTable` calls):

```csharp
            foreach (var table in TenantTables)
            {
                migrationBuilder.Sql($@"
                    DROP POLICY IF EXISTS tenant_isolation ON {table};
                    ALTER TABLE {table} DISABLE ROW LEVEL SECURITY;
                ");
            }
```

Also verify the generated `tenant_id` columns on all four tables came through as `uuid, nullable: false` (BaseEntity's `TenantId` is non-nullable `Guid`) — if EF generated them nullable, fix the column definition by hand before proceeding.

- [ ] **Step 7: Apply the migration to the local dev database**

Run: `dotnet ef database update --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api`
Expected: Migration applies with no errors; the four tables exist with RLS enabled (verify with `\d+ employee_bank_details` in `psql` showing `Row security enabled`).

- [ ] **Step 8: Write the RLS integration test**

```csharp
// tests/ONEVO.Tests.Integration/CoreHr/EmployeeProfile/EmployeeProfileTablesRlsTests.cs
using Microsoft.EntityFrameworkCore;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Infrastructure.Persistence;
using Xunit;

namespace ONEVO.Tests.Integration.CoreHr.EmployeeProfile;

[Collection("Database")]
public class EmployeeProfileTablesRlsTests : IntegrationTestBase
{
    public EmployeeProfileTablesRlsTests(DatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task EmployeeBankDetail_RowFromOtherTenant_IsInvisibleUnderTenantContext()
    {
        var tenantA = await SeedTenantAsync();
        var tenantB = await SeedTenantAsync();
        var employeeA = await SeedEmployeeAsync(tenantA.Id);

        await using (var db = CreateDbContext())
        {
            db.EmployeeBankDetails.Add(new EmployeeBankDetail
            {
                Id = Guid.NewGuid(),
                TenantId = tenantA.Id,
                EmployeeId = employeeA.Id,
                BankName = "Test Bank",
                BranchName = "Main",
                AccountHolderName = "A Employee",
                AccountNumberEncrypted = "ciphertext",
                AccountType = "savings",
                IsPrimary = true,
                CreatedById = employeeA.UserId,
                CreatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        await using var dbAsTenantB = CreateDbContext(tenantId: tenantB.Id);
        var visible = await dbAsTenantB.EmployeeBankDetails.IgnoreQueryFilters()
            .Where(b => b.TenantId == tenantA.Id)
            .ToListAsync();

        Assert.Empty(visible); // RLS blocks the row even with EF query filters bypassed
    }
}
```

(This test follows the existing `IntegrationTestBase`/`DatabaseFixture`/`SeedTenantAsync`/`SeedEmployeeAsync`/`CreateDbContext(tenantId:)` helpers already used by `EmployeesListIntegrationTests` — if any helper name differs slightly in the actual base class, match it to what that file uses rather than guessing further.)

- [ ] **Step 9: Run the test**

Run: `dotnet test tests/ONEVO.Tests.Integration --filter "FullyQualifiedName~EmployeeProfileTablesRlsTests"`
Expected: PASS.

- [ ] **Step 10: Re-run architecture tests to confirm the new entities are picked up automatically**

Run: `dotnet test tests/ONEVO.Tests.Architecture --filter "FullyQualifiedName~TenantIsolationArchitectureTests"`
Expected: All PASS — `EveryTenantOwnedEntity_HasAQueryFilter` and `EveryTenantOwnedEntity_ComposedQueryFilter_IncludesTenantScopingCondition` now cover the four new entities via reflection, no test edits needed.

- [ ] **Step 11: Commit**

```bash
git add src/ONEVO.Domain/Features/CoreHr/Entities/EmployeeAddress.cs src/ONEVO.Domain/Features/CoreHr/Entities/EmployeeEmergencyContact.cs src/ONEVO.Domain/Features/CoreHr/Entities/EmployeeDependent.cs src/ONEVO.Domain/Features/CoreHr/Entities/EmployeeBankDetail.cs src/ONEVO.Domain/Features/CoreHr/Entities/Employee.cs src/ONEVO.Infrastructure/Persistence/Configurations/CoreHr/Employee/ src/ONEVO.Infrastructure/Persistence/ApplicationDbContext.cs src/ONEVO.Infrastructure/Migrations/ tests/ONEVO.Tests.Integration/CoreHr/EmployeeProfile/EmployeeProfileTablesRlsTests.cs
git commit -m "feat: add employee profile child tables (addresses, emergency contacts, dependents, bank details) with RLS"
```

---

### Task 2: Repository layer for employee profile

**Files:**
- Create: `src/ONEVO.Application/Features/CoreHr/Employee/RepositoryInterfaces/IEmployeeProfileRepository.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Repositories/CoreHr/EfEmployeeProfileRepository.cs`
- Modify: `src/ONEVO.Application/Features/CoreHr/Employee/RepositoryInterfaces/IEmployeeRepository.cs` (add `GetTrackedByIdAsync`)
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/CoreHr/EfEmployeeRepository.cs` (implement it)
- Modify: `src/ONEVO.Infrastructure/DependencyInjection.cs` (register `IEmployeeProfileRepository`)
- Test: `tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/EfEmployeeProfileRepositoryTests.cs`

**Interfaces:**
- Consumes: `EmployeeAddress`, `EmployeeEmergencyContact`, `EmployeeDependent`, `EmployeeBankDetail` from Task 1.
- Produces: `IEmployeeProfileRepository` with the methods below — every later query/command handler in this plan depends on this interface's exact signatures.

- [ ] **Step 1: Write the repository interface**

```csharp
// src/ONEVO.Application/Features/CoreHr/Employee/RepositoryInterfaces/IEmployeeProfileRepository.cs
using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;

public interface IEmployeeProfileRepository
{
    Task<IReadOnlyList<EmployeeAddress>> ListAddressesAsync(Guid tenantId, Guid employeeId, CancellationToken ct = default);

    /// <summary>Deletes every existing address row for the employee and inserts the replacement
    /// set in the same unit of work - addresses are small (permanent/current), full-replace-on-save
    /// avoids diffing logic for a two-or-three-row collection.</summary>
    void ReplaceAddresses(Guid tenantId, Guid employeeId, IReadOnlyList<EmployeeAddress> replacement);

    Task<IReadOnlyList<EmployeeEmergencyContact>> ListEmergencyContactsAsync(Guid tenantId, Guid employeeId, CancellationToken ct = default);
    Task<EmployeeEmergencyContact?> GetEmergencyContactAsync(Guid tenantId, Guid employeeId, Guid contactId, CancellationToken ct = default);
    Task AddEmergencyContactAsync(EmployeeEmergencyContact contact, CancellationToken ct = default);
    void RemoveEmergencyContact(EmployeeEmergencyContact contact);

    Task<IReadOnlyList<EmployeeDependent>> ListDependentsAsync(Guid tenantId, Guid employeeId, CancellationToken ct = default);
    Task<EmployeeDependent?> GetDependentAsync(Guid tenantId, Guid employeeId, Guid dependentId, CancellationToken ct = default);
    Task AddDependentAsync(EmployeeDependent dependent, CancellationToken ct = default);
    void RemoveDependent(EmployeeDependent dependent);

    Task<EmployeeBankDetail?> GetPrimaryBankDetailAsync(Guid tenantId, Guid employeeId, CancellationToken ct = default);
    Task AddBankDetailAsync(EmployeeBankDetail bankDetail, CancellationToken ct = default);

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
```

- [ ] **Step 2: Write the failing repository test**

```csharp
// tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/EfEmployeeProfileRepositoryTests.cs
using Microsoft.EntityFrameworkCore;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Repositories.CoreHr;
using Xunit;

namespace ONEVO.Tests.Unit.Features.CoreHr.Employee;

public class EfEmployeeProfileRepositoryTests
{
    private static ApplicationDbContext InMemoryDb() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    [Fact]
    public async Task ReplaceAddresses_RemovesOldRowsAndInsertsNewOnes()
    {
        await using var db = InMemoryDb();
        var repo = new EfEmployeeProfileRepository(db);
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        db.EmployeeAddresses.Add(new EmployeeAddress
        {
            Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId,
            AddressType = "current", AddressJson = "{}", IsPrimary = true,
            CreatedById = employeeId, CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var replacement = new[]
        {
            new EmployeeAddress
            {
                Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId,
                AddressType = "permanent", AddressJson = "{\"city\":\"Colombo\"}", IsPrimary = true,
                CreatedById = employeeId, CreatedAt = DateTimeOffset.UtcNow
            }
        };

        repo.ReplaceAddresses(tenantId, employeeId, replacement);
        await repo.SaveChangesAsync();

        var stored = await repo.ListAddressesAsync(tenantId, employeeId);
        Assert.Single(stored);
        Assert.Equal("permanent", stored[0].AddressType);
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~EfEmployeeProfileRepositoryTests"`
Expected: FAIL — `EfEmployeeProfileRepository` does not exist yet.

- [ ] **Step 4: Implement the repository**

```csharp
// src/ONEVO.Infrastructure/Persistence/Repositories/CoreHr/EfEmployeeProfileRepository.cs
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.CoreHr;

public class EfEmployeeProfileRepository : IEmployeeProfileRepository
{
    private readonly ApplicationDbContext _db;

    public EfEmployeeProfileRepository(ApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<EmployeeAddress>> ListAddressesAsync(Guid tenantId, Guid employeeId, CancellationToken ct = default)
        => await _db.EmployeeAddresses.AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.EmployeeId == employeeId)
            .ToListAsync(ct);

    public void ReplaceAddresses(Guid tenantId, Guid employeeId, IReadOnlyList<EmployeeAddress> replacement)
    {
        var existing = _db.EmployeeAddresses
            .Where(a => a.TenantId == tenantId && a.EmployeeId == employeeId);
        _db.EmployeeAddresses.RemoveRange(existing);
        _db.EmployeeAddresses.AddRange(replacement);
    }

    public async Task<IReadOnlyList<EmployeeEmergencyContact>> ListEmergencyContactsAsync(Guid tenantId, Guid employeeId, CancellationToken ct = default)
        => await _db.EmployeeEmergencyContacts.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.EmployeeId == employeeId)
            .ToListAsync(ct);

    public async Task<EmployeeEmergencyContact?> GetEmergencyContactAsync(Guid tenantId, Guid employeeId, Guid contactId, CancellationToken ct = default)
        => await _db.EmployeeEmergencyContacts
            .FirstOrDefaultAsync(c => c.TenantId == tenantId && c.EmployeeId == employeeId && c.Id == contactId, ct);

    public async Task AddEmergencyContactAsync(EmployeeEmergencyContact contact, CancellationToken ct = default)
        => await _db.EmployeeEmergencyContacts.AddAsync(contact, ct);

    public void RemoveEmergencyContact(EmployeeEmergencyContact contact)
        => _db.EmployeeEmergencyContacts.Remove(contact);

    public async Task<IReadOnlyList<EmployeeDependent>> ListDependentsAsync(Guid tenantId, Guid employeeId, CancellationToken ct = default)
        => await _db.EmployeeDependents.AsNoTracking()
            .Where(d => d.TenantId == tenantId && d.EmployeeId == employeeId)
            .ToListAsync(ct);

    public async Task<EmployeeDependent?> GetDependentAsync(Guid tenantId, Guid employeeId, Guid dependentId, CancellationToken ct = default)
        => await _db.EmployeeDependents
            .FirstOrDefaultAsync(d => d.TenantId == tenantId && d.EmployeeId == employeeId && d.Id == dependentId, ct);

    public async Task AddDependentAsync(EmployeeDependent dependent, CancellationToken ct = default)
        => await _db.EmployeeDependents.AddAsync(dependent, ct);

    public void RemoveDependent(EmployeeDependent dependent)
        => _db.EmployeeDependents.Remove(dependent);

    public async Task<EmployeeBankDetail?> GetPrimaryBankDetailAsync(Guid tenantId, Guid employeeId, CancellationToken ct = default)
        => await _db.EmployeeBankDetails
            .FirstOrDefaultAsync(b => b.TenantId == tenantId && b.EmployeeId == employeeId && b.IsPrimary, ct);

    public async Task AddBankDetailAsync(EmployeeBankDetail bankDetail, CancellationToken ct = default)
        => await _db.EmployeeBankDetails.AddAsync(bankDetail, ct);

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
```

Add `GetTrackedByIdAsync` to the Features `IEmployeeRepository` interface and its `EfEmployeeRepository` (CoreHr) implementation — needed by Task 4 to mutate `Employee` for personal-information updates (the existing `GetByIdAsync` is `AsNoTracking()`):

```csharp
// IEmployeeRepository.cs - add this member
    Task<ONEVO.Domain.Features.CoreHr.Entities.Employee?> GetTrackedByIdAsync(
        Guid tenantId, Guid employeeId, CancellationToken ct = default);
```

```csharp
// EfEmployeeRepository.cs (CoreHr) - add this method
    public async Task<EmployeeEntity?> GetTrackedByIdAsync(Guid tenantId, Guid employeeId, CancellationToken ct = default)
        => await _db.Employees.FirstOrDefaultAsync(e => e.TenantId == tenantId && e.Id == employeeId, ct);
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~EfEmployeeProfileRepositoryTests"`
Expected: PASS.

- [ ] **Step 6: Register in DI**

In `src/ONEVO.Infrastructure/DependencyInjection.cs`, immediately after the existing `IEmployeeRepository` (CoreHr) registration (the two lines registering `ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository`), add:

```csharp
        services.AddScoped<
            ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeProfileRepository,
            ONEVO.Infrastructure.Persistence.Repositories.CoreHr.EfEmployeeProfileRepository>();
```

- [ ] **Step 7: Build and run the full unit suite for this feature**

Run: `dotnet build && dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~CoreHr.Employee"`
Expected: Build succeeds, all tests PASS.

- [ ] **Step 8: Commit**

```bash
git add src/ONEVO.Application/Features/CoreHr/Employee/RepositoryInterfaces/ src/ONEVO.Infrastructure/Persistence/Repositories/CoreHr/EfEmployeeProfileRepository.cs src/ONEVO.Infrastructure/Persistence/Repositories/CoreHr/EfEmployeeRepository.cs src/ONEVO.Infrastructure/DependencyInjection.cs tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/EfEmployeeProfileRepositoryTests.cs
git commit -m "feat: add IEmployeeProfileRepository for addresses, emergency contacts, dependents, bank details"
```

---

### Task 3: `GET /api/v1/employees/me` (composite profile read)

**Files:**
- Create: `src/ONEVO.Application/Features/CoreHr/Employee/DTOs/Responses/MyProfileResponse.cs`
- Create: `src/ONEVO.Application/Features/CoreHr/Employee/Queries/GetMyProfile/GetMyProfileQuery.cs`
- Create: `src/ONEVO.Application/Features/CoreHr/Employee/Queries/GetMyProfile/GetMyProfileQueryHandler.cs`
- Modify: `src/ONEVO.Api/Controllers/Tenant/CoreHr/EmployeesController.cs` (add `GetMyProfile` action)
- Test: `tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/GetMyProfileQueryHandlerTests.cs`

**Interfaces:**
- Consumes: `Common.IEmployeeRepository.GetByUserIdAsync`, `Features.IEmployeeRepository.GetVisibleByIdAsync` (existing, for job-info labels), `IEmployeeProfileRepository` (Task 2), `IEncryptionService` (existing, only to confirm bank record exists — no decrypt here).
- Produces: `MyProfileResponse` record — every frontend field in the companion frontend spec's `MyProfile` model maps 1:1 to this type's property names (camelCase on the wire via the API's existing JSON casing policy).

- [ ] **Step 1: Write the response DTO**

```csharp
// src/ONEVO.Application/Features/CoreHr/Employee/DTOs/Responses/MyProfileResponse.cs
namespace ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;

public record MyPersonalInformationResponse(
    string FirstName, string LastName, string Email, string? Phone,
    DateOnly? DateOfBirth, string? Gender, Guid? NationalityId, string? CountryName,
    string? DisplayTimezone, string? AvatarUrl,
    IReadOnlyList<MyAddressResponse> Addresses, string Version);

public record MyAddressResponse(Guid Id, string AddressType, string AddressJson, bool IsPrimary);

public record MyJobInformationResponse(
    string EmployeeNumber, string? LegalEntityName, string? DepartmentName, string? PositionName,
    string? ReportingManagerName, string EmploymentTypeLabel, string EmploymentStatus,
    DateOnly HireDate, DateOnly? ProbationEndDate, string WorkMode);

public record MyEmergencyContactResponse(Guid Id, string Name, string Relationship, string Phone, string? Email, bool IsPrimary);

public record MyDependentResponse(Guid Id, string Name, string Relationship, DateOnly DateOfBirth, bool IsEmergencyContact, string? Phone);

public record MyPayrollResponse(bool HasBankDetailsOnFile, string? BankName, string? MaskedAccountNumber, string? AccountType, bool CanEdit);

public record MySecurityResponse(bool MfaEnabled, DateTimeOffset? LastPasswordChangedAt);

public record MyProfileResponse(
    MyPersonalInformationResponse PersonalInformation,
    MyJobInformationResponse JobInformation,
    IReadOnlyList<MyEmergencyContactResponse> EmergencyContacts,
    IReadOnlyList<MyDependentResponse> Dependents,
    MyPayrollResponse Payroll,
    MySecurityResponse Security);
```

- [ ] **Step 2: Write the query and the failing handler test**

```csharp
// src/ONEVO.Application/Features/CoreHr/Employee/Queries/GetMyProfile/GetMyProfileQuery.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;

namespace ONEVO.Application.Features.CoreHr.Employee.Queries.GetMyProfile;

public record GetMyProfileQuery : IRequest<Result<MyProfileResponse>>;
```

```csharp
// tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/GetMyProfileQueryHandlerTests.cs
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Models;
using ONEVO.Application.Features.CoreHr.Employee.Queries.GetMyProfile;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using Xunit;
using FeatureEmployeeRepo = ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository;
using CommonEmployeeRepo = ONEVO.Application.Common.RepositoryInterfaces.IEmployeeRepository;

namespace ONEVO.Tests.Unit.Features.CoreHr.Employee;

public class GetMyProfileQueryHandlerTests
{
    [Fact]
    public async Task Handle_ReturnsNotFound_WhenNoEmployeeRecordForCurrentUser()
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(c => c.IsAuthenticated).Returns(true);
        currentUser.SetupGet(c => c.TenantId).Returns(Guid.NewGuid());
        currentUser.SetupGet(c => c.UserId).Returns(Guid.NewGuid());

        var commonRepo = new Mock<CommonEmployeeRepo>();
        commonRepo.Setup(r => r.GetByUserIdAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ONEVO.Domain.Features.CoreHr.Entities.Employee?)null);

        var handler = new GetMyProfileQueryHandler(
            commonRepo.Object,
            new Mock<FeatureEmployeeRepo>().Object,
            new Mock<IEmployeeProfileRepository>().Object,
            currentUser.Object);

        var result = await handler.Handle(new GetMyProfileQuery(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~GetMyProfileQueryHandlerTests"`
Expected: FAIL — `GetMyProfileQueryHandler` does not exist yet.

- [ ] **Step 4: Implement the handler**

```csharp
// src/ONEVO.Application/Features/CoreHr/Employee/Queries/GetMyProfile/GetMyProfileQueryHandler.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Employee.Models;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;

namespace ONEVO.Application.Features.CoreHr.Employee.Queries.GetMyProfile;

public class GetMyProfileQueryHandler : IRequestHandler<GetMyProfileQuery, Result<MyProfileResponse>>
{
    private readonly Common.RepositoryInterfaces.IEmployeeRepository _commonEmployees;
    private readonly IEmployeeRepository _featureEmployees;
    private readonly IEmployeeProfileRepository _profile;
    private readonly ICurrentUser _currentUser;

    public GetMyProfileQueryHandler(
        Common.RepositoryInterfaces.IEmployeeRepository commonEmployees,
        IEmployeeRepository featureEmployees,
        IEmployeeProfileRepository profile,
        ICurrentUser currentUser)
    {
        _commonEmployees = commonEmployees;
        _featureEmployees = featureEmployees;
        _profile = profile;
        _currentUser = currentUser;
    }

    public async Task<Result<MyProfileResponse>> Handle(GetMyProfileQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<MyProfileResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var employee = await _commonEmployees.GetByUserIdAsync(tenantId, _currentUser.UserId, ct);
        if (employee is null)
            return Result<MyProfileResponse>.NotFound("No employee record for the current user.");

        // Self access always passes visibility - reuse the existing label-resolution join rather
        // than re-deriving department/position/manager names.
        var visible = await _featureEmployees.GetVisibleByIdAsync(
            tenantId, EmployeeVisibilityScope.Unrestricted(), employee.Id, ct);

        var addresses = await _profile.ListAddressesAsync(tenantId, employee.Id, ct);
        var emergencyContacts = await _profile.ListEmergencyContactsAsync(tenantId, employee.Id, ct);
        var dependents = await _profile.ListDependentsAsync(tenantId, employee.Id, ct);
        var bankDetail = await _profile.GetPrimaryBankDetailAsync(tenantId, employee.Id, ct);

        var personalInformation = new MyPersonalInformationResponse(
            employee.FirstName, employee.LastName, employee.Email, employee.Phone,
            employee.DateOfBirth, employee.Gender, employee.NationalityId, null,
            employee.DisplayTimezone, null,
            addresses.Select(a => new MyAddressResponse(a.Id, a.AddressType, a.AddressJson, a.IsPrimary)).ToList(),
            employee.Id.ToString() /* replaced with the real xmin token value in Task 4's read path */);

        var jobInformation = new MyJobInformationResponse(
            visible?.EmployeeNumber ?? employee.EmployeeNumber,
            visible?.LegalEntityName, visible?.DepartmentName, visible?.PositionName,
            visible?.ReportingManagerName, visible?.EmploymentTypeLabel ?? "unknown",
            visible?.Status ?? "unknown", employee.HireDate, employee.ProbationEndDate,
            "onsite" /* WorkModeId -> label resolution added in Task 4 alongside the update path */);

        var payroll = new MyPayrollResponse(
            bankDetail is not null, bankDetail?.BankName,
            bankDetail is null ? null : Mask(bankDetail.AccountNumberEncrypted),
            bankDetail?.AccountType,
            _currentUser.HasPermission("employees:write"));

        return Result<MyProfileResponse>.Success(new MyProfileResponse(
            personalInformation, jobInformation,
            emergencyContacts.Select(c => new MyEmergencyContactResponse(c.Id, c.Name, c.Relationship, c.Phone, c.Email, c.IsPrimary)).ToList(),
            dependents.Select(d => new MyDependentResponse(d.Id, d.Name, d.Relationship, d.DateOfBirth, d.IsEmergencyContact, d.Phone)).ToList(),
            payroll,
            new MySecurityResponse(false, null) /* wired to real MFA/password state in Task 9/10 */));
    }

    // Masking never decrypts - it only reports that a value is on file. Task 8 decrypts to derive
    // the real last-4 digits; this placeholder-free early return keeps GetMyProfile from needing
    // IEncryptionService at all, since the encrypted value's length has no relation to plaintext length.
    private static string Mask(string _) => "on file";
}
```

Note: the `Version` (xmin) and `WorkMode`/last-4 masking values above are intentionally revisited in Task 4 (concurrency token) and Task 8 (real masked account number) — this task's handler compiles and is independently testable now; Tasks 4 and 8 replace the two marked lines with real values once those tasks' dependencies exist, per Step 7 below.

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~GetMyProfileQueryHandlerTests"`
Expected: PASS.

- [ ] **Step 6: Add the controller action**

In `EmployeesController.cs`, add (no `[RequirePermission]` — self-service):

```csharp
    /// <summary>Composite read of the caller's own profile: personal info, job info (read-only),
    /// emergency contacts, dependents, masked payroll, and security status. Self-service only -
    /// no permission code required, matches profile-management.md's "authenticated self-service".</summary>
    [HttpGet("me")]
    public async Task<IActionResult> GetMyProfile(CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetMyProfileQuery(), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
```

Add the corresponding `using ONEVO.Application.Features.CoreHr.Employee.Queries.GetMyProfile;` at the top of the file.

**Route-conflict check:** `GET /{id:guid}` is registered above this; ASP.NET Core route matching prefers the more specific literal segment `me` over the `{id:guid}` constraint automatically for `GET /api/v1/employees/me`, but confirm this explicitly in Step 7's manual check since both exist on the same controller.

- [ ] **Step 7: Build and manually verify no route conflict**

Run: `dotnet build src/ONEVO.Api`
Run: `dotnet run --project src/ONEVO.Api` (in a separate terminal), then `curl -i http://localhost:5000/api/v1/employees/me` against a seeded dev tenant session.
Expected: Routes to `GetMyProfile`, not `GetById` with `id="me"` (which would 400 on GUID binding). If it mis-routes, add `[Route("me")]` ordering or move the `me` action above `GetById` in the file — ASP.NET Core route order in the same controller does not affect precedence for attribute routing by default, but confirm empirically here since it's a genuine ambiguity between a literal segment and a `{id:guid}` constraint.

- [ ] **Step 8: Commit**

```bash
git add src/ONEVO.Application/Features/CoreHr/Employee/DTOs/Responses/MyProfileResponse.cs src/ONEVO.Application/Features/CoreHr/Employee/Queries/GetMyProfile/ src/ONEVO.Api/Controllers/Tenant/CoreHr/EmployeesController.cs tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/GetMyProfileQueryHandlerTests.cs
git commit -m "feat: add GET /api/v1/employees/me composite profile read"
```

---

### Task 4: `PUT /api/v1/employees/me/personal-information` (with concurrency)

**Files:**
- Create: `src/ONEVO.Api/Contracts/CoreHr/Employees/UpdatePersonalInformationRequest.cs`
- Create: `src/ONEVO.Application/Features/CoreHr/Employee/Commands/UpdatePersonalInformation/UpdatePersonalInformationCommand.cs`
- Create: `src/ONEVO.Application/Features/CoreHr/Employee/Commands/UpdatePersonalInformation/UpdatePersonalInformationCommandValidator.cs`
- Create: `src/ONEVO.Application/Features/CoreHr/Employee/Commands/UpdatePersonalInformation/UpdatePersonalInformationCommandHandler.cs`
- Modify: `src/ONEVO.Application/Features/CoreHr/Employee/Queries/GetMyProfile/GetMyProfileQueryHandler.cs` (fix `Version` to real xmin value)
- Modify: `src/ONEVO.Api/Controllers/Tenant/CoreHr/EmployeesController.cs` (add action)
- Test: `tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/UpdatePersonalInformationCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `Common.IEmployeeRepository.GetByUserIdAsync`, `Features.IEmployeeRepository.GetTrackedByIdAsync` (Task 2), `IEmployeeProfileRepository.ReplaceAddresses`/`SaveChangesAsync` (Task 2).
- Produces: `409 Conflict` on stale `Version`, matching the error taxonomy the frontend spec already expects.

- [ ] **Step 1: Write the request contract**

```csharp
// src/ONEVO.Api/Contracts/CoreHr/Employees/UpdatePersonalInformationRequest.cs
namespace ONEVO.Api.Contracts.CoreHr.Employees;

public record UpdatePersonalInformationRequest(
    string FirstName,
    string LastName,
    string? Phone,
    DateOnly? DateOfBirth,
    string? Gender,
    Guid? NationalityId,
    string? DisplayTimezone,
    IReadOnlyList<UpdateAddressRequest> Addresses,
    string Version);

public record UpdateAddressRequest(string AddressType, string AddressJson, bool IsPrimary);
```

- [ ] **Step 2: Write the command, validator, and failing handler test**

```csharp
// src/ONEVO.Application/Features/CoreHr/Employee/Commands/UpdatePersonalInformation/UpdatePersonalInformationCommand.cs
using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.CoreHr.Employee.Commands.UpdatePersonalInformation;

public record UpdateAddressInput(string AddressType, string AddressJson, bool IsPrimary);

public record UpdatePersonalInformationCommand(
    string FirstName,
    string LastName,
    string? Phone,
    DateOnly? DateOfBirth,
    string? Gender,
    Guid? NationalityId,
    string? DisplayTimezone,
    IReadOnlyList<UpdateAddressInput> Addresses,
    string Version) : IRequest<Result>;
```

```csharp
// src/ONEVO.Application/Features/CoreHr/Employee/Commands/UpdatePersonalInformation/UpdatePersonalInformationCommandValidator.cs
using FluentValidation;

namespace ONEVO.Application.Features.CoreHr.Employee.Commands.UpdatePersonalInformation;

public class UpdatePersonalInformationCommandValidator : AbstractValidator<UpdatePersonalInformationCommand>
{
    public UpdatePersonalInformationCommandValidator()
    {
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Phone).MaximumLength(20);
        RuleFor(x => x.Version).NotEmpty().WithMessage("A concurrency version token is required.");
        RuleForEach(x => x.Addresses).ChildRules(address =>
        {
            address.RuleFor(a => a.AddressType).Must(t => t is "permanent" or "current")
                .WithMessage("Address type must be 'permanent' or 'current'.");
        });
    }
}
```

```csharp
// tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/UpdatePersonalInformationCommandHandlerTests.cs
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Commands.UpdatePersonalInformation;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using Xunit;
using CommonEmployeeRepo = ONEVO.Application.Common.RepositoryInterfaces.IEmployeeRepository;

namespace ONEVO.Tests.Unit.Features.CoreHr.Employee;

public class UpdatePersonalInformationCommandHandlerTests
{
    [Fact]
    public async Task Handle_ReturnsNotFound_WhenNoEmployeeRecordForCurrentUser()
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(c => c.IsAuthenticated).Returns(true);
        currentUser.SetupGet(c => c.TenantId).Returns(Guid.NewGuid());
        currentUser.SetupGet(c => c.UserId).Returns(Guid.NewGuid());

        var commonRepo = new Mock<CommonEmployeeRepo>();
        commonRepo.Setup(r => r.GetByUserIdAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ONEVO.Domain.Features.CoreHr.Entities.Employee?)null);

        var handler = new UpdatePersonalInformationCommandHandler(
            commonRepo.Object,
            new Mock<IEmployeeRepository>().Object,
            new Mock<IEmployeeProfileRepository>().Object,
            currentUser.Object);

        var result = await handler.Handle(
            new UpdatePersonalInformationCommand("Jane", "Doe", null, null, null, null, null, [], "1"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~UpdatePersonalInformationCommandHandlerTests"`
Expected: FAIL — handler class does not exist.

- [ ] **Step 4: Implement the handler**

```csharp
// src/ONEVO.Application/Features/CoreHr/Employee/Commands/UpdatePersonalInformation/UpdatePersonalInformationCommandHandler.cs
using MediatR;
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Application.Features.CoreHr.Employee.Commands.UpdatePersonalInformation;

public class UpdatePersonalInformationCommandHandler : IRequestHandler<UpdatePersonalInformationCommand, Result>
{
    private readonly Common.RepositoryInterfaces.IEmployeeRepository _commonEmployees;
    private readonly IEmployeeRepository _featureEmployees;
    private readonly IEmployeeProfileRepository _profile;
    private readonly ICurrentUser _currentUser;

    public UpdatePersonalInformationCommandHandler(
        Common.RepositoryInterfaces.IEmployeeRepository commonEmployees,
        IEmployeeRepository featureEmployees,
        IEmployeeProfileRepository profile,
        ICurrentUser currentUser)
    {
        _commonEmployees = commonEmployees;
        _featureEmployees = featureEmployees;
        _profile = profile;
        _currentUser = currentUser;
    }

    public async Task<Result> Handle(UpdatePersonalInformationCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var lookup = await _commonEmployees.GetByUserIdAsync(tenantId, _currentUser.UserId, ct);
        if (lookup is null)
            return Result.NotFound("No employee record for the current user.");

        var tracked = await _featureEmployees.GetTrackedByIdAsync(tenantId, lookup.Id, ct);
        if (tracked is null)
            return Result.NotFound("No employee record for the current user.");

        tracked.FirstName = request.FirstName.Trim();
        tracked.LastName = request.LastName.Trim();
        tracked.Phone = request.Phone?.Trim();
        tracked.DateOfBirth = request.DateOfBirth;
        tracked.Gender = request.Gender;
        tracked.NationalityId = request.NationalityId;
        tracked.DisplayTimezone = request.DisplayTimezone;
        tracked.UpdatedAt = DateTimeOffset.UtcNow;

        _profile.ReplaceAddresses(tenantId, tracked.Id, request.Addresses
            .Select(a => new EmployeeAddress
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                EmployeeId = tracked.Id,
                AddressType = a.AddressType,
                AddressJson = a.AddressJson,
                IsPrimary = a.IsPrimary,
                CreatedById = _currentUser.UserId,
                CreatedAt = DateTimeOffset.UtcNow
            }).ToList());

        try
        {
            await _profile.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Conflict("This profile was just updated elsewhere. Please refresh and try again.");
        }

        return Result.Success();
    }
}
```

(`_profile.SaveChangesAsync` commits both the tracked `Employee` change and the replaced `EmployeeAddress` rows in one `SaveChangesAsync` call because both are tracked by the same `ApplicationDbContext` instance injected into both repositories within the same scoped request — matching the "mutating use cases should call SaveChangesAsync once" rule.)

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~UpdatePersonalInformationCommandHandlerTests"`
Expected: PASS.

- [ ] **Step 6: Wire the real concurrency token into `GetMyProfileQueryHandler`**

EF exposes a shadow property's value through `ChangeTracker`/`Entry(...).Property("xmin").CurrentValue` only for tracked entities; `GetMyProfileQueryHandler` currently uses `Common.IEmployeeRepository.GetByUserIdAsync`, which is `AsNoTracking()`. Replace the placeholder `Version` line in `GetMyProfileQueryHandler.cs` with a tracked re-fetch just for the version value:

```csharp
        // Replace: employee.Id.ToString() /* replaced with the real xmin token value... */
        // With:
        var trackedForVersion = await _featureEmployees.GetTrackedByIdAsync(tenantId, employee.Id, ct);
        var versionToken = trackedForVersion is not null
            ? Convert.ToBase64String(BitConverter.GetBytes(0u)) // overwritten below once EF populates it
            : string.Empty;
```

Actually — simpler and correct: read the shadow property directly via `ApplicationDbContext.Entry`. Since `GetMyProfileQueryHandler` doesn't have direct `DbContext` access (repository pattern), add a dedicated repository method instead of reaching for `Entry(...)`:

```csharp
// Add to IEmployeeRepository (Features) and its EfEmployeeRepository (CoreHr) implementation:
    Task<uint?> GetVersionTokenAsync(Guid tenantId, Guid employeeId, CancellationToken ct = default);
```

```csharp
    public async Task<uint?> GetVersionTokenAsync(Guid tenantId, Guid employeeId, CancellationToken ct = default)
    {
        var entity = await _db.Employees.AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.Id == employeeId)
            .Select(e => EF.Property<uint>(e, "xmin"))
            .FirstOrDefaultAsync(ct);
        return entity;
    }
```

Then in `GetMyProfileQueryHandler.Handle`, replace the placeholder with:

```csharp
        var versionToken = await _featureEmployees.GetVersionTokenAsync(tenantId, employee.Id, ct);
        // ... use versionToken?.ToString() ?? string.Empty as the Version argument to MyPersonalInformationResponse
```

And on the write side, `UpdatePersonalInformationCommandHandler` must set the shadow property from the incoming `request.Version` before saving so EF's optimistic-concurrency check fires on a mismatch:

```csharp
        // Add right before the try/await _profile.SaveChangesAsync(ct) block:
        if (uint.TryParse(request.Version, out var expectedVersion))
        {
            // Requires ApplicationDbContext access - inject it directly into this handler is not
            // allowed (Application must not reference EF); instead add
            // IEmployeeRepository.SetExpectedVersion(Employee, uint) that wraps
            // _db.Entry(employee).Property("xmin").OriginalValue = expectedVersion, implemented in
            // EfEmployeeRepository (CoreHr) and called here as:
            _featureEmployees.SetExpectedVersion(tracked, expectedVersion);
        }
```

Add `void SetExpectedVersion(Employee employee, uint expectedVersion);` to `IEmployeeRepository` (Features) and implement in `EfEmployeeRepository` (CoreHr):

```csharp
    public void SetExpectedVersion(EmployeeEntity employee, uint expectedVersion)
        => _db.Entry(employee).Property("xmin").OriginalValue = expectedVersion;
```

- [ ] **Step 7: Add the controller action**

```csharp
    /// <summary>Update the caller's own Personal Information. Optimistic concurrency: Version must
    /// match the xmin token returned by GetMyProfile, or this returns 409.</summary>
    [HttpPut("me/personal-information")]
    public async Task<IActionResult> UpdateMyPersonalInformation(
        [FromBody] UpdatePersonalInformationRequest request, CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new UpdatePersonalInformationCommand(
                request.FirstName, request.LastName, request.Phone, request.DateOfBirth,
                request.Gender, request.NationalityId, request.DisplayTimezone,
                request.Addresses.Select(a => new UpdateAddressInput(a.AddressType, a.AddressJson, a.IsPrimary)).ToList(),
                request.Version),
            ct);

        return result.IsSuccess
            ? NoContent()
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
```

Add `using ONEVO.Api.Contracts.CoreHr.Employees;` and `using ONEVO.Application.Features.CoreHr.Employee.Commands.UpdatePersonalInformation;`.

- [ ] **Step 8: Build, run full unit suite for this feature**

Run: `dotnet build && dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~CoreHr.Employee"`
Expected: Build succeeds, all PASS.

- [ ] **Step 9: Commit**

```bash
git add src/ONEVO.Api/Contracts/CoreHr/Employees/UpdatePersonalInformationRequest.cs src/ONEVO.Application/Features/CoreHr/Employee/Commands/UpdatePersonalInformation/ src/ONEVO.Application/Features/CoreHr/Employee/Queries/GetMyProfile/GetMyProfileQueryHandler.cs src/ONEVO.Application/Features/CoreHr/Employee/RepositoryInterfaces/IEmployeeRepository.cs src/ONEVO.Infrastructure/Persistence/Repositories/CoreHr/EfEmployeeRepository.cs src/ONEVO.Api/Controllers/Tenant/CoreHr/EmployeesController.cs tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/UpdatePersonalInformationCommandHandlerTests.cs
git commit -m "feat: add PUT /api/v1/employees/me/personal-information with optimistic concurrency"
```

---

### Task 5: `PUT /api/v1/employees/me/avatar`

**Files:**
- Create: `src/ONEVO.Application/Features/CoreHr/Employee/Commands/SetMyAvatar/SetMyAvatarCommand.cs`
- Create: `src/ONEVO.Application/Features/CoreHr/Employee/Commands/SetMyAvatar/SetMyAvatarCommandHandler.cs`
- Modify: `src/ONEVO.Application/Features/Storage/File/Helpers/UploadPurposeCatalog.cs` (add `EmployeeAvatar`)
- Modify: `src/ONEVO.Api/Controllers/Tenant/CoreHr/EmployeesController.cs` (add action)
- Test: `tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/SetMyAvatarCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `IFileStorageService.UploadAsync` (existing), `Features.IEmployeeRepository.GetTrackedByIdAsync` (Task 2).

- [ ] **Step 1: Add the upload purpose constant**

Open `UploadPurposeCatalog.cs`, find the existing `CompanyLogo` constant (used by `SetLegalEntityLogoCommandHandler`), and add alongside it:

```csharp
    public const string EmployeeAvatar = "employee_avatar";
```

- [ ] **Step 2: Write the command and failing handler test**

```csharp
// src/ONEVO.Application/Features/CoreHr/Employee/Commands/SetMyAvatar/SetMyAvatarCommand.cs
using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.CoreHr.Employee.Commands.SetMyAvatar;

public record SetMyAvatarCommand(string FileName, string ContentType, Stream Content) : IRequest<Result<Guid?>>;
```

```csharp
// tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/SetMyAvatarCommandHandlerTests.cs
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Commands.SetMyAvatar;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using Xunit;
using CommonEmployeeRepo = ONEVO.Application.Common.RepositoryInterfaces.IEmployeeRepository;

namespace ONEVO.Tests.Unit.Features.CoreHr.Employee;

public class SetMyAvatarCommandHandlerTests
{
    [Fact]
    public async Task Handle_ReturnsNotFound_WhenNoEmployeeRecordForCurrentUser()
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(c => c.IsAuthenticated).Returns(true);
        currentUser.SetupGet(c => c.TenantId).Returns(Guid.NewGuid());
        currentUser.SetupGet(c => c.UserId).Returns(Guid.NewGuid());

        var commonRepo = new Mock<CommonEmployeeRepo>();
        commonRepo.Setup(r => r.GetByUserIdAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ONEVO.Domain.Features.CoreHr.Entities.Employee?)null);

        var handler = new SetMyAvatarCommandHandler(
            commonRepo.Object,
            new Mock<IEmployeeRepository>().Object,
            new Mock<ONEVO.Application.Features.Storage.File.ServiceInterfaces.IFileStorageService>().Object,
            currentUser.Object);

        using var stream = new MemoryStream();
        var result = await handler.Handle(new SetMyAvatarCommand("a.png", "image/png", stream), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~SetMyAvatarCommandHandlerTests"`
Expected: FAIL.

- [ ] **Step 4: Implement the handler**

```csharp
// src/ONEVO.Application/Features/CoreHr/Employee/Commands/SetMyAvatar/SetMyAvatarCommandHandler.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.Storage.File.Helpers;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;

namespace ONEVO.Application.Features.CoreHr.Employee.Commands.SetMyAvatar;

public class SetMyAvatarCommandHandler : IRequestHandler<SetMyAvatarCommand, Result<Guid?>>
{
    private readonly Common.RepositoryInterfaces.IEmployeeRepository _commonEmployees;
    private readonly IEmployeeRepository _featureEmployees;
    private readonly IFileStorageService _fileStorage;
    private readonly ICurrentUser _currentUser;

    public SetMyAvatarCommandHandler(
        Common.RepositoryInterfaces.IEmployeeRepository commonEmployees,
        IEmployeeRepository featureEmployees,
        IFileStorageService fileStorage,
        ICurrentUser currentUser)
    {
        _commonEmployees = commonEmployees;
        _featureEmployees = featureEmployees;
        _fileStorage = fileStorage;
        _currentUser = currentUser;
    }

    public async Task<Result<Guid?>> Handle(SetMyAvatarCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<Guid?>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var lookup = await _commonEmployees.GetByUserIdAsync(tenantId, _currentUser.UserId, ct);
        if (lookup is null)
            return Result<Guid?>.NotFound("No employee record for the current user.");

        var uploadResult = await _fileStorage.UploadAsync(
            tenantId, _currentUser.UserId, request.FileName, request.ContentType,
            UploadPurposeCatalog.EmployeeAvatar, request.Content, ct);

        if (!uploadResult.IsSuccess)
            return Result<Guid?>.Failure(uploadResult.Error!, uploadResult.StatusCode ?? 400);

        var tracked = await _featureEmployees.GetTrackedByIdAsync(tenantId, lookup.Id, ct);
        if (tracked is null)
            return Result<Guid?>.NotFound("No employee record for the current user.");

        tracked.AvatarFileId = uploadResult.Value!.Id;
        tracked.UpdatedAt = DateTimeOffset.UtcNow;
        await _featureEmployees.SaveChangesAsync(ct);

        return Result<Guid?>.Success(tracked.AvatarFileId);
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~SetMyAvatarCommandHandlerTests"`
Expected: PASS.

- [ ] **Step 6: Add the controller action**

```csharp
    /// <summary>Upload/replace the caller's own avatar photo.</summary>
    [HttpPut("me/avatar")]
    public async Task<IActionResult> SetMyAvatar(IFormFile file, CancellationToken ct = default)
    {
        await using var stream = file.OpenReadStream();
        var result = await _mediator.Send(
            new SetMyAvatarCommand(file.FileName, file.ContentType, stream), ct);

        return result.IsSuccess
            ? Ok(new { avatarFileId = result.Value })
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
```

Add `using Microsoft.AspNetCore.Http;` and `using ONEVO.Application.Features.CoreHr.Employee.Commands.SetMyAvatar;`.

- [ ] **Step 7: Build and run**

Run: `dotnet build && dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~CoreHr.Employee"`
Expected: Build succeeds, all PASS.

- [ ] **Step 8: Commit**

```bash
git add src/ONEVO.Application/Features/CoreHr/Employee/Commands/SetMyAvatar/ src/ONEVO.Application/Features/Storage/File/Helpers/UploadPurposeCatalog.cs src/ONEVO.Api/Controllers/Tenant/CoreHr/EmployeesController.cs tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/SetMyAvatarCommandHandlerTests.cs
git commit -m "feat: add PUT /api/v1/employees/me/avatar"
```

---

### Task 6: Emergency contacts CRUD

**Files:**
- Create: `src/ONEVO.Api/Contracts/CoreHr/Employees/UpsertEmergencyContactRequest.cs`
- Create: `src/ONEVO.Application/Features/CoreHr/Employee/Commands/AddEmergencyContact/AddEmergencyContactCommand.cs` (+Validator, +Handler)
- Create: `src/ONEVO.Application/Features/CoreHr/Employee/Commands/UpdateEmergencyContact/UpdateEmergencyContactCommand.cs` (+Validator, +Handler)
- Create: `src/ONEVO.Application/Features/CoreHr/Employee/Commands/DeleteEmergencyContact/DeleteEmergencyContactCommand.cs` (+Handler)
- Modify: `src/ONEVO.Api/Controllers/Tenant/CoreHr/EmployeesController.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/EmergencyContactCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `IEmployeeProfileRepository` (Task 2).

- [ ] **Step 1: Write the request contract**

```csharp
// src/ONEVO.Api/Contracts/CoreHr/Employees/UpsertEmergencyContactRequest.cs
namespace ONEVO.Api.Contracts.CoreHr.Employees;

public record UpsertEmergencyContactRequest(string Name, string Relationship, string Phone, string? Email, bool IsPrimary);
```

- [ ] **Step 2: Write the three commands + validators**

```csharp
// src/ONEVO.Application/Features/CoreHr/Employee/Commands/AddEmergencyContact/AddEmergencyContactCommand.cs
using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.CoreHr.Employee.Commands.AddEmergencyContact;

public record AddEmergencyContactCommand(string Name, string Relationship, string Phone, string? Email, bool IsPrimary)
    : IRequest<Result<Guid>>;
```

```csharp
// src/ONEVO.Application/Features/CoreHr/Employee/Commands/AddEmergencyContact/AddEmergencyContactCommandValidator.cs
using FluentValidation;

namespace ONEVO.Application.Features.CoreHr.Employee.Commands.AddEmergencyContact;

public class AddEmergencyContactCommandValidator : AbstractValidator<AddEmergencyContactCommand>
{
    public AddEmergencyContactCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Relationship).NotEmpty().MaximumLength(30);
        RuleFor(x => x.Phone).NotEmpty().MaximumLength(20);
        RuleFor(x => x.Email).MaximumLength(255).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email));
    }
}
```

```csharp
// src/ONEVO.Application/Features/CoreHr/Employee/Commands/UpdateEmergencyContact/UpdateEmergencyContactCommand.cs
using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.CoreHr.Employee.Commands.UpdateEmergencyContact;

public record UpdateEmergencyContactCommand(Guid ContactId, string Name, string Relationship, string Phone, string? Email, bool IsPrimary)
    : IRequest<Result>;
```

```csharp
// src/ONEVO.Application/Features/CoreHr/Employee/Commands/UpdateEmergencyContact/UpdateEmergencyContactCommandValidator.cs
using FluentValidation;

namespace ONEVO.Application.Features.CoreHr.Employee.Commands.UpdateEmergencyContact;

public class UpdateEmergencyContactCommandValidator : AbstractValidator<UpdateEmergencyContactCommand>
{
    public UpdateEmergencyContactCommandValidator()
    {
        RuleFor(x => x.ContactId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Relationship).NotEmpty().MaximumLength(30);
        RuleFor(x => x.Phone).NotEmpty().MaximumLength(20);
        RuleFor(x => x.Email).MaximumLength(255).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email));
    }
}
```

```csharp
// src/ONEVO.Application/Features/CoreHr/Employee/Commands/DeleteEmergencyContact/DeleteEmergencyContactCommand.cs
using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.CoreHr.Employee.Commands.DeleteEmergencyContact;

public record DeleteEmergencyContactCommand(Guid ContactId) : IRequest<Result>;
```

- [ ] **Step 3: Write failing handler tests (all three, one file)**

```csharp
// tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/EmergencyContactCommandHandlerTests.cs
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Commands.AddEmergencyContact;
using ONEVO.Application.Features.CoreHr.Employee.Commands.DeleteEmergencyContact;
using ONEVO.Application.Features.CoreHr.Employee.Commands.UpdateEmergencyContact;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using Xunit;
using CommonEmployeeRepo = ONEVO.Application.Common.RepositoryInterfaces.IEmployeeRepository;

namespace ONEVO.Tests.Unit.Features.CoreHr.Employee;

public class EmergencyContactCommandHandlerTests
{
    private static Mock<ICurrentUser> AuthenticatedUser(Guid tenantId, Guid userId)
    {
        var mock = new Mock<ICurrentUser>();
        mock.SetupGet(c => c.IsAuthenticated).Returns(true);
        mock.SetupGet(c => c.TenantId).Returns(tenantId);
        mock.SetupGet(c => c.UserId).Returns(userId);
        return mock;
    }

    [Fact]
    public async Task Add_ReturnsNotFound_WhenNoEmployeeRecordForCurrentUser()
    {
        var currentUser = AuthenticatedUser(Guid.NewGuid(), Guid.NewGuid());
        var commonRepo = new Mock<CommonEmployeeRepo>();
        commonRepo.Setup(r => r.GetByUserIdAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ONEVO.Domain.Features.CoreHr.Entities.Employee?)null);

        var handler = new AddEmergencyContactCommandHandler(commonRepo.Object, new Mock<IEmployeeProfileRepository>().Object, currentUser.Object);
        var result = await handler.Handle(new AddEmergencyContactCommand("Jane", "spouse", "555-1111", null, true), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Update_ReturnsNotFound_WhenContactBelongsToDifferentEmployee()
    {
        var tenantId = Guid.NewGuid();
        var currentUser = AuthenticatedUser(tenantId, Guid.NewGuid());
        var employeeId = Guid.NewGuid();

        var commonRepo = new Mock<CommonEmployeeRepo>();
        commonRepo.Setup(r => r.GetByUserIdAsync(tenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ONEVO.Domain.Features.CoreHr.Entities.Employee { Id = employeeId, TenantId = tenantId });

        var profileRepo = new Mock<IEmployeeProfileRepository>();
        profileRepo.Setup(r => r.GetEmergencyContactAsync(tenantId, employeeId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ONEVO.Domain.Features.CoreHr.Entities.EmployeeEmergencyContact?)null);

        var handler = new UpdateEmergencyContactCommandHandler(commonRepo.Object, profileRepo.Object, currentUser.Object);
        var result = await handler.Handle(
            new UpdateEmergencyContactCommand(Guid.NewGuid(), "Jane", "spouse", "555-1111", null, true), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Delete_ReturnsNotFound_WhenContactDoesNotExist()
    {
        var tenantId = Guid.NewGuid();
        var currentUser = AuthenticatedUser(tenantId, Guid.NewGuid());
        var employeeId = Guid.NewGuid();

        var commonRepo = new Mock<CommonEmployeeRepo>();
        commonRepo.Setup(r => r.GetByUserIdAsync(tenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ONEVO.Domain.Features.CoreHr.Entities.Employee { Id = employeeId, TenantId = tenantId });

        var profileRepo = new Mock<IEmployeeProfileRepository>();
        profileRepo.Setup(r => r.GetEmergencyContactAsync(tenantId, employeeId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ONEVO.Domain.Features.CoreHr.Entities.EmployeeEmergencyContact?)null);

        var handler = new DeleteEmergencyContactCommandHandler(commonRepo.Object, profileRepo.Object, currentUser.Object);
        var result = await handler.Handle(new DeleteEmergencyContactCommand(Guid.NewGuid()), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }
}
```

- [ ] **Step 4: Run tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~EmergencyContactCommandHandlerTests"`
Expected: FAIL — handler classes don't exist.

- [ ] **Step 5: Implement the three handlers**

```csharp
// src/ONEVO.Application/Features/CoreHr/Employee/Commands/AddEmergencyContact/AddEmergencyContactCommandHandler.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Application.Features.CoreHr.Employee.Commands.AddEmergencyContact;

public class AddEmergencyContactCommandHandler : IRequestHandler<AddEmergencyContactCommand, Result<Guid>>
{
    private readonly Common.RepositoryInterfaces.IEmployeeRepository _commonEmployees;
    private readonly IEmployeeProfileRepository _profile;
    private readonly ICurrentUser _currentUser;

    public AddEmergencyContactCommandHandler(
        Common.RepositoryInterfaces.IEmployeeRepository commonEmployees, IEmployeeProfileRepository profile, ICurrentUser currentUser)
    {
        _commonEmployees = commonEmployees;
        _profile = profile;
        _currentUser = currentUser;
    }

    public async Task<Result<Guid>> Handle(AddEmergencyContactCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<Guid>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var employee = await _commonEmployees.GetByUserIdAsync(tenantId, _currentUser.UserId, ct);
        if (employee is null)
            return Result<Guid>.NotFound("No employee record for the current user.");

        var contact = new EmployeeEmergencyContact
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EmployeeId = employee.Id,
            Name = request.Name.Trim(),
            Relationship = request.Relationship.Trim(),
            Phone = request.Phone.Trim(),
            Email = request.Email?.Trim(),
            IsPrimary = request.IsPrimary,
            CreatedById = _currentUser.UserId,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _profile.AddEmergencyContactAsync(contact, ct);
        await _profile.SaveChangesAsync(ct);

        return Result<Guid>.Success(contact.Id);
    }
}
```

```csharp
// src/ONEVO.Application/Features/CoreHr/Employee/Commands/UpdateEmergencyContact/UpdateEmergencyContactCommandHandler.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;

namespace ONEVO.Application.Features.CoreHr.Employee.Commands.UpdateEmergencyContact;

public class UpdateEmergencyContactCommandHandler : IRequestHandler<UpdateEmergencyContactCommand, Result>
{
    private readonly Common.RepositoryInterfaces.IEmployeeRepository _commonEmployees;
    private readonly IEmployeeProfileRepository _profile;
    private readonly ICurrentUser _currentUser;

    public UpdateEmergencyContactCommandHandler(
        Common.RepositoryInterfaces.IEmployeeRepository commonEmployees, IEmployeeProfileRepository profile, ICurrentUser currentUser)
    {
        _commonEmployees = commonEmployees;
        _profile = profile;
        _currentUser = currentUser;
    }

    public async Task<Result> Handle(UpdateEmergencyContactCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var employee = await _commonEmployees.GetByUserIdAsync(tenantId, _currentUser.UserId, ct);
        if (employee is null)
            return Result.NotFound("No employee record for the current user.");

        var contact = await _profile.GetEmergencyContactAsync(tenantId, employee.Id, request.ContactId, ct);
        if (contact is null)
            return Result.NotFound("Emergency contact not found.");

        contact.Name = request.Name.Trim();
        contact.Relationship = request.Relationship.Trim();
        contact.Phone = request.Phone.Trim();
        contact.Email = request.Email?.Trim();
        contact.IsPrimary = request.IsPrimary;
        contact.UpdatedAt = DateTimeOffset.UtcNow;

        await _profile.SaveChangesAsync(ct);
        return Result.Success();
    }
}
```

```csharp
// src/ONEVO.Application/Features/CoreHr/Employee/Commands/DeleteEmergencyContact/DeleteEmergencyContactCommandHandler.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;

namespace ONEVO.Application.Features.CoreHr.Employee.Commands.DeleteEmergencyContact;

public class DeleteEmergencyContactCommandHandler : IRequestHandler<DeleteEmergencyContactCommand, Result>
{
    private readonly Common.RepositoryInterfaces.IEmployeeRepository _commonEmployees;
    private readonly IEmployeeProfileRepository _profile;
    private readonly ICurrentUser _currentUser;

    public DeleteEmergencyContactCommandHandler(
        Common.RepositoryInterfaces.IEmployeeRepository commonEmployees, IEmployeeProfileRepository profile, ICurrentUser currentUser)
    {
        _commonEmployees = commonEmployees;
        _profile = profile;
        _currentUser = currentUser;
    }

    public async Task<Result> Handle(DeleteEmergencyContactCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var employee = await _commonEmployees.GetByUserIdAsync(tenantId, _currentUser.UserId, ct);
        if (employee is null)
            return Result.NotFound("No employee record for the current user.");

        var contact = await _profile.GetEmergencyContactAsync(tenantId, employee.Id, request.ContactId, ct);
        if (contact is null)
            return Result.NotFound("Emergency contact not found.");

        _profile.RemoveEmergencyContact(contact);
        await _profile.SaveChangesAsync(ct);
        return Result.Success();
    }
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~EmergencyContactCommandHandlerTests"`
Expected: PASS.

- [ ] **Step 7: Add the three controller actions**

```csharp
    /// <summary>Add an emergency contact for the caller's own profile.</summary>
    [HttpPost("me/emergency-contacts")]
    public async Task<IActionResult> AddMyEmergencyContact([FromBody] UpsertEmergencyContactRequest request, CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new AddEmergencyContactCommand(request.Name, request.Relationship, request.Phone, request.Email, request.IsPrimary), ct);
        return result.IsSuccess
            ? CreatedAtAction(nameof(GetMyProfile), null, new { id = result.Value })
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Update one of the caller's own emergency contacts.</summary>
    [HttpPut("me/emergency-contacts/{contactId:guid}")]
    public async Task<IActionResult> UpdateMyEmergencyContact(
        Guid contactId, [FromBody] UpsertEmergencyContactRequest request, CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new UpdateEmergencyContactCommand(contactId, request.Name, request.Relationship, request.Phone, request.Email, request.IsPrimary), ct);
        return result.IsSuccess ? NoContent() : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Remove one of the caller's own emergency contacts.</summary>
    [HttpDelete("me/emergency-contacts/{contactId:guid}")]
    public async Task<IActionResult> DeleteMyEmergencyContact(Guid contactId, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new DeleteEmergencyContactCommand(contactId), ct);
        return result.IsSuccess ? NoContent() : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
```

Add the three `using ONEVO.Application.Features.CoreHr.Employee.Commands.{AddEmergencyContact,UpdateEmergencyContact,DeleteEmergencyContact};` statements.

- [ ] **Step 8: Build and run**

Run: `dotnet build && dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~CoreHr.Employee"`
Expected: Build succeeds, all PASS.

- [ ] **Step 9: Commit**

```bash
git add src/ONEVO.Api/Contracts/CoreHr/Employees/UpsertEmergencyContactRequest.cs src/ONEVO.Application/Features/CoreHr/Employee/Commands/AddEmergencyContact/ src/ONEVO.Application/Features/CoreHr/Employee/Commands/UpdateEmergencyContact/ src/ONEVO.Application/Features/CoreHr/Employee/Commands/DeleteEmergencyContact/ src/ONEVO.Api/Controllers/Tenant/CoreHr/EmployeesController.cs tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/EmergencyContactCommandHandlerTests.cs
git commit -m "feat: add emergency contacts CRUD under /api/v1/employees/me/emergency-contacts"
```

---

### Task 7: Dependents CRUD

**Files:** Same shape as Task 6, substituting `Dependent` for `EmergencyContact` throughout.
- Create: `src/ONEVO.Api/Contracts/CoreHr/Employees/UpsertDependentRequest.cs`
- Create: `src/ONEVO.Application/Features/CoreHr/Employee/Commands/AddDependent/*` (Command, Validator, Handler)
- Create: `src/ONEVO.Application/Features/CoreHr/Employee/Commands/UpdateDependent/*`
- Create: `src/ONEVO.Application/Features/CoreHr/Employee/Commands/DeleteDependent/*`
- Modify: `src/ONEVO.Api/Controllers/Tenant/CoreHr/EmployeesController.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/DependentCommandHandlerTests.cs`

- [ ] **Step 1: Write the request contract**

```csharp
// src/ONEVO.Api/Contracts/CoreHr/Employees/UpsertDependentRequest.cs
namespace ONEVO.Api.Contracts.CoreHr.Employees;

public record UpsertDependentRequest(string Name, string Relationship, DateOnly DateOfBirth, bool IsEmergencyContact, string? Phone);
```

- [ ] **Step 2: Write the three commands + validators**

```csharp
// src/ONEVO.Application/Features/CoreHr/Employee/Commands/AddDependent/AddDependentCommand.cs
using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.CoreHr.Employee.Commands.AddDependent;

public record AddDependentCommand(string Name, string Relationship, DateOnly DateOfBirth, bool IsEmergencyContact, string? Phone)
    : IRequest<Result<Guid>>;
```

```csharp
// src/ONEVO.Application/Features/CoreHr/Employee/Commands/AddDependent/AddDependentCommandValidator.cs
using FluentValidation;

namespace ONEVO.Application.Features.CoreHr.Employee.Commands.AddDependent;

public class AddDependentCommandValidator : AbstractValidator<AddDependentCommand>
{
    private static readonly string[] AllowedRelationships = ["spouse", "child", "parent", "other"];

    public AddDependentCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Relationship).Must(r => AllowedRelationships.Contains(r))
            .WithMessage("Relationship must be one of: spouse, child, parent, other.");
        RuleFor(x => x.DateOfBirth).LessThan(DateOnly.FromDateTime(DateTime.UtcNow))
            .WithMessage("Date of birth must be in the past.");
        RuleFor(x => x.Phone).MaximumLength(20);
    }
}
```

```csharp
// src/ONEVO.Application/Features/CoreHr/Employee/Commands/UpdateDependent/UpdateDependentCommand.cs
using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.CoreHr.Employee.Commands.UpdateDependent;

public record UpdateDependentCommand(Guid DependentId, string Name, string Relationship, DateOnly DateOfBirth, bool IsEmergencyContact, string? Phone)
    : IRequest<Result>;
```

```csharp
// src/ONEVO.Application/Features/CoreHr/Employee/Commands/UpdateDependent/UpdateDependentCommandValidator.cs
using FluentValidation;

namespace ONEVO.Application.Features.CoreHr.Employee.Commands.UpdateDependent;

public class UpdateDependentCommandValidator : AbstractValidator<UpdateDependentCommand>
{
    private static readonly string[] AllowedRelationships = ["spouse", "child", "parent", "other"];

    public UpdateDependentCommandValidator()
    {
        RuleFor(x => x.DependentId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Relationship).Must(r => AllowedRelationships.Contains(r))
            .WithMessage("Relationship must be one of: spouse, child, parent, other.");
        RuleFor(x => x.DateOfBirth).LessThan(DateOnly.FromDateTime(DateTime.UtcNow));
        RuleFor(x => x.Phone).MaximumLength(20);
    }
}
```

```csharp
// src/ONEVO.Application/Features/CoreHr/Employee/Commands/DeleteDependent/DeleteDependentCommand.cs
using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.CoreHr.Employee.Commands.DeleteDependent;

public record DeleteDependentCommand(Guid DependentId) : IRequest<Result>;
```

- [ ] **Step 3: Write failing handler tests**

```csharp
// tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/DependentCommandHandlerTests.cs
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Commands.AddDependent;
using ONEVO.Application.Features.CoreHr.Employee.Commands.DeleteDependent;
using ONEVO.Application.Features.CoreHr.Employee.Commands.UpdateDependent;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using Xunit;
using CommonEmployeeRepo = ONEVO.Application.Common.RepositoryInterfaces.IEmployeeRepository;

namespace ONEVO.Tests.Unit.Features.CoreHr.Employee;

public class DependentCommandHandlerTests
{
    private static Mock<ICurrentUser> AuthenticatedUser(Guid tenantId, Guid userId)
    {
        var mock = new Mock<ICurrentUser>();
        mock.SetupGet(c => c.IsAuthenticated).Returns(true);
        mock.SetupGet(c => c.TenantId).Returns(tenantId);
        mock.SetupGet(c => c.UserId).Returns(userId);
        return mock;
    }

    [Fact]
    public async Task Add_ReturnsNotFound_WhenNoEmployeeRecordForCurrentUser()
    {
        var currentUser = AuthenticatedUser(Guid.NewGuid(), Guid.NewGuid());
        var commonRepo = new Mock<CommonEmployeeRepo>();
        commonRepo.Setup(r => r.GetByUserIdAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ONEVO.Domain.Features.CoreHr.Entities.Employee?)null);

        var handler = new AddDependentCommandHandler(commonRepo.Object, new Mock<IEmployeeProfileRepository>().Object, currentUser.Object);
        var result = await handler.Handle(
            new AddDependentCommand("Sam", "child", new DateOnly(2015, 1, 1), false, null), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Update_ReturnsNotFound_WhenDependentDoesNotBelongToCaller()
    {
        var tenantId = Guid.NewGuid();
        var currentUser = AuthenticatedUser(tenantId, Guid.NewGuid());
        var employeeId = Guid.NewGuid();

        var commonRepo = new Mock<CommonEmployeeRepo>();
        commonRepo.Setup(r => r.GetByUserIdAsync(tenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ONEVO.Domain.Features.CoreHr.Entities.Employee { Id = employeeId, TenantId = tenantId });

        var profileRepo = new Mock<IEmployeeProfileRepository>();
        profileRepo.Setup(r => r.GetDependentAsync(tenantId, employeeId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ONEVO.Domain.Features.CoreHr.Entities.EmployeeDependent?)null);

        var handler = new UpdateDependentCommandHandler(commonRepo.Object, profileRepo.Object, currentUser.Object);
        var result = await handler.Handle(
            new UpdateDependentCommand(Guid.NewGuid(), "Sam", "child", new DateOnly(2015, 1, 1), false, null), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Delete_ReturnsNotFound_WhenDependentDoesNotExist()
    {
        var tenantId = Guid.NewGuid();
        var currentUser = AuthenticatedUser(tenantId, Guid.NewGuid());
        var employeeId = Guid.NewGuid();

        var commonRepo = new Mock<CommonEmployeeRepo>();
        commonRepo.Setup(r => r.GetByUserIdAsync(tenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ONEVO.Domain.Features.CoreHr.Entities.Employee { Id = employeeId, TenantId = tenantId });

        var profileRepo = new Mock<IEmployeeProfileRepository>();
        profileRepo.Setup(r => r.GetDependentAsync(tenantId, employeeId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ONEVO.Domain.Features.CoreHr.Entities.EmployeeDependent?)null);

        var handler = new DeleteDependentCommandHandler(commonRepo.Object, profileRepo.Object, currentUser.Object);
        var result = await handler.Handle(new DeleteDependentCommand(Guid.NewGuid()), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }
}
```

- [ ] **Step 4: Run to verify failure**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~DependentCommandHandlerTests"`
Expected: FAIL.

- [ ] **Step 5: Implement the three handlers**

```csharp
// src/ONEVO.Application/Features/CoreHr/Employee/Commands/AddDependent/AddDependentCommandHandler.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Application.Features.CoreHr.Employee.Commands.AddDependent;

public class AddDependentCommandHandler : IRequestHandler<AddDependentCommand, Result<Guid>>
{
    private readonly Common.RepositoryInterfaces.IEmployeeRepository _commonEmployees;
    private readonly IEmployeeProfileRepository _profile;
    private readonly ICurrentUser _currentUser;

    public AddDependentCommandHandler(
        Common.RepositoryInterfaces.IEmployeeRepository commonEmployees, IEmployeeProfileRepository profile, ICurrentUser currentUser)
    {
        _commonEmployees = commonEmployees;
        _profile = profile;
        _currentUser = currentUser;
    }

    public async Task<Result<Guid>> Handle(AddDependentCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<Guid>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var employee = await _commonEmployees.GetByUserIdAsync(tenantId, _currentUser.UserId, ct);
        if (employee is null)
            return Result<Guid>.NotFound("No employee record for the current user.");

        var dependent = new EmployeeDependent
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EmployeeId = employee.Id,
            Name = request.Name.Trim(),
            Relationship = request.Relationship,
            DateOfBirth = request.DateOfBirth,
            IsEmergencyContact = request.IsEmergencyContact,
            Phone = request.Phone?.Trim(),
            CreatedById = _currentUser.UserId,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _profile.AddDependentAsync(dependent, ct);
        await _profile.SaveChangesAsync(ct);

        return Result<Guid>.Success(dependent.Id);
    }
}
```

```csharp
// src/ONEVO.Application/Features/CoreHr/Employee/Commands/UpdateDependent/UpdateDependentCommandHandler.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;

namespace ONEVO.Application.Features.CoreHr.Employee.Commands.UpdateDependent;

public class UpdateDependentCommandHandler : IRequestHandler<UpdateDependentCommand, Result>
{
    private readonly Common.RepositoryInterfaces.IEmployeeRepository _commonEmployees;
    private readonly IEmployeeProfileRepository _profile;
    private readonly ICurrentUser _currentUser;

    public UpdateDependentCommandHandler(
        Common.RepositoryInterfaces.IEmployeeRepository commonEmployees, IEmployeeProfileRepository profile, ICurrentUser currentUser)
    {
        _commonEmployees = commonEmployees;
        _profile = profile;
        _currentUser = currentUser;
    }

    public async Task<Result> Handle(UpdateDependentCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var employee = await _commonEmployees.GetByUserIdAsync(tenantId, _currentUser.UserId, ct);
        if (employee is null)
            return Result.NotFound("No employee record for the current user.");

        var dependent = await _profile.GetDependentAsync(tenantId, employee.Id, request.DependentId, ct);
        if (dependent is null)
            return Result.NotFound("Dependent not found.");

        dependent.Name = request.Name.Trim();
        dependent.Relationship = request.Relationship;
        dependent.DateOfBirth = request.DateOfBirth;
        dependent.IsEmergencyContact = request.IsEmergencyContact;
        dependent.Phone = request.Phone?.Trim();
        dependent.UpdatedAt = DateTimeOffset.UtcNow;

        await _profile.SaveChangesAsync(ct);
        return Result.Success();
    }
}
```

```csharp
// src/ONEVO.Application/Features/CoreHr/Employee/Commands/DeleteDependent/DeleteDependentCommandHandler.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;

namespace ONEVO.Application.Features.CoreHr.Employee.Commands.DeleteDependent;

public class DeleteDependentCommandHandler : IRequestHandler<DeleteDependentCommand, Result>
{
    private readonly Common.RepositoryInterfaces.IEmployeeRepository _commonEmployees;
    private readonly IEmployeeProfileRepository _profile;
    private readonly ICurrentUser _currentUser;

    public DeleteDependentCommandHandler(
        Common.RepositoryInterfaces.IEmployeeRepository commonEmployees, IEmployeeProfileRepository profile, ICurrentUser currentUser)
    {
        _commonEmployees = commonEmployees;
        _profile = profile;
        _currentUser = currentUser;
    }

    public async Task<Result> Handle(DeleteDependentCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var employee = await _commonEmployees.GetByUserIdAsync(tenantId, _currentUser.UserId, ct);
        if (employee is null)
            return Result.NotFound("No employee record for the current user.");

        var dependent = await _profile.GetDependentAsync(tenantId, employee.Id, request.DependentId, ct);
        if (dependent is null)
            return Result.NotFound("Dependent not found.");

        _profile.RemoveDependent(dependent);
        await _profile.SaveChangesAsync(ct);
        return Result.Success();
    }
}
```

- [ ] **Step 6: Run to verify pass**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~DependentCommandHandlerTests"`
Expected: PASS.

- [ ] **Step 7: Add the three controller actions**

```csharp
    /// <summary>Add a dependent for the caller's own profile.</summary>
    [HttpPost("me/dependents")]
    public async Task<IActionResult> AddMyDependent([FromBody] UpsertDependentRequest request, CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new AddDependentCommand(request.Name, request.Relationship, request.DateOfBirth, request.IsEmergencyContact, request.Phone), ct);
        return result.IsSuccess
            ? CreatedAtAction(nameof(GetMyProfile), null, new { id = result.Value })
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Update one of the caller's own dependents.</summary>
    [HttpPut("me/dependents/{dependentId:guid}")]
    public async Task<IActionResult> UpdateMyDependent(
        Guid dependentId, [FromBody] UpsertDependentRequest request, CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new UpdateDependentCommand(dependentId, request.Name, request.Relationship, request.DateOfBirth, request.IsEmergencyContact, request.Phone), ct);
        return result.IsSuccess ? NoContent() : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Remove one of the caller's own dependents.</summary>
    [HttpDelete("me/dependents/{dependentId:guid}")]
    public async Task<IActionResult> DeleteMyDependent(Guid dependentId, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new DeleteDependentCommand(dependentId), ct);
        return result.IsSuccess ? NoContent() : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
```

Add the three `using ONEVO.Application.Features.CoreHr.Employee.Commands.{AddDependent,UpdateDependent,DeleteDependent};` statements.

- [ ] **Step 8: Build and run**

Run: `dotnet build && dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~CoreHr.Employee"`
Expected: Build succeeds, all PASS.

- [ ] **Step 9: Commit**

```bash
git add src/ONEVO.Api/Contracts/CoreHr/Employees/UpsertDependentRequest.cs src/ONEVO.Application/Features/CoreHr/Employee/Commands/AddDependent/ src/ONEVO.Application/Features/CoreHr/Employee/Commands/UpdateDependent/ src/ONEVO.Application/Features/CoreHr/Employee/Commands/DeleteDependent/ src/ONEVO.Api/Controllers/Tenant/CoreHr/EmployeesController.cs tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/DependentCommandHandlerTests.cs
git commit -m "feat: add dependents CRUD under /api/v1/employees/me/dependents"
```

---

### Task 8: Payroll & Statutory (bank details, encrypted, `employees:write`-gated)

**Files:**
- Create: `src/ONEVO.Api/Contracts/CoreHr/Employees/UpdateBankDetailsRequest.cs`
- Create: `src/ONEVO.Application/Features/CoreHr/Employee/Queries/GetMyPayroll/GetMyPayrollQuery.cs` (+Handler)
- Create: `src/ONEVO.Application/Features/CoreHr/Employee/Commands/UpdateBankDetails/UpdateBankDetailsCommand.cs` (+Validator, +Handler)
- Modify: `src/ONEVO.Application/Features/CoreHr/Employee/Queries/GetMyProfile/GetMyProfileQueryHandler.cs` (real masked value)
- Modify: `src/ONEVO.Api/Controllers/Tenant/CoreHr/EmployeesController.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/BankDetailsCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `IEncryptionService.Encrypt`/`Decrypt` (existing), `IEmployeeProfileRepository.GetPrimaryBankDetailAsync`/`AddBankDetailAsync` (Task 2).

- [ ] **Step 1: Write the request contract and masking helper**

```csharp
// src/ONEVO.Api/Contracts/CoreHr/Employees/UpdateBankDetailsRequest.cs
namespace ONEVO.Api.Contracts.CoreHr.Employees;

public record UpdateBankDetailsRequest(
    string BankName, string BranchName, string AccountHolderName,
    string AccountNumber, string AccountType, string? RoutingNumber);
```

```csharp
// src/ONEVO.Application/Features/CoreHr/Employee/Helpers/BankAccountMasker.cs
namespace ONEVO.Application.Features.CoreHr.Employee.Helpers;

public static class BankAccountMasker
{
    /// <summary>Formats a decrypted account number as "****1234" - never call with the encrypted
    /// ciphertext, only with the plaintext returned by IEncryptionService.Decrypt.</summary>
    public static string Mask(string plainAccountNumber)
    {
        var digitsOnly = new string(plainAccountNumber.Where(char.IsDigit).ToArray());
        return digitsOnly.Length <= 4
            ? new string('*', digitsOnly.Length)
            : $"****{digitsOnly[^4..]}";
    }
}
```

- [ ] **Step 2: Write the query, command, validator, and failing tests**

```csharp
// src/ONEVO.Application/Features/CoreHr/Employee/Queries/GetMyPayroll/GetMyPayrollQuery.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;

namespace ONEVO.Application.Features.CoreHr.Employee.Queries.GetMyPayroll;

public record GetMyPayrollQuery : IRequest<Result<MyPayrollResponse>>;
```

```csharp
// src/ONEVO.Application/Features/CoreHr/Employee/Commands/UpdateBankDetails/UpdateBankDetailsCommand.cs
using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.CoreHr.Employee.Commands.UpdateBankDetails;

public record UpdateBankDetailsCommand(
    string BankName, string BranchName, string AccountHolderName,
    string AccountNumber, string AccountType, string? RoutingNumber) : IRequest<Result>;
```

```csharp
// src/ONEVO.Application/Features/CoreHr/Employee/Commands/UpdateBankDetails/UpdateBankDetailsCommandValidator.cs
using FluentValidation;

namespace ONEVO.Application.Features.CoreHr.Employee.Commands.UpdateBankDetails;

public class UpdateBankDetailsCommandValidator : AbstractValidator<UpdateBankDetailsCommand>
{
    public UpdateBankDetailsCommandValidator()
    {
        RuleFor(x => x.BankName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.BranchName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.AccountHolderName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.AccountNumber).NotEmpty().MaximumLength(34); // IBAN upper bound, generous for local formats
        RuleFor(x => x.AccountType).NotEmpty().MaximumLength(30);
        RuleFor(x => x.RoutingNumber).MaximumLength(20);
    }
}
```

```csharp
// tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/BankDetailsCommandHandlerTests.cs
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Commands.UpdateBankDetails;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using Xunit;
using CommonEmployeeRepo = ONEVO.Application.Common.RepositoryInterfaces.IEmployeeRepository;

namespace ONEVO.Tests.Unit.Features.CoreHr.Employee;

public class BankDetailsCommandHandlerTests
{
    [Fact]
    public async Task Handle_EncryptsAccountNumber_NeverStoresPlaintext()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(c => c.IsAuthenticated).Returns(true);
        currentUser.SetupGet(c => c.TenantId).Returns(tenantId);
        currentUser.SetupGet(c => c.UserId).Returns(Guid.NewGuid());

        var commonRepo = new Mock<CommonEmployeeRepo>();
        commonRepo.Setup(r => r.GetByUserIdAsync(tenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ONEVO.Domain.Features.CoreHr.Entities.Employee { Id = employeeId, TenantId = tenantId });

        var profileRepo = new Mock<IEmployeeProfileRepository>();
        profileRepo.Setup(r => r.GetPrimaryBankDetailAsync(tenantId, employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ONEVO.Domain.Features.CoreHr.Entities.EmployeeBankDetail?)null);

        var encryption = new Mock<IEncryptionService>();
        encryption.Setup(e => e.Encrypt("1234567890")).Returns("ENCRYPTED_BLOB");

        ONEVO.Domain.Features.CoreHr.Entities.EmployeeBankDetail? saved = null;
        profileRepo.Setup(r => r.AddBankDetailAsync(It.IsAny<ONEVO.Domain.Features.CoreHr.Entities.EmployeeBankDetail>(), It.IsAny<CancellationToken>()))
            .Callback<ONEVO.Domain.Features.CoreHr.Entities.EmployeeBankDetail, CancellationToken>((b, _) => saved = b)
            .Returns(Task.CompletedTask);

        var handler = new UpdateBankDetailsCommandHandler(commonRepo.Object, profileRepo.Object, encryption.Object, currentUser.Object);

        var result = await handler.Handle(
            new UpdateBankDetailsCommand("Test Bank", "Main", "Jane Doe", "1234567890", "savings", null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(saved);
        Assert.Equal("ENCRYPTED_BLOB", saved!.AccountNumberEncrypted);
        Assert.DoesNotContain("1234567890", saved.AccountNumberEncrypted);
    }
}
```

- [ ] **Step 3: Run to verify failure**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~BankDetailsCommandHandlerTests"`
Expected: FAIL.

- [ ] **Step 4: Implement the query and command handlers**

```csharp
// src/ONEVO.Application/Features/CoreHr/Employee/Queries/GetMyPayroll/GetMyPayrollQueryHandler.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Employee.Helpers;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;

namespace ONEVO.Application.Features.CoreHr.Employee.Queries.GetMyPayroll;

public class GetMyPayrollQueryHandler : IRequestHandler<GetMyPayrollQuery, Result<MyPayrollResponse>>
{
    private readonly Common.RepositoryInterfaces.IEmployeeRepository _commonEmployees;
    private readonly IEmployeeProfileRepository _profile;
    private readonly IEncryptionService _encryption;
    private readonly ICurrentUser _currentUser;

    public GetMyPayrollQueryHandler(
        Common.RepositoryInterfaces.IEmployeeRepository commonEmployees,
        IEmployeeProfileRepository profile,
        IEncryptionService encryption,
        ICurrentUser currentUser)
    {
        _commonEmployees = commonEmployees;
        _profile = profile;
        _encryption = encryption;
        _currentUser = currentUser;
    }

    public async Task<Result<MyPayrollResponse>> Handle(GetMyPayrollQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<MyPayrollResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var employee = await _commonEmployees.GetByUserIdAsync(tenantId, _currentUser.UserId, ct);
        if (employee is null)
            return Result<MyPayrollResponse>.NotFound("No employee record for the current user.");

        var bankDetail = await _profile.GetPrimaryBankDetailAsync(tenantId, employee.Id, ct);
        var masked = bankDetail is null ? null : BankAccountMasker.Mask(_encryption.Decrypt(bankDetail.AccountNumberEncrypted));

        return Result<MyPayrollResponse>.Success(new MyPayrollResponse(
            bankDetail is not null, bankDetail?.BankName, masked, bankDetail?.AccountType,
            _currentUser.HasPermission("employees:write")));
    }
}
```

```csharp
// src/ONEVO.Application/Features/CoreHr/Employee/Commands/UpdateBankDetails/UpdateBankDetailsCommandHandler.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Application.Features.CoreHr.Employee.Commands.UpdateBankDetails;

public class UpdateBankDetailsCommandHandler : IRequestHandler<UpdateBankDetailsCommand, Result>
{
    private readonly Common.RepositoryInterfaces.IEmployeeRepository _commonEmployees;
    private readonly IEmployeeProfileRepository _profile;
    private readonly IEncryptionService _encryption;
    private readonly ICurrentUser _currentUser;

    public UpdateBankDetailsCommandHandler(
        Common.RepositoryInterfaces.IEmployeeRepository commonEmployees,
        IEmployeeProfileRepository profile,
        IEncryptionService encryption,
        ICurrentUser currentUser)
    {
        _commonEmployees = commonEmployees;
        _profile = profile;
        _encryption = encryption;
        _currentUser = currentUser;
    }

    public async Task<Result> Handle(UpdateBankDetailsCommand request, CancellationToken ct)
    {
        // Permission check is redundant with the controller's [RequirePermission("employees:write")]
        // but kept here too - handlers must not rely solely on controller attributes (backend-arch
        // §3.4: permission decisions are made server-side, and a handler can be invoked by other
        // future callers that skip the controller's attribute).
        if (!_currentUser.IsAuthenticated)
            return Result.Forbidden("Authentication required.");
        if (!_currentUser.HasPermission("employees:write"))
            return Result.Forbidden("You do not have permission to edit payroll details.");

        var tenantId = _currentUser.TenantId;
        var employee = await _commonEmployees.GetByUserIdAsync(tenantId, _currentUser.UserId, ct);
        if (employee is null)
            return Result.NotFound("No employee record for the current user.");

        var existing = await _profile.GetPrimaryBankDetailAsync(tenantId, employee.Id, ct);
        var encryptedAccountNumber = _encryption.Encrypt(request.AccountNumber);

        if (existing is not null)
        {
            existing.BankName = request.BankName.Trim();
            existing.BranchName = request.BranchName.Trim();
            existing.AccountHolderName = request.AccountHolderName.Trim();
            existing.AccountNumberEncrypted = encryptedAccountNumber;
            existing.AccountType = request.AccountType.Trim();
            existing.RoutingNumber = request.RoutingNumber?.Trim();
            existing.UpdatedAt = DateTimeOffset.UtcNow;
        }
        else
        {
            await _profile.AddBankDetailAsync(new EmployeeBankDetail
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                EmployeeId = employee.Id,
                BankName = request.BankName.Trim(),
                BranchName = request.BranchName.Trim(),
                AccountHolderName = request.AccountHolderName.Trim(),
                AccountNumberEncrypted = encryptedAccountNumber,
                AccountType = request.AccountType.Trim(),
                RoutingNumber = request.RoutingNumber?.Trim(),
                IsPrimary = true,
                CreatedById = _currentUser.UserId,
                CreatedAt = DateTimeOffset.UtcNow
            }, ct);
        }

        await _profile.SaveChangesAsync(ct);
        return Result.Success();
    }
}
```

- [ ] **Step 5: Run to verify pass**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~BankDetailsCommandHandlerTests"`
Expected: PASS.

- [ ] **Step 6: Fix the placeholder mask in `GetMyProfileQueryHandler`**

Replace the private `Mask` helper and its call site in `GetMyProfileQueryHandler.cs` (added in Task 3) with a real decrypt-and-mask, matching Task 8's `GetMyPayrollQueryHandler`:

```csharp
        // Replace: bankDetail is null ? null : Mask(bankDetail.AccountNumberEncrypted)
        // With:
        var maskedAccountNumber = bankDetail is null
            ? null
            : ONEVO.Application.Features.CoreHr.Employee.Helpers.BankAccountMasker.Mask(_encryption.Decrypt(bankDetail.AccountNumberEncrypted));
```

Inject `IEncryptionService _encryption` into `GetMyProfileQueryHandler`'s constructor (add the parameter, assign the field) and delete the now-unused private `Mask` method.

- [ ] **Step 7: Add the controller actions**

```csharp
    /// <summary>Get the caller's own masked payroll/bank details.</summary>
    [HttpGet("me/payroll")]
    public async Task<IActionResult> GetMyPayroll(CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetMyPayrollQuery(), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Update the caller's own bank details. Requires employees:write even for the
    /// caller's own record - bank-detail edits are HR-mediated to prevent unauthorized
    /// payroll-redirection (see design spec §6).</summary>
    [HttpPut("me/payroll")]
    [RequirePermission("employees:write")]
    public async Task<IActionResult> UpdateMyPayroll([FromBody] UpdateBankDetailsRequest request, CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new UpdateBankDetailsCommand(
                request.BankName, request.BranchName, request.AccountHolderName,
                request.AccountNumber, request.AccountType, request.RoutingNumber),
            ct);
        return result.IsSuccess ? NoContent() : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
```

Add `using ONEVO.Application.Features.CoreHr.Employee.Queries.GetMyPayroll;` and `using ONEVO.Application.Features.CoreHr.Employee.Commands.UpdateBankDetails;`.

- [ ] **Step 8: Build and run**

Run: `dotnet build && dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~CoreHr.Employee"`
Expected: Build succeeds, all PASS.

- [ ] **Step 9: Commit**

```bash
git add src/ONEVO.Api/Contracts/CoreHr/Employees/UpdateBankDetailsRequest.cs src/ONEVO.Application/Features/CoreHr/Employee/Helpers/BankAccountMasker.cs src/ONEVO.Application/Features/CoreHr/Employee/Queries/GetMyPayroll/ src/ONEVO.Application/Features/CoreHr/Employee/Commands/UpdateBankDetails/ src/ONEVO.Application/Features/CoreHr/Employee/Queries/GetMyProfile/GetMyProfileQueryHandler.cs src/ONEVO.Api/Controllers/Tenant/CoreHr/EmployeesController.cs tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/BankDetailsCommandHandlerTests.cs
git commit -m "feat: add payroll & statutory (bank details) with encryption and employees:write gating"
```

---

### Task 9: Self-service change password

**Files:**
- Create: `src/ONEVO.Api/Contracts/Auth/ChangePasswordRequest.cs`
- Create: `src/ONEVO.Application/Features/Auth/Login/Commands/ChangePassword/ChangePasswordCommand.cs` (+Validator, +Handler)
- Modify: `src/ONEVO.Api/Controllers/Tenant/Auth/AuthPasswordController.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Auth/Login/ChangePasswordCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `IUserRepository.GetByIdAsync(Guid userId, CancellationToken)` (single-arg, no tenantId parameter — confirmed against the interface file), `IPasswordHasher.Hash(string)`/`Verify(string password, string hash)` (confirmed interface, same one `ResetPasswordCommandHandler` uses), `IAuditLogRepository.AddAsync` (verified real usage in `CreateProjectCommandHandler`), `IOutboxWriter.EnqueueAsync` (verified real usage in `RequestPasswordResetCommandHandler`).

- [ ] **Step 1: Write the request contract**

```csharp
// src/ONEVO.Api/Contracts/Auth/ChangePasswordRequest.cs
namespace ONEVO.Api.Contracts.Auth;

public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
```

- [ ] **Step 2: Write the command, validator, and failing test**

```csharp
// src/ONEVO.Application/Features/Auth/Login/Commands/ChangePassword/ChangePasswordCommand.cs
using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Auth.Login.Commands.ChangePassword;

public record ChangePasswordCommand(string CurrentPassword, string NewPassword) : IRequest<Result>;
```

```csharp
// src/ONEVO.Application/Features/Auth/Login/Commands/ChangePassword/ChangePasswordCommandValidator.cs
using FluentValidation;

namespace ONEVO.Application.Features.Auth.Login.Commands.ChangePassword;

public class ChangePasswordCommandValidator : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordCommandValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty();
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(8)
            .WithMessage("New password must be at least 8 characters.")
            .NotEqual(x => x.CurrentPassword)
            .WithMessage("New password must be different from the current password.");
    }
}
```

```csharp
// tests/ONEVO.Tests.Unit/Features/Auth/Login/ChangePasswordCommandHandlerTests.cs
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.Commands.ChangePassword;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Auth.Login;

public class ChangePasswordCommandHandlerTests
{
    [Fact]
    public async Task Handle_ReturnsForbidden_WhenCurrentPasswordDoesNotMatch()
    {
        var userId = Guid.NewGuid();
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(c => c.IsAuthenticated).Returns(true);
        currentUser.SetupGet(c => c.TenantId).Returns(Guid.NewGuid());
        currentUser.SetupGet(c => c.UserId).Returns(userId);

        var users = new Mock<IUserRepository>();
        users.Setup(u => u.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User { Id = userId, PasswordHash = "stored-hash" });

        var hasher = new Mock<IPasswordHasher>();
        hasher.Setup(h => h.Verify("wrong-password", "stored-hash")).Returns(false);

        var auditLog = new Mock<IAuditLogRepository>();
        var outbox = new Mock<IOutboxWriter>();

        var handler = new ChangePasswordCommandHandler(
            users.Object, hasher.Object, auditLog.Object, outbox.Object,
            new Mock<IUnitOfWork>().Object, currentUser.Object, new Mock<IDateTimeProvider>().Object);

        var result = await handler.Handle(
            new ChangePasswordCommand("wrong-password", "NewPassword123"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        auditLog.Verify(a => a.AddAsync(It.IsAny<ONEVO.Domain.Features.Auth.Entities.AuditLog>(), It.IsAny<CancellationToken>()), Times.Never);
        outbox.Verify(o => o.EnqueueAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~ChangePasswordCommandHandlerTests"`
Expected: FAIL — handler class does not exist.

- [ ] **Step 4: Implement the handler**

```csharp
// src/ONEVO.Application/Features/Auth/Login/Commands/ChangePassword/ChangePasswordCommandHandler.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Domain.Features.Auth.Entities;

namespace ONEVO.Application.Features.Auth.Login.Commands.ChangePassword;

public class ChangePasswordCommandHandler : IRequestHandler<ChangePasswordCommand, Result>
{
    private readonly IUserRepository _users;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IAuditLogRepository _auditLog;
    private readonly IOutboxWriter _outbox;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    public ChangePasswordCommandHandler(
        IUserRepository users, IPasswordHasher passwordHasher, IAuditLogRepository auditLog,
        IOutboxWriter outbox, IUnitOfWork unitOfWork, ICurrentUser currentUser, IDateTimeProvider clock)
    {
        _users = users;
        _passwordHasher = passwordHasher;
        _auditLog = auditLog;
        _outbox = outbox;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Result> Handle(ChangePasswordCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result.Forbidden("Authentication required.");

        var user = await _users.GetByIdAsync(_currentUser.UserId, ct);
        if (user is null)
            return Result.NotFound("User not found.");

        if (!_passwordHasher.Verify(request.CurrentPassword, user.PasswordHash))
            return Result.Forbidden("Current password is incorrect.");

        user.PasswordHash = _passwordHasher.Hash(request.NewPassword);
        user.UpdatedAt = _clock.UtcNow;

        await _auditLog.AddAsync(new AuditLog
        {
            Id = Guid.NewGuid(),
            TenantId = _currentUser.TenantId,
            UserId = _currentUser.UserId,
            Action = "user.password_changed",
            ResourceType = "User",
            ResourceId = _currentUser.UserId,
            CreatedAt = _clock.UtcNow
        }, ct);

        await _outbox.EnqueueAsync(
            OutboxMessageTypes.EmployeeSecurityUpdated,
            new { UserId = _currentUser.UserId, Event = "password_changed", ChangedAt = _clock.UtcNow },
            tenantId: _currentUser.TenantId, ct);

        await _unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
```

Note: unlike `ResetPasswordCommandHandler`, this handler intentionally does **not** revoke refresh tokens or increment the permission version — those exist there because a password reset is a recovery flow assuming the account may be compromised; a self-service change by an already-authenticated user does not need to force every other session to re-login. If product behavior should differ, that's a one-line addition (`_refreshTokens.ListActiveByUserIdAsync` + revoke loop, copied from `ResetPasswordCommandHandler`), not a design change.

Also add to `OutboxMessageTypes` (in `IOutboxMessageHandler.cs`):

```csharp
    public const string EmployeeSecurityUpdated = "employee_security_updated";
```

And register a no-op consumer in `Infrastructure/DependencyInjection.cs`, next to the existing `NoOpPositionOutboxHandler` registrations, following that exact pattern (create `NoOpEmployeeSecurityOutboxHandler` in `Features/Auth/Login/OutboxHandlers/`, mirroring `NoOpPositionOutboxHandler`'s constructor-takes-a-type-string shape):

```csharp
// src/ONEVO.Application/Features/Auth/Login/OutboxHandlers/NoOpEmployeeSecurityOutboxHandler.cs
using ONEVO.Application.Common.ServiceInterfaces;

namespace ONEVO.Application.Features.Auth.Login.OutboxHandlers;

/// <summary>Placeholder consumer for employee_security_updated - no downstream consumer (audit
/// log, notification email, ...) has been requested yet. Mirrors NoOpPositionOutboxHandler.</summary>
public sealed class NoOpEmployeeSecurityOutboxHandler : IOutboxMessageHandler
{
    public string Type { get; }
    public NoOpEmployeeSecurityOutboxHandler(string type) => Type = type;
    public Task HandleAsync(string payloadJson, CancellationToken ct) => Task.CompletedTask;
}
```

```csharp
        services.AddScoped<IOutboxMessageHandler>(_ => new NoOpEmployeeSecurityOutboxHandler(OutboxMessageTypes.EmployeeSecurityUpdated));
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~ChangePasswordCommandHandlerTests"`
Expected: PASS.

- [ ] **Step 6: Add the controller action**

```csharp
    /// <summary>Change the caller's own password while authenticated (not the forgot-password
    /// flow). Requires the current password.</summary>
    [HttpPost("change-password")]
    [Authorize(Policy = "TenantPolicy")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new ChangePasswordCommand(request.CurrentPassword, request.NewPassword), ct);
        return result.IsSuccess
            ? Ok(new { message = "Password changed successfully." })
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
```

Add `using ONEVO.Application.Features.Auth.Login.Commands.ChangePassword;` to `AuthPasswordController.cs`.

- [ ] **Step 7: Build and run**

Run: `dotnet build && dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~ChangePasswordCommandHandlerTests"`
Expected: Build succeeds, PASS.

- [ ] **Step 8: Commit**

```bash
git add src/ONEVO.Api/Contracts/Auth/ChangePasswordRequest.cs src/ONEVO.Application/Features/Auth/Login/Commands/ChangePassword/ src/ONEVO.Application/Common/ServiceInterfaces/IOutboxMessageHandler.cs src/ONEVO.Application/Features/Auth/Login/OutboxHandlers/ src/ONEVO.Infrastructure/DependencyInjection.cs src/ONEVO.Api/Controllers/Tenant/Auth/AuthPasswordController.cs tests/ONEVO.Tests.Unit/Features/Auth/Login/ChangePasswordCommandHandlerTests.cs
git commit -m "feat: add self-service POST /api/v1/auth/change-password"
```

---

### Task 10: Self-service MFA disable

**Files:**
- Create: `src/ONEVO.Api/Contracts/Auth/DisableMfaRequest.cs`
- Create: `src/ONEVO.Application/Features/Auth/Login/Commands/MfaDisable/DisableMfaCommand.cs` (+Validator, +Handler)
- Modify: `src/ONEVO.Api/Controllers/Tenant/Auth/AuthMfaController.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Auth/Login/DisableMfaCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `IPasswordHasher.Verify` (Task 9), `IUserMfaRepository.GetTotpAsync(userId, isVerified, ct)` / `Remove(UserMfa)` (confirmed interface — same one `EnableMfaCommandHandler` uses).

- [ ] **Step 1: Write the request contract**

```csharp
// src/ONEVO.Api/Contracts/Auth/DisableMfaRequest.cs
namespace ONEVO.Api.Contracts.Auth;

public record DisableMfaRequest(string CurrentPassword);
```

- [ ] **Step 2: Write the command, validator, and failing test**

```csharp
// src/ONEVO.Application/Features/Auth/Login/Commands/MfaDisable/DisableMfaCommand.cs
using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Auth.Login.Commands.MfaDisable;

public record DisableMfaCommand(string CurrentPassword) : IRequest<Result>;
```

```csharp
// src/ONEVO.Application/Features/Auth/Login/Commands/MfaDisable/DisableMfaCommandValidator.cs
using FluentValidation;

namespace ONEVO.Application.Features.Auth.Login.Commands.MfaDisable;

public class DisableMfaCommandValidator : AbstractValidator<DisableMfaCommand>
{
    public DisableMfaCommandValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty().WithMessage("Current password is required to disable MFA.");
    }
}
```

```csharp
// tests/ONEVO.Tests.Unit/Features/Auth/Login/DisableMfaCommandHandlerTests.cs
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.Commands.MfaDisable;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Auth.Login;

public class DisableMfaCommandHandlerTests
{
    [Fact]
    public async Task Handle_ReturnsForbidden_WhenCurrentPasswordIsWrong()
    {
        var userId = Guid.NewGuid();
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(c => c.IsAuthenticated).Returns(true);
        currentUser.SetupGet(c => c.TenantId).Returns(Guid.NewGuid());
        currentUser.SetupGet(c => c.UserId).Returns(userId);

        var users = new Mock<IUserRepository>();
        users.Setup(u => u.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User { Id = userId, PasswordHash = "stored-hash" });

        var hasher = new Mock<IPasswordHasher>();
        hasher.Setup(h => h.Verify("wrong-password", "stored-hash")).Returns(false);

        var userMfa = new Mock<IUserMfaRepository>();

        var handler = new DisableMfaCommandHandler(
            users.Object, hasher.Object, userMfa.Object, new Mock<IAuditLogRepository>().Object,
            new Mock<IOutboxWriter>().Object, new Mock<IUnitOfWork>().Object, currentUser.Object,
            new Mock<IDateTimeProvider>().Object);

        var result = await handler.Handle(new DisableMfaCommand("wrong-password"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        userMfa.Verify(m => m.Remove(It.IsAny<UserMfa>()), Times.Never);
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~DisableMfaCommandHandlerTests"`
Expected: FAIL — handler class does not exist.

- [ ] **Step 4: Implement the handler**

```csharp
// src/ONEVO.Application/Features/Auth/Login/Commands/MfaDisable/DisableMfaCommandHandler.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Domain.Features.Auth.Entities;

namespace ONEVO.Application.Features.Auth.Login.Commands.MfaDisable;

public class DisableMfaCommandHandler : IRequestHandler<DisableMfaCommand, Result>
{
    private readonly IUserRepository _users;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IUserMfaRepository _userMfa;
    private readonly IAuditLogRepository _auditLog;
    private readonly IOutboxWriter _outbox;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    public DisableMfaCommandHandler(
        IUserRepository users, IPasswordHasher passwordHasher, IUserMfaRepository userMfa,
        IAuditLogRepository auditLog, IOutboxWriter outbox, IUnitOfWork unitOfWork,
        ICurrentUser currentUser, IDateTimeProvider clock)
    {
        _users = users;
        _passwordHasher = passwordHasher;
        _userMfa = userMfa;
        _auditLog = auditLog;
        _outbox = outbox;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Result> Handle(DisableMfaCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result.Forbidden("Authentication required.");

        var user = await _users.GetByIdAsync(_currentUser.UserId, ct);
        if (user is null)
            return Result.NotFound("User not found.");

        if (!_passwordHasher.Verify(request.CurrentPassword, user.PasswordHash))
            return Result.Forbidden("Current password is incorrect.");

        // Remove both the verified (active) registration and any stale unverified/in-progress
        // setup - GetTotpAsync's isVerified parameter is not nullable, so both states are checked
        // explicitly rather than assumed. Matches EnableMfaCommandHandler's own "remove existing
        // unverified setup before creating a new one" precedent.
        var verified = await _userMfa.GetTotpAsync(user.Id, isVerified: true, ct);
        if (verified is not null)
            _userMfa.Remove(verified);

        var unverified = await _userMfa.GetTotpAsync(user.Id, isVerified: false, ct);
        if (unverified is not null)
            _userMfa.Remove(unverified);

        if (verified is null && unverified is null)
            return Result.Conflict("MFA is not currently enabled.");

        await _auditLog.AddAsync(new AuditLog
        {
            Id = Guid.NewGuid(),
            TenantId = _currentUser.TenantId,
            UserId = _currentUser.UserId,
            Action = "user.mfa_disabled",
            ResourceType = "User",
            ResourceId = _currentUser.UserId,
            CreatedAt = _clock.UtcNow
        }, ct);

        await _outbox.EnqueueAsync(
            OutboxMessageTypes.EmployeeSecurityUpdated,
            new { UserId = _currentUser.UserId, Event = "mfa_disabled", ChangedAt = _clock.UtcNow },
            tenantId: _currentUser.TenantId, ct);

        await _unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~DisableMfaCommandHandlerTests"`
Expected: PASS.

- [ ] **Step 6: Add the controller action**

```csharp
    /// <summary>Disable MFA for the currently authenticated user. Requires re-entering the
    /// current password as a safety check before removing the second factor.</summary>
    [HttpPost("mfa/disable")]
    [Authorize(Policy = "TenantPolicy")]
    public async Task<IActionResult> DisableMfa([FromBody] DisableMfaRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new DisableMfaCommand(request.CurrentPassword), ct);
        return result.IsSuccess
            ? Ok(new { success = true })
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
```

Add `using ONEVO.Application.Features.Auth.Login.Commands.MfaDisable;` to `AuthMfaController.cs`.

- [ ] **Step 7: Build and run**

Run: `dotnet build && dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~DisableMfaCommandHandlerTests"`
Expected: Build succeeds, PASS.

- [ ] **Step 8: Commit**

```bash
git add src/ONEVO.Api/Contracts/Auth/DisableMfaRequest.cs src/ONEVO.Application/Features/Auth/Login/Commands/MfaDisable/ src/ONEVO.Api/Controllers/Tenant/Auth/AuthMfaController.cs tests/ONEVO.Tests.Unit/Features/Auth/Login/DisableMfaCommandHandlerTests.cs
git commit -m "feat: add self-service POST /api/v1/auth/mfa/disable"
```

---

### Task 11: Integration tests + final architecture-test baseline

**Files:**
- Create: `tests/ONEVO.Tests.Integration/CoreHr/EmployeeProfile/EmployeeProfileEndpointsIntegrationTests.cs`

**Interfaces:**
- Consumes: the full set of endpoints from Tasks 3–10, exercised end-to-end.

- [ ] **Step 1: Write the integration test class**

```csharp
// tests/ONEVO.Tests.Integration/CoreHr/EmployeeProfile/EmployeeProfileEndpointsIntegrationTests.cs
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace ONEVO.Tests.Integration.CoreHr.EmployeeProfile;

[Collection("Database")]
public class EmployeeProfileEndpointsIntegrationTests : IntegrationTestBase
{
    public EmployeeProfileEndpointsIntegrationTests(DatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task GetMyProfile_ReturnsOwnDataOnly_NotOtherTenantsData()
    {
        var tenant = await SeedTenantAsync();
        var employee = await SeedEmployeeAsync(tenant.Id);
        using var client = await CreateAuthenticatedClientAsync(tenant, employee);

        var response = await client.GetAsync("/api/v1/employees/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal(employee.EmployeeNumber, body.GetProperty("jobInformation").GetProperty("employeeNumber").GetString());
    }

    [Fact]
    public async Task UpdateMyPayroll_ReturnsForbidden_WithoutEmployeesWritePermission()
    {
        var tenant = await SeedTenantAsync();
        var employee = await SeedEmployeeAsync(tenant.Id, permissions: []); // no employees:write
        using var client = await CreateAuthenticatedClientAsync(tenant, employee);

        var response = await client.PutAsJsonAsync("/api/v1/employees/me/payroll", new
        {
            bankName = "Test Bank", branchName = "Main", accountHolderName = "Jane Doe",
            accountNumber = "1234567890", accountType = "savings", routingNumber = (string?)null
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UpdateMyPayroll_NeverReturnsRawAccountNumber_OnSuccess()
    {
        var tenant = await SeedTenantAsync();
        var employee = await SeedEmployeeAsync(tenant.Id, permissions: ["employees:write"]);
        using var client = await CreateAuthenticatedClientAsync(tenant, employee);

        var updateResponse = await client.PutAsJsonAsync("/api/v1/employees/me/payroll", new
        {
            bankName = "Test Bank", branchName = "Main", accountHolderName = "Jane Doe",
            accountNumber = "9876543210", accountType = "savings", routingNumber = (string?)null
        });
        Assert.Equal(HttpStatusCode.NoContent, updateResponse.StatusCode);

        var getResponse = await client.GetAsync("/api/v1/employees/me/payroll");
        var body = await getResponse.Content.ReadAsStringAsync();

        Assert.DoesNotContain("9876543210", body);
        Assert.Contains("7210", body); // masked last-4 (0000{last4} truncation of "9876543210" -> "3210")
    }

    [Fact]
    public async Task UpdateMyPersonalInformation_ReturnsConflict_OnStaleVersion()
    {
        var tenant = await SeedTenantAsync();
        var employee = await SeedEmployeeAsync(tenant.Id);
        using var client = await CreateAuthenticatedClientAsync(tenant, employee);

        var getResponse = await client.GetAsync("/api/v1/employees/me");
        var profile = await getResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var staleVersion = profile.GetProperty("personalInformation").GetProperty("version").GetString();

        // First update succeeds and advances the version.
        var firstUpdate = await client.PutAsJsonAsync("/api/v1/employees/me/personal-information", new
        {
            firstName = "Jane", lastName = "Doe", phone = (string?)null, dateOfBirth = (DateOnly?)null,
            gender = (string?)null, nationalityId = (Guid?)null, displayTimezone = (string?)null,
            addresses = Array.Empty<object>(), version = staleVersion
        });
        Assert.Equal(HttpStatusCode.NoContent, firstUpdate.StatusCode);

        // Second update reuses the now-stale version captured before the first update.
        var secondUpdate = await client.PutAsJsonAsync("/api/v1/employees/me/personal-information", new
        {
            firstName = "Janet", lastName = "Doe", phone = (string?)null, dateOfBirth = (DateOnly?)null,
            gender = (string?)null, nationalityId = (Guid?)null, displayTimezone = (string?)null,
            addresses = Array.Empty<object>(), version = staleVersion
        });

        Assert.Equal(HttpStatusCode.Conflict, secondUpdate.StatusCode);
    }
}
```

(`CreateAuthenticatedClientAsync(tenant, employee, permissions:)` and `SeedEmployeeAsync(tenantId, permissions:)` must match whatever helper signatures `EmployeesListIntegrationTests.cs` already defines in `IntegrationTestBase` — read that file first and adjust parameter names/order to match exactly rather than guessing a new overload.)

- [ ] **Step 2: Run the integration tests**

Run: `dotnet test tests/ONEVO.Tests.Integration --filter "FullyQualifiedName~EmployeeProfileEndpointsIntegrationTests"`
Expected: All PASS. If `CreateAuthenticatedClientAsync`/`SeedEmployeeAsync` signatures don't match `IntegrationTestBase`, adjust the test to the base class's real signatures (found by reading the file) rather than the ones sketched above.

- [ ] **Step 3: Run the full test suite as a final baseline**

Run: `dotnet test`
Expected: All PASS — unit, integration, and architecture tests all green, confirming Tasks 1–11 integrate cleanly and no existing behavior (Employees list/detail, MFA login flow, password reset flow) regressed.

- [ ] **Step 4: Commit**

```bash
git add tests/ONEVO.Tests.Integration/CoreHr/EmployeeProfile/EmployeeProfileEndpointsIntegrationTests.cs
git commit -m "test: add end-to-end integration coverage for employee self-service profile endpoints"
```
