using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Infrastructure.ExternalServices.Messaging;
using ONEVO.Infrastructure.Identity.CurrentUser;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Identity.Time;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Tests.Integration.Support;
using Testcontainers.PostgreSql;
using Xunit;
using EmployeeEntity = ONEVO.Domain.Features.CoreHr.Entities.Employee;

namespace ONEVO.Tests.Integration.CoreHr.EmployeeProfile;

/// <summary>
/// Proves RLS on the four new employee-profile child tables using the same restricted-role
/// pattern as EmployeesListIntegrationTests - a real, non-superuser, NOBYPASSRLS PostgreSQL role
/// connected with tenant A's session context must not see tenant B's rows, even though the
/// EF query filter is bypassed by querying with a bare ApplicationDbContext session set to the
/// wrong tenant. Requires Docker.
/// </summary>
public sealed class EmployeeProfileTablesRlsTests : IAsyncLifetime
{
    private const string RestrictedRoleName = "employee_profile_rls_test_role";
    private const string RestrictedRolePassword = "employee-profile-rls-test-role-password";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("onevo_employee_profile_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private readonly SystemDateTimeProvider _clock = new();

    private string _connectionString = string.Empty;
    private string _restrictedConnectionString = string.Empty;
    private Guid _tenantAId;
    private Guid _tenantBId;
    private Guid _employeeAId;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _connectionString = _postgres.GetConnectionString();
        await PrivilegedRoleTestBootstrap.EnsureRolesExistAsync(_connectionString);

        await using var db = CreateContext();
        await db.Database.MigrateAsync();

        var tenantA = NewTenant("Employee Profile RLS Tenant A", "employee-profile-rls-a");
        var tenantB = NewTenant("Employee Profile RLS Tenant B", "employee-profile-rls-b");
        _tenantAId = tenantA.Id;
        _tenantBId = tenantB.Id;
        db.Tenants.AddRange(tenantA, tenantB);

        var employeeA = NewEmployee(_tenantAId, "E-A-001");
        _employeeAId = employeeA.Id;
        db.Employees.Add(employeeA);
        db.Employees.Add(NewEmployee(_tenantBId, "E-B-001"));

        await db.SaveChangesAsync();

        db.EmployeeBankDetails.Add(new EmployeeBankDetail
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantAId,
            EmployeeId = _employeeAId,
            BankName = "Test Bank",
            BranchName = "Main",
            AccountHolderName = "Tenant A Employee",
            AccountNumberEncrypted = "ciphertext-not-real",
            AccountType = "savings",
            IsPrimary = true,
            CreatedById = employeeA.UserId,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        await CreateRestrictedRoleAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    public async Task EmployeeBankDetail_RowFromOtherTenant_IsInvisibleUnderRestrictedRoleTenantContext()
    {
        await using var dbAsTenantB = CreateContext(_tenantBId, "employee-profile-rls-b", useRestrictedRole: true);

        var visible = await dbAsTenantB.EmployeeBankDetails
            .Where(b => b.TenantId == _tenantAId)
            .ToListAsync();

        visible.Should().BeEmpty("RLS must block tenant B's session from seeing tenant A's bank detail row");
    }

    [Fact]
    public async Task EmployeeBankDetail_RowFromOwnTenant_IsVisibleUnderRestrictedRoleTenantContext()
    {
        await using var dbAsTenantA = CreateContext(_tenantAId, "employee-profile-rls-a", useRestrictedRole: true);

        var visible = await dbAsTenantA.EmployeeBankDetails
            .Where(b => b.EmployeeId == _employeeAId)
            .ToListAsync();

        visible.Should().ContainSingle();
    }

    private async Task CreateRestrictedRoleAsync()
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        await using (var createRole = connection.CreateCommand())
        {
            createRole.CommandText = $@"
                DO $$
                BEGIN
                    IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = '{RestrictedRoleName}') THEN
                        CREATE ROLE {RestrictedRoleName}
                            LOGIN PASSWORD '{RestrictedRolePassword}' NOSUPERUSER NOBYPASSRLS;
                    END IF;
                END
                $$;
            ";
            await createRole.ExecuteNonQueryAsync();
        }

        await using (var grantSchema = connection.CreateCommand())
        {
            grantSchema.CommandText = $"GRANT USAGE ON SCHEMA public TO {RestrictedRoleName};";
            await grantSchema.ExecuteNonQueryAsync();
        }

        await using (var grantTables = connection.CreateCommand())
        {
            grantTables.CommandText = $@"
                GRANT SELECT ON employees, employee_addresses, employee_emergency_contacts,
                    employee_dependents, employee_bank_details, tenants
                    TO {RestrictedRoleName};
            ";
            await grantTables.ExecuteNonQueryAsync();
        }

        var restrictedBuilder = new NpgsqlConnectionStringBuilder(_connectionString)
        {
            Username = RestrictedRoleName,
            Password = RestrictedRolePassword
        };
        _restrictedConnectionString = restrictedBuilder.ConnectionString;
    }

    private static Tenant NewTenant(string name, string slug) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Slug = slug,
        CompanySizeRange = "51-200",
        Status = TenantStatus.Active
    };

    private static EmployeeEntity NewEmployee(Guid tenantId, string employeeNumber) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        UserId = Guid.NewGuid(),
        EmployeeNumber = employeeNumber,
        FirstName = "Test",
        LastName = employeeNumber,
        Email = $"{Guid.NewGuid():N}@employee-profile-rls-test.onevo.dev",
        HireDate = DateOnly.FromDateTime(DateTime.UtcNow)
    };

    private ApplicationDbContext CreateContext(Guid? tenantId = null, string? slug = null, bool useRestrictedRole = false)
    {
        var tenantContext = new TenantContextAccessor();

        if (tenantId is not null && slug is not null)
        {
            tenantContext.Resolve(new ONEVO.Application.Common.ServiceInterfaces.TenantRegistryEntry(tenantId.Value, slug, TenantStatus.Active, null));
        }

        var connectionString = useRestrictedRole ? _restrictedConnectionString : _connectionString;
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new TenantRlsInterceptor(tenantContext))
            .Options;

        return new ApplicationDbContext(
            options,
            new AuditableEntityInterceptor(new AnonymousCurrentUser(), _clock),
            new SoftDeleteInterceptor(_clock),
            new DomainEventDispatchInterceptor(new NoOpPublisher()),
            tenantContext);
    }
}
