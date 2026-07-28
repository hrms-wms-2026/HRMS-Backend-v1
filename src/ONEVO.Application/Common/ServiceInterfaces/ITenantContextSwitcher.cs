using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Common.ServiceInterfaces;

/// <summary>
/// Switches the current request's tenant context mid-handler, after a base-domain login
/// resolves a winning tenant/user pair. Resolving IWritableTenantContext alone is not enough:
/// TenantRlsInterceptor only re-applies the app.current_tenant_id / app.tenant_context_mode
/// session GUCs on DbConnection.Open, so this also resets the current DbContext connection to
/// guarantee the next tenant-scoped query observes the new tenant rather than a stale
/// system-mode connection.
/// </summary>
public interface ITenantContextSwitcher
{
    Task SwitchToTenantAsync(TenantRegistryEntry tenant, CancellationToken ct = default);
}
