using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.Queries.GetActivityDailySummary;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.DailyReport.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.Screenshots.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.WorkSessions.RepositoryInterfaces;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;

namespace ONEVO.Application.Features.Monitoring.DailyReport.Queries.GetEmployeeDailyReport;

public class GetEmployeeDailyReportQueryHandler
    : IRequestHandler<GetEmployeeDailyReportQuery, Result<EmployeeDailyReportDto>>
{
    private static readonly TimeSpan ScreenshotUrlExpiry = TimeSpan.FromMinutes(15);
    private const int MaxScreenshotsPerReport = 100;

    private readonly IActivityDailySummaryRepository _summaries;
    private readonly IWorkSessionRepository _workSessions;
    private readonly IEvidenceAssetRepository _evidenceAssets;
    private readonly IEmployeeRepository _employees;
    private readonly IFileStorageService _fileStorage;
    private readonly ITenantContext _tenantContext;

    public GetEmployeeDailyReportQueryHandler(
        IActivityDailySummaryRepository summaries,
        IWorkSessionRepository workSessions,
        IEvidenceAssetRepository evidenceAssets,
        IEmployeeRepository employees,
        IFileStorageService fileStorage,
        ITenantContext tenantContext)
    {
        _summaries = summaries;
        _workSessions = workSessions;
        _evidenceAssets = evidenceAssets;
        _employees = employees;
        _fileStorage = fileStorage;
        _tenantContext = tenantContext;
    }

    public async Task<Result<EmployeeDailyReportDto>> Handle(
        GetEmployeeDailyReportQuery request,
        CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        if (tenantId == Guid.Empty)
            return Result<EmployeeDailyReportDto>.Failure("Tenant context is required.", 401);

        if (request.EmployeeId == Guid.Empty)
            return Result<EmployeeDailyReportDto>.Failure("employeeId is required.", 400);

        var employee = await _employees.GetByIdAsync(tenantId, request.EmployeeId, cancellationToken);
        if (employee is null)
            return Result<EmployeeDailyReportDto>.NotFound("Employee not found.");

        var activityEntity = await _summaries.GetAsync(
            tenantId, request.EmployeeId, request.Date, cancellationToken);
        var activityDto = activityEntity is null
            ? null
            : GetActivityDailySummaryQueryHandler.Map(activityEntity);

        var workSessions = await _workSessions.GetForUserAndDateAsync(
            tenantId, employee.UserId, request.Date, cancellationToken);

        DateTimeOffset? clockInAt = workSessions.Count == 0 ? null : workSessions.Min(s => s.ClockInAt);
        DateTimeOffset? clockOutAt = workSessions.Count == 0 ? null : workSessions.Max(s => s.ClockOutAt);
        var breakSeconds = workSessions.Sum(s => s.AccumulatedBreakSeconds);
        var breakSessionCount = workSessions.Sum(s => s.BreakSessionCount);
        var workedSeconds = workSessions.Sum(s => s.AccumulatedWorkSeconds);

        var (screenshotItems, _) = await _evidenceAssets.GetPagedAsync(
            tenantId, request.EmployeeId, request.Date, request.Date, 1, MaxScreenshotsPerReport, cancellationToken);

        var screenshots = new List<ScreenshotEntryDto>(screenshotItems.Count);
        foreach (var asset in screenshotItems)
        {
            var urlResult = await _fileStorage.GetSignedUrlAsync(
                tenantId, asset.FileRecordId, ScreenshotUrlExpiry, cancellationToken);

            screenshots.Add(new ScreenshotEntryDto(
                asset.Id,
                asset.CapturedAt,
                asset.EvidenceType,
                asset.TriggerType,
                urlResult.IsSuccess ? urlResult.Value : null));
        }

        return Result<EmployeeDailyReportDto>.Success(new EmployeeDailyReportDto
        {
            EmployeeId = request.EmployeeId,
            Date = request.Date,
            Activity = activityDto,
            ClockInAt = clockInAt,
            ClockOutAt = clockOutAt,
            BreakMinutes = breakSeconds / 60,
            BreakSessionCount = breakSessionCount,
            WorkedMinutes = workedSeconds / 60,
            Screenshots = screenshots
        });
    }
}
