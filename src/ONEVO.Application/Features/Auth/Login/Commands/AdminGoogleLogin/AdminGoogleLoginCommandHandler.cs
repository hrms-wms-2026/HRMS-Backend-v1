using System.Text.Json;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.Models.Auth;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.DTOs.Responses;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.ServiceInterfaces;
using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;

namespace ONEVO.Application.Features.Auth.Login.Commands.AdminGoogleLogin;

/// <summary>
/// Google OAuth admin login. The Google ID token proves identity only; authorization is
/// database-backed: the account must exist as an active row in platform_users, and effective
/// permissions come from platform_user_roles -> platform_role_permissions. There is no email
/// allowlist and no hardcoded admin identity. Fixed architecture decision: issues an opaque,
/// cookie-backed admin session via AddCookie and ITicketStore; no JWT is returned.
/// Security rules (audit 2026-11-05, F-01): Google must have verified the email; an MFA-enrolled
/// admin always gets the same TOTP challenge as password login instead of a session; and a Google
/// account is linked to a platform user only after that user passes MFA (the link is written by
/// VerifyAdminMfaCommandHandler), never on an email match alone.
/// </summary>
public sealed class AdminGoogleLoginCommandHandler
    : IRequestHandler<AdminGoogleLoginCommand, Result<AdminLoginResultDto>>
{
    private readonly IGoogleIdTokenValidator _google;
    private readonly IPlatformOAuthAppResolver _oauthApps;
    private readonly ISecureTokenGenerator _tokens;
    private readonly IDateTimeProvider _clock;
    private readonly IPlatformUserRepository _platformUsers;
    private readonly IPlatformPermissionResolver _permissionResolver;
    private readonly IPlatformAuthEventRepository _authEvents;
    private readonly IPlatformMfaChallengeStore _mfaChallenges;
    private readonly IUnitOfWork _uow;

    private static readonly TimeSpan MfaChallengeLifetime = TimeSpan.FromMinutes(10);

    public AdminGoogleLoginCommandHandler(
        IGoogleIdTokenValidator google,
        IPlatformOAuthAppResolver oauthApps,
        ISecureTokenGenerator tokens,
        IDateTimeProvider clock,
        IPlatformUserRepository platformUsers,
        IPlatformPermissionResolver permissionResolver,
        IPlatformAuthEventRepository authEvents,
        IPlatformMfaChallengeStore mfaChallenges,
        IUnitOfWork uow)
    {
        _google = google;
        _oauthApps = oauthApps;
        _tokens = tokens;
        _clock = clock;
        _platformUsers = platformUsers;
        _permissionResolver = permissionResolver;
        _authEvents = authEvents;
        _mfaChallenges = mfaChallenges;
        _uow = uow;
    }

    public async Task<Result<AdminLoginResultDto>> Handle(
        AdminGoogleLoginCommand request,
        CancellationToken ct)
    {
        var googleApp = await _oauthApps.GetActiveAppForProviderAsync("google", ct);
        if (googleApp is null || string.IsNullOrWhiteSpace(googleApp.ClientId))
            return Result<AdminLoginResultDto>.Failure(
                "Google OAuth app is not configured.", 500);

        var payload = await _google.ValidateAsync(request.GoogleIdToken, googleApp.ClientId, ct);
        if (payload is null || string.IsNullOrWhiteSpace(payload.Email))
        {
            await WriteAuthEventAsync(PlatformAuthEvent.LoginFailed, userId: null, request,
                new { reason = "invalid_google_token", method = "google" }, ct);
            return Result<AdminLoginResultDto>.Failure(
                "Invalid or expired Google ID token.", 401);
        }

        if (!payload.EmailVerified)
        {
            await WriteAuthEventAsync(PlatformAuthEvent.LoginFailed, userId: null, request,
                new { reason = "google_email_unverified", method = "google" }, ct);
            return Result<AdminLoginResultDto>.Failure(
                "Invalid or expired Google ID token.", 401);
        }

        var email = payload.Email.Trim().ToLowerInvariant();

        // An existing Google binding is the only thing that identifies a platform user by Google
        // account. An email match alone is only a candidate for linking (see below).
        var user = await _platformUsers.GetByGoogleSubAsync(payload.Subject, ct);
        var linkCandidate = false;
        if (user is null)
        {
            user = await _platformUsers.GetByEmailAsync(email, ct);
            if (user is not null && !string.IsNullOrEmpty(user.GoogleSub))
            {
                await WriteAuthEventAsync(PlatformAuthEvent.LoginFailed, user.Id, request,
                    new { reason = "google_account_mismatch", method = "google" }, ct);
                return NotAuthorizedForConsole();
            }

            linkCandidate = user is not null;
        }

        if (user is null)
        {
            await WriteAuthEventAsync(PlatformAuthEvent.LoginFailed, userId: null, request,
                new { reason = "platform_user_not_found", method = "google" }, ct);
            return NotAuthorizedForConsole();
        }

        var profile = await _permissionResolver.ResolveActiveUserAsync(user.Id, ct);
        if (profile is null)
        {
            await WriteAuthEventAsync(PlatformAuthEvent.LoginFailed, user.Id, request,
                new { reason = "platform_user_not_active", method = "google" }, ct);
            return Result<AdminLoginResultDto>.Forbidden(
                "This platform user is not active.");
        }

        var mfaEnrolled = user.MfaStatus == PlatformUser.MfaEnrolled;
        if (linkCandidate && !mfaEnrolled)
        {
            // Linking needs a second factor to prove the Google identity belongs to this admin.
            await WriteAuthEventAsync(PlatformAuthEvent.LoginFailed, user.Id, request,
                new { reason = "google_link_requires_mfa", method = "google" }, ct);
            return Result<AdminLoginResultDto>.Forbidden(
                "This Google account is not linked to a Developer Platform admin. Sign in with your password and set up MFA first.");
        }

        if (mfaEnrolled)
        {
            await WriteAuthEventAsync(PlatformAuthEvent.LoginSucceeded, user.Id, request,
                new { method = "google", mfa_pending = true, google_link_pending = linkCandidate }, ct);

            var challenge = linkCandidate
                ? await _mfaChallenges.CreateForGoogleLinkAsync(user.Id, payload.Subject, MfaChallengeLifetime, ct)
                : await _mfaChallenges.CreateAsync(user.Id, MfaChallengeLifetime, ct);

            return Result<AdminLoginResultDto>.Success(new AdminLoginResultDto(
                CsrfToken: string.Empty,
                CsrfTokenHash: string.Empty,
                ExpiresAt: null,
                PlatformUserId: Guid.Empty,
                Email: string.Empty,
                PlatformRole: string.Empty,
                RequiresMfa: true,
                MfaSessionToken: challenge,
                Permissions: Array.Empty<string>()));
        }

        user.LastLoginAt = _clock.UtcNow;
        await WriteAuthEventAsync(PlatformAuthEvent.LoginSucceeded, user.Id, request,
            new { method = "google" }, ct);

        var rawCsrfToken = _tokens.GenerateCsrfToken();
        var csrfHash = _tokens.HashToken(rawCsrfToken);

        return Result<AdminLoginResultDto>.Success(new AdminLoginResultDto(
            CsrfToken: rawCsrfToken,
            CsrfTokenHash: csrfHash,
            ExpiresAt: _clock.UtcNow.Add(SessionPolicy.SlidingWindow),
            PlatformUserId: user.Id,
            Email: user.Email,
            PlatformRole: profile.RoleNames.Count > 0 ? profile.RoleNames[0] : string.Empty));
    }

    private static Result<AdminLoginResultDto> NotAuthorizedForConsole() =>
        Result<AdminLoginResultDto>.Forbidden(
            "This Google account is not authorized for the Developer Platform admin console.");

    private async Task WriteAuthEventAsync(
        string eventType, Guid? userId, AdminGoogleLoginCommand request, object metadata, CancellationToken ct)
    {
        await _authEvents.AddAsync(new PlatformAuthEvent
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            EventType = eventType,
            SourceIp = request.IpAddress,
            UserAgent = request.UserAgent,
            MetadataJson = JsonSerializer.Serialize(metadata),
            CreatedAt = _clock.UtcNow
        }, ct);
        await _uow.SaveChangesAsync(ct);
    }
}
