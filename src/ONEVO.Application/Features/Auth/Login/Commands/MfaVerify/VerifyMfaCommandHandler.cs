using MediatR;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Application.Common.Models;

using ONEVO.Application.Features.Auth.Invite.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Permission.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Roles.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Domain.Features.InfrastructureModule.Entities;

namespace ONEVO.Application.Features.Auth.Login.Commands.MfaVerify;

public class VerifyMfaCommandHandler : IRequestHandler<VerifyMfaCommand, Result<LoginResponseDto>>
{
    private const int MaximumFailedAttempts = 5;

    private readonly IUserRepository _users;
    private readonly IUserMfaRepository _userMfas;
    private readonly IMfaChallengeStore _mfaChallenges;
    private readonly IEncryptionService _encryption;
    private readonly ITotpService _totpService;
    private readonly ILoginContinuationService _continuation;
    private readonly ITenantContext _tenantContext;
    private readonly ITenantRepository _tenants;
    private readonly ITenantContextSwitcher _tenantSwitcher;

    public VerifyMfaCommandHandler(
        IUserRepository users,
        IUserMfaRepository userMfas,
        IMfaChallengeStore mfaChallenges,
        IEncryptionService encryption,
        ITotpService totpService,
        ILoginContinuationService continuation,
        ITenantContext tenantContext,
        ITenantRepository tenants,
        ITenantContextSwitcher tenantSwitcher)
    {
        _users = users;
        _userMfas = userMfas;
        _mfaChallenges = mfaChallenges;
        _encryption = encryption;
        _totpService = totpService;
        _continuation = continuation;
        _tenantContext = tenantContext;
        _tenants = tenants;
        _tenantSwitcher = tenantSwitcher;
    }

    public async Task<Result<LoginResponseDto>> Handle(VerifyMfaCommand request, CancellationToken cancellationToken)
    {
        var isTenantContext = _tenantContext.IsResolved
            && _tenantContext.ContextMode == TenantContextMode.Tenant;
        var challengeState = isTenantContext
            ? await _mfaChallenges.GetAsync(request.MfaChallenge, cancellationToken)
            : await _mfaChallenges.GetForPreTenantContinuationAsync(
                request.MfaChallenge,
                cancellationToken);
        if (challengeState is null)
            return Result<LoginResponseDto>.Failure("Invalid or expired MFA session.", 401);

        if (isTenantContext && challengeState.TenantId != _tenantContext.TenantId)
            return Result<LoginResponseDto>.Failure("Invalid or expired MFA session.", 401);

        var tenant = await _tenants.GetByIdAsync(challengeState.TenantId, cancellationToken);
        if (tenant is null || tenant.Status is not (TenantStatus.Active or TenantStatus.Trial))
            return Result<LoginResponseDto>.Failure("Invalid or expired MFA session.", 401);

        if (!isTenantContext)
        {
            await _tenantSwitcher.SwitchToTenantAsync(
                new TenantRegistryEntry(tenant.Id, tenant.Slug, tenant.Status, null),
                cancellationToken);
        }

        var user = await _users.GetByIdAsync(challengeState.UserId, cancellationToken);
        if (user is null || !user.IsActive)
            return Result<LoginResponseDto>.Failure("User not found.", 401);

        if (user.TenantId != challengeState.TenantId)
            return Result<LoginResponseDto>.Failure("Invalid or expired MFA session.", 401);

        var mfaRecord = await _userMfas.GetTotpAsync(user.Id, isVerified: true, cancellationToken);

        if (mfaRecord is null)
        {
            // First-time verification (not yet marked verified)
            mfaRecord = await _userMfas.GetTotpAsync(user.Id, isVerified: false, cancellationToken);
            if (mfaRecord is null)
                return Result<LoginResponseDto>.Failure("MFA is not configured.", 400);
        }

        var secret = _encryption.Decrypt(mfaRecord.Secret);
        if (!_totpService.Verify(secret, request.Code))
        {
            await _mfaChallenges.RegisterFailedAttemptAsync(
                request.MfaChallenge,
                MaximumFailedAttempts,
                cancellationToken);
            return Result<LoginResponseDto>.Failure("Invalid MFA code.", 401);
        }

        var consumedChallenge = await _mfaChallenges.TryConsumeAsync(
            request.MfaChallenge,
            cancellationToken);
        if (consumedChallenge is null
            || consumedChallenge.UserId != user.Id
            || consumedChallenge.TenantId != challengeState.TenantId)
        {
            return Result<LoginResponseDto>.Failure("Invalid or expired MFA session.", 401);
        }

        if (!mfaRecord.IsVerified)
        {
            mfaRecord.IsVerified = true;
        }

        return await _continuation.FinishAuthenticatedLoginAsync(
            user, consumedChallenge.Origin, request.IpAddress, request.UserAgent, cancellationToken);
    }
}
