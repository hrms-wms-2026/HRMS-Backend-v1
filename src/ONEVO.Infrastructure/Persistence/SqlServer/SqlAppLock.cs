using Microsoft.EntityFrameworkCore;

namespace ONEVO.Infrastructure.Persistence.SqlServer;

/// <summary>A named lock held until the current transaction ends. Must run inside a transaction.</summary>
public static class SqlAppLock
{
    public const int WaitForever = -1;
    public const int NoWait = 0;

    /// <summary>Returns false when the lock was not free within the timeout.</summary>
    public static async Task<bool> TryAcquireAsync(
        ApplicationDbContext db, string resource, int timeoutMilliseconds, CancellationToken ct)
    {
        var results = await db.Database.SqlQuery<int>($"""
            DECLARE @result int;
            EXEC @result = sp_getapplock
                @Resource = {resource},
                @LockMode = 'Exclusive',
                @LockOwner = 'Transaction',
                @LockTimeout = {timeoutMilliseconds};
            SELECT @result AS [Value];
            """).ToListAsync(ct);

        var result = results.Single();
        return result switch
        {
            >= 0 => true,
            -1 => false,
            _ => throw new InvalidOperationException($"sp_getapplock failed with {result} for '{resource}'."),
        };
    }
}
