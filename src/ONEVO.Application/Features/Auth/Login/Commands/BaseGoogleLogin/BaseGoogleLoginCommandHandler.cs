using System.Text.Json;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.DTOs.Responses;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.ServiceInterfaces;

namespace ONEVO.Application.Features.Auth.Login.Commands.BaseGoogleLogin;

/// <summary>
/// Base-domain Google login. Identity is the Google subject ("sub"): users already linked to it are
/// found first; only when none are, users with the verified email who are not linked to another
/// Google subject are offered, and the link is saved when that login fully completes. Tenant policy
/// (Google allowed, allowed email domains) filters candidates. Failures are only ever
/// <see cref="GoogleLoginErrors"/>: 401 for Google, one 403 for every application reason.
/// No user is ever created here.
/// </summary>
public class BaseGoogleLoginCommandHandler : IRequestHandler<BaseGoogleLoginCommand, Result<BaseLoginResultDto>>
{
    private const string GoogleProvider = "google";
    private const string LegalChallengeOrigin = "google_sso";
    private const int MaximumEligibleCandidates = 8;
    private static readonly TimeSpan WorkspaceChallengeLifetime = TimeSpan.FromMinutes(5);

    private readonly IGoogleIdTokenValidator _google;
    private readonly IPlatformOAuthAppResolver _oauthApps;
    private readonly IGoogleLoginCandidateRepository _candidates;
    private readonly ILoginWorkspaceSelectionChallengeRepository _workspaceChallenges;
    private readonly ILoginContinuationService _continuation;
    private readonly IDateTimeProvider _clock;

    public BaseGoogleLoginCommandHandler(
        IGoogleIdTokenValidator google,
        IPlatformOAuthAppResolver oauthApps,
        IGoogleLoginCandidateRepository candidates,
        ILoginWorkspaceSelectionChallengeRepository workspaceChallenges,
        ILoginContinuationService continuation,
        IDateTimeProvider clock)
    {
        _google = google;
        _oauthApps = oauthApps;
        _candidates = candidates;
        _workspaceChallenges = workspaceChallenges;
        _continuation = continuation;
        _clock = clock;
    }

    public async Task<Result<BaseLoginResultDto>> Handle(BaseGoogleLoginCommand request, CancellationToken ct)
    {
        var googleApp = await _oauthApps.GetActiveAppForProviderAsync(GoogleProvider, ct);
        if (googleApp is null || string.IsNullOrWhiteSpace(googleApp.ClientId))
            return Result<BaseLoginResultDto>.Failure("Google sign-in is not configured.", 500);

        var payload = await _google.ValidateAsync(request.GoogleIdToken, googleApp.ClientId, ct);
        if (payload is null
            || string.IsNullOrWhiteSpace(payload.Subject)
            || string.IsNullOrWhiteSpace(payload.Email)
            || !payload.EmailVerified)
        {
            return GoogleAuthFailed();
        }

        var normalizedEmail = payload.Email.Trim().ToLowerInvariant();

        var rows = await _candidates.GetCandidatesAsync(payload.Subject, normalizedEmail, ct);

        // Overflow is judged on the procedure's own TOP (9) probe, before the C# domain filter, so a
        // filter cannot hide that more than eight workspaces matched.
        if (rows.Count > MaximumEligibleCandidates)
            return NoAccess();

        var emailDomain = ExtractDomain(normalizedEmail);
        var candidates = rows.Where(r => IsDomainAllowed(r.AllowedLoginDomainsJson, emailDomain)).ToList();
        if (candidates.Count == 0)
            return NoAccess();

        if (candidates.Count == 1)
        {
            var candidate = candidates[0];
            var continuationRequest = new LoginContinuationRequest(
                candidate.TenantId,
                candidate.UserId,
                SwitchTenantContext: true,
                GenericFailureMessage: GoogleLoginErrors.NoAccessMessage,
                LegalChallengeOrigin: LegalChallengeOrigin,
                IpAddress: request.IpAddress,
                UserAgent: request.UserAgent,
                FinalizationMode: LoginFinalizationMode.BaseDomainExchange)
            {
                ExternalIdentity = PendingIdentity(payload.Subject, normalizedEmail, candidate.MatchedBySubject)
            };

            var result = await _continuation.ContinueAsync(continuationRequest, ct);
            if (!result.IsSuccess)
            {
                // The continuation re-validates tenant and user and answers 401 when either is no
                // longer active. For Google that is "no access", never a credential error.
                return result.StatusCode is null or 401 or 403
                    ? NoAccess()
                    : Result<BaseLoginResultDto>.Failure(result.Error ?? GoogleLoginErrors.NoAccessMessage, result.StatusCode.Value);
            }

            return Result<BaseLoginResultDto>.Success(new BaseLoginResultDto(result.Value, null));
        }

        // 2-8 eligible candidates: the same durable workspace-selection challenge the password path
        // uses, carrying the pending Google link so the selected workspace can be linked on success.
        var snapshots = candidates
            .Select(c => new WorkspaceCandidateSnapshot(c.TenantId, c.UserId, c.Slug, c.DisplayName))
            .ToList();
        var matchedBySubject = candidates.All(c => c.MatchedBySubject);

        var rawChallenge = await _workspaceChallenges.CreateAsync(
            normalizedEmail,
            LegalChallengeOrigin,
            snapshots,
            PendingIdentity(payload.Subject, normalizedEmail, matchedBySubject),
            request.IpAddress,
            request.UserAgent,
            WorkspaceChallengeLifetime,
            ct);

        var workspaceOptions = candidates
            .Select(c => new BaseLoginWorkspaceOptionDto(c.Slug, c.DisplayName))
            .ToList();

        var workspaceSelectionDto = new BaseLoginWorkspaceSelectionRequiredDto(
            rawChallenge, workspaceOptions, _clock.UtcNow.Add(WorkspaceChallengeLifetime));

        return Result<BaseLoginResultDto>.Success(new BaseLoginResultDto(null, workspaceSelectionDto));
    }

    private static PendingExternalIdentity PendingIdentity(string subject, string email, bool matchedBySubject) =>
        new(GoogleProvider, subject, email, matchedBySubject);

    /// <summary>
    /// Same rule as invitation acceptance: the email's domain must equal one listed domain
    /// (case-insensitive). No list or an empty list means any domain. A list that cannot be read
    /// fails closed: a corrupted restriction must not turn into "any domain".
    /// </summary>
    private static bool IsDomainAllowed(string? allowedDomainsJson, string? emailDomain)
    {
        if (string.IsNullOrWhiteSpace(allowedDomainsJson))
            return true;

        List<string>? domains;
        try
        {
            domains = JsonSerializer.Deserialize<List<string>>(allowedDomainsJson);
        }
        catch (JsonException)
        {
            return false;
        }

        var allowed = domains?
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .Select(d => d.Trim().ToLowerInvariant())
            .ToHashSet() ?? [];

        return allowed.Count == 0 || (emailDomain is not null && allowed.Contains(emailDomain));
    }

    private static string? ExtractDomain(string normalizedEmail)
    {
        var at = normalizedEmail.LastIndexOf('@');
        return at < 0 || at == normalizedEmail.Length - 1 ? null : normalizedEmail[(at + 1)..];
    }

    private static Result<BaseLoginResultDto> GoogleAuthFailed() =>
        Result<BaseLoginResultDto>.Failure(
            GoogleLoginErrors.GoogleAuthFailedMessage, 401, GoogleLoginErrors.GoogleAuthFailedCode);

    private static Result<BaseLoginResultDto> NoAccess() =>
        Result<BaseLoginResultDto>.Failure(GoogleLoginErrors.NoAccessMessage, 403, GoogleLoginErrors.NoAccessCode);
}
