using MediatR;
using ONEVO.Application.Common.Models;

using ONEVO.Application.Features.Auth.Invite.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Permission.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Roles.Mappers;
using ONEVO.Application.Features.Auth.Roles.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Billing.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Provisioning.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Subscription.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Roles.DTOs.Responses;

namespace ONEVO.Application.Features.DevPlatform.Tenancy.Queries.AdminListTenantRoles;

public sealed class AdminListTenantRolesQueryHandler
    : IRequestHandler<AdminListTenantRolesQuery, Result<IReadOnlyList<RoleSummaryDto>>>
{
    private readonly ITenantRepository _tenants;
    private readonly IRoleRepository _roles;
    private readonly IRolePermissionRepository _rolePermissions;

    public AdminListTenantRolesQueryHandler(
        ITenantRepository tenants,
        IRoleRepository roles,
        IRolePermissionRepository rolePermissions)
    {
        _tenants = tenants;
        _roles = roles;
        _rolePermissions = rolePermissions;
    }

    public async Task<Result<IReadOnlyList<RoleSummaryDto>>> Handle(
        AdminListTenantRolesQuery request,
        CancellationToken ct)
    {
        if (await _tenants.GetByIdAsync(request.TenantId, ct) is null)
            return Result<IReadOnlyList<RoleSummaryDto>>.NotFound("Tenant not found.");

        var roles = await _roles.ListByTenantAsync(request.TenantId, ct);

        var summaries = new List<RoleSummaryDto>(roles.Count);
        foreach (var role in roles)
        {
            var permissions = await _rolePermissions.ListByRoleAsync(role.Id, ct);
            summaries.Add(RoleMapper.ToSummaryDto(role, permissions.Count));
        }

        return Result<IReadOnlyList<RoleSummaryDto>>.Success(summaries);
    }
}
