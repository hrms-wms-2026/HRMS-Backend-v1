namespace ONEVO.Application.Features.Monitoring.CheckIn.Queries.GetFaceReferenceStatus;

using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Biometrics.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.Monitoring.Biometrics.Entities;

public sealed class GetFaceReferenceStatusQueryHandler(
    ITrayCurrentDevice device,
    ITenantRepository tenants,
    ITenantContextSwitcher tenantSwitcher,
    IBiometricProfileRepository profiles,
    ITrayEmployeeIdentityResolver employeeIdentity)
    : IRequestHandler<GetFaceReferenceStatusQuery, Result<FaceReferenceStatusDto>>
{
    public async Task<Result<FaceReferenceStatusDto>> Handle(
        GetFaceReferenceStatusQuery request, CancellationToken ct)
    {
        if (!device.IsAuthenticated || device.TenantId == Guid.Empty || device.UserId == Guid.Empty)
            return Result<FaceReferenceStatusDto>.Failure("A valid tray device token is required.", 401);

        var tenant = await tenants.GetByIdAsync(device.TenantId, ct);
        if (tenant is null)
            return Result<FaceReferenceStatusDto>.Failure("Tenant not found.", 401);

        await tenantSwitcher.SwitchToTenantAsync(
            new TenantRegistryEntry(tenant.Id, tenant.Slug, tenant.Status, PlanCode: null), ct);

        var employeeId = await employeeIdentity.ResolveEmployeeIdAsync(
            device.TenantId, device.UserId, device.LegalEntityId, ct);
        var profile = await profiles.GetByEmployeeIdAsync(device.TenantId, employeeId, ct);

        var count = new[]
        {
            profile?.ReferencePhotoFileId,
            profile?.LeftReferencePhotoFileId,
            profile?.RightReferencePhotoFileId
        }.Count(id => id is not null);

        var enrolled = profile is { Status: BiometricProfileStatus.Enrolled } && profile.ReferencePhotoFileId is not null;
        return Result<FaceReferenceStatusDto>.Success(new FaceReferenceStatusDto(enrolled, count));
    }
}
