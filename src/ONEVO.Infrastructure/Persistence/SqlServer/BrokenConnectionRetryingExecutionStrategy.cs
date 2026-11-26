using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ONEVO.Infrastructure.Persistence.SqlServer;

/// <summary>
/// EF's SQL Server retry, plus a retry when the error broke the connection. The app connection string has
/// no silent reconnect (TenantRlsInterceptor.WithoutSilentReconnect), so a connection kept open for the
/// request relies on this retry to reopen it, and reopening writes the tenant context again.
/// EF's transient list knows the Windows error for a dropped connection (10054) but not SqlClient's managed
/// networking on Linux, which reports it as number 0, class 20.
/// </summary>
public sealed class BrokenConnectionRetryingExecutionStrategy : SqlServerRetryingExecutionStrategy
{
    public const int RetryCount = 3;

    public BrokenConnectionRetryingExecutionStrategy(ExecutionStrategyDependencies dependencies)
        : base(dependencies, RetryCount)
    {
    }

    /// <summary>Severity 20 and above ends the session: SQL Server or the transport closed the connection.</summary>
    public static bool IsBrokenConnection(Exception exception)
        => exception is SqlException { Class: >= 20 };

    protected override bool ShouldRetryOn(Exception exception)
        => base.ShouldRetryOn(exception) || IsBrokenConnection(exception);
}
