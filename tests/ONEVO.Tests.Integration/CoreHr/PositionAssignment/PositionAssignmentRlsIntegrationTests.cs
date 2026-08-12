using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Infrastructure.ExternalServices.Messaging;
using ONEVO.Infrastructure.Identity.CurrentUser;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Identity.Time;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.CoreHr;
using ONEVO.Tests.Integration.Support;
using Testcontainers.PostgreSql;
using Xunit;

namespace ONEVO.Tests.Integration.CoreHr.PositionAssignment;

/// <summary>
/// Proves position_assignments and employee_hierarchy_closure RLS actually isolates tenants
/// when queried through a restricted, non-superuser, non-BYPASSRLS role (mirrors
/// RestrictedRoleRlsEnforcementTests), and that the DB-level partial unique index rejects a
/// second active PrimaryEmployment assignment for the same employee. Requires Docker.
/// </summary>
public sealed class PositionAssignmentRlsIntegrationTests : IAsyncLifetime
{
    private const string RestrictedRoleName = "position_assignment_rls_test_role";
    private const string RestrictedRolePassword = "position-assignment-rls-test-role-password";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("onevo_position_assignment_rls_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private readonly SystemDateTimeProvider _clock = new();

    private string _connectionString = string.Empty;
    private string _restrictedConnectionString = string.Empty;
    private Guid _tenantAId;
    private Guid _tenantBId;
    private Guid _tenantAEmployeeId;
    private Guid _tenantAPositionId;
    private Guid _tenantASecondPositionId;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _connectionString = _postgres.GetConnectionString();
        await PrivilegedRoleTestBootstrap.EnsureRolesExistAsync(_connectionString);

        await using var db = CreateContext();
        await db.Database.MigrateAsync();

        var tenantA = NewTenant("Position Assignment RLS Tenant A", "posn-assign-rls-a");
        var tenantB = NewTenant("Position Assignment RLS Tenant B", "posn-assign-rls-b");
        _tenantAId = tenantA.Id;
        _tenantBId = tenantB.Id;
        db.Tenants.AddRange(tenantA, tenantB);

        var employeeA = NewEmployee(_tenantAId, "PARLS-0001");
        _tenantAEmployeeId = employeeA.Id;
        db.Employees.Add(employeeA);

        var positionA = NewPosition(_tenantAId, "Engineer");
        var positionA2 = NewPosition(_tenantAId, "Senior Engineer");
        _tenantAPositionId = positionA.Id;
        _tenantASecondPositionId = positionA2.Id;
        db.Positions.AddRange(positionA, positionA2);

        await db.SaveChangesAsync();

        await CreateRestrictedRoleAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    public async Task RestrictedRole_IsNotSuperuserAndDoesNotBypassRls()
    {
        await using var connection = new NpgsqlConnection(_restrictedConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT rolsuper, rolbypassrls FROM pg_roles WHERE rolname = current_user";
        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();

        reader.GetBoolean(0).Should().BeFalse("the runtime test role must not be a PostgreSQL superuser");
        reader.GetBoolean(1).Should().BeFalse("the runtime test role must not have BYPASSRLS");
    }

    [Fact]
    public async Task PositionAssignments_AreIsolatedByTenant_ThroughRestrictedRole()
    {
        await using (var seedDb = CreateContext(_tenantAId, "posn-assign-rls-a", useRestrictedRole: true))
        {
            var repo = new EfPositionAssignmentRepository(seedDb);
            await repo.AddAsync(NewAssignment(_tenantAId, _tenantAEmployeeId, _tenantAPositionId));
            await repo.SaveChangesAsync();
        }

        await using var dbA = CreateContext(_tenantAId, "posn-assign-rls-a", useRestrictedRole: true);
        (await dbA.PositionAssignments.ToListAsync()).Should().ContainSingle();

        await using var dbB = CreateContext(_tenantBId, "posn-assign-rls-b", useRestrictedRole: true);
        (await dbB.PositionAssignments.ToListAsync()).Should().BeEmpty(
            "tenant B must not see tenant A's position_assignments rows");
    }

    [Fact]
    public async Task EmployeeHierarchyClosure_IsIsolatedByTenant_ThroughRestrictedRole()
    {
        await using (var seedDb = CreateContext(_tenantAId, "posn-assign-rls-a", useRestrictedRole: true))
        {
            seedDb.EmployeeHierarchyClosures.Add(new ONEVO.Domain.Features.CoreHr.Entities.EmployeeHierarchyClosure
            {
                TenantId = _tenantAId,
                AncestorEmployeeId = Guid.NewGuid(),
                DescendantEmployeeId = _tenantAEmployeeId,
                Depth = 1,
                SourcePositionAssignmentId = Guid.NewGuid(),
                GeneratedAt = DateTimeOffset.UtcNow,
            });
            await seedDb.SaveChangesAsync();
        }

        await using var dbA = CreateContext(_tenantAId, "posn-assign-rls-a", useRestrictedRole: true);
        (await dbA.EmployeeHierarchyClosures.ToListAsync()).Should().ContainSingle();

        await using var dbB = CreateContext(_tenantBId, "posn-assign-rls-b", useRestrictedRole: true);
        (await dbB.EmployeeHierarchyClosures.ToListAsync()).Should().BeEmpty(
            "tenant B must not see tenant A's employee_hierarchy_closure rows");
    }

    [Fact]
    public async Task Inserting_Second_Active_Primary_Assignment_For_Same_Employee_Throws_Unique_Violation()
    {
        await using (var firstDb = CreateContext(_tenantAId, "posn-assign-rls-a", useRestrictedRole: true))
        {
            var repo = new EfPositionAssignmentRepository(firstDb);
            await repo.AddAsync(NewAssignment(_tenantAId, _tenantAEmployeeId, _tenantAPositionId));
            await repo.SaveChangesAsync();
        }

        await using var secondDb = CreateContext(_tenantAId, "posn-assign-rls-a", useRestrictedRole: true);
        var secondRepo = new EfPositionAssignmentRepository(secondDb);
        await secondRepo.AddAsync(NewAssignment(_tenantAId, _tenantAEmployeeId, _tenantASecondPositionId));

        var act = async () => await secondRepo.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>(
            "the partial unique index must reject a second active PrimaryEmployment assignment for the same employee");
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
                GRANT SELECT, INSERT, UPDATE, DELETE ON position_assignments, employee_hierarchy_closure
                    TO {RestrictedRoleName};
                GRANT SELECT ON tenants, employees, positions TO {RestrictedRoleName};
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

    private static ONEVO.Domain.Features.CoreHr.Entities.Employee NewEmployee(Guid tenantId, string employeeNumber) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        UserId = Guid.NewGuid(),
        EmployeeNumber = employeeNumber,
        FirstName = "Test",
        LastName = "Employee",
        Email = $"{Guid.NewGuid():N}@rls-test.onevo.dev",
        HireDate = DateOnly.FromDateTime(DateTime.UtcNow),
    };

    private static ONEVO.Domain.Features.OrgStructure.Entities.Position NewPosition(Guid tenantId, string name) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        Name = name,
    };

    private static ONEVO.Domain.Features.CoreHr.Entities.PositionAssignment NewAssignment(Guid tenantId, Guid employeeId, Guid positionId) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        EmployeeId = employeeId,
        PositionId = positionId,
        AssignmentKind = PositionAssignmentKind.PrimaryEmployment,
        AssignmentStatus = PositionAssignmentStatus.Active,
        EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow),
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
