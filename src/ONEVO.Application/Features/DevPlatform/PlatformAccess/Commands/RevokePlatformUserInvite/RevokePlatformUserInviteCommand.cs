using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.DevPlatform.PlatformAccess.Commands.RevokePlatformUserInvite;

public record RevokePlatformUserInviteCommand(Guid PlatformUserId) : IRequest<Result>;
