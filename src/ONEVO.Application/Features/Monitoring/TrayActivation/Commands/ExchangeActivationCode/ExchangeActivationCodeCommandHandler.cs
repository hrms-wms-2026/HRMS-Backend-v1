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

namespace ONEVO.Application.Features.Monitoring.TrayActivation.Commands.ExchangeActivationCode;

public class ExchangeActivationCodeCommandHandler
    : IRequestHandler<ExchangeActivationCodeCommand, Result<TrayAuthResponseDto>>
{
    private static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(90);
    private const int AccessTokenExpiresInSeconds = 3600;
    private const int RefreshTokenExpiresInSeconds = 7_776_000; // 90 days

    private readonly ITrayActivationRepository _repository;
    private readonly IUserRepository _userRepository;
    private readonly ITenantRepository _tenantRepository;
    private readonly ITenantContextSwitcher _tenantSwitcher;
    private readonly ITrayTokenService _tokenService;
    private readonly IDateTimeProvider _clock;
    private readonly IUnitOfWork _unitOfWork;

    public ExchangeActivationCodeCommandHandler(
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
        ExchangeActivationCodeCommand request,
        CancellationToken cancellationToken)
    {
        var codeHash = _tokenService.HashToken(request.Code);

        // TenantId is unknown at this stage — the code lookup must match globally
        // within the hashed code; the repository filters by hash only here.
        var activationCode = await _repository.FindActiveCodeByHashAsync(codeHash, cancellationToken);

        if (activationCode is null)
            return Result<TrayAuthResponseDto>.Failure("Invalid or expired activation code.", 401);

        var now = _clock.UtcNow;

        await _repository.MarkCodeUsedAsync(activationCode, cancellationToken);

        var device = new TrayDeviceRegistration
        {
            Id = Guid.NewGuid(),
            TenantId = activationCode.TenantId,
            UserId = activationCode.UserId,
            DeviceName = request.DeviceName,
            DeviceOs = request.DeviceOs,
            DeviceFingerprint = request.DeviceFingerprint,
            IsActive = true,
            ActivatedAt = now,
            CreatedAt = now
        };

        await _repository.AddDeviceRegistrationAsync(device, cancellationToken);

        var rawRefreshToken = _tokenService.GenerateRawRefreshToken();
        var refreshTokenHash = _tokenService.HashToken(rawRefreshToken);

        var refreshToken = new TrayDeviceRefreshToken
        {
            Id = Guid.NewGuid(),
            TenantId = activationCode.TenantId,
            UserId = activationCode.UserId,
            DeviceRegistrationId = device.Id,
            TokenHash = refreshTokenHash,
            ExpiresAt = now.Add(RefreshTokenLifetime),
            IsRevoked = false,
            CreatedAt = now
        };

        await _repository.AddRefreshTokenAsync(refreshToken, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var accessToken = _tokenService.GenerateAccessToken(
            device.Id, activationCode.UserId, activationCode.TenantId);

        var (employeeName, employeeEmail, employeeNumber) = await ResolveEmployeeIdentityAsync(
            activationCode.UserId, activationCode.TenantId, cancellationToken);

        return Result<TrayAuthResponseDto>.Success(new TrayAuthResponseDto(
            accessToken,
            AccessTokenExpiresInSeconds,
            rawRefreshToken,
            RefreshTokenExpiresInSeconds,
            employeeName,
            employeeEmail,
            employeeNumber));
    }

    /// <summary>
    /// Display-only identity for the tray UI. Prefers the HR Employee profile (name, email,
    /// employee number); falls back to the auth User's name/email if no Employee row is linked
    /// yet, so a fresh device activation never fails just because HR onboarding hasn't finished.
    /// Exchange runs anonymously at the base host (System tenant-context mode), so before reading
    /// employees/users — both RLS-protected to 'admin' or matching 'tenant' mode only — the
    /// context must switch into the now-resolved tenant. If the tenant row itself is gone, the
    /// identity fields simply stay null; the activation code is already consumed and the device
    /// already registered, so this must not fail the whole exchange.
    /// </summary>
    private async Task<(string? Name, string? Email, string? Number)> ResolveEmployeeIdentityAsync(
        Guid userId, Guid tenantId, CancellationToken ct)
    {
        var tenant = await _tenantRepository.GetByIdAsync(tenantId, ct);
        if (tenant is null)
            return (null, null, null);

        await _tenantSwitcher.SwitchToTenantAsync(
            new TenantRegistryEntry(tenant.Id, tenant.Slug, tenant.Status, PlanCode: null), ct);

        var profile = await _repository.FindEmployeeProfileAsync(userId, tenantId, ct);
        if (profile is not null)
            return (FullNameOrNull(profile.FirstName, profile.LastName), profile.Email, profile.EmployeeNumber);

        var user = await _userRepository.GetByIdAsync(userId, ct);
        if (user is not null)
            return (FullNameOrNull(user.FirstName, user.LastName), user.Email, null);

        return (null, null, null);
    }

    private static string? FullNameOrNull(string first, string last)
    {
        var name = $"{first} {last}".Trim();
        return string.IsNullOrEmpty(name) ? null : name;
    }
}
