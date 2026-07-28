using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Infrastructure.ExternalServices.Messaging;
using ONEVO.Infrastructure.Identity.CurrentUser;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Identity.Time;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Tests.Integration.Support;
using Testcontainers.PostgreSql;

namespace ONEVO.Tests.Integration.Security;

/// <summary>
/// Proves auth_lookup_base_login_candidates is callable by a restricted, non-BYPASSRLS role via
/// EXECUTE only, that PUBLIC has no access, and that the same restricted role cannot read
/// cross-tenant users/tenants rows directly (only through the function). Requires Docker.
/// </summary>
public sealed class AuthLookupBaseLoginCandidatesFunctionTests : IAsyncLifetime
{
    private const string RestrictedRoleName = "base_login_fn_test_role";
    private const string RestrictedRolePassword = "base-login-fn-test-role-password";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("onevo_base_login_fn_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private readonly SystemDateTimeProvider _clock = new();

    private string _connectionString = string.Empty;
    private string _restrictedConnectionString = string.Empty;
    private Guid _tenantAId;
    private Guid _tenantBId;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _connectionString = _postgres.GetConnectionString();
        await PrivilegedRoleTestBootstrap.EnsureRolesExistAsync(_connectionString);

        await using var db = CreateContext();
        await db.Database.MigrateAsync();

        var tenantA = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = "Base Login Fn Tenant A",
            Slug = "base-login-fn-tenant-a",
            CompanySizeRange = "51-200",
            Status = TenantStatus.Active
        };
        var tenantB = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = "Base Login Fn Tenant B",
            Slug = "base-login-fn-tenant-b",
            CompanySizeRange = "51-200",
            Status = TenantStatus.Active
        };
        _tenantAId = tenantA.Id;
        _tenantBId = tenantB.Id;

        db.Tenants.AddRange(tenantA, tenantB);
        await db.SaveChangesAsync();

        var userA = new User
        {
            Id = Guid.NewGuid(),
            TenantId = tenantA.Id,
            Email = "base-login-fn-shared@test.onevo.dev",
            PasswordHash = "not-a-real-hash-a",
            FirstName = "Fn",
            LastName = "TesterA",
            IsActive = true
        };
        var userB = new User
        {
            Id = Guid.NewGuid(),
            TenantId = tenantB.Id,
            Email = "base-login-fn-shared@test.onevo.dev",
            PasswordHash = "not-a-real-hash-b",
            FirstName = "Fn",
            LastName = "TesterB",
            IsActive = true
        };

        // Seeded through EF (superuser connection, so RLS never blocks it) rather than raw SQL, so
        // the auditable-entity interceptor fills in required audit columns (e.g. created_by_id)
        // consistently with how the rest of this table's NOT NULL columns evolve over time.
        db.Users.AddRange(userA, userB);
        await db.SaveChangesAsync();

        await CreateRestrictedRoleAsync();
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

        await using (var grantExecute = connection.CreateCommand())
        {
            grantExecute.CommandText =
                $"GRANT USAGE ON SCHEMA auth_internal TO {RestrictedRoleName}; " +
                $"GRANT EXECUTE ON FUNCTION auth_internal.auth_lookup_base_login_candidates(varchar) TO {RestrictedRoleName};";
            await grantExecute.ExecuteNonQueryAsync();
        }

        var restrictedBuilder = new NpgsqlConnectionStringBuilder(_connectionString)
        {
            Username = RestrictedRoleName,
            Password = RestrictedRolePassword
        };
        _restrictedConnectionString = restrictedBuilder.ConnectionString;
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    public async Task RestrictedRole_CanCallFunctionAndSeesBothTenantsCandidates()
    {
        await using var connection = new NpgsqlConnection(_restrictedConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT tenant_id FROM auth_internal.auth_lookup_base_login_candidates(@email)";
        command.Parameters.AddWithValue("email", "base-login-fn-shared@test.onevo.dev");

        var rows = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(reader.GetGuid(0));
        }

        rows.Should().BeEquivalentTo(new[] { _tenantAId, _tenantBId },
            "SECURITY DEFINER must return matching active users across tenants despite the caller's own RLS context");
    }

    [Fact]
    public async Task RestrictedRole_CannotSelectUsersTableDirectlyAcrossTenants()
    {
        await using var connection = new NpgsqlConnection(_restrictedConnectionString);
        await connection.OpenAsync();

        var act = async () =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT id FROM users LIMIT 1";
            await command.ExecuteReaderAsync();
        };

        await act.Should().ThrowAsync<PostgresException>(
            "the restricted role has no direct table grant on users; only EXECUTE on the function");
    }

    [Fact]
    public async Task RestrictedRole_CannotSelectEmailColumn_OnlyNormalizedEmail()
    {
        await using var connection = new NpgsqlConnection(_restrictedConnectionString);
        await connection.OpenAsync();

        var act = async () =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT normalized_email FROM users LIMIT 1";
            await command.ExecuteReaderAsync();
        };

        await act.Should().ThrowAsync<PostgresException>(
            "the restricted role has no direct column grant on users; only EXECUTE on the function");
    }

    [Fact]
    public async Task PublicRole_CannotExecuteFunction()
    {
        await using var adminConnection = new NpgsqlConnection(_connectionString);
        await adminConnection.OpenAsync();

        await using var checkGrant = adminConnection.CreateCommand();
        checkGrant.CommandText = """
            SELECT has_function_privilege('public', 'auth_internal.auth_lookup_base_login_candidates(varchar)', 'EXECUTE');
            """;
        var publicCanExecute = (bool)(await checkGrant.ExecuteScalarAsync())!;

        publicCanExecute.Should().BeFalse("PUBLIC execute must be revoked");
    }

    private ApplicationDbContext CreateContext()
    {
        var tenantContext = new TenantContextAccessor();
        tenantContext.SetSystemMode();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_connectionString)
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
