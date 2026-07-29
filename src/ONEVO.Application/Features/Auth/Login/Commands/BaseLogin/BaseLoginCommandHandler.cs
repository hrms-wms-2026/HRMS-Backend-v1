using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.DTOs.Responses;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;

namespace ONEVO.Application.Features.Auth.Login.Commands.BaseLogin;

public class BaseLoginCommandHandler : IRequestHandler<BaseLoginCommand, Result<BaseLoginResultDto>>
{
    private static readonly TimeSpan WorkspaceChallengeLifetime = TimeSpan.FromMinutes(5);
    private const string GenericCredentialFailureMessage = "Invalid email or password.";
    private const string LegalChallengeOrigin = "password";

    private readonly IBaseLoginCandidateRepository _candidates;
    private readonly IBaseLoginFixedWorkVerifier _verifier;
    private readonly ILoginWorkspaceSelectionChallengeRepository _workspaceChallenges;
    private readonly ILoginContinuationService _continuation;
    private readonly IDateTimeProvider _clock;

    public BaseLoginCommandHandler(
        IBaseLoginCandidateRepository candidates,
        IBaseLoginFixedWorkVerifier verifier,
        ILoginWorkspaceSelectionChallengeRepository workspaceChallenges,
        ILoginContinuationService continuation,
        IDateTimeProvider clock)
    {
        _candidates = candidates;
        _verifier = verifier;
        _workspaceChallenges = workspaceChallenges;
        _continuation = continuation;
        _clock = clock;
    }

    public async Task<Result<BaseLoginResultDto>> Handle(BaseLoginCommand request, CancellationToken cancellationToken)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        var candidateRows = await _candidates.GetCandidatesAsync(normalizedEmail, cancellationToken);
        var verification = await _verifier.VerifyAsync(candidateRows, request.Password, cancellationToken);

        if (verification.IsOverflow)
            return Result<BaseLoginResultDto>.Failure(GenericCredentialFailureMessage, 401);

        if (verification.MatchedCandidates.Count == 0)
            return Result<BaseLoginResultDto>.Failure(GenericCredentialFailureMessage, 401);

        if (verification.MatchedCandidates.Count == 1)
        {
            return await CompleteSingleCandidateLoginAsync(
                verification.MatchedCandidates[0], request.IpAddress, request.UserAgent, cancellationToken);
        }

        return await CreateWorkspaceSelectionChallengeAsync(
            normalizedEmail, verification.MatchedCandidates, request.IpAddress, request.UserAgent, cancellationToken);
    }

    private async Task<Result<BaseLoginResultDto>> CompleteSingleCandidateLoginAsync(
        BaseLoginCandidateRow candidate,
        string? ipAddress,
        string? userAgent,
        CancellationToken ct)
    {
        var continuationRequest = new LoginContinuationRequest(
            candidate.TenantId,
            candidate.UserId,
            SwitchTenantContext: true,
            GenericFailureMessage: GenericCredentialFailureMessage,
            LegalChallengeOrigin: LegalChallengeOrigin,
            IpAddress: ipAddress,
            UserAgent: userAgent,
            FinalizationMode: LoginFinalizationMode.BaseDomainExchange);

        var result = await _continuation.ContinueAsync(continuationRequest, ct);
        if (!result.IsSuccess)
            return Result<BaseLoginResultDto>.Failure(result.Error ?? GenericCredentialFailureMessage, result.StatusCode ?? 401);

        return Result<BaseLoginResultDto>.Success(new BaseLoginResultDto(result.Value, null));
    }

    private async Task<Result<BaseLoginResultDto>> CreateWorkspaceSelectionChallengeAsync(
        string normalizedEmail,
        IReadOnlyList<BaseLoginCandidateRow> matchedCandidates,
        string? ipAddress,
        string? userAgent,
        CancellationToken ct)
    {
        var snapshots = matchedCandidates
            .Select(c => new WorkspaceCandidateSnapshot(c.TenantId, c.UserId, c.Slug, c.DisplayName))
            .ToList();

        var rawChallenge = await _workspaceChallenges.CreateAsync(
            normalizedEmail, LegalChallengeOrigin, snapshots, ipAddress, userAgent, WorkspaceChallengeLifetime, ct);

        var workspaceOptions = matchedCandidates
            .Select(c => new BaseLoginWorkspaceOptionDto(c.Slug, c.DisplayName))
            .ToList();

        var workspaceSelectionDto = new BaseLoginWorkspaceSelectionRequiredDto(
            rawChallenge, workspaceOptions, _clock.UtcNow.Add(WorkspaceChallengeLifetime));

        return Result<BaseLoginResultDto>.Success(new BaseLoginResultDto(null, workspaceSelectionDto));
    }
}
