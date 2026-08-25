using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Infrastructure.ExternalServices.Messaging;
using ONEVO.Infrastructure.Identity.CurrentUser;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Identity.Time;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Tests.Integration.Support;
using Testcontainers.PostgreSql;
using Xunit;

namespace ONEVO.Tests.Integration.Features.TimeAttendance;

/// <summary>
/// Item 8 of the Attendance Correction fix plan: proves the additive approval-snapshot migration
/// (20260824154945_AddAttendanceCorrectionApprovalRequired) correctly backfills `approval_required`
/// for rows that existed under the *previous* migration's schema (20260824120000_AddAttendanceCorrections,
/// which has no approval_required column at all), rather than only being exercised against a
/// database that already has the column.
///
/// This needs full, isolated control over the migration timeline (migrate to an exact historical
/// point, insert legacy-shaped rows, migrate forward), which the other AttendanceCorrections
/// integration tests' shared, fully-migrated fixture database cannot provide - hence its own
/// throwaway Testcontainers instance and no WebApplicationFactory/HTTP layer at all.
/// </summary>
public sealed class AttendanceCorrectionsMigrationUpgradeTests : IAsyncLifetime
{
    private const string PreApprovalSnapshotMigration = "20260822063849_AddBreakRecordOpenUniqueness";
    private const string BaseTableMigration = "20260824120000_AddAttendanceCorrections";
    private const string ApprovalSnapshotMigration = "20260824154945_AddAttendanceCorrectionApprovalRequired";

    private PostgreSqlContainer _postgres = null!;
    private string _migratorConnectionString = null!;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("onevo_attendance_migration_upgrade_test")
            .WithUsername("test")
            .WithPassword("test")
            .Build();
        await _postgres.StartAsync();

        var adminConnectionString = _postgres.GetConnectionString();
        await PrivilegedRoleTestBootstrap.EnsureRolesExistAsync(adminConnectionString);

        // PrivilegedRoleTestBootstrap only creates the roles (so the app can authenticate as
        // onevo_app during startup validation); it does not grant onevo_migrator schema-level
        // DDL rights the way ops/postgres/local-bootstrap-roles.sql does for real deployments.
        // Every other integration test in this suite sidesteps this by migrating as the
        // Testcontainers superuser instead - this test deliberately migrates as onevo_migrator to
        // mirror production, so it must grant what local-bootstrap-roles.sql would.
        var databaseName = new NpgsqlConnectionStringBuilder(adminConnectionString).Database;
        await using (var adminConnection = new NpgsqlConnection(adminConnectionString))
        {
            await adminConnection.OpenAsync();
            await using var grantCommand = adminConnection.CreateCommand();
            grantCommand.CommandText = $"""
                GRANT CREATE, USAGE ON SCHEMA public TO onevo_migrator;
                GRANT CREATE ON DATABASE "{databaseName}" TO onevo_migrator;
                GRANT USAGE ON SCHEMA public TO onevo_app;
                ALTER DEFAULT PRIVILEGES FOR ROLE onevo_migrator IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO onevo_app;
                ALTER DEFAULT PRIVILEGES FOR ROLE onevo_migrator IN SCHEMA public GRANT USAGE, SELECT ON SEQUENCES TO onevo_app;
                GRANT onevo_auth_base_login_fn_owner TO onevo_migrator;
                """;
            await grantCommand.ExecuteNonQueryAsync();
        }

        _migratorConnectionString = new NpgsqlConnectionStringBuilder(adminConnectionString)
        {
            Username = "onevo_migrator",
            Password = PrivilegedRoleTestBootstrap.MigratorRolePassword
        }.ConnectionString;
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    public async Task MigratingForward_BackfillsApprovalRequiredFromHistoricalStatusAndReviewFields()
    {
        // Step 1: migrate only up to the migration immediately before attendance_corrections
        // exists at all, so every table the correction rows FK to (tenants/legal_entities/
        // employees/users) is already present.
        await using (var context = CreateContext())
        {
            await context.Database.GetService<IMigrator>().MigrateAsync(PreApprovalSnapshotMigration);
        }

        Guid tenantId, legalEntityId, employeeId, requesterId, reviewerId;
        await using (var context = CreateContext())
        {
            var userId = Guid.NewGuid();
            var tenant = new Tenant { Id = Guid.NewGuid(), Name = "Migration Upgrade Co", Slug = "mig-upgrade-co", CompanySizeRange = "1-50" };
            var legalEntity = new LegalEntity { Id = Guid.NewGuid(), TenantId = tenant.Id, Name = "Migration Upgrade Co", CountryCode = "LK", CurrencyCode = "LKR" };
            var requester = new User
            {
                Id = userId, TenantId = tenant.Id, Email = "requester@mig-upgrade.test",
                FirstName = "Req", LastName = "User", PasswordHash = "not-a-real-hash",
                IsActive = true, EmailVerified = true, CreatedAt = DateTimeOffset.UtcNow, CreatedById = userId
            };
            var reviewerUserId = Guid.NewGuid();
            var reviewer = new User
            {
                Id = reviewerUserId, TenantId = tenant.Id, Email = "reviewer@mig-upgrade.test",
                FirstName = "Rev", LastName = "User", PasswordHash = "not-a-real-hash",
                IsActive = true, EmailVerified = true, CreatedAt = DateTimeOffset.UtcNow, CreatedById = userId
            };
            var employee = new Employee
            {
                Id = Guid.NewGuid(), TenantId = tenant.Id, UserId = userId, EmployeeNumber = "MIG-001",
                FirstName = "Req", LastName = "User", Email = requester.Email, LegalEntityId = legalEntity.Id,
                EmploymentTypeId = 1, EmploymentStatusId = 1, WorkModeId = 1,
                HireDate = new DateOnly(2025, 1, 1), CreatedAt = DateTimeOffset.UtcNow, CreatedById = userId
            };

            context.AddRange(tenant, legalEntity, requester, reviewer, employee);
            await context.SaveChangesAsync();

            tenantId = tenant.Id;
            legalEntityId = legalEntity.Id;
            employeeId = employee.Id;
            requesterId = userId;
            reviewerId = reviewerUserId;
        }

        // Step 2: migrate exactly to the base table's own migration - attendance_corrections now
        // exists, but without approval_required, matching the historical pre-approval-snapshot shape.
        await using (var context = CreateContext())
        {
            await context.Database.GetService<IMigrator>().MigrateAsync(BaseTableMigration);
        }

        // Step 3: with the schema still at the pre-approval_required shape, insert one
        // representative row per historical workflow outcome via raw SQL - the current compiled
        // AttendanceCorrection entity always includes approval_required, so EF's normal
        // SaveChanges cannot be used to write a row that predates the column.
        var (pendingId, approvedManualId, approvedAutoId, rejectedId, cancelledId) =
            (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        await using (var connection = new NpgsqlConnection(_migratorConnectionString))
        {
            await connection.OpenAsync();
            await SetAdminModeAsync(connection);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO attendance_corrections (
                    id, tenant_id, employee_id, legal_entity_id, work_date, correction_type,
                    reason, status, requested_by_id, reviewed_by_id, reviewed_at, created_at, updated_at
                ) VALUES
                    (@pending, @tenant, @employee, @legalEntity, CURRENT_DATE, 'clock_in', 'legacy pending', 'pending', @requester, NULL, NULL, now(), now()),
                    (@approvedManual, @tenant, @employee, @legalEntity, CURRENT_DATE, 'clock_in', 'legacy approved manual', 'approved', @requester, @reviewer, now(), now(), now()),
                    (@approvedAuto, @tenant, @employee, @legalEntity, CURRENT_DATE, 'clock_in', 'legacy approved auto', 'approved', @requester, NULL, NULL, now(), now()),
                    (@rejected, @tenant, @employee, @legalEntity, CURRENT_DATE, 'clock_in', 'legacy rejected', 'rejected', @requester, @reviewer, now(), now(), now()),
                    (@cancelled, @tenant, @employee, @legalEntity, CURRENT_DATE, 'clock_in', 'legacy cancelled', 'cancelled', @requester, @requester, now(), now(), now());
                """;
            command.Parameters.AddWithValue("pending", pendingId);
            command.Parameters.AddWithValue("approvedManual", approvedManualId);
            command.Parameters.AddWithValue("approvedAuto", approvedAutoId);
            command.Parameters.AddWithValue("rejected", rejectedId);
            command.Parameters.AddWithValue("cancelled", cancelledId);
            command.Parameters.AddWithValue("tenant", tenantId);
            command.Parameters.AddWithValue("employee", employeeId);
            command.Parameters.AddWithValue("legalEntity", legalEntityId);
            command.Parameters.AddWithValue("requester", requesterId);
            command.Parameters.AddWithValue("reviewer", reviewerId);
            await command.ExecuteNonQueryAsync();
        }

        // Step 4: migrate forward through the approval-snapshot migration, which adds the column
        // and runs the backfill UPDATE against exactly these rows.
        await using (var context = CreateContext())
        {
            await context.Database.GetService<IMigrator>().MigrateAsync(ApprovalSnapshotMigration);
        }

        await using (var connection = new NpgsqlConnection(_migratorConnectionString))
        {
            await connection.OpenAsync();
            await SetAdminModeAsync(connection);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT id, approval_required FROM attendance_corrections ORDER BY reason;";
            await using var reader = await command.ExecuteReaderAsync();
            var backfilled = new Dictionary<Guid, bool>();
            while (await reader.ReadAsync())
                backfilled[reader.GetGuid(0)] = reader.GetBoolean(1);

            backfilled[pendingId].Should().BeTrue("a pending request was, by definition, waiting for approval");
            backfilled[approvedManualId].Should().BeTrue("reviewed_by_id/reviewed_at evidence a real approver acted");
            backfilled[approvedAutoId].Should().BeFalse("no reviewer evidence means this was auto-approved, not manually reviewed");
            backfilled[rejectedId].Should().BeTrue("a rejection can only happen to an approval-required request");
            backfilled[cancelledId].Should().BeTrue("a cancellation can only happen to a still-pending, approval-required request");
        }
    }

    /// <summary>
    /// Plain ADO.NET connections (unlike CreateContext()'s ApplicationDbContext, which wires
    /// TenantRlsInterceptor) never set app.tenant_context_mode themselves, so attendance_corrections'
    /// FORCE ROW LEVEL SECURITY policy - which applies even to onevo_migrator, the table's own owner -
    /// would otherwise silently filter every row out of both the raw INSERT and the raw SELECT below.
    /// </summary>
    private static async Task SetAdminModeAsync(NpgsqlConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT set_config('app.tenant_context_mode', 'admin', false);";
        await command.ExecuteNonQueryAsync();
    }

    private ApplicationDbContext CreateContext()
    {
        var dateTimeProvider = new SystemDateTimeProvider();

        // Real `dotnet ef database update` (ApplicationDbContextFactory) never wires
        // TenantRlsInterceptor either - it doesn't need to, because migrations are pure DDL and
        // RLS row-security policies don't gate schema changes. This test additionally performs
        // real DML (SaveChangesAsync inserts of the tenant/legal-entity/user/employee fixture
        // rows) through the same connection, which DOES hit "employees" RLS - so unlike the
        // design-time factory, this context needs the interceptor, set to admin mode (bypasses
        // the tenant-match predicate) purely for that seeding step.
        var tenantContext = new TenantContextAccessor();
        tenantContext.SetAdminMode();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_migratorConnectionString)
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new TenantRlsInterceptor(tenantContext))
            .Options;

        return new ApplicationDbContext(
            options,
            new AuditableEntityInterceptor(new AnonymousCurrentUser(), dateTimeProvider),
            new SoftDeleteInterceptor(dateTimeProvider),
            new DomainEventDispatchInterceptor(new NoOpPublisher()),
            tenantContext);
    }
}
