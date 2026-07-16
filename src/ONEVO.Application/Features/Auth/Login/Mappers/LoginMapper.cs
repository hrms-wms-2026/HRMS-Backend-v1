using ONEVO.Application.Features.Auth.Login.DTOs.Responses;
using ONEVO.Domain.Features.InfrastructureModule.Entities;

namespace ONEVO.Application.Features.Auth.Login.Mappers;

internal static class LoginMapper
{
    public static CurrentUserDto ToCurrentUserDto(User user) =>
        new(user.Id, user.TenantId, user.Email);

    public static LoginResponseDto ToPasswordChangeRequired(User user) =>
        new(
            CsrfTokenHash: string.Empty,
            CsrfToken: string.Empty,
            ExpiresAt: null,
            User: ToCurrentUserDto(user),
            RequiresPasswordChange: true);

    public static LoginResponseDto ToMfaRequired(User user, string mfaChallenge) =>
        new(
            CsrfTokenHash: string.Empty,
            CsrfToken: string.Empty,
            ExpiresAt: null,
            User: ToCurrentUserDto(user),
            RequiresMfa: true,
            MfaChallenge: mfaChallenge);
}
