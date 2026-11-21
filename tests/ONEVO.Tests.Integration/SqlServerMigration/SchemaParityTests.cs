using System.Text.Json.Nodes;
using Microsoft.Data.SqlClient;
using Xunit;

namespace ONEVO.Tests.Integration.SqlServerMigration;

/// <summary>
/// Every foreign key, check constraint and index PostgreSQL really held must exist here. Many were
/// created by raw SQL and never reached the EF model, so generating the baseline alone loses them.
/// Source: docs/reports/database/2026-11-07-postgres-schema.json.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class SchemaParityTests(SqlServerFixture fixture)
{
    // Same index, under EF's generated name.
    private static readonly Dictionary<string, string> RenamedIndexes = new(StringComparer.Ordinal)
    {
        ["ix_access_grant_requests_one_pending"] =
            "ix_access_grant_requests_tenant_id_onboarding_draft_id_target_position_id_position_access_template_id",
        ["ix_attendance_records_tenant_employee_date"] = "ix_attendance_records_tenant_id_employee_id_date",
        ["ix_break_records_tenant_employee_start"] = "ix_break_records_tenant_id_employee_id_break_start",
        ["ix_presence_sessions_tenant_employee_date"] = "ix_presence_sessions_tenant_id_employee_id_date",
        // Replaced on purpose, not renamed: 20261119120000_SingleReactionPerCommentEmployee allows one
        // reaction per employee per comment, so the unique index drops the emoji column.
        ["ix_task_comment_reactions_comment_id_employee_id_emoji"] = "ix_task_comment_reactions_comment_id_employee_id",
    };

    // Primary keys are compared by table, as EF names some differently. "t" is a deferred trigger:
    // those rules moved to the app (spec section 8).
    private static readonly string[] ComparedConstraintTypes = ["f", "c"];

    [Fact]
    public async Task EveryPostgresObject_ExistsOnSqlServer()
    {
        var database = await fixture.CreateDatabaseAsync();
        var expected = JsonNode.Parse(await File.ReadAllTextAsync(ReportPath()))!;
        await using var connection = new SqlConnection(database.Admin);
        await connection.OpenAsync();

        var tables = await NamesAsync(connection, "SELECT name FROM sys.tables");
        var constraints = await NamesAsync(connection,
            "SELECT name FROM sys.foreign_keys UNION ALL SELECT name FROM sys.check_constraints");
        var indexes = await NamesAsync(connection, "SELECT name FROM sys.indexes WHERE name IS NOT NULL");

        var missing = new List<string>();
        missing.AddRange(expected["columns"]!.AsArray()
            .Select(c => (string)c!["table"]!).Distinct()
            .Where(table => table != "__EFMigrationsHistory" && !tables.Contains(table))
            .Select(table => $"table {table}"));
        missing.AddRange(expected["constraints"]!.AsArray()
            .Where(c => ComparedConstraintTypes.Contains((string)c!["type"]!))
            .Select(c => (string)c!["name"]!)
            .Where(name => !Exists(constraints, name))
            .Select(name => $"constraint {name}"));
        var constraintBacked = expected["constraints"]!.AsArray().Select(c => (string)c!["name"]!).ToHashSet();
        missing.AddRange(expected["indexes"]!.AsArray()
            .Select(i => (string)i!["name"]!)
            .Where(name => !constraintBacked.Contains(name))
            .Select(name => RenamedIndexes.GetValueOrDefault(name, name))
            .Where(name => !Exists(indexes, name))
            .Select(name => $"index {name}"));

        Assert.True(missing.Count == 0, string.Join(Environment.NewLine, missing));
    }

    // legal_entities.address_json: left behind on PostgreSQL, no code uses it.
    private static readonly string[] DroppedColumns = ["legal_entities.address_json"];

    // Not on PostgreSQL: rowversion replaces xmin, and these back case-insensitive unique indexes.
    private static readonly string[] AddedColumns =
        ["xmin", "code_lower", "email_lower", "MigrationId", "ProductVersion"];

    // Tables created by migrations after the SQL Server baseline. They are not on PostgreSQL by design.
    // Add a table here in the same change as the migration that creates it.
    private static readonly string[] TablesAddedAfterBaseline =
    [
        // 20261112072239_AddPlatformSupport
        "platform_announcements", "support_tickets", "support_ticket_comments", "support_ticket_attachments",
        // 20261119155238_AddPermissionLifecycle
        "permission_changes",
        // 20261120033318_AddRateLimitSettings
        "rate_limit_settings", "rate_limit_setting_changes",
    ];

    // Columns added to existing tables by migrations after the SQL Server baseline, as "table.column".
    // Add a column here in the same change as the migration that creates it.
    private static readonly string[] ColumnsAddedAfterBaseline =
    [
        // 20261119155238_AddPermissionLifecycle
        "permissions.retired_at",
    ];

    [Fact]
    public async Task EveryPostgresColumn_ExistsOnSqlServer_AndNothingExtra()
    {
        var database = await fixture.CreateDatabaseAsync();
        var expected = JsonNode.Parse(await File.ReadAllTextAsync(ReportPath()))!["columns"]!.AsArray()
            .Select(c => $"{(string)c!["table"]!}.{(string)c["name"]!}")
            .Where(c => !c.StartsWith("__EFMigrationsHistory.", StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);
        await using var connection = new SqlConnection(database.Admin);
        await connection.OpenAsync();
        var actual = await NamesAsync(connection, """
            SELECT t.name + '.' + c.name
            FROM sys.columns c JOIN sys.tables t ON t.object_id = c.object_id
            WHERE t.name <> '__EFMigrationsHistory'
            """);

        Assert.Empty(expected.Except(actual).Except(DroppedColumns));
        Assert.Empty(actual.Except(expected)
            .Where(c => !AddedColumns.Contains(c.Split('.')[1]))
            .Where(c => !TablesAddedAfterBaseline.Contains(c.Split('.')[0]))
            .Where(c => !ColumnsAddedAfterBaseline.Contains(c)));
    }

    /// <summary>
    /// PostgreSQL treats NULL keys as distinct in a unique index, SQL Server as equal. So a nullable
    /// key must be filtered out, or a second row with a NULL key is wrongly rejected. Only indexes
    /// listed below may keep a nullable key.
    /// </summary>
    [Fact]
    public async Task UniqueIndexes_KeepNullKeysDistinct_AsOnPostgres()
    {
        string[] allowed =
        [
            // NULLS NOT DISTINCT on PostgreSQL, so SQL Server already behaves the same.
            "ux_attendance_alerts_tenant_employee_date_signal_source",
            "ux_wm_monitor_alerts_open_key",
            // Computed columns reported as nullable, whose source the column, filter or a check keeps non-null.
            "ix_users_tenant_id_normalized_email",
            "ux_departments_tenant_legal_entity_code_lower",
            "ux_meeting_occurrence_attendees_guest_email",
        ];
        var database = await fixture.CreateDatabaseAsync();
        await using var connection = new SqlConnection(database.Admin);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT i.name, c.name
            FROM sys.indexes i
            JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 0
            JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            JOIN sys.tables t ON t.object_id = i.object_id
            WHERE i.is_unique = 1 AND i.is_primary_key = 0 AND c.is_nullable = 1 AND t.name <> '__EFMigrationsHistory'
              AND ISNULL(REPLACE(REPLACE(i.filter_definition, '[', ''), ']', ''), '') NOT LIKE '%' + c.name + ' IS NOT NULL%'
            """, connection);

        var nullableKeys = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (!allowed.Contains(reader.GetString(0)))
            {
                nullableKeys.Add($"{reader.GetString(0)}.{reader.GetString(1)}");
            }
        }

        Assert.True(nullableKeys.Count == 0, string.Join(", ", nullableKeys));
    }

    [Fact]
    public async Task TenantForeignKey_RejectsARowPointingAtAnotherTenantsParent()
    {
        var database = await fixture.CreateDatabaseAsync();
        await using var connection = new SqlConnection(database.Admin);
        await connection.OpenAsync();
        await using (var mode = ONEVO.Infrastructure.Persistence.Interceptors.TenantRlsInterceptor
            .SetTenantCommand(connection, string.Empty, "admin"))
        {
            await mode.ExecuteNonQueryAsync();
        }

        var keys = await NamesAsync(connection, """
            SELECT fk.name FROM sys.foreign_keys fk
            WHERE fk.name LIKE '%[_]tenant' AND fk.is_disabled = 0 AND fk.is_not_trusted = 0
            """);

        Assert.Contains("fk_meeting_details_task_tenant", keys);
        Assert.Contains("fk_meeting_series_links_series_tenant", keys);
    }

    [Theory]
    [InlineData("departments", "ACC", "acc", true)]
    [InlineData("departments", "ACC", "OPS", false)]
    public async Task DepartmentCode_IsUniqueIgnoringCase(string table, string first, string second, bool rejected)
    {
        var database = await fixture.CreateDatabaseAsync();
        await using var connection = new SqlConnection(database.Admin);
        await connection.OpenAsync();
        await using (var mode = ONEVO.Infrastructure.Persistence.Interceptors.TenantRlsInterceptor
            .SetTenantCommand(connection, string.Empty, "admin"))
        {
            await mode.ExecuteNonQueryAsync();
        }

        await RowFactory.DisableConstraintsAsync(connection);
        var (tenant, entity) = (Guid.NewGuid(), Guid.NewGuid());
        Task InsertAsync(string code) => RowFactory.InsertAsync(connection, table, new Dictionary<string, object?>
        {
            ["id"] = Guid.NewGuid(),
            ["tenant_id"] = tenant,
            ["legal_entity_id"] = entity,
            ["code"] = code,
        });

        await InsertAsync(first);
        if (rejected)
        {
            await Assert.ThrowsAsync<SqlException>(() => InsertAsync(second));
        }
        else
        {
            await InsertAsync(second);
        }
    }

    // PostgreSQL names were cut to about 63 bytes; SQL Server keeps the full name.
    private static bool Exists(HashSet<string> names, string name)
        => names.Contains(name)
            || (name.Length >= 60 && names.Any(candidate => candidate.StartsWith(name.TrimEnd('~'), StringComparison.Ordinal)));

    private static async Task<HashSet<string>> NamesAsync(SqlConnection connection, string sql)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    private static string ReportPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        // A git worktree has a .git file instead of a .git directory.
        while (directory is not null && !Path.Exists(Path.Combine(directory.FullName, ".git")))
        {
            directory = directory.Parent;
        }

        return Path.Combine(directory!.FullName, "docs", "reports", "database", "2026-11-07-postgres-schema.json");
    }
}
