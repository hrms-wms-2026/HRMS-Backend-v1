using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;
using ONEVO.Application.Features.TimeAttendance.Queries.LocationChangeRequests;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Application.Features.TimeAttendance.Commands.LocationChangeRequests;

/// <summary>Tray-authenticated equivalent of CreateLocationChangeRequestCommand - the tray has no
/// tenant-user session (ITrayCurrentDevice, not ICurrentUser), so identity is resolved from the
/// device's own JWT claims instead.</summary>
public sealed record TraySubmitLocationChangeRequestCommand(
    double Latitude, double Longitude, double? AccuracyMeters, string Reason)
    : IRequest<Result<LocationChangeRequestResponse>>;

public sealed record TrayRespondToLocationChangeRequestCommand(Guid Id, bool Apply)
    : IRequest<Result<LocationChangeRequestResponse>>;

public sealed class TraySubmitLocationChangeRequestCommandHandler(
    LocationChangeRequestWorkflow workflow,
    ITrayCurrentDevice device,
    IEmployeeRepository employees,
    ILegalEntityRepository legalEntities)
    : IRequestHandler<TraySubmitLocationChangeRequestCommand, Result<LocationChangeRequestResponse>>
{
    public async Task<Result<LocationChangeRequestResponse>> Handle(
        TraySubmitLocationChangeRequestCommand request, CancellationToken ct)
    {
        if (!device.IsAuthenticated || device.LegalEntityId is not Guid legalEntityId)
            return Result<LocationChangeRequestResponse>.Failure("A valid tray device token is required.", 401);

        var employee = await employees.GetByUserAndLegalEntityAsync(device.TenantId, device.UserId, legalEntityId, ct);
        if (employee is null)
            return Result<LocationChangeRequestResponse>.NotFound("Current employee record was not found.");
        var legalEntity = await legalEntities.GetByIdForTenantAsync(device.TenantId, legalEntityId, ct);
        if (legalEntity is null)
            return Result<LocationChangeRequestResponse>.NotFound("Company was not found.");

        return await workflow.CreateAsync(
            device.TenantId, employee, legalEntity, request.Latitude, request.Longitude, request.AccuracyMeters, request.Reason, ct);
    }
}

public sealed class GetPendingLocationChangeDecisionQueryHandler(
    ITrayCurrentDevice device,
    IEmployeeRepository employees,
    ILocationChangeRequestRepository requests,
    IMonitoringToggleResolver toggles)
    : IRequestHandler<GetPendingLocationChangeDecisionQuery, Result<LocationChangeRequestResponse?>>
{
    public async Task<Result<LocationChangeRequestResponse?>> Handle(
        GetPendingLocationChangeDecisionQuery query, CancellationToken ct)
    {
        if (!device.IsAuthenticated || device.LegalEntityId is not Guid legalEntityId)
            return Result<LocationChangeRequestResponse?>.Failure("A valid tray device token is required.", 401);

        // Only ever prompt when the employee's WorkLocationVerification monitoring toggle is on -
        // same gate SubmitCheckInCommandHandler uses for auto-registration and EfEmployeeRepository
        // uses for the onsite/remote warning, so "approved request exists" alone is never enough.
        var monitoringEnabled = await toggles.IsEnabledAsync(
            device.TenantId, device.UserId, legalEntityId, MonitoringCapability.WorkLocationVerification, ct);
        if (!monitoringEnabled)
            return Result<LocationChangeRequestResponse?>.Success(null);

        var employee = await employees.GetByUserAndLegalEntityAsync(device.TenantId, device.UserId, legalEntityId, ct);
        if (employee is null)
            return Result<LocationChangeRequestResponse?>.NotFound("Current employee record was not found.");

        var active = await requests.GetActiveForEmployeeAsync(device.TenantId, employee.Id, ct);
        if (active is not { Status: LocationChangeRequest.StatusApproved })
            return Result<LocationChangeRequestResponse?>.Success(null);

        return Result<LocationChangeRequestResponse?>.Success(new LocationChangeRequestResponse(
            active.Id,
            active.EmployeeId,
            $"{employee.FirstName} {employee.LastName}".Trim(),
            active.RequestedLatitude,
            active.RequestedLongitude,
            active.RequestedAccuracyMeters,
            active.Reason,
            active.Status,
            active.RequestedAt,
            active.ReviewedById,
            active.ReviewedAt,
            active.ReviewComment,
            active.AppliedAt));
    }
}

public sealed class TrayRespondToLocationChangeRequestCommandHandler(
    LocationChangeRequestWorkflow workflow,
    ITrayCurrentDevice device,
    IEmployeeRepository employees)
    : IRequestHandler<TrayRespondToLocationChangeRequestCommand, Result<LocationChangeRequestResponse>>
{
    public async Task<Result<LocationChangeRequestResponse>> Handle(
        TrayRespondToLocationChangeRequestCommand request, CancellationToken ct)
    {
        if (!device.IsAuthenticated || device.LegalEntityId is not Guid legalEntityId)
            return Result<LocationChangeRequestResponse>.Failure("A valid tray device token is required.", 401);

        var employee = await employees.GetByUserAndLegalEntityAsync(device.TenantId, device.UserId, legalEntityId, ct);
        if (employee is null)
            return Result<LocationChangeRequestResponse>.NotFound("Current employee record was not found.");

        return await workflow.RespondAsync(device.TenantId, employee.Id, request.Id, request.Apply, ct);
    }
}
