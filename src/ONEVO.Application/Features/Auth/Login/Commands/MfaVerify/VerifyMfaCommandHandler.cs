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

namespace ONEVO.Application.Features.Auth.Login.Commands.MfaVerify;

public class VerifyMfaCommandHandler : IRequestHandler<VerifyMfaCommand, Result<LoginResponseDto>>
{
    private const int MaximumFailedAttempts = 5;

    private readonly IUserRepository _users;
    private readonly IUserMfaRepository _userMfas;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMfaChallengeStore _mfaChallenges;
    private readonly IEncryptionService _encryption;
    private readonly ITotpService _totpService;
    private readonly ILoginSessionMaterialFactory _sessionMaterialFactory;
    private readonly IDateTimeProvider _clock;
    private readonly ITenantContext _tenantContext;

    public VerifyMfaCommandHandler(
        IUserRepository users,
        IUserMfaRepository userMfas,
        IUnitOfWork unitOfWork,
        IMfaChallengeStore mfaChallenges,
        IEncryptionService encryption,
        ITotpService totpService,
        ILoginSessionMaterialFactory sessionMaterialFactory,
        IDateTimeProvider clock,
        ITenantContext tenantContext)
    {
        _users = users;
        _userMfas = userMfas;
        _unitOfWork = unitOfWork;
        _mfaChallenges = mfaChallenges;
        _encryption = encryption;
        _totpService = totpService;
        _sessionMaterialFactory = sessionMaterialFactory;
        _clock = clock;
        _tenantContext = tenantContext;
    }

    public async Task<Result<LoginResponseDto>> Handle(VerifyMfaCommand request, CancellationToken cancellationToken)
    {
        if (!_tenantContext.IsResolved || _tenantContext.ContextMode != TenantContextMode.Tenant)
            return Result<LoginResponseDto>.Failure("Invalid or expired MFA session.", 401);

        var challengeState = await _mfaChallenges.GetAsync(request.MfaChallenge, cancellationToken);
        if (challengeState is null || challengeState.TenantId != _tenantContext.TenantId)
            return Result<LoginResponseDto>.Failure("Invalid or expired MFA session.", 401);

        var user = await _users.GetByIdAsync(challengeState.UserId, cancellationToken);
        if (user is null || !user.IsActive)
            return Result<LoginResponseDto>.Failure("User not found.", 401);

        if (user.TenantId != _tenantContext.TenantId)
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
            || consumedChallenge.TenantId != _tenantContext.TenantId)
        {
            return Result<LoginResponseDto>.Failure("Invalid or expired MFA session.", 401);
        }

        if (!mfaRecord.IsVerified)
        {
            mfaRecord.IsVerified = true;
        }

        user.LastLoginAt = _clock.UtcNow;
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return await _sessionMaterialFactory.PrepareAsync(user, request.IpAddress, request.UserAgent, cancellationToken);
    }
}
