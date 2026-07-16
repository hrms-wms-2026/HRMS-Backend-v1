using MediatR;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Helpers;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.ServiceInterfaces;
using ONEVO.Application.Common.Exceptions;

namespace ONEVO.Application.Features.DevPlatform.PlatformAccess.Commands.UpdatePlatformRolePermissions;

public class UpdatePlatformRolePermissionsCommandHandler : IRequestHandler<UpdatePlatformRolePermissionsCommand>
{
    private readonly IPlatformRoleRepository _roleRepository;
    private readonly IPlatformAccessManagementService _accessService;
    private readonly ICurrentPlatformUserContext _currentUser;

    public UpdatePlatformRolePermissionsCommandHandler(
        IPlatformRoleRepository roleRepository,
        IPlatformAccessManagementService accessService,
        ICurrentPlatformUserContext currentUser)
    {
        _roleRepository = roleRepository;
        _accessService = accessService;
        _currentUser = currentUser;
    }

    public async Task Handle(UpdatePlatformRolePermissionsCommand request, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId == null)
            throw new UnauthorizedAccessException("Current platform user cannot be resolved.");

        var role = await _roleRepository.GetRoleByIdAsync(request.RoleId, cancellationToken);
        if (role == null)
            throw new NotFoundException($"Platform role {request.RoleId} not found.");

        var catalogPermissions = PlatformPermissionCatalog.GetAll().Select(p => p.Code).ToHashSet();
        foreach (var p in request.Permissions)
        {
            if (!catalogPermissions.Contains(p))
                throw new ArgumentException($"Unknown permission code: {p}");
        }

        await _accessService.ValidateRolePermissionLockoutPreventionAsync(request.RoleId, request.Permissions, cancellationToken);

        await _roleRepository.ReplacePermissionsAsync(request.RoleId, request.Permissions, cancellationToken);
        _roleRepository.UpdateRole(role);
        // Note: SaveChanges is handled by UnitOfWork via the Controller/Pipeline.
    }
}
