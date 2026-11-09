using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Services.SharedPlatform.Outbox;
using Xunit;

namespace ONEVO.Tests.Integration.SharedPlatform;

/// <summary>
/// While one instance is claiming a batch, another instance must still be able to claim the rest.
/// A claim that locks every due row (not just the batch it takes) makes other instances see an
/// empty outbox and back off, so scaled-out instances stop sharing the work.
/// </summary>
public sealed class OutboxClaimLockingTests
{
    private const string Type = "outbox_it_claim_locking";

    [Fact]
    public async Task ASecondClaimer_GetsTheRowsTheFirstClaimDidNotTake()
    {
        // A production-like mix: due work, a long published history, and a few abandoned leases.
        var host = await OutboxTestHost.CreateAsync();
        var template = await host.SeedPendingAsync(Type);
        await CopyAsync(host, template, 299, "status = 'pending'");
        await CopyAsync(host, template, 3000,
            "status = 'published', processed_at = SYSDATETIMEOFFSET(), next_attempt_at = DATEADD(day, -1, SYSDATETIMEOFFSET())");
        await CopyAsync(host, template, 10,
            "status = 'processing', locked_by = 'crashed', locked_until = DATEADD(minute, -1, SYSDATETIMEOFFSET())");
        List<Guid> abandoned;
        await using (var admin = host.NewAdminContext())
            abandoned = await admin.Database.SqlQuery<Guid>(
                $"SELECT id AS Value FROM outbox_messages WHERE locked_by = 'crashed'").ToListAsync();
        Assert.Equal(10, abandoned.Count);
        var services = host.BuildServices(_ => { });
        var lease = TimeSpan.FromMinutes(5);
        var locksHeld = -1;

        await using var first = services.CreateAsyncScope();
        var firstDb = SystemContext(first);
        await using var second = services.CreateAsyncScope();
        var secondDb = SystemContext(second);

        var (firstBatch, secondBatch) = await firstDb.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            // Hold the first claim's locks, as they are held while its statement runs.
            await using var transaction = await firstDb.Database.BeginTransactionAsync();
            var a = await OutboxClaimStore.ClaimBatchAsync(firstDb, "first", lease, 20, CancellationToken.None);
            locksHeld = await RowLocksAsync(host, firstDb);
            var b = await OutboxClaimStore.ClaimBatchAsync(secondDb, "second", lease, 20, CancellationToken.None);
            await transaction.RollbackAsync();
            return (a, b);
        });

        // Expired leases and due pending rows are both claimed; only the claimed rows stay locked.
        Assert.Equal(20, firstBatch.Count);
        Assert.Equal(20, locksHeld);
        Assert.Equal(20, secondBatch.Count);
        Assert.Subset(firstBatch.Concat(secondBatch).Select(m => m.Id).ToHashSet(), abandoned.ToHashSet());
        Assert.Empty(firstBatch.Select(m => m.Id).Intersect(secondBatch.Select(m => m.Id)));
    }

    /// <summary>Inserts <paramref name="count"/> copies of the template row with new ids, then
    /// applies <paramref name="set"/> to them.</summary>
    private static async Task CopyAsync(OutboxTestHost host, Guid templateId, int count, string set)
    {
        await using var admin = host.NewAdminContext();
        var batch = Guid.NewGuid().ToString("N");
        // set is a fixed SQL fragment from this test; the rest are generated ids and counts.
        var copy = $"""
            DECLARE @cols nvarchar(max) = (
                SELECT STRING_AGG(QUOTENAME(name), ',') FROM sys.columns
                WHERE object_id = OBJECT_ID('outbox_messages') AND is_computed = 0
                  AND system_type_id <> 189 AND name NOT IN ('id', 'last_error'));
            DECLARE @sql nvarchar(max) =
                N'INSERT INTO outbox_messages (id, last_error, ' + @cols + N')
                  SELECT NEWID(), N''{batch}'', ' + @cols + N'
                  FROM outbox_messages CROSS JOIN (SELECT TOP ({count}) 1 AS n FROM sys.all_columns a CROSS JOIN sys.all_columns b) n
                  WHERE id = ''{templateId}''';
            EXEC sp_executesql @sql;
            UPDATE outbox_messages SET {set}, last_error = NULL WHERE last_error = N'{batch}';
            """;
        await admin.Database.ExecuteSqlRawAsync(copy);
    }

    /// <summary>Row locks the session behind <paramref name="db"/> holds on the table's clustered
    /// index, read from an admin connection.</summary>
    private static async Task<int> RowLocksAsync(OutboxTestHost host, ApplicationDbContext db)
    {
        var spid = await db.Database.SqlQueryRaw<short>("SELECT CAST(@@SPID AS smallint) AS Value").SingleAsync();
        await using var admin = host.NewAdminContext();
        return await admin.Database.SqlQuery<int>($"""
            SELECT COUNT(*) AS Value
            FROM sys.dm_tran_locks l
            JOIN sys.partitions p ON p.hobt_id = l.resource_associated_entity_id
            WHERE l.request_session_id = {spid} AND l.resource_type = 'KEY'
              AND p.object_id = OBJECT_ID('outbox_messages') AND p.index_id = 1
            """).SingleAsync();
    }

    private static ApplicationDbContext SystemContext(AsyncServiceScope scope)
    {
        scope.ServiceProvider.GetRequiredService<IWritableTenantContext>().SetSystemMode();
        return scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    }
}
