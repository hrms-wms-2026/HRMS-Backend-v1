using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.CheckIn.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Services;
using ONEVO.Domain.Features.Monitoring.CheckIn.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;
using CoreEmployeeRepository = ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository;

namespace ONEVO.Application.Features.Monitoring.CheckIn.Commands.SubmitCheckIn;

public class SubmitCheckInCommandHandler
    : IRequestHandler<SubmitCheckInCommand, Result<CheckInResponseDto>>
{
    private readonly ICheckInRepository _repository;
    private readonly ITrayCurrentDevice _device;
    private readonly ITenantRepository _tenants;
    private readonly ITenantContextSwitcher _tenantSwitcher;
    private readonly IMonitoringToggleResolver _toggles;
    private readonly IDateTimeProvider _clock;
    private readonly IUnitOfWork _unitOfWork;
    private readonly CoreEmployeeRepository _employees;
    private readonly ILegalEntityRepository _legalEntities;
    private readonly IExpectedWorkAreaResolver _expectedWorkAreas;
    private readonly IEmployeeWorkLocationRepository _workLocations;
    private readonly ILocationChangeRequestRepository _locationChangeRequests;

    public SubmitCheckInCommandHandler(
        ICheckInRepository repository,
        ITrayCurrentDevice device,
        ITenantRepository tenants,
        ITenantContextSwitcher tenantSwitcher,
        IMonitoringToggleResolver toggles,
        IDateTimeProvider clock,
        IUnitOfWork unitOfWork,
        CoreEmployeeRepository employees,
        ILegalEntityRepository legalEntities,
        IExpectedWorkAreaResolver expectedWorkAreas,
        IEmployeeWorkLocationRepository workLocations,
        ILocationChangeRequestRepository locationChangeRequests)
    {
        _repository = repository;
        _device = device;
        _tenants = tenants;
        _tenantSwitcher = tenantSwitcher;
        _toggles = toggles;
        _clock = clock;
        _unitOfWork = unitOfWork;
        _employees = employees;
        _legalEntities = legalEntities;
        _expectedWorkAreas = expectedWorkAreas;
        _workLocations = workLocations;
        _locationChangeRequests = locationChangeRequests;
    }

    public async Task<Result<CheckInResponseDto>> Handle(
        SubmitCheckInCommand request,
        CancellationToken cancellationToken)
    {
        if (!_device.IsAuthenticated
            || _device.TenantId == Guid.Empty
            || _device.UserId == Guid.Empty
            || _device.DeviceRegistrationId == Guid.Empty)
        {
            return Result<CheckInResponseDto>.Failure("A valid tray device token is required.", 401);
        }

        var tenant = await _tenants.GetByIdAsync(_device.TenantId, cancellationToken);
        if (tenant is null)
            return Result<CheckInResponseDto>.Failure("Tenant not found.", 401);

        // Tray requests may hit the base host (system mode). Switch into the JWT
        // tenant so EF query filters + PostgreSQL RLS accept the write.
        await _tenantSwitcher.SwitchToTenantAsync(
            new TenantRegistryEntry(tenant.Id, tenant.Slug, tenant.Status, PlanCode: null),
            cancellationToken);

        var now = _clock.UtcNow;

        // Tenants that have turned Location tracking off must not have location persisted even if
        // an out-of-date or misbehaving tray client still sends a fix - the toggle is a data-
        // collection consent boundary, not just a client-side hint. Everything else about the
        // check-in (identity/liveness) is independent of location and still proceeds.
        var locationTrackingEnabled = _device.LegalEntityId is Guid legalEntityId
            && await _toggles.IsEnabledAsync(
                _device.TenantId, _device.UserId, legalEntityId, MonitoringCapability.WorkLocationVerification, cancellationToken);

        var checkIn = new EmployeeCheckIn
        {
            Id = Guid.NewGuid(),
            TenantId = _device.TenantId,
            UserId = _device.UserId,
            DeviceRegistrationId = _device.DeviceRegistrationId,
            Latitude = locationTrackingEnabled ? request.Latitude : null,
            Longitude = locationTrackingEnabled ? request.Longitude : null,
            LocationAccuracy = locationTrackingEnabled ? request.LocationAccuracy : null,
            LocationAddress = locationTrackingEnabled ? request.LocationAddress : null,
            DeviceSerialNumber = request.DeviceSerialNumber,
            CheckedInAt = now,
            CreatedAt = now
        };

        await _repository.AddCheckInAsync(checkIn, cancellationToken);

        // Location monitoring must be on for both of these - they exist to maintain the employee's
        // registered reference point, which only matters while their location is actually being
        // checked against something.
        Guid? pendingLocationChangeRequestId = null;
        if (locationTrackingEnabled && _device.LegalEntityId is Guid deviceLegalEntityId)
        {
            var employee = await _employees.GetByUserAndLegalEntityAsync(
                _device.TenantId, _device.UserId, deviceLegalEntityId, cancellationToken);
            if (employee is not null)
            {
                var legalEntity = await _legalEntities.GetByIdForTenantAsync(
                    _device.TenantId, deviceLegalEntityId, cancellationToken);
                if (legalEntity is not null)
                {
                    var workAreaResult = await _expectedWorkAreas.ResolveAsync(
                        employee, legalEntity, DateOnly.FromDateTime(now.UtcDateTime), cancellationToken);
                    if (workAreaResult.IsSuccess && workAreaResult.Value!.WorkArea == "remote")
                    {
                        // First-ever location fix for a remote employee becomes their registered
                        // reference point - no separate "confirm your location" screen. Only ever
                        // set once this way; changing it afterward requires an approved
                        // LocationChangeRequest the employee then opts into (below).
                        if (checkIn.Latitude is double latitude && checkIn.Longitude is double longitude)
                        {
                            var existingLocation = await _workLocations.GetByEmployeeIdAsync(
                                _device.TenantId, employee.Id, cancellationToken);
                            if (existingLocation is null)
                            {
                                await _workLocations.AddAsync(new EmployeeWorkLocation
                                {
                                    Id = Guid.NewGuid(),
                                    TenantId = _device.TenantId,
                                    EmployeeId = employee.Id,
                                    Latitude = latitude,
                                    Longitude = longitude,
                                    AccuracyMeters = checkIn.LocationAccuracy,
                                    RegisteredAt = now,
                                    UpdatedAt = now
                                }, cancellationToken);
                            }
                        }

                        // An approved-but-undecided change request re-prompts on every clock-in
                        // until the employee opts in - saying no earlier just meant "not this time".
                        var approvedRequest = await _locationChangeRequests.GetActiveForEmployeeAsync(
                            _device.TenantId, employee.Id, cancellationToken);
                        if (approvedRequest is { Status: LocationChangeRequest.StatusApproved })
                            pendingLocationChangeRequestId = approvedRequest.Id;
                    }
                }
            }
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<CheckInResponseDto>.Success(new CheckInResponseDto(
            checkIn.Id,
            checkIn.CheckedInAt,
            checkIn.Latitude,
            checkIn.Longitude,
            checkIn.DeviceSerialNumber,
            FaceScanRequired: true,
            pendingLocationChangeRequestId));
    }
}
