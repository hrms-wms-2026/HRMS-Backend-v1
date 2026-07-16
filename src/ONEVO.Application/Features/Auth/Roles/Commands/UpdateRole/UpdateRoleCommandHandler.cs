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

namespace ONEVO.Application.Features.Auth.Roles.Commands.UpdateRole;

public class UpdateRoleCommandHandler : IRequestHandler<UpdateRoleCommand, Result<RoleSummaryDto>>
{
    private readonly IRoleRepository _roles;
    private readonly IRolePermissionRepository _rolePermissions;
    private readonly ICurrentUser _currentUser;
    private readonly IUnitOfWork _unitOfWork;

    public UpdateRoleCommandHandler(
        IRoleRepository roles,
        IRolePermissionRepository rolePermissions,
        ICurrentUser currentUser,
        IUnitOfWork unitOfWork)
    {
        _roles = roles;
        _rolePermissions = rolePermissions;
        _currentUser = currentUser;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<RoleSummaryDto>> Handle(UpdateRoleCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<RoleSummaryDto>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        if (tenantId == Guid.Empty)
            return Result<RoleSummaryDto>.Forbidden("Tenant context missing.");

        var role = await _roles.GetByIdForTenantAsync(tenantId, request.RoleId, ct);
        if (role is null)
            return Result<RoleSummaryDto>.NotFound("Role not found.");

        if (role.IsSystem)
            return Result<RoleSummaryDto>.Forbidden("System roles cannot be modified.");

        var name = request.Name.Trim();
        if (!string.Equals(role.Name, name, StringComparison.Ordinal))
        {
            var nameClash = await _roles.GetByNameForTenantAsync(tenantId, name, ct);
            if (nameClash is not null && nameClash.Id != role.Id)
                return Result<RoleSummaryDto>.Conflict($"A role named '{name}' already exists.");
        }

        role.Name = name;
        role.Description = request.Description?.Trim() ?? string.Empty;

        await _unitOfWork.SaveChangesAsync(ct);

        var permissionCount = (await _rolePermissions.ListByRoleAsync(role.Id, ct)).Count;

        var dto = RoleMapper.ToSummaryDto(role, permissionCount);

        return Result<RoleSummaryDto>.Success(dto);
    }
}
