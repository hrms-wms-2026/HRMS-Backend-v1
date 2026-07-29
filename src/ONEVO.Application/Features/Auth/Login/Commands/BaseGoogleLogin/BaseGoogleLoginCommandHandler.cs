using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.DTOs.Responses;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.ServiceInterfaces;

namespace ONEVO.Application.Features.Auth.Login.Commands.BaseGoogleLogin;

public class BaseGoogleLoginCommandHandler : IRequestHandler<BaseGoogleLoginCommand, Result<BaseLoginResultDto>>
{
    private const string GoogleProvider = "google";
    private const string GenericFailureMessage = "Invalid email or password.";
    private const string LegalChallengeOrigin = "google_sso";
    private const int MaximumEligibleCandidates = 8;
    private static readonly TimeSpan WorkspaceChallengeLifetime = TimeSpan.FromMinutes(5);

    private readonly IGoogleIdTokenValidator _google;
    private readonly IPlatformOAuthAppResolver _oauthApps;
    private readonly IBaseLoginCandidateRepository _candidates;
    private readonly ILoginWorkspaceSelectionChallengeRepository _workspaceChallenges;
    private readonly ILoginContinuationService _continuation;
    private readonly IDateTimeProvider _clock;

    public BaseGoogleLoginCommandHandler(
        IGoogleIdTokenValidator google,
        IPlatformOAuthAppResolver oauthApps,
        IBaseLoginCandidateRepository candidates,
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
        if (payload is null || string.IsNullOrWhiteSpace(payload.Email) || !payload.EmailVerified)
            return Result<BaseLoginResultDto>.Failure(GenericFailureMessage, 401);

        var normalizedEmail = payload.Email.Trim().ToLowerInvariant();

        // Same allowlisted pre-tenant candidate lookup the password path uses (eligibility:
        // tenant Active/Trial + user IsActive only - no tenant_auth_policies gate). The
        // password_hash column is present on the row but never read here: Google already proved
        // identity, so no credential is verified for these candidates.
        var candidateRows = await _candidates.GetCandidatesAsync(normalizedEmail, ct);

        if (candidateRows.Count == 0 || candidateRows.Count > MaximumEligibleCandidates)
            return Result<BaseLoginResultDto>.Failure(GenericFailureMessage, 401);

        if (candidateRows.Count == 1)
        {
            var candidate = candidateRows[0];
            var continuationRequest = new LoginContinuationRequest(
                candidate.TenantId,
                candidate.UserId,
                SwitchTenantContext: true,
                GenericFailureMessage: GenericFailureMessage,
                LegalChallengeOrigin: LegalChallengeOrigin,
                IpAddress: request.IpAddress,
                UserAgent: request.UserAgent,
                FinalizationMode: LoginFinalizationMode.BaseDomainExchange);

            var result = await _continuation.ContinueAsync(continuationRequest, ct);
            if (!result.IsSuccess)
                return Result<BaseLoginResultDto>.Failure(result.Error ?? GenericFailureMessage, result.StatusCode ?? 401);

            return Result<BaseLoginResultDto>.Success(new BaseLoginResultDto(result.Value, null));
        }

        // 2-8 eligible candidates: reuse the same durable workspace-selection challenge the
        // password path uses. By the time a workspace is selected, identity is already proven
        // (Google here, password there); SelectWorkspaceCommandHandler does not re-verify either.
        var snapshots = candidateRows
            .Select(c => new WorkspaceCandidateSnapshot(c.TenantId, c.UserId, c.Slug, c.DisplayName))
            .ToList();

        var rawChallenge = await _workspaceChallenges.CreateAsync(
            normalizedEmail,
            LegalChallengeOrigin,
            snapshots,
            request.IpAddress,
            request.UserAgent,
            WorkspaceChallengeLifetime,
            ct);

        var workspaceOptions = candidateRows
            .Select(c => new BaseLoginWorkspaceOptionDto(c.Slug, c.DisplayName))
            .ToList();

        var workspaceSelectionDto = new BaseLoginWorkspaceSelectionRequiredDto(
            rawChallenge, workspaceOptions, _clock.UtcNow.Add(WorkspaceChallengeLifetime));

        return Result<BaseLoginResultDto>.Success(new BaseLoginResultDto(null, workspaceSelectionDto));
    }
}
