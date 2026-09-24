using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.DevPlatform.PlatformAccess.Commands.InvitePlatformManager;

public record InvitePlatformManagerCommand(
    string Email,
    string FullName,
    IReadOnlyList<Guid> RoleIds) : IRequest<Result>;
