using System.Collections.Concurrent;
using Microsoft.Data.SqlClient;

namespace ONEVO.Tests.Integration.Support;

/// <summary>
/// Keeps a test run's SQL Server databases from filling the disk. A full run creates several
/// hundred databases and no test drops its own, which fills a CI runner's disk within minutes. A
/// tracked database older than <see cref="DropAfter"/> that nobody is connected to is finished with:
/// no test keeps its database idle that long, and a pooled connection still counts as connected,
/// so a database in use is never dropped.
/// </summary>
public sealed class TestDatabaseReaper(string masterConnectionString)
{
    // Most databases are created in the first minutes of a run and are idle within a few minutes;
    // a database still in use keeps pooled sessions open and is skipped regardless of age.
    private static readonly TimeSpan DropAfter = TimeSpan.FromMinutes(5);
    private readonly ConcurrentDictionary<string, DateTime> _created = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTime _lastSweep = DateTime.MinValue;

    public void Track(string databaseName) => _created[databaseName] = DateTime.UtcNow;

    /// <summary>Drops finished databases. Runs at most once a minute and never blocks a caller
    /// while another sweep is running.</summary>
    public async Task DropFinishedAsync()
    {
        if (DateTime.UtcNow - _lastSweep < TimeSpan.FromMinutes(1) || !await _gate.WaitAsync(0))
            return;
        try
        {
            _lastSweep = DateTime.UtcNow;
            var candidates = _created.Where(c => DateTime.UtcNow - c.Value > DropAfter).Select(c => c.Key).ToList();
            if (candidates.Count == 0)
                return;

            await using var connection = new SqlConnection(masterConnectionString);
            await connection.OpenAsync();
            foreach (var name in candidates)
            {
                await using var command = connection.CreateCommand();
                command.CommandTimeout = 60;
                // name is a test-generated Guid-derived value; identifiers cannot be parameters.
                command.CommandText = $"""
                    IF DB_ID(N'{name}') IS NOT NULL
                       AND NOT EXISTS (SELECT 1 FROM sys.dm_exec_sessions WHERE database_id = DB_ID(N'{name}'))
                        DROP DATABASE [{name}];
                    SELECT CAST(CASE WHEN DB_ID(N'{name}') IS NULL THEN 1 ELSE 0 END AS bit);
                    """;
                try
                {
                    if (await command.ExecuteScalarAsync() is true)
                        _created.TryRemove(name, out _);
                }
                catch (SqlException)
                {
                    // Someone connected between the check and the drop: still in use, try later.
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>SIMPLE recovery and a small growth step keep a test database near its used size:
    /// migrations otherwise leave a large log, and SQL Server grows data files in 64 MB steps.</summary>
    public static string CompactSettingsSql(string databaseName, string logicalData, string logicalLog) => $"""
        ALTER DATABASE [{databaseName}] SET RECOVERY SIMPLE;
        ALTER DATABASE [{databaseName}] MODIFY FILE (NAME = N'{logicalData}', FILEGROWTH = 4MB);
        ALTER DATABASE [{databaseName}] MODIFY FILE (NAME = N'{logicalLog}', FILEGROWTH = 4MB);
        """;
}
