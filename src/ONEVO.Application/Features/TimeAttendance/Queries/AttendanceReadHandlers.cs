using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Models;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.Models;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.PositionAssignment.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Calendar.Services;
using ONEVO.Application.Features.Leave.Request.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Type.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.Queries.GetActivityDailySummary;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Screenshots.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Services;
using ONEVO.Application.Features.TimeAttendance.Team.DTOs;
using ONEVO.Application.Features.TimeAttendance.Team.Services;
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
    IDateTimeProvider? dateTimeProvider = null,
    IActivityDailySummaryRepository? activitySummaries = null,
    ICheckInRepository? checkIns = null,
    IEvidenceAssetRepository? evidenceAssets = null,
    IFileStorageService? fileStorage = null,
    IActivityLiveDaySummary? liveActivity = null,
    IInactivityCaptureAttemptRepository? activityChecks = null,
    IClockInPolicyRepository? policies = null,
    IPositionAssignmentRepository? positionAssignments = null,
    ILeaveTypeRepository? leaveTypes = null,
    ILeaveVisibilityScopeProvider? leaveVisibilityScope = null)
    : IRequestHandler<GetAttendanceTodayQuery, Result<AttendanceTodayResponse>>,
      IRequestHandler<GetMyAttendanceHistoryQuery, Result<PagedResult<AttendanceHistoryRow>>>,
      IRequestHandler<GetCoveredAttendanceHistoryQuery, Result<PagedResult<AttendanceHistoryRow>>>,
      IRequestHandler<GetAttendanceDayDetailQuery, Result<AttendanceDayDetailResponse>>,
      IRequestHandler<GetMyAttendanceMonthlySummaryQuery, Result<AttendanceMonthlySummaryResponse>>,
      IRequestHandler<GetCoveredTeamTodayQuery, Result<TeamTodayResponse>>
{
    private const string AttendanceReadPermission = "attendance:read";
    private static readonly TimeSpan ScreenshotUrlExpiry = TimeSpan.FromMinutes(15);
    private const int MaxScreenshotsPerDay = 100;

    // Kept as a plain code constant (not a configurable policy field) because this is a
    // display-only summary, not the payroll-affecting ClockInPolicy.LateArrivalMinute tiers -
    // coupling this count to that graduated deduction schedule would be the wrong dependency.
    private static readonly TimeSpan LateOrEarlyGrace = TimeSpan.Zero;

    public Task<Result<AttendanceTodayResponse>> Handle(GetAttendanceTodayQuery _, CancellationToken ct)
        => todayState.GetTodayAsync(ct);

    public async Task<Result<PagedResult<AttendanceHistoryRow>>> Handle(
        GetMyAttendanceHistoryQuery query, CancellationToken ct)
    {
        var validation = ValidateRange(query.From, query.To);
        if (validation is not null)
            return Result<PagedResult<AttendanceHistoryRow>>.Failure(validation);

        var employee = await employees.GetDefaultForUserAsync(currentUser.TenantId, currentUser.UserId, ct);
        if (employee is null)
            return Result<PagedResult<AttendanceHistoryRow>>.NotFound("Current employee record was not found.");

        var pageNumber = query.Paging.PageNumber < 1 ? 1 : query.Paging.PageNumber;
        var skip = (pageNumber - 1) * query.Paging.PageSize;
        var (records, totalCount) = await attendance.ListRecordsAsync(
            currentUser.TenantId, [employee.Id], query.From, query.To, skip, query.Paging.PageSize, ct);
        var rows = await BuildRowsAsync(records, includeEmployee: false, employee.LegalEntityId, employee.Id, ct);
        return Result<PagedResult<AttendanceHistoryRow>>.Success(
            new PagedResult<AttendanceHistoryRow>(rows, pageNumber, query.Paging.PageSize, totalCount));
    }

    public async Task<Result<AttendanceMonthlySummaryResponse>> Handle(
        GetMyAttendanceMonthlySummaryQuery query, CancellationToken ct)
    {
        var validation = ValidateRange(query.From, query.To);
        if (validation is not null)
            return Result<AttendanceMonthlySummaryResponse>.Failure(validation);
        if (query.To.DayNumber - query.From.DayNumber > 31)
            return Result<AttendanceMonthlySummaryResponse>.Failure("Range must be 31 days or fewer.");

        var employee = await employees.GetDefaultForUserAsync(currentUser.TenantId, currentUser.UserId, ct);
        if (employee is null)
            return Result<AttendanceMonthlySummaryResponse>.NotFound("Current employee record was not found.");

        var (records, _) = await attendance.ListRecordsAsync(
            currentUser.TenantId, [employee.Id], query.From, query.To, 0, 62, ct);

        var legalEntity = legalEntities is not null && employee.LegalEntityId is Guid entityId
            ? await legalEntities.GetByIdForTenantAsync(currentUser.TenantId, entityId, ct)
            : null;
        var timezone = TryFindTimezone(legalEntity?.Timezone ?? records.FirstOrDefault()?.ScheduleTimezone);
        var now = dateTimeProvider?.UtcNow ?? DateTimeOffset.UtcNow;

        var workingDays = 0;
        var daysPresent = 0;
        var lateArrivals = 0;
        var earlyDepartures = 0;
        var missingClockOuts = 0;

        foreach (var record in records)
        {
            if (record.ExpectedWorkingDay)
                workingDays += 1;

            if (record.ActualStart is null)
                continue;

            daysPresent += 1;

            if (record.ScheduledStart is TimeOnly scheduledStart)
            {
                var localStart = TimeZoneInfo.ConvertTime(record.ActualStart.Value, timezone).TimeOfDay;
                if (localStart - scheduledStart.ToTimeSpan() > LateOrEarlyGrace)
                    lateArrivals += 1;
            }

            if (record.ActualEnd is DateTimeOffset actualEnd && record.ScheduledEnd is TimeOnly scheduledEnd)
            {
                var localEnd = TimeZoneInfo.ConvertTime(actualEnd, timezone).TimeOfDay;
                if (scheduledEnd.ToTimeSpan() - localEnd > LateOrEarlyGrace)
                    earlyDepartures += 1;
            }

            if (record.ActualEnd is null
                && now - record.ActualStart.Value >= AttendanceDayStatusResolver.MissingClockOutThreshold)
                missingClockOuts += 1;
        }

        return Result<AttendanceMonthlySummaryResponse>.Success(
            new AttendanceMonthlySummaryResponse(workingDays, daysPresent, lateArrivals, earlyDepartures, missingClockOuts));
    }

    public async Task<Result<PagedResult<AttendanceHistoryRow>>> Handle(
        GetCoveredAttendanceHistoryQuery query, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated || !currentUser.HasPermission(AttendanceReadPermission))
            return Result<PagedResult<AttendanceHistoryRow>>.Forbidden();

        var validation = ValidateRange(query.From, query.To);
        if (validation is not null)
            return Result<PagedResult<AttendanceHistoryRow>>.Failure(validation);

        var actor = await employees.GetDefaultForUserAsync(currentUser.TenantId, currentUser.UserId, ct);
        if (actor?.LegalEntityId is null)
            return Result<PagedResult<AttendanceHistoryRow>>.NotFound("Current employee record was not found.");

        var visibility = await authority.ResolveVisibilityAsync(
            new EmployeeAuthorityVisibilityRequest(
                currentUser.UserId,
                actor.LegalEntityId.Value,
                AttendanceReadPermission,
                IncludeSelf: false,
                EmployeeAuthorityPurpose.TimeTrackingRead), ct);

        // The covered ("Team") view is strictly other people — the actor's own history lives on
        // the "My" tab. IncludeSelf: false stops the self channel, but company-wide or department
        // coverage still expands to every active employee in the legal entity, which re-introduces
        // the actor, so strip their id explicitly here too.
        var coveredEmployeeIds = visibility.EmployeeIds.Where(id => id != actor.Id).ToList();

        IReadOnlyCollection<Guid> employeeIds;
        if (query.EmployeeId is Guid requestedEmployeeId)
        {
            if (requestedEmployeeId == actor.Id || !coveredEmployeeIds.Contains(requestedEmployeeId))
                return Result<PagedResult<AttendanceHistoryRow>>.Forbidden();

            employeeIds = [requestedEmployeeId];
        }
        else
        {
            employeeIds = coveredEmployeeIds;
        }

        var pageNumber = query.Paging.PageNumber < 1 ? 1 : query.Paging.PageNumber;
        var skip = (pageNumber - 1) * query.Paging.PageSize;
        var (records, totalCount) = await attendance.ListRecordsAsync(
            currentUser.TenantId, employeeIds, query.From, query.To, skip, query.Paging.PageSize, ct);
        var rows = await BuildRowsAsync(records, includeEmployee: true, actor.LegalEntityId, actor.Id, ct);
        return Result<PagedResult<AttendanceHistoryRow>>.Success(
            new PagedResult<AttendanceHistoryRow>(rows, pageNumber, query.Paging.PageSize, totalCount));
    }

    public async Task<Result<AttendanceDayDetailResponse>> Handle(
        GetAttendanceDayDetailQuery query, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result<AttendanceDayDetailResponse>.Forbidden();

        var actor = await employees.GetDefaultForUserAsync(currentUser.TenantId, currentUser.UserId, ct);
        if (actor is null)
            return Result<AttendanceDayDetailResponse>.NotFound("Current employee record was not found.");

        var isSelf = query.EmployeeId == actor.Id;
        bool canSeeActivity;

        if (isSelf)
        {
            canSeeActivity = true;
        }
        else
        {
            if (!currentUser.HasPermission(AttendanceReadPermission))
                return Result<AttendanceDayDetailResponse>.Forbidden();
            if (actor.LegalEntityId is null)
                return Result<AttendanceDayDetailResponse>.NotFound("Current employee record was not found.");

            var visibility = await authority.ResolveVisibilityAsync(
                new EmployeeAuthorityVisibilityRequest(
                    currentUser.UserId,
                    actor.LegalEntityId.Value,
                    AttendanceReadPermission,
                    IncludeSelf: true,
                    EmployeeAuthorityPurpose.TimeTrackingRead), ct);

            if (!visibility.EmployeeIds.Contains(query.EmployeeId))
                return Result<AttendanceDayDetailResponse>.Forbidden();

            canSeeActivity = currentUser.HasPermission("monitoring:read");
        }

        var record = await attendance.GetRecordAsync(currentUser.TenantId, query.EmployeeId, query.Date, ct);
        if (record is null)
            return Result<AttendanceDayDetailResponse>.NotFound("No attendance record was found for this date.");

        var rows = await BuildRowsAsync([record], includeEmployee: !isSelf, actor.LegalEntityId, actor.Id, ct);
        var summary = rows[0];

        var legalEntity = legalEntities is not null && actor.LegalEntityId is Guid entityId
            ? await legalEntities.GetByIdForTenantAsync(currentUser.TenantId, entityId, ct)
            : null;
        var timezone = TryFindTimezone(legalEntity?.Timezone ?? record.ScheduleTimezone);
        var dayWindow = AttendanceTodayStateService.GetLocalDayWindow(query.Date, timezone);
        var breaks = await attendance.ListBreaksAsync(
            currentUser.TenantId, query.EmployeeId, dayWindow.Start, dayWindow.End, ct)
            ?? Array.Empty<BreakRecord>();

        var timelineEvents = new List<TimelineEvent>();
        if (record.ActualStart is DateTimeOffset clockIn)
            timelineEvents.Add(new TimelineEvent("ClockIn", clockIn, record.AttendanceSource ?? "web"));
        foreach (var breakRecord in breaks)
        {
            timelineEvents.Add(new TimelineEvent("BreakStart", breakRecord.BreakStart, breakRecord.AutoDetected ? "desktop_tray" : "web"));
            if (breakRecord.BreakEnd is DateTimeOffset breakEnd)
                timelineEvents.Add(new TimelineEvent("BreakEnd", breakEnd, breakRecord.AutoDetected ? "desktop_tray" : "web"));
        }
        if (record.ActualEnd is DateTimeOffset clockOut)
            timelineEvents.Add(new TimelineEvent("ClockOut", clockOut, record.AttendanceSource ?? "web"));
        timelineEvents = timelineEvents.OrderBy(item => item.Timestamp).ToList();

        ActivityDailySummaryDto? dailyActivity = null;
        if (canSeeActivity && activitySummaries is not null)
        {
            var activityEntity = await activitySummaries.GetAsync(currentUser.TenantId, query.EmployeeId, query.Date, ct);
            if (activityEntity is not null)
                dailyActivity = GetActivityDailySummaryQueryHandler.Map(activityEntity);
            else if (liveActivity is not null)
                dailyActivity = await liveActivity.ComposeAsync(currentUser.TenantId, query.EmployeeId, query.Date, ct);
        }

        var checkInLocations = Array.Empty<CheckInLocationDto>() as IReadOnlyList<CheckInLocationDto>;
        IReadOnlyList<AttendanceDayScreenshotDto> screenshots = [];
        if (canSeeActivity)
        {
            var targetEmployee = await employees.GetByIdAsync(currentUser.TenantId, query.EmployeeId, ct);
            if (checkIns is not null && targetEmployee is not null)
            {
                var checkInRecords = await checkIns.ListForUserInRangeAsync(
                    currentUser.TenantId, targetEmployee.UserId, dayWindow.Start, dayWindow.End, ct);
                checkInLocations = checkInRecords
                    .Select(c => new CheckInLocationDto(
                        c.Id, c.CheckedInAt, c.Latitude, c.Longitude, c.LocationAccuracy, c.LocationAddress))
                    .ToList();
            }

            if (evidenceAssets is not null && fileStorage is not null)
            {
                var ownerIds = new List<Guid> { query.EmployeeId };
                if (targetEmployee is not null && targetEmployee.UserId != Guid.Empty && targetEmployee.UserId != query.EmployeeId)
                    ownerIds.Add(targetEmployee.UserId);

                var assets = await evidenceAssets.ListForOwnersInRangeAsync(
                    currentUser.TenantId, ownerIds, dayWindow.Start, dayWindow.End, MaxScreenshotsPerDay, ct);
                var signed = new List<AttendanceDayScreenshotDto>(assets.Count);
                foreach (var asset in assets)
                {
                    string? url = null;
                    try
                    {
                        var urlResult = await fileStorage.GetSignedUrlAsync(
                            currentUser.TenantId, asset.FileRecordId, ScreenshotUrlExpiry, ct);
                        if (urlResult.IsSuccess)
                            url = urlResult.Value;
                    }
                    catch
                    {
                        url = null;
                    }

                    signed.Add(new AttendanceDayScreenshotDto(
                        asset.Id,
                        asset.CapturedAt,
                        asset.EvidenceType,
                        asset.TriggerType,
                        url));
                }

                screenshots = signed;
            }
        }

        IReadOnlyList<AttendanceActivityCheckDto> checks = [];
        if (canSeeActivity && activityChecks is not null)
        {
            var attempts = await activityChecks.ListForEmployeeInRangeAsync(
                currentUser.TenantId, query.EmployeeId, dayWindow.Start, dayWindow.End, ct);
            var urlByAsset = screenshots
                .Where(shot => shot.Url is not null)
                .ToDictionary(shot => shot.Id, shot => shot.Url);
            checks = attempts.Select(attempt => new AttendanceActivityCheckDto(
                attempt.Id,
                attempt.PromptedAt,
                attempt.Outcome,
                attempt.EvidenceAssetId is Guid assetId && urlByAsset.TryGetValue(assetId, out var url) ? url : null))
                .ToList();
        }

        return Result<AttendanceDayDetailResponse>.Success(
            new AttendanceDayDetailResponse(summary, timelineEvents, dailyActivity, checkInLocations, screenshots, checks));
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
                && employeeLeaves.Any(request => DateOnly.FromDateTime(request.StartAt.UtcDateTime) <= record.Date
                    && DateOnly.FromDateTime(request.EndAt.UtcDateTime) >= record.Date);
            var schedule = new AttendanceSchedule(
                record.ScheduledStart is not null && record.ScheduledEnd is not null
                    ? "configured"
                    : "not_configured",
                record.ExpectedWorkingDay,
                record.ScheduledStart,
                record.ScheduledEnd,
                record.RequiredWorkMinutes);
            var dayWindow = AttendanceTodayStateService.GetLocalDayWindow(record.Date, timezone);
            var now = dateTimeProvider?.UtcNow ?? DateTimeOffset.UtcNow;
            var hasEmployeeBreaks = breaksByEmployee.TryGetValue(record.EmployeeId, out var employeeBreaks);
            var hasOpenBreak = hasEmployeeBreaks && employeeBreaks!.Any(breakRecord => breakRecord.BreakEnd is null);
            var breakUsedMinutes = hasEmployeeBreaks
                ? AttendanceTodayStateService.CalculateBreakUsage(employeeBreaks!, dayWindow, now)
                : record.BreakMinutes;
            var localNow = record.Date.ToDateTime(
                record.ScheduledStart ?? TimeOnly.MinValue,
                DateTimeKind.Unspecified);
            var status = AttendanceDayStatusResolver.Resolve(
                schedule,
                "configured",
                record,
                hasApprovedLeave,
                hasOpenBreak,
                legalEntity?.BreakDurationMinutes,
                breakUsedMinutes,
                new DateTimeOffset(localNow, TimeSpan.Zero),
                now);

            return new AttendanceHistoryRow(
                record.Id,
                record.Date,
                includeEmployee && identities.TryGetValue(record.EmployeeId, out var identity) ? identity : null,
                record.ActualStart,
                record.ActualEnd,
                record.ActualStart is not null && record.ActualEnd is null,
                record.BreakMinutes,
                AttendanceTodayStateService.CalculateWorkedMinutes(record, breakUsedMinutes, now),
                record.ExpectedWorkModeName?.ToLowerInvariant(),
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
                status.IsOverBreakAllowance,
                schedule.Start?.ToString("HH:mm"),
                schedule.End?.ToString("HH:mm"),
                schedule.RequiredWorkMinutes);
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

    private static string? ValidateRange(DateOnly from, DateOnly to)
        => from > to ? "from must be less than or equal to to." : null;

    public async Task<Result<TeamTodayResponse>> Handle(GetCoveredTeamTodayQuery query, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated || !currentUser.HasPermission(AttendanceReadPermission))
            return Result<TeamTodayResponse>.Forbidden();

        var limit = Math.Clamp(query.Limit <= 0 ? 50 : query.Limit, 1, 200);

        var actor = await employees.GetDefaultForUserAsync(currentUser.TenantId, currentUser.UserId, ct);
        if (actor?.LegalEntityId is null)
            return Result<TeamTodayResponse>.NotFound("Current employee record was not found.");

        var legalEntity = legalEntities is null
            ? null
            : await legalEntities.GetByIdForTenantAsync(currentUser.TenantId, actor.LegalEntityId.Value, ct);
        if (legalEntity is null)
            return Result<TeamTodayResponse>.NotFound("Current employee record was not found.");

        var visibility = await authority.ResolveVisibilityAsync(
            new EmployeeAuthorityVisibilityRequest(
                currentUser.UserId,
                actor.LegalEntityId.Value,
                AttendanceReadPermission,
                IncludeSelf: false,
                EmployeeAuthorityPurpose.TimeTrackingRead), ct);

        // Same defensive strip as GetCoveredAttendanceHistoryQuery above: IncludeSelf:false stops
        // the self channel, but company-wide/department coverage can still re-introduce the actor.
        var coveredEmployeeIds = visibility.EmployeeIds.Where(id => id != actor.Id).ToList();

        var utcNow = dateTimeProvider?.UtcNow ?? DateTimeOffset.UtcNow;
        var scheduleResolution = AttendanceScheduleResolver.Resolve(legalEntity, utcNow);
        var workDate = scheduleResolution.WorkDate;

        if (coveredEmployeeIds.Count == 0)
        {
            return Result<TeamTodayResponse>.Success(new TeamTodayResponse(
                workDate,
                scheduleResolution.Timezone,
                legalEntity.Id,
                new TeamTodaySummary(0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
                Array.Empty<TeamTodayMember>(),
                0));
        }

        var dayWindow = AttendanceTodayStateService.GetLocalDayWindow(workDate, scheduleResolution.TimeZone);

        var records = await attendance.ListRecordsForDateAsync(currentUser.TenantId, coveredEmployeeIds, workDate, ct);
        var recordsByEmployee = records.ToDictionary(r => r.EmployeeId);

        var breakRecords = await attendance.ListBreaksForEmployeesAsync(
            currentUser.TenantId, coveredEmployeeIds, dayWindow.Start, dayWindow.End, ct)
            ?? Array.Empty<BreakRecord>();
        var breaksByEmployee = breakRecords
            .GroupBy(record => record.EmployeeId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<BreakRecord>)group.ToArray());

        var approvedLeave = leaveRequests is null
            ? Array.Empty<Domain.Features.Leave.Request.Entities.LeaveRequest>()
            : await leaveRequests.ListApprovedCoveringAsync(
                currentUser.TenantId, coveredEmployeeIds, workDate, workDate, ct);
        var leaveByEmployee = approvedLeave
            .GroupBy(request => request.EmployeeId)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(request => request.EndAt).First());

        var policyStatus = "not_configured";
        if (policies is not null)
        {
            var activePolicies = ClockInPolicyResolver.ResolveActiveFullCompanyPolicies(
                await policies.ListByLegalEntityAsync(currentUser.TenantId, legalEntity.Id, includeInactive: false, ct),
                workDate);
            policyStatus = activePolicies.Count switch
            {
                0 => "not_configured",
                1 => "configured",
                _ => "configuration_conflict",
            };
        }

        var identities = await attendance.ListEmployeeIdentitiesAsync(
            currentUser.TenantId, legalEntity.Id, coveredEmployeeIds, ct);
        var employeesById = await employees.ListByIdsAsync(currentUser.TenantId, coveredEmployeeIds, ct);
        var primaryPositionByEmployee = positionAssignments is null
            ? new Dictionary<Guid, ONEVO.Domain.Features.CoreHr.Entities.PositionAssignment>()
            : await positionAssignments.GetActivePrimaryByEmployeeIdsAsync(currentUser.TenantId, coveredEmployeeIds, ct);
        var leaveTypeNames = leaveTypes is null
            ? new Dictionary<Guid, string>()
            : (await leaveTypes.ListAsync(currentUser.TenantId, includeInactive: true, ct))
                .ToDictionary(type => type.Id, type => type.Name);

        EmployeeVisibilityScope? leaveScope = null;
        if (leaveVisibilityScope is not null)
        {
            var scopeResolution = await leaveVisibilityScope.ResolveForCurrentUserAsync(ct);
            leaveScope = scopeResolution.Scope;
        }

        var members = new List<TeamTodayMember>(coveredEmployeeIds.Count);
        var counts = new int[7]; // working, onBreak, clockedOut, absent, notStarted, onLeave, notScheduled
        var lateCount = 0;
        var needsAttentionCount = 0;

        foreach (var employeeId in coveredEmployeeIds)
        {
            recordsByEmployee.TryGetValue(employeeId, out var record);
            var employeeBreaks = breaksByEmployee.TryGetValue(employeeId, out var employeeBreakRecords)
                ? employeeBreakRecords
                : Array.Empty<BreakRecord>();
            var hasOpenBreak = employeeBreaks.Any(breakRecord => breakRecord.BreakEnd is null);
            var breakUsedMinutes = AttendanceTodayStateService.CalculateBreakUsage(
                employeeBreaks, dayWindow, scheduleResolution.LocalNow);
            var hasApprovedLeave = leaveByEmployee.TryGetValue(employeeId, out var leaveRequest);

            var resolution = AttendanceDayStatusResolver.Resolve(
                scheduleResolution.Schedule,
                policyStatus,
                record,
                hasApprovedLeave,
                hasOpenBreak,
                legalEntity.BreakDurationMinutes,
                breakUsedMinutes,
                scheduleResolution.LocalNow,
                utcNow);

            employeesById.TryGetValue(employeeId, out var employeeRow);
            primaryPositionByEmployee.TryGetValue(employeeId, out var primaryPosition);
            var leaveAuthorized = leaveScope is not null && EmployeeVisibilityScopeMatcher.Includes(
                leaveScope, employeeId, primaryPosition?.PositionId, employeeRow?.DepartmentId, employeeRow?.LegalEntityId);

            var arrivedLate = record?.Status == AttendanceRecord.StatusLate;
            var mapped = TeamStatusMapper.Map(resolution, arrivedLate, leaveAuthorized);

            TeamTodayLeave? leaveBlock = null;
            if (mapped.ShowLeaveDetail && hasApprovedLeave)
            {
                var leaveTypeName = leaveTypeNames.TryGetValue(leaveRequest!.LeaveTypeId, out var name) ? name : "Leave";
                var endsOn = DateOnly.FromDateTime(
                    TimeZoneInfo.ConvertTime(leaveRequest.EndAt, scheduleResolution.TimeZone).DateTime);
                leaveBlock = new TeamTodayLeave(leaveTypeName, endsOn);
            }

            identities.TryGetValue(employeeId, out var identity);

            members.Add(new TeamTodayMember(
                employeeId,
                identity?.DisplayName ?? "Unknown",
                identity?.AvatarFileId,
                identity?.Position,
                mapped.TeamStatus,
                TeamStatusLabel(mapped.TeamStatus),
                mapped.IsLate,
                record?.ActualStart,
                record?.ActualEnd,
                mapped.AttentionType,
                mapped.AttentionLabel,
                mapped.AttentionSeverity,
                leaveBlock));

            var countIndex = mapped.TeamStatus switch
            {
                "working" => 0,
                "on_break" => 1,
                "clocked_out" => 2,
                "absent" => 3,
                "not_started" => 4,
                "on_leave" => 5,
                "not_scheduled" => 6,
                _ => -1,
            };
            if (countIndex >= 0) counts[countIndex]++;
            if (mapped.IsLate) lateCount++;
            if (mapped.AttentionSeverity is not null) needsAttentionCount++;
        }

        var ordered = members
            .OrderBy(member => AttentionSeverityRank(member.AttentionSeverity))
            .ThenBy(member => member.Status == "absent" ? 0 : 1)
            .ThenBy(member => member.IsLate ? 0 : 1)
            .ThenBy(member => member.DisplayName, StringComparer.Ordinal)
            .ThenBy(member => member.EmployeeId)
            .ToList();

        var summary = new TeamTodaySummary(
            members.Count, counts[0], counts[1], counts[2], lateCount,
            counts[3], counts[4], counts[5], counts[6], needsAttentionCount);

        return Result<TeamTodayResponse>.Success(new TeamTodayResponse(
            workDate,
            scheduleResolution.Timezone,
            legalEntity.Id,
            summary,
            ordered.Take(limit).ToList(),
            members.Count));
    }

    private static int AttentionSeverityRank(string? severity) => severity switch
    {
        "critical" => 0,
        "warning" => 1,
        _ => 2,
    };

    private static string TeamStatusLabel(string teamStatus) => teamStatus switch
    {
        "working" => "Working",
        "on_break" => "On Break",
        "clocked_out" => "Clocked Out",
        "absent" => "Absent",
        "not_started" => "Not Started",
        "on_leave" => "On Leave",
        "not_scheduled" => "Not Scheduled",
        _ => teamStatus,
    };
}
