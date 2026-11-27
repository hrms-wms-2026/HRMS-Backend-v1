using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.DevPlatform.PlatformAccess.Commands.AcceptPlatformManagerInvite;

/// <summary>Returns the accepted account's email, so sign-in can start with that account.</summary>
public record AcceptPlatformManagerInviteCommand(string RawToken, string Password) : IRequest<Result<string>>;
