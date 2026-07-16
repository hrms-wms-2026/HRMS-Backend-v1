using MediatR;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Helpers;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.ServiceInterfaces;
using ONEVO.Application.Common.Exceptions;

namespace ONEVO.Application.Features.DevPlatform.PlatformAccess.Commands.UpdatePlatformUserRoles;

public record UpdatePlatformUserRolesCommand(
    Guid UserId,
    IReadOnlyList<Guid> RoleIds) : IRequest;
