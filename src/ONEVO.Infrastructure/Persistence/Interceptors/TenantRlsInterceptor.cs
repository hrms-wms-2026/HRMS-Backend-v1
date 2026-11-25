using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ONEVO.Application.Common.ServiceInterfaces;

namespace ONEVO.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Writes ITenantContext into SESSION_CONTEXT for row-level security. It does this whenever EF opens the
/// connection, and again before a command outside a transaction or at a transaction start when the context
/// has changed since it was last written. Those are the points where EF would have reopened the
/// connection, so a request that keeps one connection open (RequestDbConnectionMiddleware) sees exactly
/// what opening per command gave, with one round trip instead of one per query.
/// </summary>
public sealed class TenantRlsInterceptor : DbConnectionInterceptor, IDbTransactionInterceptor, IDbCommandInterceptor
{
    private readonly ITenantContext _tenantContext;
    private (DbTransaction Transaction, string TenantId, string Mode)? _restoreOnRollback;

    // What this interceptor last wrote, and to which physical connection; null = unknown, so the next check writes.
    private (string TenantId, string Mode, Guid ConnectionId)? _applied;

    /// <summary>
    /// The app connection string without SqlClient's idle connection resiliency. A silent reconnect starts a
    /// new session with an empty SESSION_CONTEXT, which a connection kept open for the request would not
    /// notice. Without it a broken connection fails, and EF's retry reopens it through ConnectionOpened.
    /// A string SqlClient cannot read is returned as it is, so it still fails where it always did: on connect.
    /// </summary>
    public static string WithoutSilentReconnect(string connectionString)
    {
        try
        {
            return new SqlConnectionStringBuilder(connectionString) { ConnectRetryCount = 0 }.ConnectionString;
        }
        catch (ArgumentException)
        {
            return connectionString;
        }
    }

    public TenantRlsInterceptor(ITenantContext tenantContext)
        => _tenantContext = tenantContext;

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        _applied = null;
        EnsureCurrent(connection);
    }

    public override Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        _applied = null;
        return EnsureCurrentAsync(connection, cancellationToken);
    }

    public override void ConnectionClosed(DbConnection connection, ConnectionEndEventData eventData)
        => _applied = null;

    public override Task ConnectionClosedAsync(DbConnection connection, ConnectionEndEventData eventData)
    {
        _applied = null;
        return Task.CompletedTask;
    }

    // EF Core 10 ConnectionClosing signature includes a third InterceptionResult parameter.
    public override InterceptionResult ConnectionClosing(
        DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
    {
        _restoreOnRollback = null;
        _applied = null;
        using var cmd = SetTenantCommand(connection, string.Empty, string.Empty);
        cmd.ExecuteNonQuery();
        return result;
    }

    /// <summary>The context was written to the connection by someone else (TenantContextSwitcher inside a
    /// transaction): the next check must write it again.</summary>
    public void ForgetAppliedContext() => _applied = null;

    // A transaction start: EF would have opened the connection here.
    public InterceptionResult<DbTransaction> TransactionStarting(
        DbConnection connection, TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result)
    {
        EnsureCurrent(connection);
        return result;
    }

    public async ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
        DbConnection connection, TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result,
        CancellationToken cancellationToken = default)
    {
        await EnsureCurrentAsync(connection, cancellationToken);
        return result;
    }

    // A command outside a transaction: EF would have opened the connection for it. Inside a transaction
    // nothing is touched, so switches and their rollback restore behave as they always have.
    public InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        EnsureCurrentOutsideTransaction(command, eventData);
        return result;
    }

    public async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        await EnsureCurrentOutsideTransactionAsync(command, eventData, cancellationToken);
        return result;
    }

    public InterceptionResult<int> NonQueryExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        EnsureCurrentOutsideTransaction(command, eventData);
        return result;
    }

    public async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        await EnsureCurrentOutsideTransactionAsync(command, eventData, cancellationToken);
        return result;
    }

    public InterceptionResult<object> ScalarExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        EnsureCurrentOutsideTransaction(command, eventData);
        return result;
    }

    public async ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        await EnsureCurrentOutsideTransactionAsync(command, eventData, cancellationToken);
        return result;
    }

    /// <summary>
    /// SESSION_CONTEXT is not transactional, so a tenant switch inside a transaction would outlive
    /// a rollback. TenantContextSwitcher records the values to put back here. They belong to that
    /// one transaction: the first switch in it wins, and any other transaction ignores them.
    /// </summary>
    public void RestoreOnRollback(DbTransaction transaction, string tenantId, string mode)
    {
        if (_restoreOnRollback?.Transaction != transaction)
        {
            _restoreOnRollback = (transaction, tenantId, mode);
        }
    }

    // Commit and rollback mark the context unknown: with EF opening per command the connection closed here,
    // so the next command reopened and wrote the context again.
    public void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData)
    {
        _applied = null;
        if (TakeRestore(transaction) is not { } restore)
        {
            return;
        }

        using var cmd = SetTenantCommand(eventData.Context!.Database.GetDbConnection(), restore.TenantId, restore.Mode);
        cmd.ExecuteNonQuery();
    }

    public async Task TransactionRolledBackAsync(
        DbTransaction transaction,
        TransactionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        _applied = null;
        if (TakeRestore(transaction) is not { } restore)
        {
            return;
        }

        await using var cmd = SetTenantCommand(
            eventData.Context!.Database.GetDbConnection(), restore.TenantId, restore.Mode);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData)
    {
        _restoreOnRollback = null;
        _applied = null;
    }

    public Task TransactionCommittedAsync(
        DbTransaction transaction,
        TransactionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        _restoreOnRollback = null;
        _applied = null;
        return Task.CompletedTask;
    }

    private void EnsureCurrentOutsideTransaction(DbCommand command, CommandEventData eventData)
    {
        if (eventData.Context?.Database.CurrentTransaction is null && command.Connection is { } connection)
            EnsureCurrent(connection);
    }

    private Task EnsureCurrentOutsideTransactionAsync(DbCommand command, CommandEventData eventData, CancellationToken ct)
        => eventData.Context?.Database.CurrentTransaction is null && command.Connection is { } connection
            ? EnsureCurrentAsync(connection, ct)
            : Task.CompletedTask;

    private void EnsureCurrent(DbConnection connection)
    {
        var wanted = Wanted(connection);
        if (_applied == wanted)
            return;
        using var cmd = SetTenantCommand(connection, wanted.TenantId, wanted.Mode);
        cmd.ExecuteNonQuery();
        _applied = wanted;
    }

    private async Task EnsureCurrentAsync(DbConnection connection, CancellationToken ct)
    {
        var wanted = Wanted(connection);
        if (_applied == wanted)
            return;
        await using var cmd = SetTenantCommand(connection, wanted.TenantId, wanted.Mode);
        await cmd.ExecuteNonQueryAsync(ct);
        _applied = wanted;
    }

    private (string TenantId, string Mode, Guid ConnectionId) Wanted(DbConnection connection)
        => (ResolveTenantId(), ResolveMode(), (connection as SqlConnection)?.ClientConnectionId ?? Guid.Empty);

    private (DbTransaction Transaction, string TenantId, string Mode)? TakeRestore(DbTransaction transaction)
    {
        var restore = _restoreOnRollback;
        _restoreOnRollback = null;
        return restore?.Transaction == transaction ? restore : null;
    }

    private string ResolveTenantId() =>
        _tenantContext.ContextMode == TenantContextMode.Tenant
            ? _tenantContext.TenantId.ToString()
            : string.Empty;

    private string ResolveMode() =>
        _tenantContext.ContextMode switch
        {
            TenantContextMode.Tenant => "tenant",
            TenantContextMode.Admin => "admin",
            _ => "system"
        };

    public static DbCommand SetTenantCommand(DbConnection connection, string tenantId, string mode)
    {
        var cmd = connection.CreateCommand();
        cmd.CommandText = """
            EXEC sp_set_session_context N'app.current_tenant_id', @tenant_id;
            EXEC sp_set_session_context N'app.tenant_context_mode', @mode;
            """;

        var tenantParam = cmd.CreateParameter();
        tenantParam.ParameterName = "tenant_id";
        tenantParam.Value = tenantId;
        cmd.Parameters.Add(tenantParam);

        var modeParam = cmd.CreateParameter();
        modeParam.ParameterName = "mode";
        modeParam.Value = mode;
        cmd.Parameters.Add(modeParam);

        return cmd;
    }
}
