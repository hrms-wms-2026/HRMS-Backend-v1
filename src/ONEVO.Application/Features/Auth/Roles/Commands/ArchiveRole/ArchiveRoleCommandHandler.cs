using MediatR;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Auth.Invite.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Permission.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Roles.RepositoryInterfaces;

namespace ONEVO.Application.Features.Auth.Roles.Commands.ArchiveRole;

public class ArchiveRoleCommandHandler : IRequestHandler<ArchiveRoleCommand, Result>
{
    private readonly IRoleRepository _roles;
    private readonly ICurrentUser _currentUser;
    private readonly IUnitOfWork _unitOfWork;

    public ArchiveRoleCommandHandler(
        IRoleRepository roles,
        ICurrentUser currentUser,
        IUnitOfWork unitOfWork)
    {
        _roles = roles;
        _currentUser = currentUser;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(ArchiveRoleCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result.Failure("Authentication required.", 403);

        var tenantId = _currentUser.TenantId;
        if (tenantId == Guid.Empty)
            return Result.Failure("Tenant context missing.", 403);

        var role = await _roles.GetByIdForTenantAsync(tenantId, request.RoleId, ct);
        if (role is null)
            return Result.NotFound("Role not found.");

        if (role.IsSystem)
            return Result.Failure("System roles cannot be archived.", 403);

        // Soft delete via SoftDeleteInterceptor (sets IsDeleted + DeletedAt on Remove call).
        _roles.Remove(role);

        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
}
