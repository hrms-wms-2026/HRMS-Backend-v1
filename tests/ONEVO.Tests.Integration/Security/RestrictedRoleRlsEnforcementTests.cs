using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.Configuration.Entities;
using ONEVO.Domain.Features.IdentityVerification.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.Storage.File.Entities;
using ONEVO.Domain.Features.Storage.Quota.Entities;
using ONEVO.Infrastructure.ExternalServices.Messaging;
using ONEVO.Infrastructure.Identity;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using Testcontainers.PostgreSql;

namespace ONEVO.Tests.Integration.Security;

/// <summary>
/// Proves PostgreSQL Row-Level Security actually isolates tenants for the
/// tables covered by focused tenant isolation hardening tasks (users, roles,
/// file_records, file_upload_reservations, tenant_storage_stats,
/// mfa_challenges, employee_work_location_settings, and
/// verification_reference_photos) when queried through a restricted, non-superuser,
/// non-BYPASSRLS role — the only connection shape under which
/// FORCE ROW LEVEL SECURITY has any effect. Migrations run through the
/// Testcontainers default superuser role (same as the rest of this
/// integration suite); every isolation assertion below runs through the
/// dedicated restricted role created in InitializeAsync. Requires Docker.
/// </summary>
public sealed class RestrictedRoleRlsEnforcementTests : IAsyncLifetime
{
    private const string RestrictedRoleName = "rls_enforcement_test_role";
    private const string RestrictedRolePassword = "rls-enforcement-test-role-password";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("onevo_rls_enforcement_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private readonly SystemDateTimeProvider _clock = new();

    private string _connectionString = string.Empty;
    private string _restrictedConnectionString = string.Empty;
    private Guid _tenantAId;
    private Guid _tenantBId;
    private Guid _userAId;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _connectionString = _postgres.GetConnectionString();

        await using var db = CreateContext();
        await db.Database.MigrateAsync();

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
                    tenant_storage_stats, mfa_challenges,
                    employee_work_location_settings, verification_reference_photos
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
    public async Task UnresolvedTenantContext_SeesNoRowsThroughRestrictedRole()
    {
        // A context that never resolved a tenant (ContextMode = System, the
        // TenantContextAccessor default) sends an empty app.current_tenant_id
        // / mode = "system" session setting — neither the USING clause's
        // 'admin' branch nor its 'tenant' branch matches, so the restricted
        // role must see zero rows for a table it knows has data.
        await using (var setupDb = CreateContext(_tenantAId, "rls-test-tenant-a", useRestrictedRole: true))
        {
            setupDb.MfaChallenges.Add(new MfaChallenge
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantAId,
                UserId = _userAId,
                ChallengeHash = new string('b', 64),
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5)
            });
            await setupDb.SaveChangesAsync();
        }

        await using var unresolvedDb = CreateContext(useRestrictedRole: true);
        (await unresolvedDb.MfaChallenges.ToListAsync()).Should().BeEmpty(
            "a connection with no tenant setting must not fall back to seeing every tenant's rows");
    }

    [Fact]
    public async Task Users_AreIsolatedByTenant_ThroughRestrictedRole()
    {
        await using var dbA = CreateContext(_tenantAId, "rls-test-tenant-a", useRestrictedRole: true);
        (await dbA.Users.ToListAsync()).Should().ContainSingle(u => u.Id == _userAId);

        await using var dbB = CreateContext(_tenantBId, "rls-test-tenant-b", useRestrictedRole: true);
        (await dbB.Users.ToListAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Roles_AreIsolatedByTenant_ThroughRestrictedRole()
    {
        await using (var setupDb = CreateContext(_tenantAId, "rls-test-tenant-a", useRestrictedRole: true))
        {
            setupDb.Roles.Add(new Role { Id = Guid.NewGuid(), TenantId = _tenantAId, Name = "RLS Test Role" });
            await setupDb.SaveChangesAsync();
        }

        await using var dbA = CreateContext(_tenantAId, "rls-test-tenant-a", useRestrictedRole: true);
        (await dbA.Roles.ToListAsync()).Should().ContainSingle();

        await using var dbB = CreateContext(_tenantBId, "rls-test-tenant-b", useRestrictedRole: true);
        (await dbB.Roles.ToListAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task MfaChallenges_AreIsolatedByTenant_ThroughRestrictedRole()
    {
        await using (var setupDb = CreateContext(_tenantAId, "rls-test-tenant-a", useRestrictedRole: true))
        {
            setupDb.MfaChallenges.Add(new MfaChallenge
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantAId,
                UserId = _userAId,
                ChallengeHash = new string('a', 64),
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5)
            });
            await setupDb.SaveChangesAsync();
        }

        await using var dbA = CreateContext(_tenantAId, "rls-test-tenant-a", useRestrictedRole: true);
        (await dbA.MfaChallenges.ToListAsync()).Should().ContainSingle();

        await using var dbB = CreateContext(_tenantBId, "rls-test-tenant-b", useRestrictedRole: true);
        (await dbB.MfaChallenges.ToListAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task TenantStorageStats_AreIsolatedByTenant_ThroughRestrictedRole()
    {
        await using (var setupDb = CreateContext(_tenantAId, "rls-test-tenant-a", useRestrictedRole: true))
        {
            setupDb.TenantStorageStats.Add(new TenantStorageStats
            {
                TenantId = _tenantAId,
                UsedR2Bytes = 100,
                UsedDbBytes = 0,
                ReservedR2Bytes = 0,
                UpdatedAt = DateTimeOffset.UtcNow
            });
            await setupDb.SaveChangesAsync();
        }

        await using var dbA = CreateContext(_tenantAId, "rls-test-tenant-a", useRestrictedRole: true);
        (await dbA.TenantStorageStats.ToListAsync()).Should().ContainSingle();

        await using var dbB = CreateContext(_tenantBId, "rls-test-tenant-b", useRestrictedRole: true);
        (await dbB.TenantStorageStats.ToListAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task FileRecordsAndReservations_AreIsolatedByTenant_ThroughRestrictedRole()
    {
        await using (var setupDb = CreateContext(_tenantAId, "rls-test-tenant-a", useRestrictedRole: true))
        {
            setupDb.FileRecords.Add(new FileRecord
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantAId,
                StorageKey = "tenants/a/rls-test/key.png",
                OriginalFileName = "test.png",
                SafeFileName = "test.png",
                ContentType = "image/png",
                FileSizeBytes = 10,
                ChecksumSha256 = new string('a', 64),
                UploadedByUserId = _userAId,
                Status = FileRecordStatus.PendingScan,
                CreatedAt = DateTimeOffset.UtcNow
            });
            setupDb.FileUploadReservations.Add(new FileUploadReservation
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantAId,
                ReservedBytes = 10,
                Status = FileUploadReservationStatus.Active,
                ReservedByUserId = _userAId,
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30),
                CreatedAt = DateTimeOffset.UtcNow
            });
            await setupDb.SaveChangesAsync();
        }

        await using var dbA = CreateContext(_tenantAId, "rls-test-tenant-a", useRestrictedRole: true);
        (await dbA.FileRecords.ToListAsync()).Should().ContainSingle();
        (await dbA.FileUploadReservations.ToListAsync()).Should().ContainSingle();

        await using var dbB = CreateContext(_tenantBId, "rls-test-tenant-b", useRestrictedRole: true);
        (await dbB.FileRecords.ToListAsync()).Should().BeEmpty();
        (await dbB.FileUploadReservations.ToListAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task UserProfileSettingsAndReferencePhotos_AreIsolatedByTenant_ThroughRestrictedRole()
    {
        var settingsId = Guid.NewGuid();
        var photoId = Guid.NewGuid();

        await using (var setupDb = CreateContext(_tenantAId, "rls-test-tenant-a", useRestrictedRole: true))
        {
            setupDb.EmployeeWorkLocationSettings.Add(new EmployeeWorkLocationSettings
            {
                Id = settingsId,
                TenantId = _tenantAId,
                EmployeeId = Guid.NewGuid(),
                WorkMode = "hybrid",
                SetById = _userAId
            });
            setupDb.VerificationReferencePhotos.Add(new VerificationReferencePhoto
            {
                Id = photoId,
                TenantId = _tenantAId,
                EmployeeId = Guid.NewGuid(),
                PhotoFileId = Guid.NewGuid(),
                Status = "approved",
                IsActive = true
            });
            await setupDb.SaveChangesAsync();
        }

        await using var dbA = CreateContext(_tenantAId, "rls-test-tenant-a", useRestrictedRole: true);
        (await dbA.EmployeeWorkLocationSettings.IgnoreQueryFilters().ToListAsync())
            .Should().ContainSingle(settings => settings.Id == settingsId);
        (await dbA.VerificationReferencePhotos.IgnoreQueryFilters().ToListAsync())
            .Should().ContainSingle(photo => photo.Id == photoId);

        await using var dbB = CreateContext(_tenantBId, "rls-test-tenant-b", useRestrictedRole: true);
        (await dbB.EmployeeWorkLocationSettings.IgnoreQueryFilters().ToListAsync()).Should().BeEmpty();
        (await dbB.VerificationReferencePhotos.IgnoreQueryFilters().ToListAsync()).Should().BeEmpty();

        await using (var systemDb = CreateContext(useRestrictedRole: true))
        {
            (await systemDb.EmployeeWorkLocationSettings.IgnoreQueryFilters().ToListAsync())
                .Should().ContainSingle(settings => settings.Id == settingsId);
            (await systemDb.VerificationReferencePhotos.IgnoreQueryFilters().ToListAsync())
                .Should().ContainSingle(photo => photo.Id == photoId);
        }

        dbB.EmployeeWorkLocationSettings.Add(new EmployeeWorkLocationSettings
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantAId,
            EmployeeId = Guid.NewGuid(),
            WorkMode = "hybrid",
            SetById = _userAId
        });

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => dbB.SaveChangesAsync());
        var postgresException = Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, postgresException.SqlState);
        Assert.Contains("row-level security policy", postgresException.MessageText, StringComparison.OrdinalIgnoreCase);
    }

    private static Tenant NewTenant(string name, string slug) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Slug = slug,
        CompanySizeRange = "51-200",
        Status = TenantStatus.Active
    };

    private ApplicationDbContext CreateContext(Guid? tenantId = null, string? slug = null, bool useRestrictedRole = false)
    {
        var connectionString = useRestrictedRole ? _restrictedConnectionString : _connectionString;

        var tenantContext = new TenantContextAccessor();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new TenantRlsInterceptor(tenantContext))
            .Options;

        if (tenantId is not null && slug is not null)
        {
            tenantContext.Resolve(new ONEVO.Application.Common.ServiceInterfaces.TenantRegistryEntry(tenantId.Value, slug, TenantStatus.Active, null));
        }

        return new ApplicationDbContext(
            options,
            new AuditableEntityInterceptor(new AnonymousCurrentUser(), _clock),
            new SoftDeleteInterceptor(_clock),
            new DomainEventDispatchInterceptor(new NoOpPublisher()),
            tenantContext);
    }
}
