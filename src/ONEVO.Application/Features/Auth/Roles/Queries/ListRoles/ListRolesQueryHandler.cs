using MediatR;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Common.Models;

using ONEVO.Application.Features.Auth.Invite.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Permission.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Roles.Mappers;
using ONEVO.Application.Features.Auth.Roles.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Roles.DTOs.Responses;

namespace ONEVO.Application.Features.Auth.Roles.Queries.ListRoles;

public class ListRolesQueryHandler
    : IRequestHandler<ListRolesQuery, Result<IReadOnlyList<RoleSummaryDto>>>
{
    private readonly IRoleRepository _roles;
    private readonly IRolePermissionRepository _rolePermissions;
    private readonly IPermissionRepository _permissions;
    private readonly IModuleEntitlementService _entitlements;
    private readonly ICurrentUser _currentUser;

    public ListRolesQueryHandler(
        IRoleRepository roles,
        IRolePermissionRepository rolePermissions,
        IPermissionRepository permissions,
        IModuleEntitlementService entitlements,
        ICurrentUser currentUser)
    {
        _roles = roles;
        _rolePermissions = rolePermissions;
        _permissions = permissions;
        _entitlements = entitlements;
        _currentUser = currentUser;
    }

    public async Task<Result<IReadOnlyList<RoleSummaryDto>>> Handle(ListRolesQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<IReadOnlyList<RoleSummaryDto>>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        if (tenantId == Guid.Empty)
            return Result<IReadOnlyList<RoleSummaryDto>>.Forbidden("Tenant context missing.");

        var roles = await _roles.ListByTenantAsync(tenantId, ct);

        var rowsByRole = new Dictionary<Guid, IReadOnlyList<Domain.Features.Auth.Entities.RolePermission>>(roles.Count);
        foreach (var role in roles)
            rowsByRole[role.Id] = await _rolePermissions.ListByRoleAsync(role.Id, ct);

        // Count only what the role actually grants, so the list matches the role detail.
        var permissionIds = rowsByRole.Values.SelectMany(rows => rows).Select(rp => rp.PermissionId).Distinct().ToList();
        var grantingModules = await RoleEffectivePermissions.GetGrantingModulesAsync(_entitlements, tenantId, ct);
        var grantingIds = permissionIds.Count == 0
            ? new HashSet<Guid>()
            : RoleEffectivePermissions.Filter(await _permissions.GetByIdsAsync(permissionIds, ct), grantingModules)
                .Select(p => p.Id)
                .ToHashSet();

        var summaries = new List<RoleSummaryDto>(roles.Count);
        foreach (var role in roles)
        {
            var count = rowsByRole[role.Id].Select(rp => rp.PermissionId).Distinct().Count(grantingIds.Contains);
            summaries.Add(RoleMapper.ToSummaryDto(role, count));
        }

        return Result<IReadOnlyList<RoleSummaryDto>>.Success(summaries);
    }
}
