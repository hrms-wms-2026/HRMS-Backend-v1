using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.Models;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Request.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Services;
using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Application.Features.TimeAttendance.Queries;

public sealed class AttendanceReadHandler(
    ICurrentUser currentUser,
    IEmployeeRepository employees,
    IAttendanceReadRepository attendance,
    IEmployeeAuthorityResolver authority,
    IAttendanceTodayStateService todayState,
    ILeaveRequestReadRepository? leaveRequests = null,
    ILegalEntityRepository? legalEntities = null,
    IDateTimeProvider? dateTimeProvider = null)
    : IRequestHandler<GetAttendanceTodayQuery, Result<AttendanceTodayResponse>>,
      IRequestHandler<GetMyAttendanceHistoryQuery, Result<IReadOnlyList<AttendanceHistoryRow>>>,
      IRequestHandler<GetCoveredAttendanceHistoryQuery, Result<IReadOnlyList<AttendanceHistoryRow>>>
{
    private const string AttendanceReadPermission = "attendance:read";

    public Task<Result<AttendanceTodayResponse>> Handle(GetAttendanceTodayQuery _, CancellationToken ct)
        => todayState.GetTodayAsync(ct);

    public async Task<Result<IReadOnlyList<AttendanceHistoryRow>>> Handle(
        GetMyAttendanceHistoryQuery query, CancellationToken ct)
    {
        var validation = ValidateRange(query.From, query.To);
        if (validation is not null)
            return Result<IReadOnlyList<AttendanceHistoryRow>>.Failure(validation);

        var employee = await employees.GetDefaultForUserAsync(currentUser.TenantId, currentUser.UserId, ct);
        if (employee is null)
            return Result<IReadOnlyList<AttendanceHistoryRow>>.NotFound("Current employee record was not found.");

        var records = await attendance.ListRecordsAsync(
            currentUser.TenantId, [employee.Id], query.From, query.To, ct);
        return Result<IReadOnlyList<AttendanceHistoryRow>>.Success(
            await BuildRowsAsync(records, includeEmployee: false, employee.LegalEntityId, employee.Id, ct));
    }

    public async Task<Result<IReadOnlyList<AttendanceHistoryRow>>> Handle(
        GetCoveredAttendanceHistoryQuery query, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated || !currentUser.HasPermission(AttendanceReadPermission))
            return Result<IReadOnlyList<AttendanceHistoryRow>>.Forbidden();

        var validation = ValidateRange(query.From, query.To);
        if (validation is not null)
            return Result<IReadOnlyList<AttendanceHistoryRow>>.Failure(validation);

        var actor = await employees.GetDefaultForUserAsync(currentUser.TenantId, currentUser.UserId, ct);
        if (actor?.LegalEntityId is null)
            return Result<IReadOnlyList<AttendanceHistoryRow>>.NotFound("Current employee record was not found.");

        var visibility = await authority.ResolveVisibilityAsync(
            new EmployeeAuthorityVisibilityRequest(
                currentUser.UserId,
                actor.LegalEntityId.Value,
                AttendanceReadPermission,
                IncludeSelf: true,
                EmployeeAuthorityPurpose.TimeTrackingRead), ct);

        IReadOnlyCollection<Guid> employeeIds;
        if (query.EmployeeId is Guid requestedEmployeeId)
        {
            if (!visibility.EmployeeIds.Contains(requestedEmployeeId))
                return Result<IReadOnlyList<AttendanceHistoryRow>>.Forbidden();

            employeeIds = [requestedEmployeeId];
        }
        else
        {
            employeeIds = visibility.EmployeeIds;
        }

        var records = await attendance.ListRecordsAsync(
            currentUser.TenantId, employeeIds, query.From, query.To, ct);
        return Result<IReadOnlyList<AttendanceHistoryRow>>.Success(
            await BuildRowsAsync(records, includeEmployee: true, actor.LegalEntityId, actor.Id, ct));
    }

    private async Task<IReadOnlyList<AttendanceHistoryRow>> BuildRowsAsync(
        IReadOnlyList<AttendanceRecord> records,
        bool includeEmployee,
        Guid? legalEntityId,
        Guid currentEmployeeId,
        CancellationToken ct)
    {
        IReadOnlyDictionary<Guid, AttendanceHistoryEmployee> identities =
            includeEmployee && legalEntityId is Guid entityId
                ? await attendance.ListEmployeeIdentitiesAsync(
                    currentUser.TenantId,
                    entityId,
                    records.Select(x => x.EmployeeId).Distinct().ToArray(),
                    ct)
                : new Dictionary<Guid, AttendanceHistoryEmployee>();

        if (records.Count == 0)
            return Array.Empty<AttendanceHistoryRow>();

        var employeeIds = records.Select(record => record.EmployeeId).Distinct().ToArray();
        var from = records.Min(record => record.Date);
        var to = records.Max(record => record.Date);
        var approvedLeaveRequests = leaveRequests is null
            ? Array.Empty<Domain.Features.Leave.Request.Entities.LeaveRequest>()
            : await leaveRequests.ListApprovedCoveringAsync(
                currentUser.TenantId, employeeIds, from, to, ct);
        var leavesByEmployee = approvedLeaveRequests
            .GroupBy(request => request.EmployeeId)
            .ToDictionary(group => group.Key, group => group.ToArray());

        var legalEntity = legalEntities is not null && legalEntityId is Guid entityIdForRead
            ? await legalEntities.GetByIdForTenantAsync(currentUser.TenantId, entityIdForRead, ct)
            : null;
        var timezone = TryFindTimezone(legalEntity?.Timezone ?? records[0].ScheduleTimezone);
        var localWindows = records
            .Select(record => AttendanceTodayStateService.GetLocalDayWindow(record.Date, timezone))
            .ToList();
        var breakRecords = await attendance.ListBreaksForEmployeesAsync(
            currentUser.TenantId,
            employeeIds,
            localWindows.Min(window => window.Start),
            localWindows.Max(window => window.End),
            ct) ?? Array.Empty<BreakRecord>();
        var breaksByEmployee = breakRecords
            .GroupBy(record => record.EmployeeId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<BreakRecord>)group.ToArray());

        return records.Select(record =>
        {
            var hasApprovedLeave = leavesByEmployee.TryGetValue(record.EmployeeId, out var employeeLeaves)
                && employeeLeaves.Any(request => request.StartDate <= record.Date
                    && request.EndDate >= record.Date);
            var schedule = new AttendanceSchedule(
                record.ScheduledStart is not null && record.ScheduledEnd is not null
                    ? "configured"
                    : "not_configured",
                record.ExpectedWorkingDay,
                record.ScheduledStart,
                record.ScheduledEnd,
                record.RequiredWorkMinutes);
            var dayWindow = AttendanceTodayStateService.GetLocalDayWindow(record.Date, timezone);
            var breakUsedMinutes = breaksByEmployee.TryGetValue(record.EmployeeId, out var employeeBreaks)
                ? AttendanceTodayStateService.CalculateBreakUsage(
                    employeeBreaks, dayWindow, dayWindow.End)
                : record.BreakMinutes;
            var localNow = record.Date.ToDateTime(
                record.ScheduledStart ?? TimeOnly.MinValue,
                DateTimeKind.Unspecified);
            var status = AttendanceDayStatusResolver.Resolve(
                schedule,
                "configured",
                record,
                hasApprovedLeave,
                legalEntity?.BreakDurationMinutes,
                breakUsedMinutes,
                new DateTimeOffset(localNow, TimeSpan.Zero));

            return new AttendanceHistoryRow(
                record.Id,
                record.Date,
                includeEmployee && identities.TryGetValue(record.EmployeeId, out var identity) ? identity : null,
                record.ActualStart,
                record.ActualEnd,
                record.ActualStart is not null && record.ActualEnd is null,
                record.BreakMinutes,
                record.WorkedMinutes,
                NormalizeWorkMode(record.ExpectedWorkArea),
                record.AttendanceSource,
                status.Status,
                CanViewDetails: true,
                CanRequestCorrection: record.EmployeeId == currentEmployeeId
                    && record.Date <= (dateTimeProvider?.Today ?? DateOnly.FromDateTime(DateTime.UtcNow)),
                CanRequestWorkAreaChange: false,
                CanCorrect: false,
                status.StatusLabel,
                status.AttentionType,
                status.AttentionLabel,
                status.AttentionSeverity,
                status.BreakOverageMinutes,
                status.IsOverBreakAllowance);
        }).ToList();
    }

    private static TimeZoneInfo TryFindTimezone(string? timezone)
    {
        if (string.IsNullOrWhiteSpace(timezone))
            return TimeZoneInfo.Utc;

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timezone);
        }
        catch
        {
            return TimeZoneInfo.Utc;
        }
    }

    private static string? NormalizeWorkMode(string? value)
        => string.Equals(value, "either", StringComparison.OrdinalIgnoreCase)
            ? "hybrid"
            : value?.ToLowerInvariant();

    private static string? ValidateRange(DateOnly from, DateOnly to)
        => from > to ? "from must be less than or equal to to." : null;
}
