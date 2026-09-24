using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.DevPlatform.PlatformAccess.Commands.AcceptPlatformManagerInvite;

public record AcceptPlatformManagerInviteCommand(string RawToken, string Password) : IRequest<Result>;
