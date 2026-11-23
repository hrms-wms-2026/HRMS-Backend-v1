using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ONEVO.Infrastructure.Persistence.Seeders;

/// <summary>
/// Task 13: the reference seeders read existing rows and insert what is missing, without a lock. Two instances
/// starting together could both insert the same row and one would fail to start on the unique key. Hosted services
/// start one at a time in registration order, so <see cref="Acquire"/> (registered before the first seeder) takes a
/// session-level SQL Server application lock and <see cref="Release"/> (registered after the last) gives it back.
/// A second instance waits at <see cref="Acquire"/> until the first has seeded, then finds every row present.
/// If the process dies, its session ends and SQL Server frees the lock.
/// </summary>
public sealed class ReferenceSeedingLock(IConfiguration configuration, ILogger<ReferenceSeedingLock> logger) : IAsyncDisposable
{
    public const string Resource = "crownus:reference-seeding";
    public static readonly TimeSpan WaitLimit = TimeSpan.FromMinutes(5);

    private SqlConnection? _connection;

    public async Task AcquireAsync(CancellationToken ct)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
            return; // Nothing to seed against; the seeders report the missing connection themselves.

        _connection = new SqlConnection(connectionString);
        await _connection.OpenAsync(ct);
        await using var command = _connection.CreateCommand();
        command.CommandText = """
            DECLARE @result int;
            EXEC @result = sp_getapplock @Resource = @resource, @LockMode = 'Exclusive', @LockOwner = 'Session', @LockTimeout = @timeout;
            SELECT @result;
            """;
        command.Parameters.AddWithValue("@resource", Resource);
        command.Parameters.AddWithValue("@timeout", (int)WaitLimit.TotalMilliseconds);
        command.CommandTimeout = (int)WaitLimit.TotalSeconds + 30;

        var started = DateTime.UtcNow;
        var result = (int)(await command.ExecuteScalarAsync(ct))!;
        if (result < 0)
        {
            await DisposeAsync();
            throw new InvalidOperationException(
                $"Reference seeding lock '{Resource}' was not granted within {WaitLimit} (sp_getapplock returned {result}). "
                + "Another instance may be stuck seeding; check its log.");
        }
        if (result == 1)
            logger.LogInformation("Waited {WaitMs} ms for another instance to finish reference seeding.",
                (long)(DateTime.UtcNow - started).TotalMilliseconds);
    }

    public async Task ReleaseAsync()
    {
        if (_connection is null)
            return;
        try
        {
            await using var command = _connection.CreateCommand();
            command.CommandText = "EXEC sp_releaseapplock @Resource = @resource, @LockOwner = 'Session';";
            command.Parameters.AddWithValue("@resource", Resource);
            await command.ExecuteNonQueryAsync();
        }
        finally
        {
            await DisposeAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
            _connection = null;
        }
    }

    /// <summary>Registered before the first seeder.</summary>
    public sealed class Acquire(ReferenceSeedingLock seedingLock) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken) => seedingLock.AcquireAsync(cancellationToken);
        public Task StopAsync(CancellationToken cancellationToken) => seedingLock.ReleaseAsync();
    }

    /// <summary>Registered after the last seeder.</summary>
    public sealed class Release(ReferenceSeedingLock seedingLock) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken) => seedingLock.ReleaseAsync();
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
