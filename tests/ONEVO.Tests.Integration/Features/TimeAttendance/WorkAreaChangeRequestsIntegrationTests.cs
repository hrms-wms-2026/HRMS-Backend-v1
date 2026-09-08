using FluentAssertions;
using Npgsql;
using Testcontainers.PostgreSql;
using ONEVO.Tests.Integration.Support;
using ONEVO.Tests.Integration.Tenancy;
using Xunit;

namespace ONEVO.Tests.Integration.Features.TimeAttendance;

/// <summary>
/// Focused PostgreSQL coverage for the Work Area persistence contract. The tests intentionally use
/// an administrator connection only to migrate and seed synthetic rows, then use the restricted
/// onevo_app role for every RLS assertion. No superuser-only query is treated as proof of tenant
/// isolation.
/// </summary>
public sealed class WorkAreaChangeRequestsIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer? _postgres;
    private string _connectionString = null!;

    public WorkAreaChangeRequestsIntegrationTests()
    {
        var configured = Environment.GetEnvironmentVariable("ONEVO_TEST_DB");
        if (!string.IsNullOrWhiteSpace(configured))
            return;

        _postgres = new PostgreSqlBuilder("postgres:16-alpine")
            .WithDatabase("onevo_work_area_test")
            .WithUsername("test")
            .WithPassword("test")
            .Build();
    }

    public async Task InitializeAsync()
    {
        if (_postgres is not null)
        {
            await _postgres.StartAsync();
            _connectionString = _postgres.GetConnectionString();
        }
        else
        {
            _connectionString = Environment.GetEnvironmentVariable("ONEVO_TEST_DB")!;
        }

        await AdminTestFactory.MigrateDatabaseAsync(_connectionString);
        await using var admin = new NpgsqlConnection(_connectionString);
        await admin.OpenAsync();
        await using var grant = admin.CreateCommand();
        grant.CommandText = "GRANT SELECT, INSERT, UPDATE, DELETE ON work_area_change_requests TO onevo_app;";
        await grant.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync()
    {
        if (_postgres is not null)
            await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task MigratedSchema_HasExpectedColumnsRestrictiveForeignKeysAndIndexes()
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        var columns = await QueryStringsAsync(connection, """
            SELECT column_name
            FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name = 'work_area_change_requests'
            ORDER BY ordinal_position;
            """);
        columns.Should().Contain(new[]
        {
            "id", "tenant_id", "employee_id", "legal_entity_id", "date",
            "current_expected_work_area", "requested_work_area", "reason", "status",
            "requested_at", "reviewed_by_id", "reviewed_at", "review_comment"
        });

        var restrictiveForeignKeys = await QueryStringsAsync(connection, """
            SELECT conname
            FROM pg_constraint
            WHERE conrelid = 'work_area_change_requests'::regclass
              AND contype = 'f' AND confdeltype = 'r';
            """);
        restrictiveForeignKeys.Should().HaveCount(3);

        var indexes = await QueryStringsAsync(connection, """
            SELECT indexname
            FROM pg_indexes
            WHERE schemaname = 'public' AND tablename = 'work_area_change_requests';
            """);
        indexes.Should().Contain(new[]
        {
            "ix_work_area_change_requests_tenant_employee_date",
            "ix_work_area_change_requests_tenant_status",
            "ix_work_area_change_requests_tenant_legal_entity_status",
            "ix_work_area_change_requests_employee_id",
            "ix_work_area_change_requests_legal_entity_id",
            "ix_work_area_change_requests_reviewed_by_id",
            "ux_work_area_change_requests_active_employee_date"
        });
    }

    [Fact]
    public async Task MigratedSchema_EnablesAndForcesRlsWithTenantPolicyAndPartialUniqueIndex()
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        await using var rls = connection.CreateCommand();
        rls.CommandText = """
            SELECT relrowsecurity, relforcerowsecurity
            FROM pg_class
            WHERE oid = 'work_area_change_requests'::regclass;
            """;
        await using var reader = await rls.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();
        reader.GetBoolean(0).Should().BeTrue();
        reader.GetBoolean(1).Should().BeTrue();
        await reader.CloseAsync();

        var policies = await QueryStringsAsync(connection, """
            SELECT policyname
            FROM pg_policies
            WHERE schemaname = 'public' AND tablename = 'work_area_change_requests';
            """);
        policies.Should().Contain("tenant_isolation");

        await using var index = connection.CreateCommand();
        index.CommandText = """
            SELECT indexdef
            FROM pg_indexes
            WHERE schemaname = 'public'
              AND indexname = 'ux_work_area_change_requests_active_employee_date';
            """;
        var indexDefinition = (string?)await index.ExecuteScalarAsync();
        indexDefinition.Should().Contain("UNIQUE");
        indexDefinition.Should().Contain("pending");
        indexDefinition.Should().Contain("approved");
    }

    [Fact]
    public async Task RestrictedAppRole_CanReadOwnTenantButCannotReadOtherTenantOrBypassMissingContext()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var rowA = Guid.NewGuid();
        var rowB = Guid.NewGuid();
        await SeedSyntheticRowsAsync(tenantA, rowA, tenantB, rowB);

        await using var connection = OpenAppRoleConnection();
        await connection.OpenAsync();
        await SetTenantAsync(connection, tenantA);
        (await ScalarAsync(connection, "SELECT count(*) FROM work_area_change_requests;"))
            .Should().Be(1L);

        await SetTenantAsync(connection, tenantA);
        (await ScalarAsync(connection, "SELECT count(*) FROM work_area_change_requests WHERE id = $1;", rowB))
            .Should().Be(0L);

        await ResetTenantAsync(connection);
        (await ScalarAsync(connection, "SELECT count(*) FROM work_area_change_requests;"))
            .Should().Be(0L);
    }

    [Fact]
    public async Task RestrictedAppRole_CannotInsertUpdateOrDeleteAcrossTenantBoundary()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var rowA = Guid.NewGuid();
        var rowB = Guid.NewGuid();
        await SeedSyntheticRowsAsync(tenantA, rowA, tenantB, rowB);

        await using var connection = OpenAppRoleConnection();
        await connection.OpenAsync();
        await SetTenantAsync(connection, tenantA);

        (await ScalarAsync(connection,
            "UPDATE work_area_change_requests SET reason = 'blocked' WHERE id = $1 RETURNING id;", rowB))
            .Should().BeNull();
        (await ScalarAsync(connection,
            "DELETE FROM work_area_change_requests WHERE id = $1 RETURNING id;", rowB))
            .Should().BeNull();

        var insert = connection.CreateCommand();
        insert.CommandText = """
            INSERT INTO work_area_change_requests
                (id, tenant_id, employee_id, legal_entity_id, date,
                 current_expected_work_area, requested_work_area, reason, status, requested_at)
            VALUES ($1, $2, $3, $4, CURRENT_DATE, 'onsite', 'remote', 'blocked', 'pending', now());
            """;
        insert.Parameters.AddWithValue(rowB);
        insert.Parameters.AddWithValue(tenantB);
        insert.Parameters.AddWithValue(Guid.NewGuid());
        insert.Parameters.AddWithValue(Guid.NewGuid());
        var act = async () => await insert.ExecuteNonQueryAsync();
        await act.Should().ThrowAsync<PostgresException>()
            .Where(ex => ex.SqlState == PostgresErrorCodes.InsufficientPrivilege);
    }

    [Fact]
    public async Task ActiveUniqueIndex_FirstPendingRequest_Succeeds()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var date = DateOnly.FromDateTime(DateTime.UtcNow);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await WithReplicaRoleAsync(connection, () =>
            InsertRequestAsync(connection, Guid.NewGuid(), tenantId, employeeId, legalEntityId, date, "pending"));

        (await ScalarAsync(connection,
            "SELECT count(*) FROM work_area_change_requests WHERE tenant_id = $1 AND employee_id = $2;", tenantId, employeeId))
            .Should().Be(1L);
    }

    [Fact]
    public async Task ActiveUniqueIndex_SecondPendingSameEmployeeDate_ThrowsUniqueViolation()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var date = DateOnly.FromDateTime(DateTime.UtcNow);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await WithReplicaRoleAsync(connection, async () =>
        {
            await InsertRequestAsync(connection, Guid.NewGuid(), tenantId, employeeId, legalEntityId, date, "pending");

            var act = () => InsertRequestAsync(connection, Guid.NewGuid(), tenantId, employeeId, legalEntityId, date, "pending");

            var exception = await act.Should().ThrowAsync<PostgresException>();
            exception.Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
            exception.Which.ConstraintName.Should().Be("ux_work_area_change_requests_active_employee_date");
        });
    }

    [Fact]
    public async Task ActiveUniqueIndex_ApprovedThenPendingSameEmployeeDate_ThrowsUniqueViolation()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var date = DateOnly.FromDateTime(DateTime.UtcNow);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await WithReplicaRoleAsync(connection, async () =>
        {
            await InsertRequestAsync(connection, Guid.NewGuid(), tenantId, employeeId, legalEntityId, date, "approved");

            var act = () => InsertRequestAsync(connection, Guid.NewGuid(), tenantId, employeeId, legalEntityId, date, "pending");

            var exception = await act.Should().ThrowAsync<PostgresException>();
            exception.Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
        });
    }

    [Fact]
    public async Task ActiveUniqueIndex_PendingThenApprovedSameEmployeeDate_ThrowsUniqueViolation()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var date = DateOnly.FromDateTime(DateTime.UtcNow);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await WithReplicaRoleAsync(connection, async () =>
        {
            await InsertRequestAsync(connection, Guid.NewGuid(), tenantId, employeeId, legalEntityId, date, "pending");

            var act = () => InsertRequestAsync(connection, Guid.NewGuid(), tenantId, employeeId, legalEntityId, date, "approved");

            var exception = await act.Should().ThrowAsync<PostgresException>();
            exception.Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
        });
    }

    [Fact]
    public async Task ActiveUniqueIndex_RejectedThenNewPendingSameEmployeeDate_Succeeds()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var date = DateOnly.FromDateTime(DateTime.UtcNow);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await WithReplicaRoleAsync(connection, async () =>
        {
            await InsertRequestAsync(connection, Guid.NewGuid(), tenantId, employeeId, legalEntityId, date, "rejected");
            await InsertRequestAsync(connection, Guid.NewGuid(), tenantId, employeeId, legalEntityId, date, "pending");
        });

        (await ScalarAsync(connection,
            "SELECT count(*) FROM work_area_change_requests WHERE tenant_id = $1 AND employee_id = $2;", tenantId, employeeId))
            .Should().Be(2L);
    }

    [Fact]
    public async Task ActiveUniqueIndex_CancelledThenNewPendingSameEmployeeDate_Succeeds()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var date = DateOnly.FromDateTime(DateTime.UtcNow);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await WithReplicaRoleAsync(connection, async () =>
        {
            await InsertRequestAsync(connection, Guid.NewGuid(), tenantId, employeeId, legalEntityId, date, "cancelled");
            await InsertRequestAsync(connection, Guid.NewGuid(), tenantId, employeeId, legalEntityId, date, "pending");
        });

        (await ScalarAsync(connection,
            "SELECT count(*) FROM work_area_change_requests WHERE tenant_id = $1 AND employee_id = $2;", tenantId, employeeId))
            .Should().Be(2L);
    }

    [Fact]
    public async Task ActiveUniqueIndex_SameDateDifferentEmployee_Succeeds()
    {
        var tenantId = Guid.NewGuid();
        var employeeId1 = Guid.NewGuid();
        var employeeId2 = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var date = DateOnly.FromDateTime(DateTime.UtcNow);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await WithReplicaRoleAsync(connection, async () =>
        {
            await InsertRequestAsync(connection, Guid.NewGuid(), tenantId, employeeId1, legalEntityId, date, "pending");
            await InsertRequestAsync(connection, Guid.NewGuid(), tenantId, employeeId2, legalEntityId, date, "pending");
        });

        (await ScalarAsync(connection,
            "SELECT count(*) FROM work_area_change_requests WHERE tenant_id = $1 AND employee_id = $2;", tenantId, employeeId1))
            .Should().Be(1L);
        (await ScalarAsync(connection,
            "SELECT count(*) FROM work_area_change_requests WHERE tenant_id = $1 AND employee_id = $2;", tenantId, employeeId2))
            .Should().Be(1L);
    }

    [Fact]
    public async Task ActiveUniqueIndex_SameEmployeeDateDifferentTenant_Succeeds()
    {
        // Deliberately the same employeeId/date under two different tenant_id values - the unique
        // index is scoped by (tenant_id, employee_id, date), so this proves the tenant column, not
        // just the employee/date pair, participates in the constraint.
        var tenantId1 = Guid.NewGuid();
        var tenantId2 = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var date = DateOnly.FromDateTime(DateTime.UtcNow);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await WithReplicaRoleAsync(connection, async () =>
        {
            await InsertRequestAsync(connection, Guid.NewGuid(), tenantId1, employeeId, legalEntityId, date, "pending");
            await InsertRequestAsync(connection, Guid.NewGuid(), tenantId2, employeeId, legalEntityId, date, "pending");
        });

        (await ScalarAsync(connection,
            "SELECT count(*) FROM work_area_change_requests WHERE tenant_id = $1 AND employee_id = $2;", tenantId1, employeeId))
            .Should().Be(1L);
        (await ScalarAsync(connection,
            "SELECT count(*) FROM work_area_change_requests WHERE tenant_id = $1 AND employee_id = $2;", tenantId2, employeeId))
            .Should().Be(1L);
    }

    [Fact]
    public async Task ActiveUniqueIndex_SameEmployeeDifferentDate_Succeeds()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var date1 = DateOnly.FromDateTime(DateTime.UtcNow);
        var date2 = date1.AddDays(1);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await WithReplicaRoleAsync(connection, async () =>
        {
            await InsertRequestAsync(connection, Guid.NewGuid(), tenantId, employeeId, legalEntityId, date1, "pending");
            await InsertRequestAsync(connection, Guid.NewGuid(), tenantId, employeeId, legalEntityId, date2, "pending");
        });

        (await ScalarAsync(connection,
            "SELECT count(*) FROM work_area_change_requests WHERE tenant_id = $1 AND employee_id = $2;", tenantId, employeeId))
            .Should().Be(2L);
    }

    /// <summary>Runs action with FK-trigger enforcement suspended (session_replication_role =
    /// replica), the same technique SeedSyntheticRowsAsync below uses, since these tests insert
    /// synthetic employee_id/legal_entity_id values that don't exist in employees/legal_entities.
    /// This does NOT suspend unique-index enforcement - only ordinary/FK triggers - so the
    /// partial unique index under test is still checked exactly as it would be for a real insert.</summary>
    private static async Task WithReplicaRoleAsync(NpgsqlConnection connection, Func<Task> action)
    {
        await ExecuteAsync(connection, "SET session_replication_role = replica;");
        try
        {
            await action();
        }
        finally
        {
            await ExecuteAsync(connection, "RESET session_replication_role;");
        }
    }

    private static async Task InsertRequestAsync(
        NpgsqlConnection connection, Guid id, Guid tenantId, Guid employeeId, Guid legalEntityId, DateOnly date, string status)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO work_area_change_requests
                (id, tenant_id, employee_id, legal_entity_id, date,
                 current_expected_work_area, requested_work_area, reason, status, requested_at)
            VALUES ($1, $2, $3, $4, $5, 'onsite', 'remote', 'fixture', $6, now());
            """;
        command.Parameters.AddWithValue(id);
        command.Parameters.AddWithValue(tenantId);
        command.Parameters.AddWithValue(employeeId);
        command.Parameters.AddWithValue(legalEntityId);
        command.Parameters.AddWithValue(date);
        command.Parameters.AddWithValue(status);
        await command.ExecuteNonQueryAsync();
    }

    private async Task SeedSyntheticRowsAsync(Guid tenantA, Guid rowA, Guid tenantB, Guid rowB)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await ExecuteAsync(connection, "SET session_replication_role = replica;");
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO work_area_change_requests
                    (id, tenant_id, employee_id, legal_entity_id, date,
                     current_expected_work_area, requested_work_area, reason, status, requested_at)
                VALUES ($1, $2, $3, $4, CURRENT_DATE, 'onsite', 'remote', 'fixture', 'pending', now()),
                       ($5, $6, $7, $8, CURRENT_DATE, 'onsite', 'remote', 'fixture', 'pending', now());
                """;
            command.Parameters.AddWithValue(rowA);
            command.Parameters.AddWithValue(tenantA);
            command.Parameters.AddWithValue(Guid.NewGuid());
            command.Parameters.AddWithValue(Guid.NewGuid());
            command.Parameters.AddWithValue(rowB);
            command.Parameters.AddWithValue(tenantB);
            command.Parameters.AddWithValue(Guid.NewGuid());
            command.Parameters.AddWithValue(Guid.NewGuid());
            await command.ExecuteNonQueryAsync();
        }
        finally
        {
            await ExecuteAsync(connection, "RESET session_replication_role;");
        }
    }

    private NpgsqlConnection OpenAppRoleConnection()
        => new(new NpgsqlConnectionStringBuilder(_connectionString)
        {
            Username = "onevo_app",
            Password = PrivilegedRoleTestBootstrap.AppRolePassword
        }.ConnectionString);

    private static async Task SetTenantAsync(NpgsqlConnection connection, Guid tenantId)
        => await ExecuteAsync(connection, "SELECT set_config('app.current_tenant_id', $1, false);", tenantId.ToString());

    private static async Task ResetTenantAsync(NpgsqlConnection connection)
        => await ExecuteAsync(connection, "RESET app.current_tenant_id;");

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql, params object[] values)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        for (var i = 0; i < values.Length; i++)
            command.Parameters.AddWithValue(values[i]);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<object?> ScalarAsync(NpgsqlConnection connection, string sql, params object[] values)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        for (var i = 0; i < values.Length; i++)
            command.Parameters.AddWithValue(values[i]);
        return await command.ExecuteScalarAsync();
    }

    private static async Task<List<string>> QueryStringsAsync(NpgsqlConnection connection, string sql)
    {
        var result = new List<string>();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result.Add(reader.GetString(0));
        return result;
    }
}
