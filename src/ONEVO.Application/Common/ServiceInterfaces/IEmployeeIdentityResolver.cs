namespace ONEVO.Application.Common.ServiceInterfaces;

/// <summary>
/// Resolves the real CoreHR Employee.Id (Guid) for the authenticated tray user, distinct from
/// UserId (the auth-account identifier). Callers must already be running inside the correct
/// tenant context (see ITenantContextSwitcher) before calling this — it applies no tenant switch
/// of its own.
/// </summary>
public interface IEmployeeIdentityResolver
{
    Task<Guid?> ResolveEmployeeIdAsync(Guid userId, Guid tenantId, CancellationToken ct);
}
