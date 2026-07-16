using MediatR;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;
using ONEVO.Application.Common.Models;

using ONEVO.Application.Features.Auth.Invite.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Permission.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Roles.Mappers;
using ONEVO.Application.Features.Auth.Roles.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Roles.DTOs.Responses;

namespace ONEVO.Application.Features.Auth.Roles.Queries.GetRoleById;

public class GetRoleByIdQueryHandler : IRequestHandler<GetRoleByIdQuery, Result<RoleDetailDto>>
{
    private readonly IRoleRepository _roles;
    private readonly IRolePermissionRepository _rolePermissions;
    private readonly IPermissionRepository _permissions;
    private readonly ITenantPermissionCatalogService _permissionCatalog;
    private readonly ICurrentUser _currentUser;

    public GetRoleByIdQueryHandler(
        IRoleRepository roles,
        IRolePermissionRepository rolePermissions,
        IPermissionRepository permissions,
        ITenantPermissionCatalogService permissionCatalog,
        ICurrentUser currentUser)
    {
        _roles = roles;
        _rolePermissions = rolePermissions;
        _permissions = permissions;
        _permissionCatalog = permissionCatalog;
        _currentUser = currentUser;
    }

    public async Task<Result<RoleDetailDto>> Handle(GetRoleByIdQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<RoleDetailDto>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        if (tenantId == Guid.Empty)
            return Result<RoleDetailDto>.Forbidden("Tenant context missing.");

        var role = await _roles.GetByIdForTenantAsync(tenantId, request.RoleId, ct);
        if (role is null)
            return Result<RoleDetailDto>.NotFound("Role not found.");

        var rolePermissions = await _rolePermissions.ListByRoleAsync(role.Id, ct);
        var permissionIds = rolePermissions.Select(rp => rp.PermissionId).ToList();

        var permissions = permissionIds.Count == 0
            ? Array.Empty<Domain.Features.Auth.Entities.Permission>()
            : await _permissions.GetByIdsAsync(permissionIds, ct);

        var catalog = await _permissionCatalog.GetCatalogAsync(tenantId, ct);
        var dto = RoleMapper.ToDetailDto(role, permissions, RoleMapper.MapUniversalPermissions(catalog));

        return Result<RoleDetailDto>.Success(dto);
    }
}
