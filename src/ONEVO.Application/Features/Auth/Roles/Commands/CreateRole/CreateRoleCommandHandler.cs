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

namespace ONEVO.Application.Features.Auth.Roles.Commands.CreateRole;

public class CreateRoleCommandHandler : IRequestHandler<CreateRoleCommand, Result<RoleDetailDto>>
{
    private readonly IRoleRepository _roles;
    private readonly IRolePermissionRepository _rolePermissions;
    private readonly IPermissionRepository _permissions;
    private readonly IModuleEntitlementService _entitlements;
    private readonly ITenantPermissionCatalogService _permissionCatalog;
    private readonly ICurrentUser _currentUser;
    private readonly IUnitOfWork _unitOfWork;

    public CreateRoleCommandHandler(
        IRoleRepository roles,
        IRolePermissionRepository rolePermissions,
        IPermissionRepository permissions,
        IModuleEntitlementService entitlements,
        ITenantPermissionCatalogService permissionCatalog,
        ICurrentUser currentUser,
        IUnitOfWork unitOfWork)
    {
        _roles = roles;
        _rolePermissions = rolePermissions;
        _permissions = permissions;
        _entitlements = entitlements;
        _permissionCatalog = permissionCatalog;
        _currentUser = currentUser;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<RoleDetailDto>> Handle(CreateRoleCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<RoleDetailDto>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        if (tenantId == Guid.Empty)
            return Result<RoleDetailDto>.Forbidden("Tenant context missing.");

        var name = request.Name.Trim();

        var existing = await _roles.GetByNameForTenantAsync(tenantId, name, ct);
        if (existing is not null)
            return Result<RoleDetailDto>.Conflict($"A role named '{name}' already exists.");

        var permissionIds = (request.PermissionIds ?? Array.Empty<Guid>())
            .Distinct()
            .ToList();

        IReadOnlyList<AuthPermission> resolvedPermissions = Array.Empty<AuthPermission>();
        if (permissionIds.Count > 0)
        {
            var validation = await RolePermissionAssignability.ValidateForTenantAsync(
                tenantId,
                permissionIds,
                _permissions,
                _entitlements,
                ct);

            if (!validation.IsSuccess)
                return Result<RoleDetailDto>.Failure(validation.Error!, validation.StatusCode ?? 400);

            resolvedPermissions = validation.Value!;
        }

        var role = new Role
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = name,
            Description = request.Description?.Trim() ?? string.Empty,
            IsSystem = false
        };

        await _roles.AddAsync(role, ct);

        if (resolvedPermissions.Count > 0)
        {
            var rolePermissions = resolvedPermissions
                .Select(p => new RolePermission { TenantId = tenantId, RoleId = role.Id, PermissionId = p.Id })
                .ToList();
            await _rolePermissions.AddRangeAsync(rolePermissions, ct);
        }

        await _unitOfWork.SaveChangesAsync(ct);

        var catalog = await _permissionCatalog.GetCatalogAsync(tenantId, ct);
        var dto = RoleMapper.ToDetailDto(role, resolvedPermissions, RoleMapper.MapUniversalPermissions(catalog));

        return Result<RoleDetailDto>.Success(dto);
    }
}
