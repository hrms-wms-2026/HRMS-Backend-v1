using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Infrastructure.Persistence;

namespace ONEVO.Infrastructure.Identity.Tenancy;

public sealed class TenantContextSwitcher : ITenantContextSwitcher
{
    private readonly ApplicationDbContext _db;
    private readonly IWritableTenantContext _writableTenantContext;

    public TenantContextSwitcher(ApplicationDbContext db, IWritableTenantContext writableTenantContext)
    {
        _db = db;
        _writableTenantContext = writableTenantContext;
    }

    public async Task SwitchToTenantAsync(TenantRegistryEntry tenant, CancellationToken ct = default)
    {
        var connection = _db.Database.GetDbConnection();
        var transaction = _db.Database.CurrentTransaction;
        if (transaction is not null)
        {
            _writableTenantContext.Resolve(tenant);
            await using var command = connection.CreateCommand();
            command.Transaction = transaction.GetDbTransaction();
            command.CommandText = """
                SELECT
                    set_config('app.current_tenant_id', @tenant_id, false),
                    set_config('app.tenant_context_mode', 'tenant', false)
                """;
            var tenantParameter = command.CreateParameter();
            tenantParameter.ParameterName = "tenant_id";
            tenantParameter.Value = tenant.TenantId.ToString();
            command.Parameters.Add(tenantParameter);
            await command.ExecuteNonQueryAsync(ct);
            return;
        }

        if (connection.State == ConnectionState.Open)
        {
            await _db.Database.CloseConnectionAsync();
        }

        _writableTenantContext.Resolve(tenant);
    }
}
