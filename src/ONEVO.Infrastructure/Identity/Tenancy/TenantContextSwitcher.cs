using System.Data.Common;
using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;

namespace ONEVO.Infrastructure.Identity.Tenancy;

public sealed class TenantContextSwitcher : ITenantContextSwitcher
{
    private readonly ApplicationDbContext _db;
    private readonly IWritableTenantContext _writableTenantContext;
    private readonly TenantRlsInterceptor _rlsInterceptor;

    public TenantContextSwitcher(
        ApplicationDbContext db,
        IWritableTenantContext writableTenantContext,
        TenantRlsInterceptor rlsInterceptor)
    {
        _db = db;
        _writableTenantContext = writableTenantContext;
        _rlsInterceptor = rlsInterceptor;
    }

    public async Task SwitchToTenantAsync(TenantRegistryEntry tenant, CancellationToken ct = default)
    {
        var connection = _db.Database.GetDbConnection();
        var transaction = _db.Database.CurrentTransaction;
        if (transaction is not null)
        {
            var dbTransaction = transaction.GetDbTransaction();
            await RememberCurrentContextAsync(connection, dbTransaction, ct);
            _writableTenantContext.Resolve(tenant);
            await using var command = TenantRlsInterceptor.SetTenantCommand(
                connection, tenant.TenantId.ToString(), "tenant");
            command.Transaction = dbTransaction;
            await command.ExecuteNonQueryAsync(ct);
            return;
        }

        if (connection.State == ConnectionState.Open)
        {
            await _db.Database.CloseConnectionAsync();
        }

        _writableTenantContext.Resolve(tenant);
    }

    // SESSION_CONTEXT survives a rollback, so the values to put back are captured first.
    private async Task RememberCurrentContextAsync(
        DbConnection connection, DbTransaction transaction, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT CAST(SESSION_CONTEXT(N'app.current_tenant_id') AS nvarchar(36)),
                   CAST(SESSION_CONTEXT(N'app.tenant_context_mode') AS nvarchar(16))
            """;
        await using var reader = await command.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        _rlsInterceptor.RestoreOnRollback(
            transaction,
            reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
            reader.IsDBNull(1) ? string.Empty : reader.GetString(1));
    }
}
