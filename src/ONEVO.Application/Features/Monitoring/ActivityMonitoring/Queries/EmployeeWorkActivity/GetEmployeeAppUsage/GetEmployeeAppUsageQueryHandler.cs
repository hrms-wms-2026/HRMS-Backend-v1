using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Helpers;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.Services;
using ONEVO.Application.Features.Monitoring.AppUsage.RepositoryInterfaces;

namespace ONEVO.Application.Features.Monitoring.ActivityMonitoring.Queries.EmployeeWorkActivity.GetEmployeeAppUsage;

public sealed class GetEmployeeAppUsageQueryHandler(
    IEmployeeReadAccessGuard guard,
    IAppUsageSnapshotRepository apps,
    IMonitoringToggleResolver toggles,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<GetEmployeeAppUsageQuery, Result<EmployeeAppUsageResponse>>
{
    public async Task<Result<EmployeeAppUsageResponse>> Handle(GetEmployeeAppUsageQuery request, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;

        var access = await guard.EnsureCanRead(tenantId, request.EmployeeId, ct);
        if (!access.IsSuccess)
            return Result<EmployeeAppUsageResponse>.Failure(access.Error!, access.StatusCode ?? 400);

        var period = EmployeePeriod.Resolve(request.From, request.To, clock.Today);
        if (!period.IsSuccess)
            return Result<EmployeeAppUsageResponse>.Failure(period.Error!, period.StatusCode ?? 400);
        var p = period.Value!;
        if (EmployeeWorkActivityCalculator.ExceedsRawRange(p.From, p.To))
            return Result<EmployeeAppUsageResponse>.Failure($"The period cannot exceed {EmployeeWorkActivityCalculator.MaxRawDays} days.");

        if (!await toggles.IsEnabledForEmployeeAsync(tenantId, request.EmployeeId, MonitoringCapability.ApplicationTracking, ct))
            return Result<EmployeeAppUsageResponse>.Success(new EmployeeAppUsageResponse(p.From, p.To, false, 0, []));

        var (from, to) = EmployeeWorkActivityCalculator.UtcWindow(p.From, p.To);
        var totals = await apps.GetMinutesByProcessAsync(tenantId, request.EmployeeId, from, to, ct);
        var top = EmployeeWorkActivityCalculator.TopProcessNames(totals, EmployeeWorkActivityCalculator.TopApps);
        var samples = top.Count == 0
            ? Array.Empty<AppProcessSampleRow>()
            : await apps.GetSamplesForProcessesAsync(tenantId, request.EmployeeId, from, to, top, ct);

        return Result<EmployeeAppUsageResponse>.Success(new EmployeeAppUsageResponse(
            p.From, p.To, true, totals.Sum(t => t.Samples),
            EmployeeWorkActivityCalculator.AppUsage(totals, samples, EmployeeWorkActivityCalculator.TopApps)));
    }
}
