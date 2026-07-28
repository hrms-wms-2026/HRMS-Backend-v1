using System.Text.Json;
using MediatR;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Legal.Services;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.Security;

using ONEVO.Application.Features.Auth.Invite.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Permission.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Roles.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.ServiceInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Application.Features.Auth.Login.DTOs.Responses;

namespace ONEVO.Application.Features.Auth.Invite.Commands.AcceptInvitationGoogle;

public sealed class AcceptInvitationGoogleCommandHandler
    : IRequestHandler<AcceptInvitationGoogleCommand, Result<LoginResponseDto>>
{
    private const string GoogleProvider = "google";

    private readonly IGoogleIdTokenValidator _google;
    private readonly IPlatformOAuthAppResolver _oauthApps;
    private readonly IInvitationTokenRepository _invitations;
    private readonly ITenantAuthPolicyRepository _policies;
    private readonly IUserRepository _users;
    private readonly IUserExternalIdentityRepository _externalIdentities;
    private readonly ILegalAcceptanceSubmissionService _legalSubmission;
    private readonly ILoginContinuationService _continuation;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;
    private readonly ITenantContext _tenantContext;
    private readonly IGlobalEmailDirectoryRepository _globalDirectory;
    private readonly IUserRoleRepository _userRoles;
    private readonly IPositionRepository _positions;

    public AcceptInvitationGoogleCommandHandler(
        IGoogleIdTokenValidator google,
        IPlatformOAuthAppResolver oauthApps,
        IInvitationTokenRepository invitations,
        ITenantAuthPolicyRepository policies,
        IUserRepository users,
        IUserExternalIdentityRepository externalIdentities,
        ILegalAcceptanceSubmissionService legalSubmission,
        ILoginContinuationService continuation,
        IUnitOfWork unitOfWork,
        IDateTimeProvider clock,
        ITenantContext tenantContext,
        IGlobalEmailDirectoryRepository globalDirectory,
        IUserRoleRepository userRoles,
        IPositionRepository positions)
    {
        _google = google;
        _oauthApps = oauthApps;
        _invitations = invitations;
        _policies = policies;
        _users = users;
        _externalIdentities = externalIdentities;
        _legalSubmission = legalSubmission;
        _continuation = continuation;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _tenantContext = tenantContext;
        _globalDirectory = globalDirectory;
        _userRoles = userRoles;
        _positions = positions;
    }

    public async Task<Result<LoginResponseDto>> Handle(
        AcceptInvitationGoogleCommand request,
        CancellationToken ct)
    {
        if (!_tenantContext.IsResolved || _tenantContext.ContextMode != TenantContextMode.Tenant)
            return Result<LoginResponseDto>.NotFound("Invitation not found.");

        var googleApp = await _oauthApps.GetActiveAppForProviderAsync(
            GoogleProvider,
            ct);
        if (googleApp is null || string.IsNullOrWhiteSpace(googleApp.ClientId))
            return Result<LoginResponseDto>.Failure(
                "Google OAuth app is not configured.", 500);

        var payload = await _google.ValidateAsync(
            request.GoogleIdToken,
            googleApp.ClientId,
            ct);
        if (payload is null || string.IsNullOrWhiteSpace(payload.Email))
            return Result<LoginResponseDto>.Failure("Invalid or expired Google ID token.", 401);

        if (!payload.EmailVerified)
            return Result<LoginResponseDto>.Failure(
                "Google email must be verified to complete this invitation.", 403);

        var hash = InvitationTokenHasher.Hash(request.RawToken);
        var inv = await _invitations.GetByTokenHashAsync(hash, ct);
        if (inv is null)
            return Result<LoginResponseDto>.NotFound("Invitation not found.");

        if (inv.TenantId != _tenantContext.TenantId)
            return Result<LoginResponseDto>.NotFound("Invitation not found.");

        var now = _clock.UtcNow;
        var check = CheckInvitationUsable(inv, now);
        if (check is not null)
            return Result<LoginResponseDto>.Failure(check, 400);

        var policy = await _policies.GetByTenantIdAsync(inv.TenantId, ct);

        var invitedNorm = NormalizeEmail(inv.InvitedEmail);
        var googleNorm = NormalizeEmail(payload.Email);
        if (!string.Equals(invitedNorm, googleNorm, StringComparison.OrdinalIgnoreCase))
        {
            var allowMismatch = inv.AllowGoogleEmailMismatch ?? policy?.GoogleEmailMismatchDefault ?? false;
            if (!allowMismatch)
                return Result<LoginResponseDto>.Forbidden(
                    "Google account email must match the invited email.");

            var domains = MergeDomains(inv.AllowedEmailDomainsJson, policy?.AllowedLoginDomainsJson);
            if (domains.Count == 0)
                return Result<LoginResponseDto>.Failure(
                    "Google email mismatch is allowed but no permitted domains are configured.", 403);

            var domain = ExtractDomain(googleNorm);
            if (domain is null || !domains.Contains(domain))
                return Result<LoginResponseDto>.Forbidden(
                    "Google account email domain is not allowed for this invitation.");
        }

        var user = await _users.GetByIdAsync(inv.UserId, ct);
        if (user is null)
            return Result<LoginResponseDto>.Failure("Invited user record not found.", 500);

        var legal = await _legalSubmission.ValidateAndStageAsync(
            inv.TenantId,
            user.Id,
            request.Acceptances,
            requireComplete: true,
            request.IpAddress,
            request.UserAgent,
            ct);
        if (!legal.IsSuccess)
            return Result<LoginResponseDto>.Failure(legal.Error!, legal.StatusCode ?? 400);

        var existingBySubject = await _externalIdentities.GetByTenantProviderAndSubjectAsync(
            inv.TenantId,
            GoogleProvider,
            payload.Subject,
            ct);

        if (existingBySubject is not null && existingBySubject.UserId != inv.UserId)
            return Result<LoginResponseDto>.Conflict(
                "This Google account is already linked to another user in this organization.");

        if (existingBySubject is not null)
        {
            existingBySubject.ProviderEmail = googleNorm;
            existingBySubject.EmailVerified = payload.EmailVerified;
            existingBySubject.LastUsedAt = now;
        }
        else
        {
            await _externalIdentities.AddAsync(
                new UserExternalIdentity
                {
                    Id = Guid.NewGuid(),
                    TenantId = inv.TenantId,
                    UserId = inv.UserId,
                    Provider = GoogleProvider,
                    ProviderSubject = payload.Subject,
                    ProviderEmail = googleNorm,
                    EmailVerified = payload.EmailVerified,
                    LinkedAt = now,
                    LastUsedAt = now
                },
                ct);
        }

        user.IsActive = true;
        user.MustChangePassword = false;
        user.PasswordSetByAdmin = false;
        user.TemporaryPasswordExpiresAt = null;
        user.UpdatedAt = now;

        inv.UsedAt = now;
        inv.CompletedWith = "google";

        if (inv.PositionId.HasValue)
        {
            var position = await _positions.GetByIdAsync(inv.PositionId.Value, ct);
            if (position?.DefaultRoleId.HasValue == true)
                await _userRoles.AddAsync(new UserRole
                {
                    TenantId = inv.TenantId,
                    UserId = user.Id,
                    RoleId = position.DefaultRoleId.Value,
                    AssignedAt = now,
                    AssignedBy = user.Id
                }, ct);
        }

        await _globalDirectory.UpsertAsync(inv.InvitedEmail, inv.TenantId, ct);

        await _unitOfWork.SaveChangesAsync(ct);

        return await _continuation.FinishAuthenticatedLoginAsync(
            user, "invitation_google", request.IpAddress, request.UserAgent, ct);
    }

    private static string? CheckInvitationUsable(InvitationToken inv, DateTimeOffset now)
    {
        if (inv.UsedAt is not null) return "This invitation has already been accepted.";
        if (inv.RevokedAt is not null) return "This invitation has been revoked.";
        if (inv.ExpiresAt <= now) return "This invitation has expired.";
        return null;
    }

    private static HashSet<string> MergeDomains(string? invitationJson, string? policyJson)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var j in new[] { invitationJson, policyJson })
        {
            if (string.IsNullOrWhiteSpace(j)) continue;
            try
            {
                var list = JsonSerializer.Deserialize<List<string>>(j);
                if (list is null) continue;
                foreach (var d in list)
                {
                    if (!string.IsNullOrWhiteSpace(d))
                        set.Add(d.Trim().ToLowerInvariant());
                }
            }
            catch
            {
                // ignore malformed JSON
            }
        }

        return set;
    }

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    private static string? ExtractDomain(string normalizedEmail)
    {
        var at = normalizedEmail.LastIndexOf('@');
        return at < 0 || at == normalizedEmail.Length - 1
            ? null
            : normalizedEmail[(at + 1)..];
    }
}
