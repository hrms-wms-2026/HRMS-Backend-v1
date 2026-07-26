using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Legal.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Legal.Services;
using ONEVO.Application.Features.Auth.Login.DTOs.Responses;
using ONEVO.Application.Features.Auth.Login.Mappers;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Domain.Features.InfrastructureModule.Entities;

namespace ONEVO.Application.Features.Auth.Login.Services;

public sealed class LoginContinuationService : ILoginContinuationService
{
    private static readonly TimeSpan MfaChallengeLifetime = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan LegalChallengeLifetime = TimeSpan.FromMinutes(10);

    private readonly ITenantRepository _tenants;
    private readonly IUserRepository _users;
    private readonly IUserMfaRepository _userMfas;
    private readonly IMfaChallengeStore _mfaChallenges;
    private readonly ITenantContextSwitcher _tenantSwitcher;
    private readonly ILegalAcceptanceChecker _legalChecker;
    private readonly ILegalLoginChallengeRepository _legalChallenges;
    private readonly ILoginSessionMaterialFactory _sessionMaterialFactory;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    public LoginContinuationService(
        ITenantRepository tenants,
        IUserRepository users,
        IUserMfaRepository userMfas,
        IMfaChallengeStore mfaChallenges,
        ITenantContextSwitcher tenantSwitcher,
        ILegalAcceptanceChecker legalChecker,
        ILegalLoginChallengeRepository legalChallenges,
        ILoginSessionMaterialFactory sessionMaterialFactory,
        IUnitOfWork unitOfWork,
        IDateTimeProvider clock)
    {
        _tenants = tenants;
        _users = users;
        _userMfas = userMfas;
        _mfaChallenges = mfaChallenges;
        _tenantSwitcher = tenantSwitcher;
        _legalChecker = legalChecker;
        _legalChallenges = legalChallenges;
        _sessionMaterialFactory = sessionMaterialFactory;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<LoginResponseDto>> ContinueAsync(
        LoginContinuationRequest request,
        CancellationToken ct = default)
    {
        var tenant = await _tenants.GetByIdAsync(request.TenantId, ct);
        if (tenant is null || tenant.Status is not (TenantStatus.Active or TenantStatus.Trial))
            return Result<LoginResponseDto>.Failure(request.GenericFailureMessage, 401);

        if (request.SwitchTenantContext)
        {
            await _tenantSwitcher.SwitchToTenantAsync(
                new TenantRegistryEntry(tenant.Id, tenant.Slug, tenant.Status, null), ct);
        }

        var user = await _users.GetByIdAsync(request.UserId, ct);
        if (user is null || !user.IsActive)
            return Result<LoginResponseDto>.Failure(request.GenericFailureMessage, 401);

        if (user.MustChangePassword)
        {
            if (user.PasswordSetByAdmin && user.TemporaryPasswordExpiresAt.HasValue
                && user.TemporaryPasswordExpiresAt <= _clock.UtcNow)
            {
                return Result<LoginResponseDto>.Failure(
                    "Temporary password has expired. Request a new one from your administrator.");
            }

            return Result<LoginResponseDto>.Success(LoginMapper.ToPasswordChangeRequired(user));
        }

        var mfaRecord = await _userMfas.GetTotpAsync(user.Id, isVerified: true, ct);
        if (mfaRecord is not null)
        {
            var mfaChallenge = await _mfaChallenges.CreateAsync(
                user.Id,
                user.TenantId,
                request.LegalChallengeOrigin,
                MfaChallengeLifetime,
                ct);
            return Result<LoginResponseDto>.Success(LoginMapper.ToMfaRequired(user, mfaChallenge));
        }

        return await FinishAuthenticatedLoginAsync(
            user, request.LegalChallengeOrigin, request.IpAddress, request.UserAgent, ct);
    }

    public async Task<Result<LoginResponseDto>> FinishAuthenticatedLoginAsync(
        User user,
        string legalChallengeOrigin,
        string? ipAddress,
        string? userAgent,
        CancellationToken ct = default)
    {
        var legalCheck = await _legalChecker.CheckAsync(user.TenantId, user.Id, ct);
        if (legalCheck.Status == LegalAcceptanceStatus.Pending)
        {
            var (rawChallenge, rawCsrf) = await _legalChallenges.CreateAsync(
                user.TenantId, user.Id, legalChallengeOrigin, LegalChallengeLifetime, ct);

            return Result<LoginResponseDto>.Success(
                LoginMapper.ToLegalAcceptanceRequired(user, rawChallenge, rawCsrf, legalCheck.PendingDocuments));
        }

        var sessionResult = await _sessionMaterialFactory.PrepareAsync(user, ipAddress, userAgent, ct);
        if (!sessionResult.IsSuccess)
            return sessionResult;

        user.LastLoginAt = _clock.UtcNow;
        await _unitOfWork.SaveChangesAsync(ct);

        return sessionResult;
    }
}
