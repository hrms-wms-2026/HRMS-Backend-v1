using MediatR;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Auth.Roles;

using ONEVO.Application.Features.Auth.Invite.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Permission.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Roles.Mappers;
using ONEVO.Application.Features.Auth.Roles.RepositoryInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Application.Features.Auth.Roles.DTOs.Responses;
using AuthPermission = ONEVO.Domain.Features.Auth.Entities.Permission;

namespace ONEVO.Application.Features.Auth.Roles.Commands.AssignRolePermissions;

public class AssignRolePermissionsCommandHandler
    : IRequestHandler<AssignRolePermissionsCommand, Result<RoleDetailDto>>
{
    private readonly IRoleRepository _roles;
    private readonly IRolePermissionRepository _rolePermissions;
    private readonly IPermissionRepository _permissions;
    private readonly IModuleEntitlementService _entitlements;
    private readonly ITenantPermissionCatalogService _permissionCatalog;
    private readonly IUserRoleRepository _userRoles;
    private readonly IPermissionVersionService _permissionVersion;
    private readonly ICurrentUser _currentUser;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    public AssignRolePermissionsCommandHandler(
        IRoleRepository roles,
        IRolePermissionRepository rolePermissions,
        IPermissionRepository permissions,
        IModuleEntitlementService entitlements,
        ITenantPermissionCatalogService permissionCatalog,
        IUserRoleRepository userRoles,
        IPermissionVersionService permissionVersion,
        ICurrentUser currentUser,
        IUnitOfWork unitOfWork,
        IDateTimeProvider clock)
    {
        _roles = roles;
        _rolePermissions = rolePermissions;
        _permissions = permissions;
        _entitlements = entitlements;
        _permissionCatalog = permissionCatalog;
        _userRoles = userRoles;
        _permissionVersion = permissionVersion;
        _currentUser = currentUser;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<RoleDetailDto>> Handle(AssignRolePermissionsCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<RoleDetailDto>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        if (tenantId == Guid.Empty)
            return Result<RoleDetailDto>.Forbidden("Tenant context missing.");

        var role = await _roles.GetByIdForTenantAsync(tenantId, request.RoleId, ct);
        if (role is null)
            return Result<RoleDetailDto>.NotFound("Role not found.");

        if (role.IsSystem)
            return Result<RoleDetailDto>.Forbidden("System role permissions cannot be modified.");

        var requestedIds = request.PermissionIds.Distinct().ToList();

        IReadOnlyList<AuthPermission> resolvedPermissions = Array.Empty<AuthPermission>();
        if (requestedIds.Count > 0)
        {
            var validation = await RolePermissionAssignability.ValidateForTenantAsync(
                tenantId,
                requestedIds,
                _permissions,
                _entitlements,
                ct);

            if (!validation.IsSuccess)
                return Result<RoleDetailDto>.Failure(validation.Error!, validation.StatusCode ?? 400);

            resolvedPermissions = validation.Value!;
        }

        var existing = await _rolePermissions.ListByRoleAsync(role.Id, ct);
        var existingPermissionIds = existing.Select(rp => rp.PermissionId).ToHashSet();
        var requestedSet = requestedIds.ToHashSet();

        var toRemove = existing.Where(rp => !requestedSet.Contains(rp.PermissionId)).ToList();
        var toAdd = requestedIds
            .Where(id => !existingPermissionIds.Contains(id))
            .Select(id => new RolePermission { RoleId = role.Id, PermissionId = id })
            .ToList();

        if (toRemove.Count > 0)
            _rolePermissions.RemoveRange(toRemove);

        if (toAdd.Count > 0)
            await _rolePermissions.AddRangeAsync(toAdd, ct);

        await _unitOfWork.SaveChangesAsync(ct);

        if (toRemove.Count > 0 || toAdd.Count > 0)
        {
            var affectedUserIds = await _userRoles.ListUserIdsByRoleAsync(role.Id, _clock.UtcNow, ct);
            foreach (var userId in affectedUserIds)
                await _permissionVersion.IncrementVersionAsync(userId, ct);
        }

        var catalog = await _permissionCatalog.GetCatalogAsync(tenantId, ct);
        var dto = RoleMapper.ToDetailDto(role, resolvedPermissions, RoleMapper.MapUniversalPermissions(catalog));

        return Result<RoleDetailDto>.Success(dto);
    }
}
