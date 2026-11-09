using Microsoft.Data.SqlClient;

namespace ONEVO.Infrastructure.Configuration;

/// <summary>
/// Tenant isolation depends on these settings, so the API refuses to start without them. In
/// particular MAXDOP = 1 keeps plans serial, because SESSION_CONTEXT can return wrong results in a
/// parallel plan on a reused session. See ops/sqlserver/README.md.
/// </summary>
public static class DatabaseSettingsStartupValidator
{
    private const string RequiredCollation = "Latin1_General_100_CS_AS_SC";

    public static async Task ValidateAsync(string connectionString, CancellationToken ct)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                (SELECT CAST(value AS int) FROM sys.database_scoped_configurations WHERE name = 'MAXDOP'),
                (SELECT is_read_committed_snapshot_on FROM sys.databases WHERE database_id = DB_ID()),
                CAST(DATABASEPROPERTYEX(DB_NAME(), 'Collation') AS nvarchar(128)),
                IS_ROLEMEMBER('db_owner'),
                HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'ALTER ANY SECURITY POLICY')
            """;
        await using var reader = await command.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);

        Require(reader.GetInt32(0) == 1, "database-scoped MAXDOP must be 1");
        Require(reader.GetBoolean(1), "READ_COMMITTED_SNAPSHOT must be ON");
        Require(reader.GetString(2) == RequiredCollation, $"the database collation must be {RequiredCollation}");
        Require(reader.GetInt32(3) != 1, "the app login must not be db_owner");
        Require(reader.GetInt32(4) != 1, "the app login must not hold ALTER ANY SECURITY POLICY");
    }

    private static void Require(bool condition, string rule)
    {
        if (!condition)
        {
            throw new InvalidOperationException(
                $"Database settings are unsafe for tenant isolation: {rule}. See ops/sqlserver/README.md.");
        }
    }
}
