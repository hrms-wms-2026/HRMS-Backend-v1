namespace ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;

public interface IPermissionResolver
{
    /// <summary>
    /// Resolves effective permissions: universal auto-grants + role permissions + individual overrides.
    /// Returns ["*"] for Super Admin. Filters by active roles (not expired) and by the tenant's active
    /// subscription modules so stale role rows cannot grant out-of-plan permissions.
    /// </summary>
    Task<List<string>> ResolveAsync(Guid userId, Guid tenantId, CancellationToken ct = default);
}
