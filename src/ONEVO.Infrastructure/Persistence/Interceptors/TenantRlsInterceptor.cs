using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ONEVO.Application.Common.ServiceInterfaces;

namespace ONEVO.Infrastructure.Persistence.Interceptors;

public sealed class TenantRlsInterceptor : DbConnectionInterceptor
{
    private readonly ITenantContext _tenantContext;

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

    private static DbCommand SetTenantCommand(DbConnection connection, string tenantId, string mode)
    {
        var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT
                set_config('app.current_tenant_id', @tenant_id, false),
                set_config('app.tenant_context_mode', @mode, false)
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
