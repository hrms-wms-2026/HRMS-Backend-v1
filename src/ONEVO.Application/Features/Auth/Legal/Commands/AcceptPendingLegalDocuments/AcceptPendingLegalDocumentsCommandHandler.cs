using System.Security.Cryptography;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Legal.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Legal.Services;
using ONEVO.Application.Features.Auth.Login.DTOs.Responses;
using ONEVO.Application.Features.Auth.Login.Mappers;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Compliance.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;

namespace ONEVO.Application.Features.Auth.Legal.Commands.AcceptPendingLegalDocuments;

public class AcceptPendingLegalDocumentsCommandHandler
    : IRequestHandler<AcceptPendingLegalDocumentsCommand, Result<LoginResponseDto>>
{
    private const string GenericFailureMessage = "Legal acceptance session is invalid or expired. Please sign in again.";
    private static readonly TimeSpan LegalChallengeLifetime = TimeSpan.FromMinutes(10);

    private readonly ILegalLoginChallengeRepository _legalChallenges;
    private readonly ILegalAcceptanceSubmissionService _submission;
    private readonly ILegalAcceptanceChecker _legalChecker;
    private readonly ITenantRepository _tenants;
    private readonly IUserRepository _users;
    private readonly ITenantContextSwitcher _tenantSwitcher;
    private readonly ILoginContinuationService _continuation;
    private readonly ISecureTokenGenerator _tokens;
    private readonly IDateTimeProvider _clock;
    private readonly ITenantContext _tenantContext;

    public AcceptPendingLegalDocumentsCommandHandler(
        ILegalLoginChallengeRepository legalChallenges,
        ILegalAcceptanceSubmissionService submission,
        ILegalAcceptanceChecker legalChecker,
        ITenantRepository tenants,
        IUserRepository users,
        ITenantContextSwitcher tenantSwitcher,
        ILoginContinuationService continuation,
        ISecureTokenGenerator tokens,
        IDateTimeProvider clock,
        ITenantContext tenantContext)
    {
        _legalChallenges = legalChallenges;
        _submission = submission;
        _legalChecker = legalChecker;
        _tenants = tenants;
        _users = users;
        _tenantSwitcher = tenantSwitcher;
        _continuation = continuation;
        _tokens = tokens;
        _clock = clock;
        _tenantContext = tenantContext;
    }

    public async Task<Result<LoginResponseDto>> Handle(
        AcceptPendingLegalDocumentsCommand request,
        CancellationToken ct)
    {
        var isTenantContext = _tenantContext.IsResolved
            && _tenantContext.ContextMode == TenantContextMode.Tenant;
        var challenge = isTenantContext
            ? await _legalChallenges.GetActiveAsync(request.LegalChallenge, ct)
            : await _legalChallenges.GetActiveForPreTenantContinuationAsync(
                request.LegalChallenge,
                ct);
        if (challenge is null)
            return Result<LoginResponseDto>.Failure(GenericFailureMessage, 401);

        if (isTenantContext && challenge.TenantId != _tenantContext.TenantId)
            return Result<LoginResponseDto>.Failure(GenericFailureMessage, 401);

        if (!FixedTimeHashEquals(_tokens.HashToken(request.LegalCsrfToken), challenge.CsrfTokenHash))
            return Result<LoginResponseDto>.Failure(GenericFailureMessage, 401);

        // Revalidate tenant/user at consumption time, not just at challenge-creation time.
        var tenant = await _tenants.GetByIdAsync(challenge.TenantId, ct);
        if (tenant is null || tenant.Status is not (TenantStatus.Active or TenantStatus.Trial))
            return Result<LoginResponseDto>.Failure(GenericFailureMessage, 401);

        await _tenantSwitcher.SwitchToTenantAsync(
            new TenantRegistryEntry(tenant.Id, tenant.Slug, tenant.Status, null), ct);

        var user = await _users.GetByIdAsync(challenge.UserId, ct);
        if (user is null || !user.IsActive)
            return Result<LoginResponseDto>.Failure(GenericFailureMessage, 401);

        var legalCheck = await _legalChecker.CheckAsync(tenant.Id, user.Id, ct);
        var validation = await _submission.ValidateAndStageAsync(
            tenant.Id,
            user.Id,
            request.Acceptances,
            requireComplete: false,
            request.IpAddress,
            request.UserAgent,
            ct);
        if (!validation.IsSuccess)
            return Result<LoginResponseDto>.Failure(validation.Error!, validation.StatusCode ?? 400);

        var isComplete = legalCheck.PendingDocuments.All(
            pending => request.Acceptances.Any(
                submitted =>
                    string.Equals(
                        submitted.DocumentType,
                        pending.DocumentType,
                        StringComparison.OrdinalIgnoreCase)
                    && string.Equals(
                        submitted.Version,
                        pending.Version,
                        StringComparison.OrdinalIgnoreCase)));

        var commit = await _legalChallenges.TryCommitAcceptancesAsync(
            request.LegalChallenge,
            challenge,
            isComplete,
            LegalChallengeLifetime,
            ct);
        if (!commit.Succeeded)
            return Result<LoginResponseDto>.Failure(GenericFailureMessage, 401);

        if (!isComplete)
        {
            var remaining = await _legalChecker.CheckAsync(tenant.Id, user.Id, ct);

            return Result<LoginResponseDto>.Success(
                LoginMapper.ToLegalAcceptanceRequired(
                    user,
                    tenant,
                    commit.ReplacementRawChallenge!,
                    commit.ReplacementRawCsrfToken!,
                    remaining.PendingDocuments));
        }

        return await _continuation.FinishAuthenticatedLoginAsync(
            user,
            challenge.Origin,
            request.IpAddress,
            request.UserAgent,
            isTenantContext ? LoginFinalizationMode.TenantHostDirect : LoginFinalizationMode.BaseDomainExchange,
            ct,
            tenant);
    }

    private static bool FixedTimeHashEquals(string a, string b)
    {
        try
        {
            return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(a), Convert.FromHexString(b));
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
