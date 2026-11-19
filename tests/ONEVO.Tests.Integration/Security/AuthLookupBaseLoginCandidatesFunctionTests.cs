using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Infrastructure.ExternalServices.Messaging;
using ONEVO.Infrastructure.Identity.CurrentUser;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Identity.Time;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Tests.Integration.Support;

namespace ONEVO.Tests.Integration.Security;

/// <summary>
/// Proves auth_lookup_base_login_candidates is callable by a restricted, non-BYPASSRLS role via
/// EXECUTE only, that PUBLIC has no access, and that the same restricted role cannot read
/// cross-tenant users/tenants rows directly (only through the function). Requires Docker.
/// </summary>
public sealed class AuthLookupBaseLoginCandidatesFunctionTests : IAsyncLifetime
{
    private const string RestrictedRoleName = "crownus_app";
    private const string RestrictedRolePassword = PrivilegedRoleTestBootstrap.AppRolePassword;


    private readonly SystemDateTimeProvider _clock = new();

    private string _connectionString = string.Empty;
    private string _restrictedConnectionString = string.Empty;
    private Guid _tenantAId;
    private Guid _tenantBId;

    public async Task InitializeAsync()
    {
        _connectionString = await SharedSqlServerTemplate.CreateDatabaseAsync();

        await using var db = CreateContext();

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
        var restrictedBuilder = new SqlConnectionStringBuilder(_connectionString)
        {
            UserID = RestrictedRoleName,
            Password = RestrictedRolePassword
        };
        _restrictedConnectionString = restrictedBuilder.ConnectionString;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task RestrictedRole_CanCallFunctionAndSeesBothTenantsCandidates()
    {
        await using var connection = TestDb.CreateConnection(_restrictedConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "auth_internal.auth_lookup_base_login_candidates";
        command.CommandType = System.Data.CommandType.StoredProcedure;
        command.Parameters.AddWithValue("@normalized_email", "base-login-fn-shared@test.onevo.dev");

        var rows = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(reader.GetGuid(0));
        }

        rows.Should().BeEquivalentTo(new[] { _tenantAId, _tenantBId },
            "EXECUTE AS must return matching active users across tenants despite the caller's own RLS context");
    }

    [Fact]
    public async Task RestrictedRole_CannotReadUsersDirectlyAcrossTenants()
    {
        await using var connection = TestDb.CreateConnection(_restrictedConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT_BIG(*) FROM users";

        (await command.ExecuteScalarAsync()).Should().Be(0L,
            "without a tenant context RLS hides every user; only the lookup procedure crosses tenants");
    }

    [Fact]
    public async Task LookupOwner_CannotReadColumnsOutsideItsGrant()
    {
        await using var connection = TestDb.CreateConnection(_connectionString);
        await connection.OpenAsync();
        await using (var impersonate = connection.CreateCommand())
        {
            impersonate.CommandText = "EXECUTE AS USER = 'crownus_auth_base_login_fn_owner'";
            await impersonate.ExecuteNonQueryAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT TOP (1) email FROM dbo.users";
        var act = async () => await command.ExecuteScalarAsync();

        (await act.Should().ThrowAsync<SqlException>()).Which.Number.Should().Be(230,
            "the owner may read only the columns the lookup returns");
    }

    [Fact]
    public async Task PublicRole_CannotExecuteFunction()
    {
        await using var adminConnection = TestDb.CreateConnection(_connectionString);
        await adminConnection.OpenAsync();

        await using var checkGrant = adminConnection.CreateCommand();
        checkGrant.CommandText = """
            SELECT COUNT_BIG(*) FROM sys.database_permissions
            WHERE major_id = OBJECT_ID('auth_internal.auth_lookup_base_login_candidates')
              AND grantee_principal_id = DATABASE_PRINCIPAL_ID('public')
              AND state IN ('G', 'W');
            """;

        (await checkGrant.ExecuteScalarAsync()).Should().Be(0L, "PUBLIC must have no execute grant");
    }

    private ApplicationDbContext CreateContext()
    {
        var tenantContext = new TenantContextAccessor();
        tenantContext.SetSystemMode();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseTestSqlServer(_connectionString)
            .UseSnakeCaseNamingConvention()
            .AddTestRls(new TenantRlsInterceptor(tenantContext))
            .Options;

        return new ApplicationDbContext(
            options,
            new AuditableEntityInterceptor(new AnonymousCurrentUser(), _clock),
            new SoftDeleteInterceptor(_clock),
            new DomainEventDispatchInterceptor(new NoOpPublisher()),
            tenantContext);
    }
}
