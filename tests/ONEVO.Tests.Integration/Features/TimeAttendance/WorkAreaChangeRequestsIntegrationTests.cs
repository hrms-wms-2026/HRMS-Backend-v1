using FluentAssertions;
using ONEVO.Tests.Integration.Support;
using ONEVO.Tests.Integration.Tenancy;
using Xunit;
using ONEVO.Infrastructure.Persistence.Interceptors;

namespace ONEVO.Tests.Integration.Features.TimeAttendance;

/// <summary>
/// Shared, one-time-per-class setup for WorkAreaChangeRequestsIntegrationTests: clones the
/// database once. xUnit's IClassFixture constructs this ONCE and disposes it once after every fact
/// in the class has run, instead of IAsyncLifetime's default of once PER fact. This class was
/// already the safest possible conversion candidate: every fact generates its own fully random
/// Guid tenant/employee/row ids via Guid.NewGuid(), so there is no cross-fact collision or
/// state-sharing risk whatsoever - every count/lookup query is scoped to ids no other fact could
/// ever produce.
/// </summary>
public sealed class WorkAreaChangeRequestsIntegrationTestsFixture : IAsyncLifetime
{
    public string ConnectionString { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var configured = Environment.GetEnvironmentVariable("ONEVO_TEST_DB");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            ConnectionString = configured;
            await AdminTestFactory.MigrateDatabaseAsync(ConnectionString);
        }
        else
        {
            // Cloned from the shared, already-migrated template - see SharedSqlServerTemplate.
            ConnectionString = await SharedSqlServerTemplate.CreateDatabaseAsync();
        }

        await using var admin = TestDb.CreateConnection(ConnectionString);
        await admin.OpenAsync();
        await using var grant = admin.CreateCommand();
        grant.CommandText = "SELECT 1;";
        await grant.ExecuteNonQueryAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public async Task SeedSyntheticRowsAsync(Guid tenantA, Guid rowA, Guid tenantB, Guid rowB)
    {
        await using var connection = TestDb.CreateConnection(ConnectionString);
        await connection.OpenAsync();
        await ExecuteAsync(connection, TestDb.DisableForeignKeysSql("work_area_change_requests"));
        try
        {
            await InsertSyntheticRowAsync(connection, rowA, tenantA);
            await InsertSyntheticRowAsync(connection, rowB, tenantB);
        }
        finally
        {
            await ExecuteAsync(connection, TestDb.EnableForeignKeysSql("work_area_change_requests"));
        }
    }

    private static async Task InsertSyntheticRowAsync(SqlConnection connection, Guid id, Guid tenantId)
    {
        await using (var tenantMode = TenantRlsInterceptor.SetTenantCommand(connection, tenantId.ToString(), "tenant"))
        {
            await tenantMode.ExecuteNonQueryAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO work_area_change_requests
                (id, tenant_id, employee_id, legal_entity_id, date,
                 current_work_mode_id, current_work_mode_name, requested_work_mode_id, requested_work_mode_name,
                 reason, status, requested_at)
            VALUES (@p1, @p2, @p3, @p4, CAST(SYSDATETIME() AS date), @p5, 'onsite', @p6, 'remote', 'fixture', 'pending', SYSDATETIMEOFFSET());
            """;
        command.Parameters.AddPositional(id);
        command.Parameters.AddPositional(tenantId);
        command.Parameters.AddPositional(Guid.NewGuid());
        command.Parameters.AddPositional(Guid.NewGuid());
        command.Parameters.AddPositional(Guid.NewGuid());
        command.Parameters.AddPositional(Guid.NewGuid());
        await command.ExecuteNonQueryAsync();
    }

    public SqlConnection OpenAppRoleConnection()
        => new(new SqlConnectionStringBuilder(ConnectionString)
        {
            UserID = "onevo_app",
            Password = PrivilegedRoleTestBootstrap.AppRolePassword
        }.ConnectionString);

    private static async Task ExecuteAsync(SqlConnection connection, string sql, params object[] values)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        for (var i = 0; i < values.Length; i++)
            command.Parameters.AddPositional(values[i]);
        await command.ExecuteNonQueryAsync();
    }

}

/// <summary>
/// Focused PostgreSQL coverage for the Work Area persistence contract. The tests intentionally use
/// an administrator connection only to migrate and seed synthetic rows, then use the restricted
/// onevo_app role for every RLS assertion. No superuser-only query is treated as proof of tenant
/// isolation.
/// </summary>
public sealed class WorkAreaChangeRequestsIntegrationTests : IClassFixture<WorkAreaChangeRequestsIntegrationTestsFixture>

{
    private readonly WorkAreaChangeRequestsIntegrationTestsFixture _fixture;

    public WorkAreaChangeRequestsIntegrationTests(WorkAreaChangeRequestsIntegrationTestsFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task MigratedSchema_HasExpectedColumnsRestrictiveForeignKeysAndIndexes()
    {
        await using var connection = TestDb.CreateConnection(_fixture.ConnectionString);
        await connection.OpenAsync();

        var columns = await QueryStringsAsync(connection, """
            SELECT name FROM sys.columns
            WHERE object_id = OBJECT_ID('work_area_change_requests')
            ORDER BY column_id;
            """);
        columns.Should().Contain(new[]
        {
            "id", "tenant_id", "employee_id", "legal_entity_id", "date",
            "current_work_mode_id", "current_work_mode_name", "requested_work_mode_id", "requested_work_mode_name",
            "legacy_work_area_label", "reason", "status",
            "requested_at", "reviewed_by_id", "reviewed_at", "review_comment"
        });

        var restrictiveForeignKeys = await QueryStringsAsync(connection, """
            SELECT name FROM sys.foreign_keys
            WHERE parent_object_id = OBJECT_ID('work_area_change_requests')
              AND delete_referential_action = 0;
            """);
        restrictiveForeignKeys.Should().HaveCount(5);

        var indexes = await QueryStringsAsync(connection, """
            SELECT name FROM sys.indexes
            WHERE object_id = OBJECT_ID('work_area_change_requests') AND name IS NOT NULL;
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
        await using var connection = TestDb.CreateConnection(_fixture.ConnectionString);
        await connection.OpenAsync();

        await using var rls = connection.CreateCommand();
        rls.CommandText = TestDb.HasRlsSql;
        rls.Parameters.AddWithValue("@table", "dbo.work_area_change_requests");
        ((bool)(await rls.ExecuteScalarAsync())!).Should().BeTrue();

        var policies = await QueryStringsAsync(connection, """
            SELECT name FROM sys.security_policies
            WHERE object_id = OBJECT_ID('rls.policy_work_area_change_requests');
            """);
        policies.Should().ContainSingle();

        await using var index = connection.CreateCommand();
        index.CommandText = """
            SELECT filter_definition FROM sys.indexes
            WHERE object_id = OBJECT_ID('work_area_change_requests')
              AND name = 'ux_work_area_change_requests_active_employee_date'
              AND is_unique = 1 AND has_filter = 1;
            """;
        var filter = (string?)await index.ExecuteScalarAsync();
        filter.Should().Contain("pending");
        filter.Should().Contain("approved");
    }

    [Fact]
    public async Task RestrictedAppRole_CanReadOwnTenantButCannotReadOtherTenantOrBypassMissingContext()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var rowA = Guid.NewGuid();
        var rowB = Guid.NewGuid();
        await _fixture.SeedSyntheticRowsAsync(tenantA, rowA, tenantB, rowB);

        await using var connection = _fixture.OpenAppRoleConnection();
        await connection.OpenAsync();
        await SetTenantAsync(connection, tenantA);
        (await ScalarAsync(connection, "SELECT COUNT_BIG(*) FROM work_area_change_requests;"))
            .Should().Be(1L);

        await SetTenantAsync(connection, tenantA);
        (await ScalarAsync(connection, "SELECT COUNT_BIG(*) FROM work_area_change_requests WHERE id = @p1;", rowB))
            .Should().Be(0L);

        await ResetTenantAsync(connection);
        (await ScalarAsync(connection, "SELECT COUNT_BIG(*) FROM work_area_change_requests;"))
            .Should().Be(0L);
    }

    [Fact]
    public async Task RestrictedAppRole_CannotInsertUpdateOrDeleteAcrossTenantBoundary()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var rowA = Guid.NewGuid();
        var rowB = Guid.NewGuid();
        await _fixture.SeedSyntheticRowsAsync(tenantA, rowA, tenantB, rowB);

        await using var connection = _fixture.OpenAppRoleConnection();
        await connection.OpenAsync();
        await SetTenantAsync(connection, tenantA);

        (await ScalarAsync(connection,
            "UPDATE work_area_change_requests SET reason = 'blocked' OUTPUT inserted.id WHERE id = @p1;", rowB))
            .Should().BeNull();
        (await ScalarAsync(connection,
            "DELETE FROM work_area_change_requests OUTPUT deleted.id WHERE id = @p1;", rowB))
            .Should().BeNull();

        var insert = connection.CreateCommand();
        insert.CommandText = """
            INSERT INTO work_area_change_requests
                (id, tenant_id, employee_id, legal_entity_id, date,
                 current_work_mode_id, current_work_mode_name, requested_work_mode_id, requested_work_mode_name,
                 reason, status, requested_at)
            VALUES (@p1, @p2, @p3, @p4, CAST(SYSDATETIME() AS date), @p5, 'onsite', @p6, 'remote', 'blocked', 'pending', SYSDATETIMEOFFSET());
            """;
        insert.Parameters.AddPositional(Guid.NewGuid());
        insert.Parameters.AddPositional(tenantB);
        insert.Parameters.AddPositional(Guid.NewGuid());
        insert.Parameters.AddPositional(Guid.NewGuid());
        insert.Parameters.AddPositional(Guid.NewGuid());
        insert.Parameters.AddPositional(Guid.NewGuid());
        var act = async () => await insert.ExecuteNonQueryAsync();

        // The row uses synthetic ids, and foreign keys are checked before the block predicate.
        await using var admin = TestDb.CreateConnection(_fixture.ConnectionString);
        await admin.OpenAsync();
        await ExecuteAsync(admin, TestDb.DisableForeignKeysSql("work_area_change_requests"));
        try
        {
            await act.Should().ThrowAsync<SqlException>()
                .Where(ex => ex.Number == 33504);
        }
        finally
        {
            await ExecuteAsync(admin, TestDb.EnableForeignKeysSql("work_area_change_requests"));
        }
    }

    [Fact]
    public async Task ActiveUniqueIndex_FirstPendingRequest_Succeeds()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var date = DateOnly.FromDateTime(DateTime.UtcNow);

        await using var connection = TestDb.CreateConnection(_fixture.ConnectionString);
        await connection.OpenAsync();
        await WithReplicaRoleAsync(connection, () =>
            InsertRequestAsync(connection, Guid.NewGuid(), tenantId, employeeId, legalEntityId, date, "pending"));

        (await ScalarAsync(connection,
            "SELECT COUNT_BIG(*) FROM work_area_change_requests WHERE tenant_id = @p1 AND employee_id = @p2;", tenantId, employeeId))
            .Should().Be(1L);
    }

    [Fact]
    public async Task ActiveUniqueIndex_SecondPendingSameEmployeeDate_ThrowsUniqueViolation()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var date = DateOnly.FromDateTime(DateTime.UtcNow);

        await using var connection = TestDb.CreateConnection(_fixture.ConnectionString);
        await connection.OpenAsync();
        await WithReplicaRoleAsync(connection, async () =>
        {
            await InsertRequestAsync(connection, Guid.NewGuid(), tenantId, employeeId, legalEntityId, date, "pending");

            var act = () => InsertRequestAsync(connection, Guid.NewGuid(), tenantId, employeeId, legalEntityId, date, "pending");

            var exception = await act.Should().ThrowAsync<SqlException>();
            exception.Which.Number.Should().BeOneOf(2601, 2627);
            exception.Which.Message.Should().Contain("ux_work_area_change_requests_active_employee_date");
        });
    }

    [Fact]
    public async Task ActiveUniqueIndex_ApprovedThenPendingSameEmployeeDate_ThrowsUniqueViolation()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var date = DateOnly.FromDateTime(DateTime.UtcNow);

        await using var connection = TestDb.CreateConnection(_fixture.ConnectionString);
        await connection.OpenAsync();
        await WithReplicaRoleAsync(connection, async () =>
        {
            await InsertRequestAsync(connection, Guid.NewGuid(), tenantId, employeeId, legalEntityId, date, "approved");

            var act = () => InsertRequestAsync(connection, Guid.NewGuid(), tenantId, employeeId, legalEntityId, date, "pending");

            var exception = await act.Should().ThrowAsync<SqlException>();
            exception.Which.Number.Should().BeOneOf(2601, 2627);
        });
    }

    [Fact]
    public async Task ActiveUniqueIndex_PendingThenApprovedSameEmployeeDate_ThrowsUniqueViolation()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var date = DateOnly.FromDateTime(DateTime.UtcNow);

        await using var connection = TestDb.CreateConnection(_fixture.ConnectionString);
        await connection.OpenAsync();
        await WithReplicaRoleAsync(connection, async () =>
        {
            await InsertRequestAsync(connection, Guid.NewGuid(), tenantId, employeeId, legalEntityId, date, "pending");

            var act = () => InsertRequestAsync(connection, Guid.NewGuid(), tenantId, employeeId, legalEntityId, date, "approved");

            var exception = await act.Should().ThrowAsync<SqlException>();
            exception.Which.Number.Should().BeOneOf(2601, 2627);
        });
    }

    [Fact]
    public async Task ActiveUniqueIndex_RejectedThenNewPendingSameEmployeeDate_Succeeds()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var date = DateOnly.FromDateTime(DateTime.UtcNow);

        await using var connection = TestDb.CreateConnection(_fixture.ConnectionString);
        await connection.OpenAsync();
        await WithReplicaRoleAsync(connection, async () =>
        {
            await InsertRequestAsync(connection, Guid.NewGuid(), tenantId, employeeId, legalEntityId, date, "rejected");
            await InsertRequestAsync(connection, Guid.NewGuid(), tenantId, employeeId, legalEntityId, date, "pending");
        });

        (await ScalarAsync(connection,
            "SELECT COUNT_BIG(*) FROM work_area_change_requests WHERE tenant_id = @p1 AND employee_id = @p2;", tenantId, employeeId))
            .Should().Be(2L);
    }

    [Fact]
    public async Task ActiveUniqueIndex_CancelledThenNewPendingSameEmployeeDate_Succeeds()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var date = DateOnly.FromDateTime(DateTime.UtcNow);

        await using var connection = TestDb.CreateConnection(_fixture.ConnectionString);
        await connection.OpenAsync();
        await WithReplicaRoleAsync(connection, async () =>
        {
            await InsertRequestAsync(connection, Guid.NewGuid(), tenantId, employeeId, legalEntityId, date, "cancelled");
            await InsertRequestAsync(connection, Guid.NewGuid(), tenantId, employeeId, legalEntityId, date, "pending");
        });

        (await ScalarAsync(connection,
            "SELECT COUNT_BIG(*) FROM work_area_change_requests WHERE tenant_id = @p1 AND employee_id = @p2;", tenantId, employeeId))
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

        await using var connection = TestDb.CreateConnection(_fixture.ConnectionString);
        await connection.OpenAsync();
        await WithReplicaRoleAsync(connection, async () =>
        {
            await InsertRequestAsync(connection, Guid.NewGuid(), tenantId, employeeId1, legalEntityId, date, "pending");
            await InsertRequestAsync(connection, Guid.NewGuid(), tenantId, employeeId2, legalEntityId, date, "pending");
        });

        (await ScalarAsync(connection,
            "SELECT COUNT_BIG(*) FROM work_area_change_requests WHERE tenant_id = @p1 AND employee_id = @p2;", tenantId, employeeId1))
            .Should().Be(1L);
        (await ScalarAsync(connection,
            "SELECT COUNT_BIG(*) FROM work_area_change_requests WHERE tenant_id = @p1 AND employee_id = @p2;", tenantId, employeeId2))
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

        await using var connection = TestDb.CreateConnection(_fixture.ConnectionString);
        await connection.OpenAsync();
        await WithReplicaRoleAsync(connection, async () =>
        {
            await InsertRequestAsync(connection, Guid.NewGuid(), tenantId1, employeeId, legalEntityId, date, "pending");
            await InsertRequestAsync(connection, Guid.NewGuid(), tenantId2, employeeId, legalEntityId, date, "pending");
        });

        await SetTenantAsync(connection, tenantId1);
        (await ScalarAsync(connection,
            "SELECT COUNT_BIG(*) FROM work_area_change_requests WHERE tenant_id = @p1 AND employee_id = @p2;", tenantId1, employeeId))
            .Should().Be(1L);
        await SetTenantAsync(connection, tenantId2);
        (await ScalarAsync(connection,
            "SELECT COUNT_BIG(*) FROM work_area_change_requests WHERE tenant_id = @p1 AND employee_id = @p2;", tenantId2, employeeId))
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

        await using var connection = TestDb.CreateConnection(_fixture.ConnectionString);
        await connection.OpenAsync();
        await WithReplicaRoleAsync(connection, async () =>
        {
            await InsertRequestAsync(connection, Guid.NewGuid(), tenantId, employeeId, legalEntityId, date1, "pending");
            await InsertRequestAsync(connection, Guid.NewGuid(), tenantId, employeeId, legalEntityId, date2, "pending");
        });

        (await ScalarAsync(connection,
            "SELECT COUNT_BIG(*) FROM work_area_change_requests WHERE tenant_id = @p1 AND employee_id = @p2;", tenantId, employeeId))
            .Should().Be(2L);
    }

    /// <summary>Runs action with this table's foreign keys suspended, the same technique
    /// SeedSyntheticRowsAsync uses, since these tests insert synthetic employee_id/legal_entity_id
    /// values. Unique indexes stay on, so the filtered unique index under test is still checked
    /// exactly as it would be for a real insert.</summary>
    private static async Task WithReplicaRoleAsync(SqlConnection connection, Func<Task> action)
    {
        await ExecuteAsync(connection, TestDb.DisableForeignKeysSql("work_area_change_requests"));
        try
        {
            await action();
        }
        finally
        {
            await ExecuteAsync(connection, TestDb.EnableForeignKeysSql("work_area_change_requests"));
        }
    }

    private static async Task InsertRequestAsync(
        SqlConnection connection, Guid id, Guid tenantId, Guid employeeId, Guid legalEntityId, DateOnly date, string status)
    {
        // A strict tenant table: only its own tenant can write it, and the session stays there so
        // the test can read the row back.
        await SetTenantAsync(connection, tenantId);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO work_area_change_requests
                (id, tenant_id, employee_id, legal_entity_id, date,
                 current_work_mode_id, current_work_mode_name, requested_work_mode_id, requested_work_mode_name,
                 reason, status, requested_at)
            VALUES (@p1, @p2, @p3, @p4, @p5, @p7, 'onsite', @p8, 'remote', 'fixture', @p6, SYSDATETIMEOFFSET());
            """;
        command.Parameters.AddPositional(id);
        command.Parameters.AddPositional(tenantId);
        command.Parameters.AddPositional(employeeId);
        command.Parameters.AddPositional(legalEntityId);
        command.Parameters.AddPositional(date);
        command.Parameters.AddPositional(status);
        command.Parameters.AddPositional(Guid.NewGuid());
        command.Parameters.AddPositional(Guid.NewGuid());
        await command.ExecuteNonQueryAsync();
    }

    private static async Task SetTenantAsync(SqlConnection connection, Guid tenantId)
    {
        await using var command = TenantRlsInterceptor.SetTenantCommand(connection, tenantId.ToString(), "tenant");
        await command.ExecuteNonQueryAsync();
    }


    private static async Task ResetTenantAsync(SqlConnection connection)
    {
        await using var command = TenantRlsInterceptor.SetTenantCommand(connection, string.Empty, "system");
        await command.ExecuteNonQueryAsync();
    }


    private static async Task ExecuteAsync(SqlConnection connection, string sql, params object[] values)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        for (var i = 0; i < values.Length; i++)
            command.Parameters.AddPositional(values[i]);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<object?> ScalarAsync(SqlConnection connection, string sql, params object[] values)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        for (var i = 0; i < values.Length; i++)
            command.Parameters.AddPositional(values[i]);
        return await command.ExecuteScalarAsync();
    }

    private static async Task<List<string>> QueryStringsAsync(SqlConnection connection, string sql)
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
