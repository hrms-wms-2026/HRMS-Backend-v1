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
using ONEVO.Domain.Features.Auth.Entities;
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
    private readonly ITenantSessionExchangeService _tenantSessionExchange;
    private readonly ILoginSessionMaterialFactory _sessionMaterialFactory;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;
    private readonly IUserExternalIdentityRepository _externalIdentities;

    public LoginContinuationService(
        ITenantRepository tenants,
        IUserRepository users,
        IUserMfaRepository userMfas,
        IMfaChallengeStore mfaChallenges,
        ITenantContextSwitcher tenantSwitcher,
        ILegalAcceptanceChecker legalChecker,
        ILegalLoginChallengeRepository legalChallenges,
        ITenantSessionExchangeService tenantSessionExchange,
        ILoginSessionMaterialFactory sessionMaterialFactory,
        IUnitOfWork unitOfWork,
        IDateTimeProvider clock,
        IUserExternalIdentityRepository externalIdentities)
    {
        _tenants = tenants;
        _users = users;
        _userMfas = userMfas;
        _mfaChallenges = mfaChallenges;
        _tenantSwitcher = tenantSwitcher;
        _legalChecker = legalChecker;
        _legalChallenges = legalChallenges;
        _tenantSessionExchange = tenantSessionExchange;
        _sessionMaterialFactory = sessionMaterialFactory;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _externalIdentities = externalIdentities;
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

            return Result<LoginResponseDto>.Success(LoginMapper.ToPasswordChangeRequired(user, tenant));
        }

        var mfaRecord = await _userMfas.GetTotpAsync(user.Id, isVerified: true, ct);
        if (mfaRecord is not null)
        {
            // A pending external identity rides along in the MFA challenge and is linked only after
            // MFA verification completes the login, never here.
            var mfaChallenge = request.ExternalIdentity is null
                ? await _mfaChallenges.CreateAsync(
                    user.Id,
                    user.TenantId,
                    request.LegalChallengeOrigin,
                    MfaChallengeLifetime,
                    ct)
                : await _mfaChallenges.CreateAsync(
                    user.Id,
                    user.TenantId,
                    request.LegalChallengeOrigin,
                    request.ExternalIdentity,
                    MfaChallengeLifetime,
                    ct);
            return Result<LoginResponseDto>.Success(LoginMapper.ToMfaRequired(user, tenant, mfaChallenge));
        }

        return await FinishAuthenticatedLoginAsync(
            user, request.LegalChallengeOrigin, request.IpAddress, request.UserAgent,
            request.FinalizationMode, request.ExternalIdentity, ct, tenant);
    }

    /// <summary>
    /// Runs the legal-check-and-tail for every login entry point (base-domain password/Google,
    /// workspace selection, post-MFA, post-legal-acceptance, invite acceptance, forced password
    /// change). <paramref name="finalizationMode"/> is required and caller-supplied - this method
    /// never infers it from ITenantContext/a host string itself. BaseDomainExchange hands off to
    /// the tenant host via TenantSessionExchangeService instead of signing in (the base host must
    /// never set crownus_session/crownus_csrf); TenantHostDirect signs in immediately via
    /// ILoginSessionMaterialFactory, the same as every login did before the exchange flow existed.
    /// </summary>
    public Task<Result<LoginResponseDto>> FinishAuthenticatedLoginAsync(
        User user,
        string legalChallengeOrigin,
        string? ipAddress,
        string? userAgent,
        LoginFinalizationMode finalizationMode,
        CancellationToken ct = default,
        Tenant? tenant = null) =>
        FinishAuthenticatedLoginAsync(
            user, legalChallengeOrigin, ipAddress, userAgent, finalizationMode, null, ct, tenant);

    public async Task<Result<LoginResponseDto>> FinishAuthenticatedLoginAsync(
        User user,
        string legalChallengeOrigin,
        string? ipAddress,
        string? userAgent,
        LoginFinalizationMode finalizationMode,
        PendingExternalIdentity? externalIdentity,
        CancellationToken ct = default,
        Tenant? tenant = null)
    {
        var resolvedTenant = tenant ?? await _tenants.GetByIdAsync(user.TenantId, ct);
        if (resolvedTenant is null)
            return Result<LoginResponseDto>.Failure("Invalid or expired session.", 401);

        var legalCheck = await _legalChecker.CheckAsync(user.TenantId, user.Id, ct);
        if (legalCheck.Status == LegalAcceptanceStatus.Pending)
        {
            var (rawChallenge, rawCsrf) = await _legalChallenges.CreateAsync(
                user.TenantId, user.Id, legalChallengeOrigin, LegalChallengeLifetime, ct);

            return Result<LoginResponseDto>.Success(
                LoginMapper.ToLegalAcceptanceRequired(
                    user, resolvedTenant, rawChallenge, rawCsrf, legalCheck.PendingDocuments));
        }

        // Every gate is cleared: this is the one point where a login "fully completes" for the
        // Google link. Both finalization paths below call SaveChangesAsync, which commits it.
        if (externalIdentity is not null)
            await LinkOrTouchExternalIdentityAsync(user, externalIdentity, ct);

        if (finalizationMode == LoginFinalizationMode.BaseDomainExchange)
        {
            return await _tenantSessionExchange.CreateAsync(
                user, resolvedTenant, legalChallengeOrigin, ipAddress, userAgent, ct);
        }

        var sessionResult = await _sessionMaterialFactory.PrepareAsync(user, resolvedTenant, ipAddress, userAgent, ct);
        if (!sessionResult.IsSuccess)
            return sessionResult;

        user.LastLoginAt = _clock.UtcNow;
        await _unitOfWork.SaveChangesAsync(ct);

        return sessionResult;
    }

    /// <summary>
    /// Runs inside the user's tenant context. Subject already linked to this user: update
    /// LastUsedAt. Subject unlinked and the user has no Google link yet: add the link. Anything
    /// else (subject linked to someone else, user linked to another subject) changes nothing.
    /// The unique (tenant_id, provider, provider_subject) index stops a racing duplicate insert.
    /// </summary>
    private async Task LinkOrTouchExternalIdentityAsync(
        User user,
        PendingExternalIdentity externalIdentity,
        CancellationToken ct)
    {
        var now = _clock.UtcNow;

        var bySubject = await _externalIdentities.GetByTenantProviderAndSubjectAsync(
            user.TenantId, externalIdentity.Provider, externalIdentity.Subject, ct);
        if (bySubject is not null)
        {
            if (bySubject.UserId == user.Id)
                bySubject.LastUsedAt = now;
            return;
        }

        var existingForUser = await _externalIdentities.GetByTenantUserAndProviderAsync(
            user.TenantId, user.Id, externalIdentity.Provider, ct);
        if (existingForUser is not null)
            return;

        await _externalIdentities.AddAsync(new UserExternalIdentity
        {
            Id = Guid.NewGuid(),
            TenantId = user.TenantId,
            UserId = user.Id,
            Provider = externalIdentity.Provider,
            ProviderSubject = externalIdentity.Subject,
            ProviderEmail = externalIdentity.Email,
            EmailVerified = true,
            LinkedAt = now,
            LastUsedAt = now
        }, ct);
    }
}
