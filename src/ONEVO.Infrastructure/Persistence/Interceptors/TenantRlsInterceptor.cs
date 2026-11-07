using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ONEVO.Application.Common.ServiceInterfaces;

namespace ONEVO.Infrastructure.Persistence.Interceptors;

public sealed class TenantRlsInterceptor : DbConnectionInterceptor, IDbTransactionInterceptor
{
    private readonly ITenantContext _tenantContext;
    private (DbTransaction Transaction, string TenantId, string Mode)? _restoreOnRollback;

    public TenantRlsInterceptor(ITenantContext tenantContext)
        => _tenantContext = tenantContext;

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var cmd = SetTenantCommand(connection, ResolveTenantId(), ResolveMode());
        cmd.ExecuteNonQuery();
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await using var cmd = SetTenantCommand(connection, ResolveTenantId(), ResolveMode());
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    // EF Core 10 ConnectionClosing signature includes a third InterceptionResult parameter.
    public override InterceptionResult ConnectionClosing(
        DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
    {
        _restoreOnRollback = null;
        using var cmd = SetTenantCommand(connection, string.Empty, string.Empty);
        cmd.ExecuteNonQuery();
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

    public void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData)
    {
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
        if (TakeRestore(transaction) is not { } restore)
        {
            return;
        }

        await using var cmd = SetTenantCommand(
            eventData.Context!.Database.GetDbConnection(), restore.TenantId, restore.Mode);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData)
        => _restoreOnRollback = null;

    public Task TransactionCommittedAsync(
        DbTransaction transaction,
        TransactionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        _restoreOnRollback = null;
        return Task.CompletedTask;
    }

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
