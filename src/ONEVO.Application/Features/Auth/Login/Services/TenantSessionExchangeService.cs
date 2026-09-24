using Microsoft.Extensions.Configuration;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.DTOs.Responses;
using ONEVO.Application.Features.Auth.Login.Mappers;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;

namespace ONEVO.Application.Features.Auth.Login.Services;

public sealed class TenantSessionExchangeService : ITenantSessionExchangeService
{
    private const string GenericExchangeFailureMessage = "This sign-in link is invalid or has expired. Please sign in again.";

    // Short-lived on purpose: unlike MFA/legal challenges, nothing here waits on human input - the
    // browser follows continue_url as an immediate, automatic redirect straight out of the login
    // response.
    private static readonly TimeSpan ExchangeCodeLifetime = TimeSpan.FromMinutes(2);

    private readonly ITenantSessionExchangeChallengeRepository _challenges;
    private readonly ISecureTokenGenerator _tokens;
    private readonly ILoginSessionMaterialFactory _sessionMaterialFactory;
    private readonly ITenantRepository _tenants;
    private readonly IUserRepository _users;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;
    private readonly IConfiguration _configuration;

    public TenantSessionExchangeService(
        ITenantSessionExchangeChallengeRepository challenges,
        ISecureTokenGenerator tokens,
        ILoginSessionMaterialFactory sessionMaterialFactory,
        ITenantRepository tenants,
        IUserRepository users,
        IUnitOfWork unitOfWork,
        IDateTimeProvider clock,
        IConfiguration configuration)
    {
        _challenges = challenges;
        _tokens = tokens;
        _sessionMaterialFactory = sessionMaterialFactory;
        _tenants = tenants;
        _users = users;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _configuration = configuration;
    }

    public async Task<Result<LoginResponseDto>> CreateAsync(
        User user,
        Tenant tenant,
        string authOrigin,
        string? ipAddress,
        string? userAgent,
        CancellationToken ct = default)
    {
        var now = _clock.UtcNow;
        var rawCode = _tokens.GenerateOpaqueToken();
        var expiresAt = now.Add(ExchangeCodeLifetime);

        var challenge = new TenantSessionExchangeChallenge
        {
            Id = Guid.NewGuid(),
            CodeHash = _tokens.HashToken(rawCode),
            TenantId = tenant.Id,
            UserId = user.Id,
            AuthOrigin = authOrigin,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            ExpiresAt = expiresAt,
            ConsumedAt = null,
            CreatedAt = now,
            UpdatedAt = now
        };

        await _challenges.AddAsync(challenge, ct);

        var continueUrl = BuildContinueUrl(tenant.Slug, rawCode);

        return Result<LoginResponseDto>.Success(new LoginResponseDto(
            CsrfToken: string.Empty,
            CsrfTokenHash: string.Empty,
            ExpiresAt: null,
            User: LoginMapper.ToCurrentUserDto(user),
            Workspace: LoginMapper.ToWorkspaceResponseDto(tenant),
            TenantSessionExchange: new TenantSessionExchangeMaterial(continueUrl, expiresAt)));
    }

    public async Task<Result<LoginResponseDto>> ConsumeAsync(
        string rawCode,
        Guid resolvedTenantId,
        string? ipAddress,
        string? userAgent,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rawCode))
            return Result<LoginResponseDto>.Failure(GenericExchangeFailureMessage, 401);

        var codeHash = _tokens.HashToken(rawCode);
        var consumed = await _challenges.TryConsumeAsync(codeHash, resolvedTenantId, _clock.UtcNow, ct);
        if (consumed is null)
            return Result<LoginResponseDto>.Failure(GenericExchangeFailureMessage, 401);

        // Revalidate at consumption time, not just at code-creation time - the same rule the other
        // challenge consumers (legal/MFA) already follow.
        var tenant = await _tenants.GetByIdAsync(consumed.TenantId, ct);
        if (tenant is null || tenant.Status is not (TenantStatus.Active or TenantStatus.Trial))
            return Result<LoginResponseDto>.Failure(GenericExchangeFailureMessage, 401);

        var user = await _users.GetByIdAsync(consumed.UserId, ct);
        if (user is null || !user.IsActive || user.TenantId != tenant.Id)
            return Result<LoginResponseDto>.Failure(GenericExchangeFailureMessage, 401);

        var sessionResult = await _sessionMaterialFactory.PrepareAsync(user, tenant, ipAddress, userAgent, ct);
        if (!sessionResult.IsSuccess)
            return sessionResult;

        // LastLoginAt reflects when the session was actually established, not when the exchange
        // code was merely issued on the base host (the browser may never redeem it).
        user.LastLoginAt = _clock.UtcNow;
        await _unitOfWork.SaveChangesAsync(ct);

        return sessionResult;
    }

    /// <summary>
    /// Single place tenant continue URLs are ever built. Scheme/port come from Urls:AppBaseUrl
    /// (the SPA's own base URL, e.g. https://localhost:4200 in dev); host is {slug}.{RootDomain} -
    /// never a shared parent-domain cookie, just a plain cross-subdomain top-level navigation.
    /// </summary>
    private string BuildContinueUrl(string slug, string rawCode)
    {
        var rootDomain = _configuration["Tenancy:RootDomain"]?.Trim().TrimStart('.');
        if (string.IsNullOrWhiteSpace(rootDomain))
            throw new InvalidOperationException("Tenancy:RootDomain must be configured to build a tenant continue URL.");

        var appBaseUrl = _configuration["Urls:AppBaseUrl"];
        if (string.IsNullOrWhiteSpace(appBaseUrl) || !Uri.TryCreate(appBaseUrl, UriKind.Absolute, out var baseUri))
            throw new InvalidOperationException("Urls:AppBaseUrl must be configured to build a tenant continue URL.");

        var builder = new UriBuilder(baseUri.Scheme, $"{slug}.{rootDomain}", baseUri.Port, "/auth/continue")
        {
            Query = $"code={Uri.EscapeDataString(rawCode)}"
        };
        return builder.Uri.ToString();
    }
}
