using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Legal.Services;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.TrayActivation.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.TrayActivation.Exceptions;
using ONEVO.Application.Features.Monitoring.TrayActivation.Models;
using ONEVO.Application.Features.Monitoring.TrayActivation.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.TrayActivation.ServiceInterfaces;
using ONEVO.Domain.Features.Monitoring.TrayActivation.Entities;
using IEmployeeRepository = ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository;

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
    private readonly IDeviceChangeRequestRepository _deviceChangeRequests;
    private readonly IEmployeeRepository _employees;
    private readonly ILegalAcceptanceChecker _legalChecker;

    public TrayEnrollmentService(
        ITrayActivationRepository repository,
        IUserRepository userRepository,
        ITenantRepository tenantRepository,
        ITenantContextSwitcher tenantSwitcher,
        ITrayTokenService tokenService,
        IDateTimeProvider clock,
        IDeviceChangeRequestRepository deviceChangeRequests,
        IEmployeeRepository employees,
        ILegalAcceptanceChecker legalChecker)
    {
        _repository = repository;
        _userRepository = userRepository;
        _tenantRepository = tenantRepository;
        _tenantSwitcher = tenantSwitcher;
        _tokenService = tokenService;
        _clock = clock;
        _deviceChangeRequests = deviceChangeRequests;
        _employees = employees;
        _legalChecker = legalChecker;
    }

    public async Task<TrayAuthResponseDto> IssueAsync(
        TrayEnrollmentRequest request,
        CancellationToken ct)
    {
        var existingDevice = await _repository.FindActiveDeviceForUserAsync(
            request.UserId, request.TenantId, ct);

        if (existingDevice is not null && existingDevice.DeviceFingerprint != request.DeviceFingerprint)
        {
            // request.LegalEntityId can be null (e.g. "company_context_required" at enrollment
            // time) - fall back to the employee's resolved default legal entity so the raised
            // request is still visible in ListPendingEmployeeIdsAsync/ListApprovalInboxAsync,
            // which filter by a specific non-null legal entity. Without this fallback a
            // null-LegalEntityId request would never surface in any approver's inbox.
            var legalEntityId = request.LegalEntityId
                ?? (await _employees.GetDefaultForUserAsync(request.TenantId, request.UserId, ct))?.LegalEntityId;

            await _deviceChangeRequests.UpsertPendingAsync(new DeviceChangeRequest
            {
                Id = Guid.NewGuid(),
                TenantId = request.TenantId,
                EmployeeId = request.UserId,
                LegalEntityId = legalEntityId,
                CurrentDeviceRegistrationId = existingDevice.Id,
                NewDeviceFingerprint = request.DeviceFingerprint,
                NewDeviceName = request.DeviceName,
                NewDeviceOs = request.DeviceOs,
                Status = DeviceChangeRequest.StatusPending,
                RequestedAt = _clock.UtcNow,
            }, ct);
            await _deviceChangeRequests.SaveChangesAsync(ct);
            throw new DeviceChangePendingException();
        }

        var now = _clock.UtcNow;
        var device = new TrayDeviceRegistration
        {
            Id = Guid.NewGuid(),
            TenantId = request.TenantId,
            UserId = request.UserId,
            LegalEntityId = request.LegalEntityId,
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
            device.Id, request.UserId, request.TenantId, request.LegalEntityId);
        var (employeeName, employeeEmail, employeeNumber, profileStatus, tenantSlug) = await ResolveEmployeeIdentityAsync(
            request.UserId, request.TenantId, request.LegalEntityId, ct);
        var legalCheck = await _legalChecker.CheckAsync(request.TenantId, request.UserId, ct);

        return new TrayAuthResponseDto(
            accessToken,
            AccessTokenExpiresInSeconds,
            rawRefreshToken,
            RefreshTokenExpiresInSeconds,
            employeeName,
            employeeEmail,
            employeeNumber,
            profileStatus,
            tenantSlug,
            RequiresLegalAcceptance: legalCheck.Status == LegalAcceptanceStatus.Pending,
            PendingLegalDocuments: legalCheck.PendingDocuments);
    }

    private async Task<(string? Name, string? Email, string? Number, string Status, string? TenantSlug)> ResolveEmployeeIdentityAsync(
        Guid userId,
        Guid tenantId,
        Guid? legalEntityId,
        CancellationToken ct)
    {
        var tenant = await _tenantRepository.GetByIdAsync(tenantId, ct);
        if (tenant is null)
            return (null, null, null, "profile_unavailable", null);

        await _tenantSwitcher.SwitchToTenantAsync(
            new TenantRegistryEntry(tenant.Id, tenant.Slug, tenant.Status, PlanCode: null), ct);

        var profile = await _repository.FindEmployeeProfileAsync(userId, tenantId, legalEntityId, ct);
        if (profile is not null)
        {
            return (
                FullNameOrNull(profile.FirstName, profile.LastName),
                profile.Email,
                profile.EmployeeNumber,
                "resolved",
                tenant.Slug);
        }

        var user = await _userRepository.GetByIdAsync(userId, ct);
        if (user is not null)
            return (FullNameOrNull(user.FirstName, user.LastName), user.Email, null,
                legalEntityId.HasValue ? "profile_unavailable" : "company_context_required",
                tenant.Slug);

        return (null, null, null,
            legalEntityId.HasValue ? "profile_unavailable" : "company_context_required",
            tenant.Slug);
    }

    private static string? FullNameOrNull(string first, string last)
    {
        var name = $"{first} {last}".Trim();
        return string.IsNullOrEmpty(name) ? null : name;
    }
}
