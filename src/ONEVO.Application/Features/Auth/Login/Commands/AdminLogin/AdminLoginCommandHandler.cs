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
using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;

namespace ONEVO.Application.Features.Auth.Login.Commands.AdminLogin;

/// <summary>
/// Database-backed email/password platform login. Issues an opaque cookie-backed admin
/// session; no JWT, access token, refresh token, or credential material is returned.
/// </summary>
public sealed class AdminLoginCommandHandler
    : IRequestHandler<AdminLoginCommand, Result<AdminLoginResultDto>>
{
    private const int LockoutThreshold = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    private readonly ISecureTokenGenerator _tokens;
    private readonly IDateTimeProvider _clock;
    private readonly IPlatformUserRepository _platformUsers;
    private readonly IPlatformUserCredentialRepository _credentials;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IPlatformPermissionResolver _permissionResolver;
    private readonly IPlatformAuthEventRepository _authEvents;
    private readonly IPlatformMfaChallengeStore _mfaChallenges;
    private readonly IUnitOfWork _uow;

    private static readonly TimeSpan MfaChallengeLifetime = TimeSpan.FromMinutes(10);

    public AdminLoginCommandHandler(
        ISecureTokenGenerator tokens,
        IDateTimeProvider clock,
        IPlatformUserRepository platformUsers,
        IPlatformUserCredentialRepository credentials,
        IPasswordHasher passwordHasher,
        IPlatformPermissionResolver permissionResolver,
        IPlatformAuthEventRepository authEvents,
        IPlatformMfaChallengeStore mfaChallenges,
        IUnitOfWork uow)
    {
        _tokens = tokens;
        _clock = clock;
        _platformUsers = platformUsers;
        _credentials = credentials;
        _passwordHasher = passwordHasher;
        _permissionResolver = permissionResolver;
        _authEvents = authEvents;
        _mfaChallenges = mfaChallenges;
        _uow = uow;
    }

    public async Task<Result<AdminLoginResultDto>> Handle(
        AdminLoginCommand request,
        CancellationToken ct)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await _platformUsers.GetByEmailAsync(email, ct);
        if (user is null)
        {
            await WriteAuthEventAsync(
                PlatformAuthEvent.LoginFailed,
                null,
                request,
                new { reason = "invalid_credentials", method = "password" },
                ct);
            return InvalidCredentials();
        }

        var credential = await _credentials.GetActivePasswordCredentialAsync(user.Id, ct);
        if (credential is null || string.IsNullOrWhiteSpace(credential.PasswordHash))
        {
            await WriteAuthEventAsync(
                PlatformAuthEvent.LoginFailed,
                user.Id,
                request,
                new { reason = "credential_unavailable", method = "password" },
                ct);
            return InvalidCredentials();
        }

        var now = _clock.UtcNow;
        if (credential.LockedUntil.HasValue && credential.LockedUntil.Value > now)
        {
            await WriteAuthEventAsync(
                PlatformAuthEvent.LoginFailed,
                user.Id,
                request,
                new { reason = "credential_locked", method = "password" },
                ct);
            return InvalidCredentials();
        }

        if (!_passwordHasher.Verify(request.Password, credential.PasswordHash))
        {
            credential.FailedLoginCount++;
            credential.UpdatedAt = now;
            if (credential.FailedLoginCount >= LockoutThreshold)
            {
                credential.LockedUntil = now.Add(LockoutDuration);
            }

            _credentials.Update(credential);
            await WriteAuthEventAsync(
                PlatformAuthEvent.LoginFailed,
                user.Id,
                request,
                new { reason = "invalid_credentials", method = "password" },
                ct);
            return InvalidCredentials();
        }

        // Resolved only after the password is proven correct: an inactive account is
        // reported as 403 to the credential holder, while wrong credentials stay a
        // generic 401 that does not disclose account state.
        var profile = await _permissionResolver.ResolveActiveUserAsync(user.Id, ct);
        if (profile is null)
        {
            await WriteAuthEventAsync(
                PlatformAuthEvent.LoginFailed,
                user.Id,
                request,
                new { reason = "account_unavailable", method = "password" },
                ct);
            return Result<AdminLoginResultDto>.Failure("This account is not active.", 403);
        }

        credential.FailedLoginCount = 0;
        credential.LockedUntil = null;
        credential.LastUsedAt = now;
        credential.UpdatedAt = now;
        _credentials.Update(credential);

        if (user.MfaStatus == PlatformUser.MfaEnrolled)
        {
            await WriteAuthEventAsync(
                PlatformAuthEvent.LoginSucceeded,
                user.Id,
                request,
                new { method = "password", mfa_pending = true },
                ct);

            var mfaChallenge = await _mfaChallenges.CreateAsync(user.Id, MfaChallengeLifetime, ct);
            return Result<AdminLoginResultDto>.Success(new AdminLoginResultDto(
                CsrfToken: string.Empty,
                CsrfTokenHash: string.Empty,
                ExpiresAt: null,
                PlatformUserId: Guid.Empty,
                Email: string.Empty,
                PlatformRole: string.Empty,
                RequiresMfa: true,
                MfaSessionToken: mfaChallenge,
                Permissions: Array.Empty<string>()));
        }

        user.LastLoginAt = now;
        await WriteAuthEventAsync(
            PlatformAuthEvent.LoginSucceeded,
            user.Id,
            request,
            new { method = "password" },
            ct);

        var rawCsrfToken = _tokens.GenerateCsrfToken();
        var csrfHash = _tokens.HashToken(rawCsrfToken);

        return Result<AdminLoginResultDto>.Success(new AdminLoginResultDto(
            CsrfToken: rawCsrfToken,
            CsrfTokenHash: csrfHash,
            ExpiresAt: now.Add(SessionPolicy.SlidingWindow),
            PlatformUserId: user.Id,
            Email: user.Email,
            PlatformRole: profile.RoleNames.Count > 0 ? profile.RoleNames[0] : string.Empty,
            Permissions: profile.PermissionCodes.ToList()));
    }

    private static Result<AdminLoginResultDto> InvalidCredentials()
    {
        return Result<AdminLoginResultDto>.Failure("Invalid email or password.", 401);
    }

    private async Task WriteAuthEventAsync(
        string eventType,
        Guid? userId,
        AdminLoginCommand request,
        object metadata,
        CancellationToken ct)
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
