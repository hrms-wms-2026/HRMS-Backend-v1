using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.DeviceState.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Exceptions.Helpers;
using ONEVO.Application.Features.Monitoring.Reports.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Exceptions.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.Exceptions.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Exceptions.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Exceptions.Services;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Services;
using ExceptionType = ONEVO.Domain.Features.Monitoring.Exceptions.Entities.ExceptionType;
using MonitoringException = ONEVO.Domain.Features.Monitoring.Exceptions.Entities.Exception;

namespace ONEVO.Application.Features.Monitoring.Exceptions.Queries.GetExceptionEvidence;

/// <summary>
/// The evidence behind one case, for whoever may see that case (same IExceptionScopeResolver rule
/// as the list). Reads the repositories directly rather than going through the attendance
/// day-detail query, which is gated on attendance:read / monitoring:read - permissions a
/// reporting manager working the alert (attendance:approve) may not hold.
/// </summary>
public class GetExceptionEvidenceQueryHandler : IRequestHandler<GetExceptionEvidenceQuery, Result<ExceptionEvidenceDto>>
{
    public static readonly TimeSpan PhotoUrlExpiry = TimeSpan.FromMinutes(5);
    public const string MonitoringReadPermission = "monitoring:read";

    private readonly IExceptionRepository _exceptions;
    private readonly ICurrentUser _currentUser;
    private readonly IExceptionScopeResolver _scope;
    private readonly IEmployeeRepository _employees;
    private readonly ILegalEntityRepository _legalEntities;
    private readonly IAttendanceReadRepository _attendance;
    private readonly IFaceVerificationAttemptRepository _faceChecks;
    private readonly ICheckInRepository _checkIns;
    private readonly IFileStorageService _fileStorage;
    private readonly IDateTimeProvider _clock;
    private readonly IDeviceStateSnapshotRepository _deviceStates;
    private readonly IActivityDailySummaryRepository _dailySummaries;
    private readonly IProductivityReportRepository _aggregates;

    /// <summary>A full day of per-minute samples is ~1440; room for two devices reporting at once.</summary>
    private const int MaxDeviceSamples = 4000;

    public GetExceptionEvidenceQueryHandler(
        IExceptionRepository exceptions,
        ICurrentUser currentUser,
        IExceptionScopeResolver scope,
        IEmployeeRepository employees,
        ILegalEntityRepository legalEntities,
        IAttendanceReadRepository attendance,
        IFaceVerificationAttemptRepository faceChecks,
        ICheckInRepository checkIns,
        IFileStorageService fileStorage,
        IDateTimeProvider clock,
        IDeviceStateSnapshotRepository deviceStates,
        IActivityDailySummaryRepository dailySummaries,
        IProductivityReportRepository aggregates)
    {
        _deviceStates = deviceStates;
        _dailySummaries = dailySummaries;
        _aggregates = aggregates;
        _exceptions = exceptions;
        _currentUser = currentUser;
        _scope = scope;
        _employees = employees;
        _legalEntities = legalEntities;
        _attendance = attendance;
        _faceChecks = faceChecks;
        _checkIns = checkIns;
        _fileStorage = fileStorage;
        _clock = clock;
    }

    public async Task<Result<ExceptionEvidenceDto>> Handle(GetExceptionEvidenceQuery request, CancellationToken ct)
    {
        var tenantId = _currentUser.TenantId;
        var exception = tenantId == Guid.Empty ? null : await _exceptions.GetByIdAsync(tenantId, request.ExceptionId, ct);
        var scope = await _scope.ResolveAsync(
            forAction: false, exception is null ? [] : [exception.EmployeeId], ct);
        if (scope is null)
            return Result<ExceptionEvidenceDto>.Forbidden();
        // Out-of-scope cases answer the same as missing ones so ids can't be probed.
        if (exception is null || !scope.CanSee(exception.EmployeeId))
            return Result<ExceptionEvidenceDto>.NotFound("Exception not found.");

        var meta = ExceptionMetadata.Parse(exception.MetadataJson);
        var employee = await _employees.GetByIdAsync(tenantId, exception.EmployeeId, ct);
        var legalEntity = employee?.LegalEntityId is Guid legalEntityId
            ? await _legalEntities.GetByIdForTenantAsync(tenantId, legalEntityId, ct)
            : null;
        var zone = CompanyTimeZone.Find(legalEntity?.Timezone);
        var workDate = ResolveWorkDate(exception, meta, zone);
        var day = AttendanceTodayStateService.GetLocalDayWindow(workDate, zone);

        var record = await _attendance.GetRecordAsync(tenantId, exception.EmployeeId, workDate, ct);
        var attendance = record is null
            ? null
            : new AttendanceEvidenceDto(record.ActualStart, record.ActualEnd, record.AttendanceSource, record.Status);

        IdentityEvidenceDto? identity = null;
        IReadOnlyList<FaceCheckEvidenceDto> faceChecks = [];
        IReadOnlyList<CheckInScanEvidenceDto> scans = [];

        if (exception.Type == ExceptionType.IdentityAnomaly)
        {
            identity = await BuildIdentityAsync(tenantId, meta, ct);

            faceChecks = (await _faceChecks.ListForEmployeeInRangeAsync(tenantId, exception.EmployeeId, day.Start, day.End, ct))
                .Select(a => new FaceCheckEvidenceDto(a.Id, a.CreatedAt, a.Purpose, a.Outcome, a.FailureReason, a.SimilarityScore))
                .ToList();

            if (employee is not null && employee.UserId != Guid.Empty)
            {
                scans = (await _checkIns.ListForUserInRangeAsync(tenantId, employee.UserId, day.Start, day.End, ct))
                    .Select(c => new CheckInScanEvidenceDto(
                        c.Id, c.CheckedInAt, c.FaceScan?.Status, c.FaceScan?.SimilarityScore,
                        c.DeviceRegistrationId, c.DeviceSerialNumber))
                    .ToList();
            }
        }

        // Device timelines and activity figures are monitoring data: the rest of the product only
        // shows them for someone else with monitoring:read, so the evidence does the same. The
        // alert's own reason (the measures), attendance and identity evidence stay visible.
        var canSeeActivity = _currentUser.HasPermission(MonitoringReadPermission);

        var deviceActivity = canSeeActivity
            ? DeviceActivityTimeline.Build(await _deviceStates.ListForEmployeeInRangeAsync(
                tenantId, exception.EmployeeId, day.Start, day.End, MaxDeviceSamples, ct))
            : null;

        var pattern = await BuildPatternAsync(tenantId, exception, meta, workDate, canSeeActivity, ct);

        return Result<ExceptionEvidenceDto>.Success(new ExceptionEvidenceDto(
            exception.Id, exception.Type.ToString(), workDate, zone.Id, attendance, identity, faceChecks, scans,
            deviceActivity, pattern, ActivityHidden: !canSeeActivity));
    }

    /// <summary>The days around a nightly multi-day case, and the figures the detection job compared -
    /// as stored on the case, or recomputed (and flagged so) for cases from before they were stored.</summary>
    private async Task<PatternEvidenceDto?> BuildPatternAsync(
        Guid tenantId, MonitoringException exception, ExceptionMetadata meta, DateOnly target,
        bool canSeeActivity, CancellationToken ct)
    {
        var window = ExceptionPatternWindow.For(exception.Type, target);
        if (window is null)
            return null;

        var employeeId = exception.EmployeeId;
        var summaries = (await _dailySummaries.GetRangeAsync(tenantId, employeeId, window.From, window.To, ct))
            .GroupBy(s => s.Date).ToDictionary(g => g.Key, g => g.First());
        var (records, _) = await _attendance.ListRecordsAsync(
            tenantId, [employeeId], window.From, window.To, 0, window.To.DayNumber - window.From.DayNumber + 1, ct);
        var recordsByDate = records.GroupBy(r => r.Date).ToDictionary(g => g.Key, g => g.First());

        var days = new List<DailyPatternPointDto>();
        for (var date = window.From; date <= window.To; date = date.AddDays(1))
        {
            summaries.TryGetValue(date, out var summary);
            recordsByDate.TryGetValue(date, out var record);
            days.Add(new DailyPatternPointDto(
                date,
                canSeeActivity ? summary?.ActivityScore : null,
                canSeeActivity ? summary?.TotalActiveMinutes : null,
                canSeeActivity ? summary?.TotalIdleMinutes : null,
                record?.WorkedMinutes, record?.Status, window.InRule(date)));
        }

        if (meta.Measures is { Count: > 0 } stored)
            return new PatternEvidenceDto(window.From, window.To, days, stored);

        IReadOnlyList<PatternMeasureDto> measures = exception.Type switch
        {
            ExceptionType.SustainedLowActivity => ExceptionPatternMeasures.SustainedLowActivity(
                summaries.Values.Where(s => window.InRule(s.Date)).Select(s => s.ActivityScore).ToList()),
            ExceptionType.AttendanceIrregularity => await RecomputeIrregularityAsync(tenantId, employeeId, target, ct),
            ExceptionType.UnusualActivityPattern => ExceptionPatternMeasures.UnusualActivityPattern(
                summaries.TryGetValue(target, out var targetDay) ? targetDay.ActivityScore : null,
                (await _aggregates.GetEmployeeAggregateAsync(tenantId, employeeId, target.AddDays(-30), target.AddDays(-1), ct)).AverageActivityScore),
            _ => []
        };

        return new PatternEvidenceDto(window.From, window.To, days, measures, MeasuresRecalculated: true);
    }

    private async Task<IReadOnlyList<PatternMeasureDto>> RecomputeIrregularityAsync(
        Guid tenantId, Guid employeeId, DateOnly target, CancellationToken ct)
    {
        var thisWeek = await _aggregates.GetEmployeeAggregateAsync(tenantId, employeeId, target.AddDays(-6), target, ct);
        var trailing = await _aggregates.GetEmployeeAggregateAsync(tenantId, employeeId, target.AddDays(-34), target.AddDays(-7), ct);
        return ExceptionPatternMeasures.AttendanceIrregularity(
            thisWeek.TotalWorkedMinutes, trailing.DayCount > 0 ? trailing.TotalWorkedMinutes / 4 : 0);
    }

    private async Task<IdentityEvidenceDto> BuildIdentityAsync(Guid tenantId, ExceptionMetadata meta, CancellationToken ct)
    {
        string? url = null;
        DateTimeOffset? expiresAt = null;
        if (meta.PhotoFileId is Guid photoFileId)
        {
            try
            {
                var signed = await _fileStorage.GetSignedUrlAsync(tenantId, photoFileId, PhotoUrlExpiry, ct);
                if (signed.IsSuccess)
                {
                    url = signed.Value;
                    expiresAt = _clock.UtcNow + PhotoUrlExpiry;
                }
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                // Storage down: the rest of the evidence is still worth showing.
            }
        }

        return new IdentityEvidenceDto(
            meta.Source, meta.OccurredAt, meta.Purpose, meta.SimilarityScore,
            meta.Reasons ?? [], meta.DeviceRegistrationId, url, expiresAt,
            PhotoUnavailable: meta.PhotoFileId is not null && url is null);
    }

    /// <summary>The day the case is about, in the company timezone. Nightly cases store their target
    /// date; identity cases use when the check happened. Nightly cases from before the date was
    /// stored fall back to the job's own rule: it runs for the previous UTC day.</summary>
    public static DateOnly ResolveWorkDate(MonitoringException exception, ExceptionMetadata meta, TimeZoneInfo zone)
    {
        if (meta.WorkDate is DateOnly stored)
            return stored;
        if (exception.Type == ExceptionType.IdentityAnomaly)
            return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(meta.OccurredAt ?? exception.DetectedAt, zone).DateTime);
        return DateOnly.FromDateTime(exception.DetectedAt.UtcDateTime).AddDays(-1);
    }

}
