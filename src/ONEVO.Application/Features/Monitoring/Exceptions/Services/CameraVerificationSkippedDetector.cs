using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Exceptions.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Domain.Features.Monitoring.Exceptions.Entities;
using MonitoringException = ONEVO.Domain.Features.Monitoring.Exceptions.Entities.Exception;

namespace ONEVO.Application.Features.Monitoring.Exceptions.Services;

/// <summary>
/// Inbox plan D-b: a work day on which the employee clocked in without completing the required camera verification
/// (identity verification on, no check-in with a face scan that day) becomes an IdentityAnomaly case, so it can be
/// investigated. At most one open identity case per employee per day - the same dedup as a face mismatch. Run by the
/// nightly exception job for the finished day; the employee list's live "camera verification skipped" badge for today
/// is unchanged (D-a).
/// </summary>
public sealed class CameraVerificationSkippedDetector
{
    private readonly IAttendanceReadRepository _attendance;
    private readonly ICheckInRepository _checkIns;
    private readonly IEmployeeRepository _employees;
    private readonly IMonitoringToggleResolver _toggles;
    private readonly IExceptionRepository _exceptions;

    public CameraVerificationSkippedDetector(
        IAttendanceReadRepository attendance, ICheckInRepository checkIns, IEmployeeRepository employees,
        IMonitoringToggleResolver toggles, IExceptionRepository exceptions)
    {
        _attendance = attendance;
        _checkIns = checkIns;
        _employees = employees;
        _toggles = toggles;
        _exceptions = exceptions;
    }

    /// <summary>The new cases for <paramref name="workDate"/> (not yet added to the unit of work).</summary>
    public async Task<IReadOnlyList<MonitoringException>> DetectAsync(
        Guid tenantId, IReadOnlyCollection<Guid> employeeIds, DateOnly workDate, DateTimeOffset now, CancellationToken ct)
    {
        if (employeeIds.Count == 0)
            return [];
        var clockedIn = (await _attendance.ListRecordsForDateAsync(tenantId, employeeIds, workDate, ct))
            .Where(r => r.ActualStart is not null).ToList();
        if (clockedIn.Count == 0)
            return [];

        var people = await _employees.ListByIdsAsync(tenantId, clockedIn.Select(r => r.EmployeeId).Distinct().ToList(), ct);
        var dayStart = new DateTimeOffset(workDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        // A one-day margin each side covers every company time zone; a scan on the record's own day is what counts.
        var checkIns = await _checkIns.ListForUsersInRangeAsync(
            tenantId, people.Values.Select(p => p.UserId).Distinct().ToList(), dayStart.AddDays(-1), dayStart.AddDays(2), ct);

        var cases = new List<MonitoringException>();
        foreach (var record in clockedIn)
        {
            if (!people.TryGetValue(record.EmployeeId, out var employee) || employee.UserId == Guid.Empty)
                continue;
            var start = record.ActualStart!.Value;
            var scanned = checkIns.Any(c => c.UserId == employee.UserId && c.FaceScanId is not null
                && Math.Abs((c.CheckedInAt - start).TotalHours) < 16);
            if (scanned || !await _toggles.IsEnabledAsync(tenantId, employee.Id, MonitoringCapability.IdentityVerification, ct))
                continue;
            if (await _exceptions.HasUnresolvedSinceAsync(tenantId, employee.Id, ExceptionType.IdentityAnomaly, dayStart, ct))
                continue;

            cases.Add(new MonitoringException
            {
                Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employee.Id, Type = ExceptionType.IdentityAnomaly,
                Status = ExceptionStatus.Open, Title = "Camera verification skipped",
                Description = "Clocked in without completing the required camera verification.", DetectedAt = now,
                MetadataJson = new ExceptionMetadata
                {
                    Source = ExceptionMetadata.SourceCameraVerificationSkipped, WorkDate = workDate, OccurredAt = start,
                    Reasons = ["camera_verification_skipped"],
                }.ToJson(),
            });
        }
        return cases;
    }
}
