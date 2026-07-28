using MediatR;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Auth.Roles;
using ONEVO.Application.Features.Auth.Invite.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Permission.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Roles.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.Subscription.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Domain.Features.Auth.Entities;

namespace ONEVO.Application.Features.DevPlatform.Tenancy.Commands.ApplyRoleTemplate;

public sealed class ApplyRoleTemplateCommandHandler
    : IRequestHandler<ApplyRoleTemplateCommand, Result<ApplyRoleTemplateResultDto>>
{
    private readonly ITenantRepository _tenants;
    private readonly IRoleTemplateRepository _templates;
    private readonly IRoleRepository _roles;
    private readonly IRolePermissionRepository _rolePermissions;
    private readonly IPermissionRepository _permissions;
    private readonly IModuleEntitlementService _entitlements;
    private readonly ITenantPermissionCatalogService _permissionCatalog;
    private readonly IUserRoleRepository _userRoles;
    private readonly IPermissionVersionService _permissionVersion;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    public ApplyRoleTemplateCommandHandler(
        ITenantRepository tenants,
        IRoleTemplateRepository templates,
        IRoleRepository roles,
        IRolePermissionRepository rolePermissions,
        IPermissionRepository permissions,
        IModuleEntitlementService entitlements,
        ITenantPermissionCatalogService permissionCatalog,
        IUserRoleRepository userRoles,
        IPermissionVersionService permissionVersion,
        IUnitOfWork unitOfWork,
        IDateTimeProvider clock)
    {
        _tenants = tenants;
        _templates = templates;
        _roles = roles;
        _rolePermissions = rolePermissions;
        _permissions = permissions;
        _entitlements = entitlements;
        _permissionCatalog = permissionCatalog;
        _userRoles = userRoles;
        _permissionVersion = permissionVersion;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<ApplyRoleTemplateResultDto>> Handle(ApplyRoleTemplateCommand request, CancellationToken ct)
    {
        if (await _tenants.GetByIdAsync(request.TenantId, ct) is null)
            return Result<ApplyRoleTemplateResultDto>.NotFound("Tenant not found.");

        var template = await _templates.GetByIdAsync(request.TemplateId, ct);
        if (template is null)
            return Result<ApplyRoleTemplateResultDto>.NotFound("Role template not found.");

        if (!template.IsActive)
            return Result<ApplyRoleTemplateResultDto>.Failure("This role template is not active.");

        var permCodes = RoleTemplateJson.DeserializeStringList(template.PermissionCodesJson)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var resolvedAll = await _permissions.GetByCodesAsync(permCodes, ct);
        if (resolvedAll.Count != permCodes.Count)
        {
            var found = resolvedAll.Select(p => p.Code).ToHashSet(StringComparer.Ordinal);
            var missing = permCodes.Where(c => !found.Contains(c)).OrderBy(c => c).ToList();
            return Result<ApplyRoleTemplateResultDto>.Failure(
                $"Template references unknown permission codes: {string.Join(", ", missing)}.");
        }

        var allIds = resolvedAll.Select(p => p.Id).Distinct().ToList();
        var allowed = await _entitlements.GetAssignablePermissionsForTenantAsync(
            request.TenantId,
            allIds,
            ct);

        var allowedIds = allowed.Select(p => p.Id).ToHashSet();
        var appliedOrdered = resolvedAll
            .Where(p => allowedIds.Contains(p.Id))
            .OrderBy(p => p.Module, StringComparer.Ordinal)
            .ThenBy(p => p.Code, StringComparer.Ordinal)
            .ToList();

        var rejectedCodes = resolvedAll
            .Where(p => !allowedIds.Contains(p.Id))
            .Select(p => p.Code)
            .Distinct()
            .OrderBy(c => c, StringComparer.Ordinal)
            .ToList();

        var finalName = string.IsNullOrWhiteSpace(request.RoleNameOverride)
            ? template.Name
            : request.RoleNameOverride.Trim();

        if (string.IsNullOrWhiteSpace(finalName))
            return Result<ApplyRoleTemplateResultDto>.Failure("Role name is required.");

        if (finalName.Length > 100)
            return Result<ApplyRoleTemplateResultDto>.Failure("Role name must be at most 100 characters.");

        var catalog = await _permissionCatalog.GetCatalogAsync(request.TenantId, ct);
        var universalCodes = catalog.UniversalPermissions
            .Select(u => u.Code)
            .Distinct()
            .OrderBy(c => c, StringComparer.Ordinal)
            .ToList();

        var existingByTemplate = await _roles.GetBySourceTemplateForTenantAsync(
            request.TenantId,
            request.TemplateId,
            ct);

        if (existingByTemplate is not null && !request.ForceUpdate)
        {
            return Result<ApplyRoleTemplateResultDto>.Conflict(
                "This template is already applied to a role in the tenant. Use forceUpdate to refresh permissions.");
        }

        Role role;
        if (existingByTemplate is not null)
        {
            role = existingByTemplate;

            if (!string.IsNullOrWhiteSpace(request.RoleNameOverride))
            {
                var nameOwner = await _roles.GetByNameForTenantAsync(request.TenantId, finalName, ct);
                if (nameOwner is not null && nameOwner.Id != role.Id)
                {
                    return Result<ApplyRoleTemplateResultDto>.Conflict(
                        $"A role named '{finalName}' already exists in this tenant.");
                }

                role.Name = finalName;
            }

            if (role.IsSystem)
            {
                return Result<ApplyRoleTemplateResultDto>.Forbidden(
                    "Cannot update permissions for a system role via template apply.");
            }
        }
        else
        {
            var nameClash = await _roles.GetByNameForTenantAsync(request.TenantId, finalName, ct);
            if (nameClash is not null)
            {
                return Result<ApplyRoleTemplateResultDto>.Conflict(
                    $"A role named '{finalName}' already exists in this tenant.");
            }

            role = new Role
            {
                Id = Guid.NewGuid(),
                TenantId = request.TenantId,
                Name = finalName,
                Description = template.Description,
                IsSystem = false,
                SourceTemplateId = request.TemplateId
            };

            await _roles.AddAsync(role, ct);
        }

        var requestedPermIds = appliedOrdered.Select(p => p.Id).ToList();
        var existingRps = await _rolePermissions.ListByRoleAsync(role.Id, ct);
        var existingPermIds = existingRps.Select(rp => rp.PermissionId).ToHashSet();
        var requestedSet = requestedPermIds.ToHashSet();

        var toRemove = existingRps.Where(rp => !requestedSet.Contains(rp.PermissionId)).ToList();
        var toAdd = requestedPermIds
            .Where(id => !existingPermIds.Contains(id))
            .Select(id => new RolePermission { TenantId = request.TenantId, RoleId = role.Id, PermissionId = id })
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

        var appliedCodes = appliedOrdered.Select(p => p.Code).ToList();

        return Result<ApplyRoleTemplateResultDto>.Success(
            new ApplyRoleTemplateResultDto(
                role.Id,
                role.Name,
                appliedCodes,
                rejectedCodes,
                universalCodes));
    }
}
