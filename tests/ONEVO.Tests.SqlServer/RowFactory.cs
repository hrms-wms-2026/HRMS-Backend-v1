using System.Text;
using Microsoft.Data.SqlClient;

namespace ONEVO.Tests.SqlServer;

/// <summary>
/// Inserts a row into any table with dummy values for required columns, so tests can cover every
/// table without knowing its shape. Needs a connection with admin context.
/// </summary>
public static class RowFactory
{
    /// <summary>Lets RowFactory insert without building parent rows. Test databases only.</summary>
    public static async Task DisableConstraintsAsync(SqlConnection admin)
    {
        await using var command = admin.CreateCommand();
        command.CommandText = """
            DECLARE @sql nvarchar(max) = N'';
            SELECT @sql += N'ALTER TABLE ' + QUOTENAME(name) + N' NOCHECK CONSTRAINT ALL;' FROM sys.tables;
            EXEC sp_executesql @sql;
            """;
        await command.ExecuteNonQueryAsync();
    }

    public static async Task InsertAsync(
        SqlConnection connection, string table, IReadOnlyDictionary<string, object?> values)
    {
        var columns = new List<string>();
        var parameters = new List<string>();
        await using var command = connection.CreateCommand();
        var merged = new Dictionary<string, object?>(values, StringComparer.Ordinal);
        foreach (var column in await RequiredColumnsAsync(connection, table))
        {
            merged.TryAdd(column.Name, Dummy(column));
        }

        foreach (var (name, value) in merged)
        {
            columns.Add($"[{name}]");
            parameters.Add($"@p{parameters.Count}");
            command.Parameters.AddWithValue(parameters[^1], value ?? DBNull.Value);
        }

        var sql = new StringBuilder($"INSERT INTO [{table}] (")
            .AppendJoin(", ", columns).Append(") VALUES (").AppendJoin(", ", parameters).Append(')');
        command.CommandText = sql.ToString();
        await command.ExecuteNonQueryAsync();
    }

    private sealed record Column(string Name, string Type, int Length);

    private static async Task<List<Column>> RequiredColumnsAsync(SqlConnection connection, string table)
    {
        var columns = new List<Column>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT c.name, t.name, c.max_length
            FROM sys.columns c
            JOIN sys.types t ON t.user_type_id = c.user_type_id
            WHERE c.object_id = OBJECT_ID(@table)
              AND c.is_nullable = 0 AND c.is_identity = 0 AND c.is_computed = 0
              AND c.default_object_id = 0 AND t.name NOT IN ('timestamp')
            """;
        command.Parameters.AddWithValue("@table", table);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            columns.Add(new Column(reader.GetString(0), reader.GetString(1), reader.GetInt16(2)));
        }

        return columns;
    }

    private static object Dummy(Column column) => column.Type switch
    {
        "uniqueidentifier" => Guid.NewGuid(),
        "nvarchar" => Text(column.Length),
        "bit" => false,
        "int" or "smallint" or "tinyint" => 1,
        "bigint" => 1L,
        "decimal" or "numeric" or "float" or "real" => 1,
        "datetimeoffset" => DateTimeOffset.UtcNow,
        "datetime2" => DateTime.UtcNow,
        "date" => DateTime.UtcNow.Date,
        "time" => TimeSpan.FromHours(9),
        "varbinary" or "binary" => new byte[] { 1 },
        _ => throw new NotSupportedException($"{column.Name}: {column.Type}"),
    };

    private static string Text(int bytes)
    {
        var value = Guid.NewGuid().ToString("N");
        return bytes == -1 || bytes / 2 >= value.Length ? value : value[..(bytes / 2)];
    }
}
