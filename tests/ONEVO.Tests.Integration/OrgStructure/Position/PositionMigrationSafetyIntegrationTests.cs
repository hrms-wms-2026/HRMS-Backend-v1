using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using ONEVO.Infrastructure.ExternalServices.Messaging;
using ONEVO.Infrastructure.Identity.CurrentUser;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Identity.Time;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.OrgStructure;
using ONEVO.Tests.Integration.Support;
using Testcontainers.PostgreSql;
using Xunit;

namespace ONEVO.Tests.Integration.OrgStructure.Position;

public class PositionMigrationSafetyIntegrationTests : IAsyncLifetime
{
    // The migration immediately before AddPositionFoundationSchema. Kept as a literal (not a
    // reflective "previous migration" lookup) so the test fails loudly if the migration history
    // is reordered, rather than silently replaying against the wrong baseline.
    private const string MigrationBeforeFoundationSchema = "20260804053523_AddDepartmentCodeCaseInsensitiveUniqueIndex";
    private const string FoundationSchemaMigration = "20260804102821_AddPositionFoundationSchema";

    private PostgreSqlContainer? _postgres;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .Build();

        await _postgres.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_postgres is not null)
        {
            await _postgres.DisposeAsync();
        }
    }

    private static ApplicationDbContext CreateDbContext(string connectionString)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention();

        var dateTimeProvider = new SystemDateTimeProvider();
        return new ApplicationDbContext(
            optionsBuilder.Options,
            new AuditableEntityInterceptor(new AnonymousCurrentUser(), dateTimeProvider),
            new SoftDeleteInterceptor(dateTimeProvider),
            new DomainEventDispatchInterceptor(new NoOpPublisher()),
            new TenantContextAccessor());
    }

    private static async Task<PositionsRlsSnapshot> CaptureRlsSnapshotAsync(NpgsqlConnection connection)
    {
        bool rowSecurityEnabled;
        bool rowSecurityForced;

        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT relrowsecurity, relforcerowsecurity FROM pg_class WHERE relname = 'positions';";
            await using var reader = await cmd.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync(), "positions table not found in pg_class.");
            rowSecurityEnabled = reader.GetBoolean(0);
            rowSecurityForced = reader.GetBoolean(1);
        }

        var policies = new List<string>();
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = """
                SELECT policyname || '|' || qual || '|' || COALESCE(with_check, '')
                FROM pg_policies
                WHERE tablename = 'positions'
                ORDER BY policyname;
                """;
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                policies.Add(reader.GetString(0));
            }
        }

        return new PositionsRlsSnapshot(rowSecurityEnabled, rowSecurityForced, policies);
    }

    [Fact]
    public async Task Migration_ReplaysCleanly_OverLegacyPositionRow_InsertedBeforeFoundationSchemaMigration()
    {
        Assert.NotNull(_postgres);
        var connectionString = _postgres.GetConnectionString();

        await PrivilegedRoleTestBootstrap.EnsureRolesExistAsync(connectionString);

        var tenantId = Guid.NewGuid();
        var legacyPositionId = Guid.NewGuid();
        var createdById = Guid.NewGuid();

        // Step 1: apply migrations only up to the migration immediately before
        // AddPositionFoundationSchema - the schema at this point matches the legacy production
        // shape of "positions" (no legal_entity_id, no department_id, no code/is_active/etc.).
        using (var db = CreateDbContext(connectionString))
        {
            var migrator = db.GetInfrastructure().GetRequiredService<IMigrator>();
            var appliedMigrations = (await db.Database.GetAppliedMigrationsAsync()).ToList();
            Assert.DoesNotContain(FoundationSchemaMigration, appliedMigrations);

            await migrator.MigrateAsync(MigrationBeforeFoundationSchema);

            appliedMigrations = (await db.Database.GetAppliedMigrationsAsync()).ToList();
            Assert.Contains(MigrationBeforeFoundationSchema, appliedMigrations);
            Assert.DoesNotContain(FoundationSchemaMigration, appliedMigrations);
        }

        PositionsRlsSnapshot rlsBefore;
        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync();
            rlsBefore = await CaptureRlsSnapshotAsync(connection);
        }

        // Step 2: insert a legacy positions row using only columns that existed before the
        // foundation migration. No legal_entity_id, no department_id, no Guid.Empty placeholders.
        using (var db = CreateDbContext(connectionString))
        {
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO positions (id, tenant_id, name, created_at, created_by_id, is_deleted) VALUES ({0}, {1}, {2}, NOW(), {3}, false);",
                legacyPositionId, tenantId, "Legacy Orphan Position", createdById);
        }

        // Step 3: apply the remaining migrations, including AddPositionFoundationSchema, against
        // a database that already contains the legacy row.
        using (var db = CreateDbContext(connectionString))
        {
            var migrator = db.GetInfrastructure().GetRequiredService<IMigrator>();
            await migrator.MigrateAsync();

            var appliedMigrations = (await db.Database.GetAppliedMigrationsAsync()).ToList();
            Assert.Contains(FoundationSchemaMigration, appliedMigrations);
        }

        // Step 4: the legacy row must still exist, with legal_entity_id and department_id NULL -
        // not backfilled to any fake value.
        using (var db = CreateDbContext(connectionString))
        {
            var legacyRow = await db.Positions
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == legacyPositionId);

            Assert.NotNull(legacyRow);
            Assert.Equal(tenantId, legacyRow.TenantId);
            Assert.Equal("Legacy Orphan Position", legacyRow.Name);
            Assert.Null(legacyRow.LegalEntityId);
            Assert.Null(legacyRow.DepartmentId);
        }

        // Step 5: scoped repository methods still ignore the orphan legacy position (it has no
        // legal entity / department to scope into).
        using (var db = CreateDbContext(connectionString))
        {
            var repo = new EfPositionRepository(db);
            var legalEntityId = Guid.NewGuid();

            var scopedList = await repo.ListByLegalEntityAsync(tenantId, legalEntityId, includeInactive: true);
            Assert.Empty(scopedList);

            var scopedGet = await repo.GetByIdForLegalEntityAsync(tenantId, legalEntityId, legacyPositionId);
            Assert.Null(scopedGet);
        }

        // Step 6: raw schema-level assertions against real PostgreSQL - no Guid.Empty anywhere,
        // the new columns are nullable, the new foreign keys exist with non-cascade delete
        // behavior, and the RLS policy on positions is exactly what it was before this migration.
        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync();

            await using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = """
                    SELECT COUNT(*) FROM positions
                    WHERE legal_entity_id = '00000000-0000-0000-0000-000000000000'
                       OR department_id = '00000000-0000-0000-0000-000000000000';
                    """;
                var guidEmptyCount = (long)(await cmd.ExecuteScalarAsync())!;
                Assert.Equal(0, guidEmptyCount);
            }

            var nullability = new Dictionary<string, string>();
            await using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = """
                    SELECT column_name, is_nullable
                    FROM information_schema.columns
                    WHERE table_name = 'positions' AND column_name IN ('legal_entity_id', 'department_id');
                    """;
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    nullability[reader.GetString(0)] = reader.GetString(1);
                }
            }

            Assert.Equal("YES", nullability["legal_entity_id"]);
            Assert.Equal("YES", nullability["department_id"]);

            var foreignKeyDeleteRules = new Dictionary<string, string>();
            await using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = """
                    SELECT kcu.column_name, rc.delete_rule
                    FROM information_schema.table_constraints tc
                    JOIN information_schema.referential_constraints rc
                        ON tc.constraint_name = rc.constraint_name
                        AND tc.constraint_schema = rc.constraint_schema
                    JOIN information_schema.key_column_usage kcu
                        ON tc.constraint_name = kcu.constraint_name
                        AND tc.constraint_schema = kcu.constraint_schema
                    WHERE tc.table_name = 'positions'
                        AND tc.constraint_type = 'FOREIGN KEY'
                        AND kcu.column_name IN ('legal_entity_id', 'department_id');
                    """;
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    foreignKeyDeleteRules[reader.GetString(0)] = reader.GetString(1);
                }
            }

            Assert.True(foreignKeyDeleteRules.ContainsKey("legal_entity_id"), "Expected FK from positions.legal_entity_id to legal_entities.id.");
            Assert.True(foreignKeyDeleteRules.ContainsKey("department_id"), "Expected FK from positions.department_id to departments.id.");
            Assert.NotEqual("CASCADE", foreignKeyDeleteRules["legal_entity_id"]);
            Assert.NotEqual("CASCADE", foreignKeyDeleteRules["department_id"]);

            var rlsAfter = await CaptureRlsSnapshotAsync(connection);

            // A before/after equality check that compares two empty snapshots would prove
            // nothing. positions already has a real tenant_isolation policy (added by
            // AddMissingRlsPolicies, which runs before this test's migration baseline) - assert
            // the "before" snapshot actually captured it before relying on equality with "after".
            Assert.True(rlsBefore.RowSecurityEnabled, "Expected positions RLS to already be enabled before AddPositionFoundationSchema.");
            Assert.NotEmpty(rlsBefore.Policies);

            Assert.True(rlsAfter.RowSecurityEnabled, "RLS must remain enabled on positions after AddPositionFoundationSchema.");
            Assert.True(rlsAfter.RowSecurityForced, "RLS must remain forced on positions after AddPositionFoundationSchema.");
            Assert.Equal(rlsBefore.RowSecurityEnabled, rlsAfter.RowSecurityEnabled);
            Assert.Equal(rlsBefore.RowSecurityForced, rlsAfter.RowSecurityForced);
            Assert.Equal(rlsBefore.Policies, rlsAfter.Policies);
        }
    }

    private sealed record PositionsRlsSnapshot(bool RowSecurityEnabled, bool RowSecurityForced, List<string> Policies);
}
