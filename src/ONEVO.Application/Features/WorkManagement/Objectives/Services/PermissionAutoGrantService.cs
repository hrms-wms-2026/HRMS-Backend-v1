using ONEVO.Application.Features.Auth.Permission.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;
using ONEVO.Domain.Features.Auth.Entities;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Services;

public class PermissionAutoGrantService : IPermissionAutoGrantService
{
    private readonly IPermissionResolver _permissionResolver;
    private readonly IPermissionRepository _permissions;
    private readonly IUserPermissionOverrideRepository _overrides;

    public PermissionAutoGrantService(
        IPermissionResolver permissionResolver, IPermissionRepository permissions, IUserPermissionOverrideRepository overrides)
    {
        _permissionResolver = permissionResolver;
        _permissions = permissions;
        _overrides = overrides;
    }

    public async Task EnsureGrantedAsync(Guid tenantId, Guid userId, Guid grantedByUserId, string permissionCode, CancellationToken ct = default)
    {
        var effective = await _permissionResolver.ResolveAsync(userId, tenantId, ct);
        if (effective.Contains(permissionCode) || effective.Contains("*"))
            return;

        var permission = await _permissions.GetByCodeAsync(permissionCode, ct);
        if (permission is null)
            return;

        await _overrides.AddAsync(new UserPermissionOverride
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = userId,
            PermissionId = permission.Id,
            GrantType = "grant",
            Reason = "Auto-granted on milestone head assignment",
            GrantedBy = grantedByUserId,
            CreatedAt = DateTimeOffset.UtcNow
        }, ct);
    }
}
