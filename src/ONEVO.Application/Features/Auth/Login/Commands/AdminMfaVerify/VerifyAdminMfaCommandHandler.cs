using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.Models.Auth;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.DTOs.Responses;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.ServiceInterfaces;

namespace ONEVO.Application.Features.Auth.Login.Commands.AdminMfaVerify;

/// <summary>
/// Mirrors tenant VerifyMfaCommandHandler's shape (challenge lookup -> code verify -> consume ->
/// issue session), against IPlatformMfaChallengeStore/PlatformUser instead of the tenant-scoped
/// equivalents. Does not fall back to an unverified secret the way the tenant handler used to —
/// that fallback was the dead code the backend team just removed; ConfirmAdminMfaSetupCommand is
/// the only path that can complete enrollment here, matching their corrected design.
/// </summary>
public sealed class VerifyAdminMfaCommandHandler : IRequestHandler<VerifyAdminMfaCommand, Result<AdminLoginResultDto>>
{
    private const int MaximumFailedAttempts = 5;

    private readonly IPlatformMfaChallengeStore _mfaChallenges;
    private readonly IPlatformUserRepository _platformUsers;
    private readonly IEncryptionService _encryption;
    private readonly ITotpService _totpService;
    private readonly ISecureTokenGenerator _tokens;
    private readonly IPlatformPermissionResolver _permissionResolver;
    private readonly IDateTimeProvider _clock;
    private readonly IUnitOfWork _uow;

    public VerifyAdminMfaCommandHandler(
        IPlatformMfaChallengeStore mfaChallenges,
        IPlatformUserRepository platformUsers,
        IEncryptionService encryption,
        ITotpService totpService,
        ISecureTokenGenerator tokens,
        IPlatformPermissionResolver permissionResolver,
        IDateTimeProvider clock,
        IUnitOfWork uow)
    {
        _mfaChallenges = mfaChallenges;
        _platformUsers = platformUsers;
        _encryption = encryption;
        _totpService = totpService;
        _tokens = tokens;
        _permissionResolver = permissionResolver;
        _clock = clock;
        _uow = uow;
    }

    public async Task<Result<AdminLoginResultDto>> Handle(VerifyAdminMfaCommand request, CancellationToken ct)
    {
        var challengeState = await _mfaChallenges.GetAsync(request.Challenge, ct);
        if (challengeState is null)
            return Result<AdminLoginResultDto>.Failure("Invalid or expired MFA session.", 401);

        var user = await _platformUsers.GetByIdAsync(challengeState.PlatformUserId, ct);
        if (user is null || string.IsNullOrEmpty(user.MfaSecret))
            return Result<AdminLoginResultDto>.Failure("Invalid or expired MFA session.", 401);

        var secret = _encryption.Decrypt(user.MfaSecret);
        if (!_totpService.Verify(secret, request.Code))
        {
            await _mfaChallenges.RegisterFailedAttemptAsync(request.Challenge, MaximumFailedAttempts, ct);
            return Result<AdminLoginResultDto>.Failure("Invalid MFA code.", 401);
        }

        var consumed = await _mfaChallenges.TryConsumeAsync(request.Challenge, ct);
        if (consumed is null || consumed.PlatformUserId != user.Id)
            return Result<AdminLoginResultDto>.Failure("Invalid or expired MFA session.", 401);

        var profile = await _permissionResolver.ResolveActiveUserAsync(user.Id, ct);
        if (profile is null)
            return Result<AdminLoginResultDto>.Failure("This account is not active.", 403);

        user.LastLoginAt = _clock.UtcNow;
        _platformUsers.UpdateUser(user);
        await _uow.SaveChangesAsync(ct);

        var rawCsrfToken = _tokens.GenerateCsrfToken();
        var csrfHash = _tokens.HashToken(rawCsrfToken);

        return Result<AdminLoginResultDto>.Success(new AdminLoginResultDto(
            CsrfToken: rawCsrfToken,
            CsrfTokenHash: csrfHash,
            ExpiresAt: _clock.UtcNow.Add(SessionPolicy.SlidingWindow),
            PlatformUserId: user.Id,
            Email: user.Email,
            PlatformRole: profile.RoleNames.Count > 0 ? profile.RoleNames[0] : string.Empty,
            Permissions: profile.PermissionCodes.ToList()));
    }
}
