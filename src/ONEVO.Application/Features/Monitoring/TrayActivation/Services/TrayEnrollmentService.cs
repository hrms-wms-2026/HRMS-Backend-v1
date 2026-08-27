using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.TrayActivation.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.TrayActivation.Models;
using ONEVO.Application.Features.Monitoring.TrayActivation.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.TrayActivation.ServiceInterfaces;
using ONEVO.Domain.Features.Monitoring.TrayActivation.Entities;

namespace ONEVO.Application.Features.Monitoring.TrayActivation.Services;

public sealed class TrayEnrollmentService : ITrayEnrollmentService
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

    public TrayEnrollmentService(
        ITrayActivationRepository repository,
        IUserRepository userRepository,
        ITenantRepository tenantRepository,
        ITenantContextSwitcher tenantSwitcher,
        ITrayTokenService tokenService,
        IDateTimeProvider clock)
    {
        _repository = repository;
        _userRepository = userRepository;
        _tenantRepository = tenantRepository;
        _tenantSwitcher = tenantSwitcher;
        _tokenService = tokenService;
        _clock = clock;
    }

    public async Task<TrayAuthResponseDto> IssueAsync(
        TrayEnrollmentRequest request,
        CancellationToken ct)
    {
        var now = _clock.UtcNow;
        var device = new TrayDeviceRegistration
        {
            Id = Guid.NewGuid(),
            TenantId = request.TenantId,
            UserId = request.UserId,
            DeviceName = request.DeviceName,
            DeviceOs = request.DeviceOs,
            DeviceFingerprint = request.DeviceFingerprint,
            IsActive = true,
            ActivatedAt = now,
            CreatedAt = now,
        };

        await _repository.AddDeviceRegistrationAsync(device, ct);

        var rawRefreshToken = _tokenService.GenerateRawRefreshToken();
        var refreshToken = new TrayDeviceRefreshToken
        {
            Id = Guid.NewGuid(),
            TenantId = request.TenantId,
            UserId = request.UserId,
            DeviceRegistrationId = device.Id,
            TokenHash = _tokenService.HashToken(rawRefreshToken),
            ExpiresAt = now.Add(RefreshTokenLifetime),
            IsRevoked = false,
            CreatedAt = now,
        };

        await _repository.AddRefreshTokenAsync(refreshToken, ct);

        var accessToken = _tokenService.GenerateAccessToken(
            device.Id, request.UserId, request.TenantId);
        var (employeeName, employeeEmail, employeeNumber) = await ResolveEmployeeIdentityAsync(
            request.UserId, request.TenantId, ct);

        return new TrayAuthResponseDto(
            accessToken,
            AccessTokenExpiresInSeconds,
            rawRefreshToken,
            RefreshTokenExpiresInSeconds,
            employeeName,
            employeeEmail,
            employeeNumber);
    }

    private async Task<(string? Name, string? Email, string? Number)> ResolveEmployeeIdentityAsync(
        Guid userId,
        Guid tenantId,
        CancellationToken ct)
    {
        var tenant = await _tenantRepository.GetByIdAsync(tenantId, ct);
        if (tenant is null)
            return (null, null, null);

        await _tenantSwitcher.SwitchToTenantAsync(
            new TenantRegistryEntry(tenant.Id, tenant.Slug, tenant.Status, PlanCode: null), ct);

        var profile = await _repository.FindEmployeeProfileAsync(userId, tenantId, ct);
        if (profile is not null)
        {
            return (
                FullNameOrNull(profile.FirstName, profile.LastName),
                profile.Email,
                profile.EmployeeNumber);
        }

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
