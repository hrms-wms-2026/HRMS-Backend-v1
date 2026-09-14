using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Application.Features.Monitoring.WorkLocation.Commands.ConfirmWorkLocation;

public class ConfirmWorkLocationCommandHandler : IRequestHandler<ConfirmWorkLocationCommand, Result>
{
    private static readonly string[] ValidLocationTypes =
    [
        DailyWorkLocationConfirmation.LocationTypeOffice,
        DailyWorkLocationConfirmation.LocationTypeHome,
        DailyWorkLocationConfirmation.LocationTypeOther
    ];

    private readonly ITrayCurrentDevice _device;
    private readonly IDailyWorkLocationConfirmationRepository _confirmations;
    private readonly IEmployeeWorkLocationRepository _workLocations;
    private readonly ITenantRepository _tenants;
    private readonly ITenantContextSwitcher _tenantSwitcher;
    private readonly IDateTimeProvider _clock;
    private readonly IUnitOfWork _unitOfWork;

    public ConfirmWorkLocationCommandHandler(
        ITrayCurrentDevice device,
        IDailyWorkLocationConfirmationRepository confirmations,
        IEmployeeWorkLocationRepository workLocations,
        ITenantRepository tenants,
        ITenantContextSwitcher tenantSwitcher,
        IDateTimeProvider clock,
        IUnitOfWork unitOfWork)
    {
        _device = device;
        _confirmations = confirmations;
        _workLocations = workLocations;
        _tenants = tenants;
        _tenantSwitcher = tenantSwitcher;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(ConfirmWorkLocationCommand request, CancellationToken ct)
    {
        if (!_device.IsAuthenticated || _device.TenantId == Guid.Empty || _device.UserId == Guid.Empty)
            return Result.Failure("A valid tray device token is required.", 401);

        if (!ValidLocationTypes.Contains(request.LocationType))
            return Result.Failure("Unsupported location type.", 400);

        var tenant = await _tenants.GetByIdAsync(_device.TenantId, ct);
        if (tenant is null)
            return Result.Failure("Tenant not found.", 401);

        await _tenantSwitcher.SwitchToTenantAsync(
            new TenantRegistryEntry(tenant.Id, tenant.Slug, tenant.Status, PlanCode: null), ct);

        var tenantId = _device.TenantId;
        var employeeId = _device.UserId;
        var now = _clock.UtcNow;
        var workDate = DateOnly.FromDateTime(now.UtcDateTime);
        var hasFix = request.Latitude is not null && request.Longitude is not null;

        await _confirmations.UpsertAsync(new DailyWorkLocationConfirmation
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EmployeeId = employeeId,
            WorkDate = workDate,
            LocationType = request.LocationType,
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            AccuracyMeters = request.AccuracyMeters,
            ConfirmedAt = now
        }, ct);

        var isRemoteLikeSelection = request.LocationType is
            DailyWorkLocationConfirmation.LocationTypeHome or DailyWorkLocationConfirmation.LocationTypeOther;

        if (hasFix && isRemoteLikeSelection)
        {
            var existing = await _workLocations.GetByEmployeeIdAsync(tenantId, employeeId, ct);
            if (existing is null)
            {
                await _workLocations.AddAsync(new EmployeeWorkLocation
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    EmployeeId = employeeId,
                    Latitude = request.Latitude!.Value,
                    Longitude = request.Longitude!.Value,
                    AccuracyMeters = request.AccuracyMeters,
                    RegisteredAt = now,
                    UpdatedAt = now
                }, ct);
            }
        }

        await _unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
