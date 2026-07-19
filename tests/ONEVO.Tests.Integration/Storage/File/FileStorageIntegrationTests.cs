using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.Storage.File.Entities;
using ONEVO.Infrastructure.ExternalServices.Messaging;
using ONEVO.Infrastructure.Identity;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.Storage.Quota;
using Testcontainers.PostgreSql;

namespace ONEVO.Tests.Integration.Storage.File;

/// <summary>
/// Proves the file storage foundation against a real PostgreSQL database: the
/// AddFileStorageTables + AddFileStorageRlsPolicies migrations apply,
/// file_records/file_upload_reservations rows are isolated per tenant by
/// PostgreSQL RLS, foreign keys are enforced, and concurrent reservations
/// against tenant_storage_stats cannot oversubscribe the tenant's limit.
///
/// Migrations and seeding run through the Testcontainers superuser role
/// (like every other integration test in this suite). The tenant-isolation
/// assertion, however, runs through a dedicated non-superuser Postgres role
/// created inline in InitializeAsync, for two reasons uncovered while writing
/// this test:
///
/// 1. PostgreSQL superusers always bypass RLS regardless of FORCE ROW LEVEL
///    SECURITY (the Testcontainers default role and the real appsettings
///    connection role are both superuser-equivalent), so testing isolation
///    through the superuser connection would trivially and misleadingly
///    always leak — for these two tables or any of the other 20+
///    RLS-protected tables in this codebase.
/// 2. Directly-constructed ApplicationDbContext instances (the pattern this
///    whole integration test suite uses) never go through
///    ONEVO.Infrastructure.DependencyInjection's AddDbContext registration,
///    which is the only place TenantRlsInterceptor is normally attached. This
///    test attaches it explicitly in CreateContext so the
///    app.current_tenant_id / app.tenant_context_mode session GUCs the
///    tenant_isolation RLS policy depends on actually get set.
///
/// The restricted role is test-only and never touches production
/// configuration. Requires Docker (Testcontainers).
/// </summary>
public sealed class FileStorageIntegrationTests : IAsyncLifetime
{
    private const string RestrictedRoleName = "file_storage_test_role";
    private const string RestrictedRolePassword = "file-storage-test-role-password";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("onevo_file_storage_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private readonly SystemDateTimeProvider _clock = new();

    private string _connectionString = string.Empty;
    private string _restrictedConnectionString = string.Empty;
    private Guid _tenantAId;
    private Guid _tenantBId;
    private Guid _userId;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _connectionString = _postgres.GetConnectionString();

        await using var db = CreateContext();
        await db.Database.MigrateAsync();

        var tenantA = NewTenant("File Storage Tenant A", "file-storage-tenant-a");
        var tenantB = NewTenant("File Storage Tenant B", "file-storage-tenant-b");
        _tenantAId = tenantA.Id;
        _tenantBId = tenantB.Id;

        var user = new User
        {
            Id = Guid.NewGuid(),
            TenantId = tenantA.Id,
            Email = "file-storage@test.onevo.dev",
            PasswordHash = "not-a-real-hash",
            FirstName = "File",
            LastName = "Tester",
            IsActive = true
        };
        _userId = user.Id;

        db.Tenants.AddRange(tenantA, tenantB);
        db.Users.Add(user);
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
                        CREATE ROLE {RestrictedRoleName} LOGIN PASSWORD '{RestrictedRolePassword}' NOSUPERUSER NOBYPASSRLS;
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
                GRANT SELECT, INSERT, UPDATE, DELETE ON file_records, file_upload_reservations TO {RestrictedRoleName};
                GRANT SELECT ON tenants, users TO {RestrictedRoleName};
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

    public async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task FileRecords_AreIsolatedByTenant()
    {
        // Every context in this test uses the restricted, non-superuser role
        // so the RLS policy is actually exercised (see class doc comment).

        // Insert as tenant A using a tenant-resolved context. file_records is
        // RLS-protected with FORCE ROW LEVEL SECURITY and no system/admin
        // bypass policy (matching production: IFileStorageService always
        // writes through a tenant-resolved context), so a system-mode write
        // here would fail the WITH CHECK clause.
        await using (var setupDb = CreateContext(_tenantAId, "file-storage-tenant-a", useRestrictedRole: true))
        {
            setupDb.FileRecords.Add(new FileRecord
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantAId,
                StorageKey = "tenants/a/employee_avatar/2026/07/key.png",
                OriginalFileName = "avatar.png",
                SafeFileName = "avatar.png",
                ContentType = "image/png",
                FileSizeBytes = 1024,
                ChecksumSha256 = new string('a', 64),
                UploadedByUserId = _userId,
                Status = FileRecordStatus.PendingScan,
                CreatedAt = DateTimeOffset.UtcNow
            });
            await setupDb.SaveChangesAsync();
        }

        // A context resolved into tenant A's own scope must see the row.
        await using (var dbA = CreateContext(_tenantAId, "file-storage-tenant-a", useRestrictedRole: true))
        {
            var visibleToTenantA = await dbA.FileRecords.ToListAsync();
            visibleToTenantA.Should().ContainSingle();
        }

        // A context resolved into tenant B's scope must never see tenant A's
        // row — enforced by the PostgreSQL tenant_isolation RLS policy.
        await using (var dbB = CreateContext(_tenantBId, "file-storage-tenant-b", useRestrictedRole: true))
        {
            var visibleToTenantB = await dbB.FileRecords.ToListAsync();
            visibleToTenantB.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task CompletedFileRecordId_ForeignKey_IsEnforced()
    {
        // Tenant-resolved context: file_upload_reservations is RLS-protected,
        // so a system-mode write would fail the WITH CHECK clause before ever
        // reaching the foreign key check this test targets.
        await using var db = CreateContext(_tenantAId, "file-storage-tenant-a");

        db.FileUploadReservations.Add(new FileUploadReservation
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantAId,
            ReservedBytes = 1024,
            Status = FileUploadReservationStatus.Active,
            ReservedByUserId = _userId,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30),
            CompletedFileRecordId = Guid.NewGuid(), // does not exist in file_records
            CreatedAt = DateTimeOffset.UtcNow
        });

        var act = async () => await db.SaveChangesAsync();

        var failure = await act.Should().ThrowAsync<DbUpdateException>(
            "file_upload_reservations.completed_file_record_id must be a foreign key to file_records.id");
        failure.Which.InnerException.Should().BeOfType<Npgsql.PostgresException>()
            .Which.SqlState.Should().Be("23503", "23503 is the PostgreSQL foreign_key_violation code");
    }

    [Fact]
    public async Task ConcurrentReservations_CannotOversubscribeReservedBytes()
    {
        const long limitBytes = 10_000;
        const long perReservationBytes = 3_000;
        const int parallelAttempts = 6;

        await using (var setupDb = CreateContext())
        {
            await setupDb.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO tenant_storage_stats (tenant_id, used_r2_bytes, used_db_bytes, reserved_r2_bytes, updated_at)
                VALUES ({_tenantAId}, 0, 0, 0, now())
            ");
        }

        var tasks = new List<Task<bool>>();
        for (var i = 0; i < parallelAttempts; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                await using var attemptDb = CreateContext();
                var repository = new EfTenantStorageStatsRepository(attemptDb);
                return await repository.TryReserveBytesAsync(_tenantAId, perReservationBytes, limitBytes);
            }));
        }

        var results = await Task.WhenAll(tasks);
        var successfulCount = results.Count(r => r);

        await using var verifyDb = CreateContext();
        var stats = await verifyDb.TenantStorageStats
            .AsNoTracking()
            .SingleAsync(s => s.TenantId == _tenantAId);

        stats.ReservedR2Bytes.Should().BeLessThanOrEqualTo(limitBytes);
        stats.ReservedR2Bytes.Should().Be(
            successfulCount * perReservationBytes,
            "every successful reservation must be reflected exactly once, and no failed reservation may leak bytes");
        successfulCount.Should().Be(3, "10,000 / 3,000 allows exactly 3 successful reservations (9,000 <= 10,000)");
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

        // Directly-constructed ApplicationDbContext instances (this pattern,
        // used across this whole integration test suite) do not go through
        // ONEVO.Infrastructure.DependencyInjection's AddDbContext registration,
        // which is the only place TenantRlsInterceptor is normally attached.
        // Add it explicitly so the app.current_tenant_id / app.tenant_context_mode
        // session GUCs the tenant_isolation RLS policy depends on actually get
        // set for this connection.
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new TenantRlsInterceptor(tenantContext))
            .Options;

        if (tenantId is not null && slug is not null)
        {
            tenantContext.Resolve(new TenantRegistryEntry(tenantId.Value, slug, TenantStatus.Active, null));
        }

        return new ApplicationDbContext(
            options,
            new AuditableEntityInterceptor(new AnonymousCurrentUser(), _clock),
            new SoftDeleteInterceptor(_clock),
            new DomainEventDispatchInterceptor(new NoOpPublisher()),
            tenantContext);
    }
}
