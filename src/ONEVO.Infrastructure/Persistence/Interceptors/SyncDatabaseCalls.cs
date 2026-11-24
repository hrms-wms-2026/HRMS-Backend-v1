namespace ONEVO.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Improvements task 26: the app's database work is async end to end. A synchronous SaveChanges or
/// Commit would make the interceptors block on async work (sync-over-async) and starve the thread
/// pool under load, so those paths throw instead.
/// </summary>
internal static class SyncDatabaseCalls
{
    public static InvalidOperationException NotSupported(string syncCall, string asyncCall) =>
        new($"Synchronous {syncCall} is not supported: it would block a thread-pool thread on async " +
            $"interceptor work. Use {asyncCall} instead.");
}
