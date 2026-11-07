using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ONEVO.Application.Common.ServiceInterfaces;

namespace ONEVO.Infrastructure.Persistence.Interceptors;

public sealed class TenantRlsInterceptor : DbConnectionInterceptor, IDbTransactionInterceptor
{
    private readonly ITenantContext _tenantContext;
    private (string TenantId, string Mode)? _restoreOnRollback;

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
        using var cmd = SetTenantCommand(connection, string.Empty, string.Empty);
        cmd.ExecuteNonQuery();
        return result;
    }

    /// <summary>
    /// SESSION_CONTEXT is not transactional, so a tenant switch inside a transaction would outlive
    /// a rollback. TenantContextSwitcher records the values to put back here.
    /// </summary>
    public void RestoreOnRollback(string tenantId, string mode)
        => _restoreOnRollback ??= (tenantId, mode);

    public void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData)
    {
        if (TakeRestore() is not { } restore)
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
        if (TakeRestore() is not { } restore)
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

    private (string TenantId, string Mode)? TakeRestore()
    {
        var restore = _restoreOnRollback;
        _restoreOnRollback = null;
        return restore;
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
