using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.Storage.File.Entities;
using ONEVO.Domain.Features.Storage.Quota.Entities;
using ONEVO.Infrastructure.ExternalServices.Messaging;
using ONEVO.Infrastructure.Identity.CurrentUser;
using ONEVO.Infrastructure.Identity.Mfa;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Identity.Time;
using ONEVO.Infrastructure.Identity.Tokens;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.Auth.Legal;
using ONEVO.Tests.Integration.Support;

namespace ONEVO.Tests.Integration.Security;

/// <summary>
/// Shared, one-time-per-class setup for RestrictedRoleRlsEnforcementTests: clones the database,
/// seeds tenant A / tenant B / user A, and creates the restricted non-superuser role ONCE. xUnit's
/// IClassFixture constructs this ONCE and disposes it once after every fact in the class has run,
/// instead of IAsyncLifetime's default of once PER fact - previously this class's own
/// InitializeAsync ran 8 times, once per [Fact]. Every fact shares the SAME tenant A / tenant B /
/// user A rows - only the MfaChallenges table is written to by more than one fact
/// (UnresolvedTenantContext_SeesNoRowsThroughRestrictedRole and
/// RootContinuationChallengeLookup_UsesAdminRlsBoundary_AndRestoresSystemMode both insert a
/// challenge for tenant A), so MfaChallenges_AreIsolatedByTenant_ThroughRestrictedRole asserts its
/// own freshly-created row is present rather than asserting an exact table count.
/// </summary>
public sealed class RestrictedRoleRlsEnforcementTestsFixture : IAsyncLifetime
{
    private const string RestrictedRoleName = "rls_enforcement_test_role";
    private const string RestrictedRolePassword = "rls-enforcement-test-role-password";

    private readonly SystemDateTimeProvider _clock = new();

    private string _connectionString = string.Empty;
    private string _restrictedConnectionString = string.Empty;
    private Guid _tenantAId;
    private Guid _tenantBId;
    private Guid _userAId;

    public SystemDateTimeProvider Clock => _clock;
    public string RestrictedConnectionString => _restrictedConnectionString;
    public Guid TenantAId => _tenantAId;
    public Guid TenantBId => _tenantBId;
    public Guid UserAId => _userAId;

    public async Task InitializeAsync()
    {
        _connectionString = await SharedPostgresTemplate.CreateDatabaseAsync();

        await using var db = CreateContext();

        var tenantA = NewTenant("RLS Test Tenant A", "rls-test-tenant-a");
        var tenantB = NewTenant("RLS Test Tenant B", "rls-test-tenant-b");
        _tenantAId = tenantA.Id;
        _tenantBId = tenantB.Id;

        var userA = new User
        {
            Id = Guid.NewGuid(),
            TenantId = tenantA.Id,
            Email = "rls-test@test.onevo.dev",
            PasswordHash = "not-a-real-hash",
            FirstName = "Rls",
            LastName = "Tester",
            IsActive = true
        };
        _userAId = userA.Id;

        db.Tenants.AddRange(tenantA, tenantB);
        await db.SaveChangesAsync();

        await CreateRestrictedRoleAsync();

        await using var seedDb = CreateContext(_tenantAId, "rls-test-tenant-a", useRestrictedRole: true);
        seedDb.Users.Add(userA);
        await seedDb.SaveChangesAsync();
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
                GRANT SELECT, INSERT, UPDATE, DELETE ON
                    users, roles, file_records, file_upload_reservations,
                    tenant_storage_stats, mfa_challenges, legal_login_challenges
                    TO {RestrictedRoleName};
                GRANT SELECT ON tenants TO {RestrictedRoleName};
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

    public Task DisposeAsync() => Task.CompletedTask;

    private static Tenant NewTenant(string name, string slug) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Slug = slug,
        CompanySizeRange = "51-200",
        Status = TenantStatus.Active
    };

    public ApplicationDbContext CreateContext(Guid? tenantId = null, string? slug = null, bool useRestrictedRole = false)
    {
        var tenantContext = new TenantContextAccessor();

        if (tenantId is not null && slug is not null)
        {
            tenantContext.Resolve(new ONEVO.Application.Common.ServiceInterfaces.TenantRegistryEntry(tenantId.Value, slug, TenantStatus.Active, null));
        }

        return CreateContext(tenantContext, useRestrictedRole);
    }

    public ApplicationDbContext CreateContext(
        TenantContextAccessor tenantContext,
        bool useRestrictedRole)
    {
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

/// <summary>
/// Proves PostgreSQL Row-Level Security actually isolates tenants for the six
/// tables named in the tenant isolation hardening task (users, roles,
/// file_records, file_upload_reservations, tenant_storage_stats,
/// mfa_challenges) when queried through a restricted, non-superuser,
/// non-BYPASSRLS role — the only connection shape under which
/// FORCE ROW LEVEL SECURITY has any effect. Migrations run through the
/// Testcontainers default superuser role (same as the rest of this
/// integration suite); every isolation assertion below runs through the
/// dedicated restricted role created in InitializeAsync. Requires Docker.
/// </summary>
public sealed class RestrictedRoleRlsEnforcementTests : IClassFixture<RestrictedRoleRlsEnforcementTestsFixture>
{
    private readonly RestrictedRoleRlsEnforcementTestsFixture _fixture;

    public RestrictedRoleRlsEnforcementTests(RestrictedRoleRlsEnforcementTestsFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task RestrictedRole_IsNotSuperuserAndDoesNotBypassRls()
    {
        await using var connection = new NpgsqlConnection(_fixture.RestrictedConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT rolsuper, rolbypassrls FROM pg_roles WHERE rolname = current_user";
        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();

        reader.GetBoolean(0).Should().BeFalse("the runtime test role must not be a PostgreSQL superuser");
        reader.GetBoolean(1).Should().BeFalse("the runtime test role must not have BYPASSRLS");
    }

    [Fact]
    public async Task UnresolvedTenantContext_SeesNoRowsThroughRestrictedRole()
    {
        // A context that never resolved a tenant (ContextMode = System, the
        // TenantContextAccessor default) sends an empty app.current_tenant_id
        // / mode = "system" session setting — neither the USING clause's
        // 'admin' branch nor its 'tenant' branch matches, so the restricted
        // role must see zero rows for a table it knows has data.
        await using (var setupDb = _fixture.CreateContext(_fixture.TenantAId, "rls-test-tenant-a", useRestrictedRole: true))
        {
            setupDb.MfaChallenges.Add(new MfaChallenge
            {
                Id = Guid.NewGuid(),
                TenantId = _fixture.TenantAId,
                UserId = _fixture.UserAId,
                ChallengeHash = new string('b', 64),
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5)
            });
            await setupDb.SaveChangesAsync();
        }

        await using var unresolvedDb = _fixture.CreateContext(useRestrictedRole: true);
        (await unresolvedDb.MfaChallenges.ToListAsync()).Should().BeEmpty(
            "a connection with no tenant setting must not fall back to seeing every tenant's rows");
    }

    [Fact]
    public async Task Users_AreIsolatedByTenant_ThroughRestrictedRole()
    {
        await using var dbA = _fixture.CreateContext(_fixture.TenantAId, "rls-test-tenant-a", useRestrictedRole: true);
        (await dbA.Users.ToListAsync()).Should().ContainSingle(u => u.Id == _fixture.UserAId);

        await using var dbB = _fixture.CreateContext(_fixture.TenantBId, "rls-test-tenant-b", useRestrictedRole: true);
        (await dbB.Users.ToListAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Roles_AreIsolatedByTenant_ThroughRestrictedRole()
    {
        await using (var setupDb = _fixture.CreateContext(_fixture.TenantAId, "rls-test-tenant-a", useRestrictedRole: true))
        {
            setupDb.Roles.Add(new Role { Id = Guid.NewGuid(), TenantId = _fixture.TenantAId, Name = "RLS Test Role" });
            await setupDb.SaveChangesAsync();
        }

        await using var dbA = _fixture.CreateContext(_fixture.TenantAId, "rls-test-tenant-a", useRestrictedRole: true);
        (await dbA.Roles.ToListAsync()).Should().ContainSingle();

        await using var dbB = _fixture.CreateContext(_fixture.TenantBId, "rls-test-tenant-b", useRestrictedRole: true);
        (await dbB.Roles.ToListAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task MfaChallenges_AreIsolatedByTenant_ThroughRestrictedRole()
    {
        // Own dedicated challenge id: other facts in this class (e.g.
        // UnresolvedTenantContext_SeesNoRowsThroughRestrictedRole, which is not tenant-scoped in
        // its own assertion) also insert MfaChallenge rows for tenant A, so under a shared
        // IClassFixture database this table can hold more than one tenant-A row by the time this
        // fact runs - assert this fact's own row is present rather than an exact table count.
        var challengeId = Guid.NewGuid();
        await using (var setupDb = _fixture.CreateContext(_fixture.TenantAId, "rls-test-tenant-a", useRestrictedRole: true))
        {
            setupDb.MfaChallenges.Add(new MfaChallenge
            {
                Id = challengeId,
                TenantId = _fixture.TenantAId,
                UserId = _fixture.UserAId,
                ChallengeHash = new string('a', 64),
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5)
            });
            await setupDb.SaveChangesAsync();
        }

        await using var dbA = _fixture.CreateContext(_fixture.TenantAId, "rls-test-tenant-a", useRestrictedRole: true);
        (await dbA.MfaChallenges.ToListAsync()).Should().Contain(m => m.Id == challengeId);

        await using var dbB = _fixture.CreateContext(_fixture.TenantBId, "rls-test-tenant-b", useRestrictedRole: true);
        (await dbB.MfaChallenges.ToListAsync()).Should().NotContain(m => m.Id == challengeId);
    }

    [Fact]
    public async Task RootContinuationChallengeLookup_UsesAdminRlsBoundary_AndRestoresSystemMode()
    {
        var tokens = new SecureTokenGenerator();
        string legalChallenge;
        string mfaChallenge;

        var seedContext = new TenantContextAccessor();
        seedContext.Resolve(new ONEVO.Application.Common.ServiceInterfaces.TenantRegistryEntry(
            _fixture.TenantAId,
            "rls-test-tenant-a",
            TenantStatus.Active,
            null));
        await using (var seedDb = _fixture.CreateContext(seedContext, useRestrictedRole: true))
        {
            var legalRepository = new EfLegalLoginChallengeRepository(
                seedDb,
                tokens,
                _fixture.Clock,
                seedContext);
            var mfaStore = new PostgresMfaChallengeStore(seedDb, tokens, _fixture.Clock, seedContext);

            (legalChallenge, _) = await legalRepository.CreateAsync(
                _fixture.TenantAId,
                _fixture.UserAId,
                "password",
                TimeSpan.FromMinutes(10));
            mfaChallenge = await mfaStore.CreateAsync(
                _fixture.UserAId,
                _fixture.TenantAId,
                "password",
                TimeSpan.FromMinutes(10));
        }

        var systemContext = new TenantContextAccessor();
        await using var lookupDb = _fixture.CreateContext(systemContext, useRestrictedRole: true);
        var legalLookup = new EfLegalLoginChallengeRepository(
            lookupDb,
            tokens,
            _fixture.Clock,
            systemContext);
        var mfaLookup = new PostgresMfaChallengeStore(
            lookupDb,
            tokens,
            _fixture.Clock,
            systemContext);

        (await legalLookup.GetActiveAsync(legalChallenge)).Should().BeNull(
            "FORCE RLS must hide legal challenges in System mode");
        (await mfaLookup.GetAsync(mfaChallenge)).Should().BeNull(
            "FORCE RLS must hide MFA challenges in System mode");

        var legalState = await legalLookup.GetActiveForPreTenantContinuationAsync(legalChallenge);
        systemContext.ContextMode.Should().Be(TenantContextMode.System);
        var mfaState = await mfaLookup.GetForPreTenantContinuationAsync(mfaChallenge);

        legalState.Should().NotBeNull();
        legalState!.TenantId.Should().Be(_fixture.TenantAId);
        legalState.UserId.Should().Be(_fixture.UserAId);
        mfaState.Should().NotBeNull();
        mfaState!.TenantId.Should().Be(_fixture.TenantAId);
        mfaState.UserId.Should().Be(_fixture.UserAId);
        systemContext.ContextMode.Should().Be(TenantContextMode.System);
        systemContext.IsResolved.Should().BeFalse();
    }

    [Fact]
    public async Task TenantStorageStats_AreIsolatedByTenant_ThroughRestrictedRole()
    {
        await using (var setupDb = _fixture.CreateContext(_fixture.TenantAId, "rls-test-tenant-a", useRestrictedRole: true))
        {
            setupDb.TenantStorageStats.Add(new TenantStorageStats
            {
                TenantId = _fixture.TenantAId,
                UsedR2Bytes = 100,
                UsedDbBytes = 0,
                ReservedR2Bytes = 0,
                UpdatedAt = DateTimeOffset.UtcNow
            });
            await setupDb.SaveChangesAsync();
        }

        await using var dbA = _fixture.CreateContext(_fixture.TenantAId, "rls-test-tenant-a", useRestrictedRole: true);
        (await dbA.TenantStorageStats.ToListAsync()).Should().ContainSingle();

        await using var dbB = _fixture.CreateContext(_fixture.TenantBId, "rls-test-tenant-b", useRestrictedRole: true);
        (await dbB.TenantStorageStats.ToListAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task FileRecordsAndReservations_AreIsolatedByTenant_ThroughRestrictedRole()
    {
        await using (var setupDb = _fixture.CreateContext(_fixture.TenantAId, "rls-test-tenant-a", useRestrictedRole: true))
        {
            setupDb.FileRecords.Add(new FileRecord
            {
                Id = Guid.NewGuid(),
                TenantId = _fixture.TenantAId,
                StorageKey = "tenants/a/rls-test/key.png",
                OriginalFileName = "test.png",
                SafeFileName = "test.png",
                ContentType = "image/png",
                FileSizeBytes = 10,
                ChecksumSha256 = new string('a', 64),
                UploadedByUserId = _fixture.UserAId,
                Status = FileRecordStatus.PendingScan,
                CreatedAt = DateTimeOffset.UtcNow
            });
            setupDb.FileUploadReservations.Add(new FileUploadReservation
            {
                Id = Guid.NewGuid(),
                TenantId = _fixture.TenantAId,
                ReservedBytes = 10,
                Status = FileUploadReservationStatus.Active,
                ReservedByUserId = _fixture.UserAId,
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30),
                CreatedAt = DateTimeOffset.UtcNow
            });
            await setupDb.SaveChangesAsync();
        }

        await using var dbA = _fixture.CreateContext(_fixture.TenantAId, "rls-test-tenant-a", useRestrictedRole: true);
        (await dbA.FileRecords.ToListAsync()).Should().ContainSingle();
        (await dbA.FileUploadReservations.ToListAsync()).Should().ContainSingle();

        await using var dbB = _fixture.CreateContext(_fixture.TenantBId, "rls-test-tenant-b", useRestrictedRole: true);
        (await dbB.FileRecords.ToListAsync()).Should().BeEmpty();
        (await dbB.FileUploadReservations.ToListAsync()).Should().BeEmpty();
    }

}
