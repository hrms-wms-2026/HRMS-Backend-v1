using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Options;
using ONEVO.Application.Common.Configuration;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.Models.Auth;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.DTOs.Responses;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.ServiceInterfaces;
using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;

namespace ONEVO.Application.Features.Auth.Login.Commands.AdminGoogleLogin;

/// <summary>
/// Google OAuth admin login. The Google ID token proves identity only; authorization is
/// database-backed: the account must exist as an active row in platform_users, and effective
/// permissions come from platform_user_roles -> platform_role_permissions. There is no email
/// allowlist and no hardcoded admin identity. Fixed architecture decision: issues an opaque,
/// cookie-backed admin session via AddCookie and ITicketStore; no JWT is returned.
/// </summary>
public sealed class AdminGoogleLoginCommandHandler
    : IRequestHandler<AdminGoogleLoginCommand, Result<AdminLoginResultDto>>
{
    private readonly IGoogleIdTokenValidator _google;
    private readonly IOptions<GoogleAuthOptions> _options;
    private readonly ISecureTokenGenerator _tokens;
    private readonly IDateTimeProvider _clock;
    private readonly IPlatformUserRepository _platformUsers;
    private readonly IPlatformPermissionResolver _permissionResolver;
    private readonly IPlatformAuthEventRepository _authEvents;
    private readonly IUnitOfWork _uow;

    public AdminGoogleLoginCommandHandler(
        IGoogleIdTokenValidator google,
        IOptions<GoogleAuthOptions> options,
        ISecureTokenGenerator tokens,
        IDateTimeProvider clock,
        IPlatformUserRepository platformUsers,
        IPlatformPermissionResolver permissionResolver,
        IPlatformAuthEventRepository authEvents,
        IUnitOfWork uow)
    {
        _google = google;
        _options = options;
        _tokens = tokens;
        _clock = clock;
        _platformUsers = platformUsers;
        _permissionResolver = permissionResolver;
        _authEvents = authEvents;
        _uow = uow;
    }

    public async Task<Result<AdminLoginResultDto>> Handle(
        AdminGoogleLoginCommand request,
        CancellationToken ct)
    {
        var admin = _options.Value.Admin;
        if (string.IsNullOrWhiteSpace(admin.ClientId))
            return Result<AdminLoginResultDto>.Failure(
                "Google OAuth admin ClientId is not configured.", 500);

        var payload = await _google.ValidateAsync(request.GoogleIdToken, admin.ClientId, ct);
        if (payload is null || string.IsNullOrWhiteSpace(payload.Email))
        {
            await WriteAuthEventAsync(PlatformAuthEvent.LoginFailed, userId: null, request,
                new { reason = "invalid_google_token", method = "google" }, ct);
            return Result<AdminLoginResultDto>.Failure(
                "Invalid or expired Google ID token.", 401);
        }

        var email = payload.Email.Trim().ToLowerInvariant();

        var user = await _platformUsers.GetByGoogleSubAsync(payload.Subject, ct)
                   ?? await _platformUsers.GetByEmailAsync(email, ct);
        if (user is null)
        {
            await WriteAuthEventAsync(PlatformAuthEvent.LoginFailed, userId: null, request,
                new { reason = "platform_user_not_found", method = "google" }, ct);
            return Result<AdminLoginResultDto>.Forbidden(
                "This Google account is not authorized for the Developer Platform admin console.");
        }

        var profile = await _permissionResolver.ResolveActiveUserAsync(user.Id, ct);
        if (profile is null)
        {
            await WriteAuthEventAsync(PlatformAuthEvent.LoginFailed, user.Id, request,
                new { reason = "platform_user_not_active", method = "google" }, ct);
            return Result<AdminLoginResultDto>.Forbidden(
                "This platform user is not active.");
        }

        if (string.IsNullOrEmpty(user.GoogleSub))
            user.GoogleSub = payload.Subject;

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
