using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.Monitoring.Settings.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;
using ONEVO.Infrastructure.ExternalServices.Messaging;
using ONEVO.Infrastructure.Identity.CurrentUser;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Identity.Time;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Tests.Integration.Support;
using Testcontainers.PostgreSql;
using Xunit;

namespace ONEVO.Tests.Integration.Migrations;

/// <summary>
/// Task 14: proves BackfillWorkModesFromLegacyData correctly derives tenant_work_modes,
/// remaps employees/attendance_records off their legacy columns, and carries ClockInPolicy's
/// location settings onto Monitoring - then proves DropLegacyWorkAreaColumns actually removes
/// the legacy columns physically. Pinned at ReworkWorkAreaChangeRequestToWorkModeIds (Task 6's
/// migration): the last point where every column this task touches already exists in its
/// legacy, pre-backfill shape (attendance_records.expected_work_mode_id/name present but null,
/// employees.legacy_work_mode_id present, tenant_work_modes empty). Nothing between that pin and
/// the current tip touches employees/legal_entities/clock_in_policies/attendance_records, so
/// fixture rows are inserted through plain EF SaveChanges (not raw SQL like
/// AttendanceCorrectionsMigrationUpgradeTests) - the compiled model matches the DB at every
/// point this test cares about. Legal entities are inserted this way specifically so
/// WorkModeSeeder (which only runs from CreateLegalEntityCommandHandler, not from any
/// SaveChanges interceptor) never fires - otherwise the "legal entity with zero WorkMode rows"
/// fallback case this test exercises would be pre-satisfied and prove nothing.
///
/// Deviation from the original plan text: work_area_change_requests.requested_work_area/
/// current_expected_work_area do not exist any more - Task 6's own migration already dropped
/// them (see its commit message: "safe in this dev database, Task 5's migration had not yet
/// populated the new columns with any real data"). So this task does not touch
/// work_area_change_requests at all; legacy_work_area_label stays an unpopulated nullable
/// column (nothing can ever write to it - its only source data is already gone).
/// </summary>
public sealed class WorkModeDataMigrationTests : IAsyncLifetime
{
    private const string PreBackfillMigration = "20260913235156_ReworkWorkAreaChangeRequestToWorkModeIds";
    private const string CurrentTipMigration = "20260914015437_AddEmployeeMonitoringOverrideAllowedRadiusMeters";
    private const string BackfillMigration = "20260914090000_BackfillWorkModesFromLegacyData";

    // tenant_work_modes gained allows_daily_location_choice/self_registers_location after this
    // task was originally pinned (AddWorkModeLocationFlags). EF's compiled WorkMode entity always
    // projects every column it currently knows about regardless of which migration the physical
    // schema was moved to, so querying WorkMode via EF between BackfillMigration and this point
    // fails with "column ... does not exist" unless the schema is brought forward to include them
    // first. Migrating here also carries DropLegacyWorkAreaColumns and the tray-identity
    // backfills along with it - none of them touch data this test's assertions depend on.
    private const string LatestMigration = "20260916095727_BackfillTrayEmployeeIdentityPhase2";

    private PostgreSqlContainer _postgres = null!;
    private string _migratorConnectionString = null!;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("onevo_workmode_migration_test")
            .WithUsername("test")
            .WithPassword("test")
            .Build();
        await _postgres.StartAsync();

        var adminConnectionString = _postgres.GetConnectionString();
        await PrivilegedRoleTestBootstrap.EnsureRolesExistAsync(adminConnectionString);

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
    public async Task MigratingForward_BackfillsWorkModesRemapsLegacyColumnsAndDropsThem()
    {
        await using (var context = CreateContext())
            await context.Database.GetService<IMigrator>().MigrateAsync(PreBackfillMigration);

        var tenant = new Tenant { Id = Guid.NewGuid(), Name = "WorkMode Migration Co", Slug = "wm-migration-co", CompanySizeRange = "1-50" };
        var legalEntityWithPolicy = new LegalEntity
        {
            Id = Guid.NewGuid(), TenantId = tenant.Id, Name = "LE With Policy", CountryCode = "LK", CurrencyCode = "LKR"
        };
        var legalEntityWithoutPolicy = new LegalEntity
        {
            Id = Guid.NewGuid(), TenantId = tenant.Id, Name = "LE Without Policy", CountryCode = "LK", CurrencyCode = "LKR"
        };

        // ClockInPolicy's flat Onsite*/Remote*/Either*/location columns were dropped from the
        // compiled entity as part of this same task, and several are NOT NULL with no DB-level
        // default (e.g. location_verification_required), so this row cannot be built via EF at
        // all any more - it is inserted entirely via raw SQL below (the columns are still
        // physically present at PreBackfillMigration, since the drop migration hasn't run yet) to
        // give the backfill migration legacy-shaped source data.
        var clockInPolicyId = Guid.NewGuid();

        var userOnsite = NewUser(tenant.Id, "onsite@wm-migration.test");
        var userRemote = NewUser(tenant.Id, "remote@wm-migration.test");
        var userHybrid = NewUser(tenant.Id, "hybrid@wm-migration.test");

        var employeeOnsite = NewEmployee(tenant.Id, legalEntityWithPolicy.Id, userOnsite.Id, "WM-001");
        var employeeRemote = NewEmployee(tenant.Id, legalEntityWithPolicy.Id, userRemote.Id, "WM-002");
        var employeeHybrid = NewEmployee(tenant.Id, legalEntityWithoutPolicy.Id, userHybrid.Id, "WM-003");

        var attendanceHybrid = new AttendanceRecord
        {
            Id = Guid.NewGuid(), TenantId = tenant.Id, EmployeeId = employeeHybrid.Id, Date = new DateOnly(2026, 9, 1),
            ExpectedWorkingDay = true,
            WorkedMinutes = 0, BreakMinutes = 0, Status = AttendanceRecord.StatusOffDay,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        };
        var attendanceField = new AttendanceRecord
        {
            Id = Guid.NewGuid(), TenantId = tenant.Id, EmployeeId = employeeOnsite.Id, Date = new DateOnly(2026, 9, 1),
            ExpectedWorkingDay = true,
            WorkedMinutes = 0, BreakMinutes = 0, Status = AttendanceRecord.StatusOffDay,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        };

        await using (var context = CreateContext())
        {
            context.AddRange(tenant, legalEntityWithPolicy, legalEntityWithoutPolicy,
                userOnsite, userRemote, userHybrid,
                employeeOnsite, employeeRemote, employeeHybrid, attendanceHybrid, attendanceField);
            await context.SaveChangesAsync();
        }

        // legacy_work_mode_id and expected_work_area were both dropped from their compiled entities
        // as part of this same task (Task 14 strips them once the backfill/remap no longer needs
        // them), so EF's INSERT above never referenced either column - legacy_work_mode_id got
        // Postgres's own column default (0, from the original AddLookupTables migration) and
        // expected_work_area stayed null. Overwrite both with raw SQL so this test can still
        // exercise the legacy-shaped source data the backfill migration reads from.
        await using (var connection = new NpgsqlConnection(_migratorConnectionString))
        {
            await connection.OpenAsync();
            await SetAdminModeAsync(connection);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE employees SET legacy_work_mode_id = 1 WHERE id = @onsite;
                UPDATE employees SET legacy_work_mode_id = 2 WHERE id = @remote;
                UPDATE employees SET legacy_work_mode_id = 3 WHERE id = @hybrid;
                UPDATE attendance_records SET expected_work_area = @hybridArea WHERE id = @hybridRecord;
                UPDATE attendance_records SET expected_work_area = @fieldArea WHERE id = @fieldRecord;
                INSERT INTO clock_in_policies (
                    id, tenant_id, legal_entity_id, name, scope_type, effective_from,
                    location_verification_required, allowed_radius_meters,
                    onsite_biometric_enabled, onsite_web_enabled, onsite_tray_enabled, onsite_photo_required,
                    remote_biometric_enabled, remote_web_enabled, remote_tray_enabled, remote_photo_required,
                    remote_location_check_required,
                    either_biometric_enabled, either_web_enabled, either_tray_enabled, either_photo_required,
                    either_location_check_required, either_source_rule,
                    field_biometric_enabled, field_web_enabled, field_tray_enabled, field_photo_requirement,
                    correction_requires_approval, notification_recipient_resolver, is_active,
                    created_by_id, created_at, updated_at)
                VALUES (
                    @policy, @tenant, @legalEntity, 'Full Company Policy', 'full_company', DATE '2025-01-01',
                    true, 200,
                    true, true, false, false,
                    false, true, false, false,
                    true,
                    false, true, true, false,
                    false, 'employee_choice',
                    false, true, false, 'off',
                    false, 'management_coverage_owner', true,
                    @createdBy, now(), now());
                """;
            command.Parameters.AddWithValue("onsite", employeeOnsite.Id);
            command.Parameters.AddWithValue("remote", employeeRemote.Id);
            command.Parameters.AddWithValue("hybrid", employeeHybrid.Id);
            command.Parameters.AddWithValue("hybridArea", AttendanceRecord.WorkAreaHybrid);
            command.Parameters.AddWithValue("hybridRecord", attendanceHybrid.Id);
            command.Parameters.AddWithValue("fieldArea", AttendanceRecord.WorkAreaField);
            command.Parameters.AddWithValue("fieldRecord", attendanceField.Id);
            command.Parameters.AddWithValue("policy", clockInPolicyId);
            command.Parameters.AddWithValue("tenant", tenant.Id);
            command.Parameters.AddWithValue("legalEntity", legalEntityWithPolicy.Id);
            command.Parameters.AddWithValue("createdBy", Guid.NewGuid());
            await command.ExecuteNonQueryAsync();
        }

        // Brings in monitoring_feature_toggles.allowed_radius_meters (AddMonitoringAllowedRadiusMeters)
        // before the backfill needs to write to it - this column does not exist at PreBackfillMigration.
        await using (var context = CreateContext())
            await context.Database.GetService<IMigrator>().MigrateAsync(CurrentTipMigration);

        await using (var context = CreateContext())
            await context.Database.GetService<IMigrator>().MigrateAsync(BackfillMigration);

        await using (var context = CreateContext())
            await context.Database.GetService<IMigrator>().MigrateAsync(LatestMigration);

        await using (var context = CreateContext())
        {
            // 1. Both legal entities have exactly 3 tenant_work_modes rows each, named Remote/Hybrid/Onsite.
            var withPolicyModes = await context.Set<WorkMode>()
                .Where(w => w.LegalEntityId == legalEntityWithPolicy.Id).ToListAsync();
            var withoutPolicyModes = await context.Set<WorkMode>()
                .Where(w => w.LegalEntityId == legalEntityWithoutPolicy.Id).ToListAsync();
            withPolicyModes.Select(w => w.Name).Should().BeEquivalentTo(["Remote", "Hybrid", "Onsite"]);
            withoutPolicyModes.Select(w => w.Name).Should().BeEquivalentTo(["Remote", "Hybrid", "Onsite"]);

            // 2. The legal entity WITH a ClockInPolicy row has real configured data, not a default.
            var remoteMode = withPolicyModes.Single(w => w.Name == "Remote");
            remoteMode.WebEnabled.Should().BeTrue();
            remoteMode.BiometricEnabled.Should().BeFalse();
            remoteMode.TrayEnabled.Should().BeFalse();
            remoteMode.IsSystemSeeded.Should().BeFalse("this was derived from real ClockInPolicy data, not a fresh default");
            var hybridMode = withPolicyModes.Single(w => w.Name == "Hybrid");
            hybridMode.TrayEnabled.Should().BeTrue();

            // 3. The legal entity WITHOUT a ClockInPolicy row got Task 3's plain defaults - matches
            //    WorkModeSeeder exactly: WebEnabled=true on all 3, everything else false.
            withoutPolicyModes.Should().OnlyContain(w => w.IsSystemSeeded);
            withoutPolicyModes.Should().OnlyContain(w => w.WebEnabled);
            withoutPolicyModes.Should().OnlyContain(w => !w.BiometricEnabled && !w.TrayEnabled && !w.PhotoRequired);

            // 4. monitoring_feature_toggles for the legal entity with a policy carries the radius/verification.
            var toggles = await context.Set<MonitoringFeatureToggles>()
                .SingleAsync(t => t.LegalEntityId == legalEntityWithPolicy.Id);
            toggles.AllowedRadiusMeters.Should().Be(200);
            toggles.WorkLocationVerification.Should().BeTrue();

            // 5. Every employee's new WorkModeId points at the correctly-named WorkMode row for their legal entity.
            var reloadedOnsite = await context.Set<Employee>().SingleAsync(e => e.Id == employeeOnsite.Id);
            var reloadedRemote = await context.Set<Employee>().SingleAsync(e => e.Id == employeeRemote.Id);
            var reloadedHybrid = await context.Set<Employee>().SingleAsync(e => e.Id == employeeHybrid.Id);
            reloadedOnsite.WorkModeId.Should().Be(withPolicyModes.Single(w => w.Name == "Onsite").Id);
            reloadedRemote.WorkModeId.Should().Be(withPolicyModes.Single(w => w.Name == "Remote").Id);
            reloadedHybrid.WorkModeId.Should().Be(withoutPolicyModes.Single(w => w.Name == "Hybrid").Id);

            // 6. The "either" AttendanceRecord row now points at the Hybrid row.
            var reloadedHybridAttendance = await context.Set<AttendanceRecord>().SingleAsync(a => a.Id == attendanceHybrid.Id);
            reloadedHybridAttendance.ExpectedWorkModeId.Should().Be(withoutPolicyModes.Single(w => w.Name == "Hybrid").Id);
            reloadedHybridAttendance.ExpectedWorkModeName.Should().Be("Hybrid");

            // 7. The "field" AttendanceRecord row has no WorkMode match, so it falls back to a plain display string.
            var reloadedFieldAttendance = await context.Set<AttendanceRecord>().SingleAsync(a => a.Id == attendanceField.Id);
            reloadedFieldAttendance.ExpectedWorkModeId.Should().BeNull();
            reloadedFieldAttendance.ExpectedWorkModeName.Should().Be("field");
        }

        // DropMigration already ran as part of the MigrateAsync(LatestMigration) call above.
        await using (var connection = new NpgsqlConnection(_migratorConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT legacy_work_mode_id FROM employees LIMIT 1;";
            var act = async () => await command.ExecuteScalarAsync();
            await act.Should().ThrowAsync<PostgresException>("employees.legacy_work_mode_id should have been dropped");
        }

        await using (var connection = new NpgsqlConnection(_migratorConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT expected_work_area FROM attendance_records LIMIT 1;";
            var act = async () => await command.ExecuteScalarAsync();
            await act.Should().ThrowAsync<PostgresException>("attendance_records.expected_work_area should have been dropped");
        }

        await using (var connection = new NpgsqlConnection(_migratorConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT remote_web_enabled FROM clock_in_policies LIMIT 1;";
            var act = async () => await command.ExecuteScalarAsync();
            await act.Should().ThrowAsync<PostgresException>("clock_in_policies' flat legacy columns should have been dropped");
        }
    }

    private static User NewUser(Guid tenantId, string email) => new()
    {
        Id = Guid.NewGuid(), TenantId = tenantId, Email = email,
        FirstName = "Emp", LastName = "User", PasswordHash = "not-a-real-hash",
        IsActive = true, EmailVerified = true, CreatedAt = DateTimeOffset.UtcNow, CreatedById = Guid.NewGuid()
    };

    private static Employee NewEmployee(Guid tenantId, Guid legalEntityId, Guid userId, string employeeNumber) => new()
    {
        Id = Guid.NewGuid(), TenantId = tenantId, UserId = userId, LegalEntityId = legalEntityId,
        EmployeeNumber = employeeNumber, FirstName = "Emp", LastName = employeeNumber, Email = $"{employeeNumber}@wm-migration.test",
        HireDate = new DateOnly(2025, 1, 1),
        CreatedAt = DateTimeOffset.UtcNow, CreatedById = userId
    };

    private static async Task SetAdminModeAsync(NpgsqlConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT set_config('app.tenant_context_mode', 'admin', false);";
        await command.ExecuteNonQueryAsync();
    }

    private ApplicationDbContext CreateContext()
    {
        var dateTimeProvider = new SystemDateTimeProvider();
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
