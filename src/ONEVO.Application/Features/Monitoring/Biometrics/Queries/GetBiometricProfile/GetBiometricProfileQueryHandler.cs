using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Biometrics.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.Biometrics.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;

namespace ONEVO.Application.Features.Monitoring.Biometrics.Queries.GetBiometricProfile;

public class GetBiometricProfileQueryHandler
    : IRequestHandler<GetBiometricProfileQuery, Result<BiometricProfileResponseDto>>
{
    private readonly IBiometricRepository _repository;
    private readonly ITrayCurrentDevice _device;
    private readonly ITenantRepository _tenants;
    private readonly ITenantContextSwitcher _tenantSwitcher;
    private readonly IEmployeeIdentityResolver _employeeResolver;

    public GetBiometricProfileQueryHandler(
        IBiometricRepository repository,
        ITrayCurrentDevice device,
        ITenantRepository tenants,
        ITenantContextSwitcher tenantSwitcher,
        IEmployeeIdentityResolver employeeResolver)
    {
        _repository = repository;
        _device = device;
        _tenants = tenants;
        _tenantSwitcher = tenantSwitcher;
        _employeeResolver = employeeResolver;
    }

    public async Task<Result<BiometricProfileResponseDto>> Handle(
        GetBiometricProfileQuery request, CancellationToken cancellationToken)
    {
        if (!_device.IsAuthenticated || _device.TenantId == Guid.Empty || _device.UserId == Guid.Empty)
            return Result<BiometricProfileResponseDto>.Failure("A valid tray device token is required.", 401);

        var tenant = await _tenants.GetByIdAsync(_device.TenantId, cancellationToken);
        if (tenant is null)
            return Result<BiometricProfileResponseDto>.Failure("Tenant not found.", 401);

        await _tenantSwitcher.SwitchToTenantAsync(
            new TenantRegistryEntry(tenant.Id, tenant.Slug, tenant.Status, PlanCode: null),
            cancellationToken);

        var employeeId = await _employeeResolver.ResolveEmployeeIdAsync(_device.UserId, _device.TenantId, cancellationToken);
        if (employeeId is null)
            return Result<BiometricProfileResponseDto>.NotFound("No employee profile linked to this account.");

        var profile = await _repository.FindActiveProfileAsync(employeeId.Value, _device.TenantId, cancellationToken);
        if (profile is null)
            return Result<BiometricProfileResponseDto>.NotFound("No active biometric enrollment.");

        return Result<BiometricProfileResponseDto>.Success(new BiometricProfileResponseDto(
            profile.Id, profile.Status, profile.CreatedAt));
    }
}
