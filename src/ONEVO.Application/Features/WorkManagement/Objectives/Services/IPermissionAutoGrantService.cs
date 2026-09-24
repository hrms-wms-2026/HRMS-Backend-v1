namespace ONEVO.Application.Features.WorkManagement.Objectives.Services;

/// <summary>
/// Auto-grants a permission code to a user via a UserPermissionOverride row if their currently
/// effective permission set doesn't already include it - design §7. Known limitation: the grant
/// takes effect only on the user's next login, since RequirePermissionAttribute reads session
/// claims, not a live IPermissionResolver.ResolveAsync call.
/// </summary>
public interface IPermissionAutoGrantService
{
    Task EnsureGrantedAsync(Guid tenantId, Guid userId, Guid grantedByUserId, string permissionCode, CancellationToken ct = default);
}
