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
    private readonly ICurrentUser _currentUser;

    public ListRolesQueryHandler(
        IRoleRepository roles,
        IRolePermissionRepository rolePermissions,
        ICurrentUser currentUser)
    {
        _roles = roles;
        _rolePermissions = rolePermissions;
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

        var summaries = new List<RoleSummaryDto>(roles.Count);
        foreach (var role in roles)
        {
            var permissions = await _rolePermissions.ListByRoleAsync(role.Id, ct);
            summaries.Add(RoleMapper.ToSummaryDto(role, permissions.Count));
        }

        return Result<IReadOnlyList<RoleSummaryDto>>.Success(summaries);
    }
}
