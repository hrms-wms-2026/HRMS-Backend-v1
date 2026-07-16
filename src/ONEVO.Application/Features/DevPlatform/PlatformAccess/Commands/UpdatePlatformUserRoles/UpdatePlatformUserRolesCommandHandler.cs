using MediatR;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Helpers;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.ServiceInterfaces;
using ONEVO.Application.Common.Exceptions;

namespace ONEVO.Application.Features.DevPlatform.PlatformAccess.Commands.UpdatePlatformUserRoles;

public class UpdatePlatformUserRolesCommandHandler : IRequestHandler<UpdatePlatformUserRolesCommand>
{
    private readonly IPlatformUserRepository _userRepository;
    private readonly IPlatformRoleRepository _roleRepository;
    private readonly IPlatformAccessManagementService _accessService;
    private readonly ICurrentPlatformUserContext _currentUser;

    public UpdatePlatformUserRolesCommandHandler(
        IPlatformUserRepository userRepository,
        IPlatformRoleRepository roleRepository,
        IPlatformAccessManagementService accessService,
        ICurrentPlatformUserContext currentUser)
    {
        _userRepository = userRepository;
        _roleRepository = roleRepository;
        _accessService = accessService;
        _currentUser = currentUser;
    }

    public async Task Handle(UpdatePlatformUserRolesCommand request, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId == null)
            throw new UnauthorizedAccessException("Current platform user cannot be resolved.");

        if (!_currentUser.HasPlatformPermission(PlatformPermissionCatalog.RolesManage))
            throw new UnauthorizedAccessException("RolesManage permission is required to update user roles.");

        var user = await _userRepository.GetByIdAsync(request.UserId, cancellationToken);
        if (user == null)
            throw new NotFoundException($"Platform user {request.UserId} not found.");

        var allRoles = await _roleRepository.ListRolesAsync(cancellationToken);
        var validRoleIds = allRoles.Select(r => r.Id).ToHashSet();
        foreach (var roleId in request.RoleIds)
        {
            if (!validRoleIds.Contains(roleId))
                throw new ArgumentException($"Unknown role ID: {roleId}");
        }

        await _accessService.ValidateUserRoleLockoutPreventionAsync(request.UserId, request.RoleIds, cancellationToken);

        await _userRepository.ReplaceRolesAsync(request.UserId, request.RoleIds, cancellationToken);
        _userRepository.UpdateUser(user);
    }
}
