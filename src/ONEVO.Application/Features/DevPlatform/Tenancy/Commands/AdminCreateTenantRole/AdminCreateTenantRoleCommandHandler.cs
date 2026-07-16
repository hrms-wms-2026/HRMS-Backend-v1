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
using ONEVO.Application.Features.DevPlatform.Billing.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Provisioning.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Subscription.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Application.Features.Auth.Roles.DTOs.Responses;

namespace ONEVO.Application.Features.DevPlatform.Tenancy.Commands.AdminCreateTenantRole;

public sealed class AdminCreateTenantRoleCommandHandler
    : IRequestHandler<AdminCreateTenantRoleCommand, Result<RoleDetailDto>>
{
    private readonly ITenantRepository _tenants;
    private readonly IRoleRepository _roles;
    private readonly IRolePermissionRepository _rolePermissions;
    private readonly IPermissionRepository _permissions;
    private readonly IModuleEntitlementService _entitlements;
    private readonly ITenantPermissionCatalogService _permissionCatalog;
    private readonly IUnitOfWork _unitOfWork;

    public AdminCreateTenantRoleCommandHandler(
        ITenantRepository tenants,
        IRoleRepository roles,
        IRolePermissionRepository rolePermissions,
        IPermissionRepository permissions,
        IModuleEntitlementService entitlements,
        ITenantPermissionCatalogService permissionCatalog,
        IUnitOfWork unitOfWork)
    {
        _tenants = tenants;
        _roles = roles;
        _rolePermissions = rolePermissions;
        _permissions = permissions;
        _entitlements = entitlements;
        _permissionCatalog = permissionCatalog;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<RoleDetailDto>> Handle(AdminCreateTenantRoleCommand request, CancellationToken ct)
    {
        if (await _tenants.GetByIdAsync(request.TenantId, ct) is null)
            return Result<RoleDetailDto>.NotFound("Tenant not found.");

        var name = request.Name.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return Result<RoleDetailDto>.Failure("Name is required.");

        var existing = await _roles.GetByNameForTenantAsync(request.TenantId, name, ct);
        if (existing is not null)
            return Result<RoleDetailDto>.Conflict($"A role named '{name}' already exists.");

        var permissionIds = (request.PermissionIds ?? Array.Empty<Guid>())
            .Distinct()
            .ToList();

        IReadOnlyList<Permission> resolvedPermissions = Array.Empty<Permission>();
        if (permissionIds.Count > 0)
        {
            var validation = await RolePermissionAssignability.ValidateForTenantAsync(
                request.TenantId,
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
            TenantId = request.TenantId,
            Name = name,
            Description = request.Description?.Trim() ?? string.Empty,
            IsSystem = false
        };

        await _roles.AddAsync(role, ct);

        if (resolvedPermissions.Count > 0)
        {
            var rolePermissions = resolvedPermissions
                .Select(p => new RolePermission { RoleId = role.Id, PermissionId = p.Id })
                .ToList();
            await _rolePermissions.AddRangeAsync(rolePermissions, ct);
        }

        await _unitOfWork.SaveChangesAsync(ct);

        var catalog = await _permissionCatalog.GetCatalogAsync(request.TenantId, ct);
        var dto = RoleMapper.ToDetailDto(role, resolvedPermissions, RoleMapper.MapUniversalPermissions(catalog));

        return Result<RoleDetailDto>.Success(dto);
    }
}
