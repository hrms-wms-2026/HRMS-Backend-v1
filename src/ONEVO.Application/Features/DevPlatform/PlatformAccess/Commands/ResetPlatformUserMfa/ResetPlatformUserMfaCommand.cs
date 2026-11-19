using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.DevPlatform.PlatformAccess.Commands.ResetPlatformUserMfa;

/// <summary>
/// Clears another platform admin's MFA enrollment so they can enroll a new authenticator
/// (security audit F-02, decision D2). The caller can never target themselves.
/// </summary>
public sealed record ResetPlatformUserMfaCommand(Guid PlatformUserId) : IRequest<Result>;
