using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Helpers;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.Services;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Services;

namespace ONEVO.Application.Features.Monitoring.ActivityMonitoring.Queries.EmployeeWorkActivity.GetEmployeeActivityByHour;

/// <summary>Active minutes per local hour of day, summed over the period (legal-entity timezone -
/// the one documented exception to UTC days in Plan 4B), plus the last active snapshot time.</summary>
public sealed class GetEmployeeActivityByHourQueryHandler(
    IEmployeeReadAccessGuard guard,
    IActivitySnapshotRepository snapshots,
    ILegalEntityRepository legalEntities,
    IMonitoringToggleResolver toggles,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<GetEmployeeActivityByHourQuery, Result<EmployeeActivityByHourResponse>>
{
    public async Task<Result<EmployeeActivityByHourResponse>> Handle(GetEmployeeActivityByHourQuery request, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;

        var access = await guard.EnsureCanRead(tenantId, request.EmployeeId, ct);
        if (!access.IsSuccess)
            return Result<EmployeeActivityByHourResponse>.Failure(access.Error!, access.StatusCode ?? 400);

        var period = EmployeePeriod.Resolve(request.From, request.To, clock.Today);
        if (!period.IsSuccess)
            return Result<EmployeeActivityByHourResponse>.Failure(period.Error!, period.StatusCode ?? 400);
        var p = period.Value!;
        if (EmployeeWorkActivityCalculator.ExceedsRawRange(p.From, p.To))
            return Result<EmployeeActivityByHourResponse>.Failure($"The period cannot exceed {EmployeeWorkActivityCalculator.MaxRawDays} days.");

        var legalEntity = access.Value!.LegalEntityId is Guid entityId
            ? await legalEntities.GetByIdForTenantAsync(tenantId, entityId, ct)
            : null;
        var zone = AttendancePeriodCalculator.ResolveTimezone(legalEntity?.Timezone);
        var zoneName = zone == TimeZoneInfo.Utc ? "UTC" : zone.Id;

        if (!await toggles.IsEnabledAsync(tenantId, request.EmployeeId, MonitoringCapability.ActivityMonitoring, ct))
            return Result<EmployeeActivityByHourResponse>.Success(new EmployeeActivityByHourResponse(p.From, p.To, false, zoneName, null, []));

        var from = AttendanceTodayStateService.GetLocalDayWindow(p.From, zone).Start;
        var to = AttendanceTodayStateService.GetLocalDayWindow(p.To, zone).End;
        var slots = await snapshots.GetActiveSecondsByHalfHourAsync(tenantId, request.EmployeeId, from, to, ct);
        var last = await snapshots.GetLastActiveAtAsync(tenantId, request.EmployeeId, from, to, ct);

        return Result<EmployeeActivityByHourResponse>.Success(new EmployeeActivityByHourResponse(
            p.From, p.To, true, zoneName, last, EmployeeWorkActivityCalculator.HourlyActiveMinutes(slots, zone)));
    }
}
