using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.TrayActivation.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.TrayActivation.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.TrayActivation.ServiceInterfaces;
using ONEVO.Domain.Features.Monitoring.TrayActivation.Entities;

namespace ONEVO.Application.Features.Monitoring.TrayActivation.Commands.RefreshTrayToken;

public class RefreshTrayTokenCommandHandler
    : IRequestHandler<RefreshTrayTokenCommand, Result<TrayAuthResponseDto>>
{
    private static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(90);
    private const int AccessTokenExpiresInSeconds = 3600;
    private const int RefreshTokenExpiresInSeconds = 7_776_000;

    private readonly ITrayActivationRepository _repository;
    private readonly IUserRepository _userRepository;
    private readonly ITenantRepository _tenantRepository;
    private readonly ITenantContextSwitcher _tenantSwitcher;
    private readonly ITrayTokenService _tokenService;
    private readonly IDateTimeProvider _clock;
    private readonly IUnitOfWork _unitOfWork;

    public RefreshTrayTokenCommandHandler(
        ITrayActivationRepository repository,
        IUserRepository userRepository,
        ITenantRepository tenantRepository,
        ITenantContextSwitcher tenantSwitcher,
        ITrayTokenService tokenService,
        IDateTimeProvider clock,
        IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _userRepository = userRepository;
        _tenantRepository = tenantRepository;
        _tenantSwitcher = tenantSwitcher;
        _tokenService = tokenService;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<TrayAuthResponseDto>> Handle(
        RefreshTrayTokenCommand request,
        CancellationToken cancellationToken)
    {
        var tokenHash = _tokenService.HashToken(request.RefreshToken);
        var existingToken = await _repository.FindActiveRefreshTokenAsync(tokenHash, cancellationToken);

        if (existingToken is null)
            return Result<TrayAuthResponseDto>.Failure("Invalid or expired refresh token.", 401);

        var device = await _repository.FindActiveDeviceAsync(
            existingToken.DeviceRegistrationId, existingToken.TenantId, cancellationToken);

        if (device is null)
            return Result<TrayAuthResponseDto>.Failure("Device is no longer active.", 401);

        if (device.DeviceFingerprint != request.DeviceFingerprint)
        {
            // Fingerprint mismatch — possible token theft; revoke all tokens for this device
            await _repository.RevokeAllRefreshTokensForDeviceAsync(
                device.Id, "fingerprint_mismatch", cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result<TrayAuthResponseDto>.Failure("Device fingerprint mismatch.", 401);
        }

        var now = _clock.UtcNow;

        // Rotate: revoke old, issue new
        await _repository.RevokeRefreshTokenAsync(existingToken, "rotated", cancellationToken);

        var newRawToken = _tokenService.GenerateRawRefreshToken();
        var newTokenHash = _tokenService.HashToken(newRawToken);

        var newRefreshToken = new TrayDeviceRefreshToken
        {
            Id = Guid.NewGuid(),
            TenantId = existingToken.TenantId,
            UserId = existingToken.UserId,
            DeviceRegistrationId = existingToken.DeviceRegistrationId,
            TokenHash = newTokenHash,
            ExpiresAt = now.Add(RefreshTokenLifetime),
            IsRevoked = false,
            CreatedAt = now
        };

        await _repository.AddRefreshTokenAsync(newRefreshToken, cancellationToken);
        await _repository.UpdateDeviceLastSeenAsync(device.Id, now, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var accessToken = _tokenService.GenerateAccessToken(
            device.Id, existingToken.UserId, existingToken.TenantId, device.LegalEntityId);

        var identity = await ResolveEmployeeIdentityAsync(
            existingToken.UserId, existingToken.TenantId, device.LegalEntityId, cancellationToken);

        return Result<TrayAuthResponseDto>.Success(new TrayAuthResponseDto(
            accessToken,
            AccessTokenExpiresInSeconds,
            newRawToken,
            RefreshTokenExpiresInSeconds,
            identity.Name,
            identity.Email,
            identity.Number,
            identity.Status,
            identity.Department,
            identity.WorkMode,
            identity.Office,
            identity.Organization));
    }

    /// <summary>
    /// Display-only identity for the tray UI. Prefers the HR Employee profile (name, email,
    /// employee number); falls back to the auth User's name/email if no Employee row is linked
    /// yet, so a token refresh never fails just because HR onboarding hasn't finished.
    /// Refresh runs anonymously at the base host (System tenant-context mode), so before reading
    /// employees/users — both RLS-protected to 'admin' or matching 'tenant' mode only — the
    /// context must switch into the now-resolved tenant. If the tenant row itself is gone, the
    /// identity fields simply stay null; the refresh token has already rotated, so this must not
    /// fail the whole refresh.
    /// </summary>
    private async Task<(string? Name, string? Email, string? Number, string Status, string? Department, string? WorkMode, string? Office, string? Organization)> ResolveEmployeeIdentityAsync(
        Guid userId, Guid tenantId, Guid? legalEntityId, CancellationToken ct)
    {
        var tenant = await _tenantRepository.GetByIdAsync(tenantId, ct);
        if (tenant is null)
            return (null, null, null, "profile_unavailable", null, null, null, null);

        await _tenantSwitcher.SwitchToTenantAsync(
            new TenantRegistryEntry(tenant.Id, tenant.Slug, tenant.Status, PlanCode: null), ct);

        var organization = string.IsNullOrWhiteSpace(tenant.Name) ? null : tenant.Name;
        var profile = await _repository.FindEmployeeProfileAsync(userId, tenantId, legalEntityId, ct);
        if (profile is not null)
            return (FullNameOrNull(profile.FirstName, profile.LastName), profile.Email, profile.EmployeeNumber, "resolved",
                profile.DepartmentName, profile.WorkModeLabel, profile.OfficeName, organization);

        var user = await _userRepository.GetByIdAsync(userId, ct);
        if (user is not null)
            return (FullNameOrNull(user.FirstName, user.LastName), user.Email, null,
                legalEntityId.HasValue ? "profile_unavailable" : "company_context_required",
                null, null, null, organization);

        return (null, null, null,
            legalEntityId.HasValue ? "profile_unavailable" : "company_context_required",
            null, null, null, organization);
    }

    private static string? FullNameOrNull(string first, string last)
    {
        var name = $"{first} {last}".Trim();
        return string.IsNullOrEmpty(name) ? null : name;
    }
}
