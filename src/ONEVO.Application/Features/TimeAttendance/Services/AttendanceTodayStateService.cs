using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.Models;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Request.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Application.Features.TimeAttendance.Services;

public sealed class AttendanceTodayStateService(
    ICurrentUser currentUser,
    IDateTimeProvider dateTime,
    IEmployeeRepository employees,
    ILegalEntityRepository legalEntities,
    IClockInPolicyRepository policies,
    IAttendanceReadRepository attendance,
    IEmployeeAuthorityResolver authority,
    IExpectedWorkAreaResolver expectedWorkAreas,
    ILeaveRequestReadRepository? leaveRequests = null)
    : IAttendanceTodayStateService
{
    private const string AttendanceReadPermission = "attendance:read";
    public const string ExpectedWorkAreaSourceAttendanceSnapshot = "attendance_record_snapshot";

    public Task<Result<AttendanceTodayContext>> ResolveContextAsync(CancellationToken ct = default)
    {
        if (!currentUser.IsAuthenticated)
            return Task.FromResult(Result<AttendanceTodayContext>.Forbidden());

        return ResolveContextAsync(currentUser.TenantId, currentUser.UserId, ct);
    }

    public async Task<Result<AttendanceTodayContext>> ResolveContextAsync(
        Guid tenantId, Guid userId, CancellationToken ct = default)
    {
        if (tenantId == Guid.Empty)
            return Result<AttendanceTodayContext>.Forbidden("Tenant context missing.");

        var employee = await employees.GetDefaultForUserAsync(tenantId, userId, ct);
        if (employee?.LegalEntityId is null)
            return Result<AttendanceTodayContext>.NotFound("Current employee record was not found.");

        var legalEntity = await legalEntities.GetByIdForTenantAsync(
            tenantId, employee.LegalEntityId.Value, ct);
        if (legalEntity is null)
            return Result<AttendanceTodayContext>.NotFound("Legal entity was not found.");

        var utcNow = dateTime.UtcNow;
        var scheduleResolution = AttendanceScheduleResolver.Resolve(legalEntity, utcNow);
        var timezone = scheduleResolution.Timezone;
        var zone = scheduleResolution.TimeZone;
        var workDate = scheduleResolution.WorkDate;
        var localNow = scheduleResolution.LocalNow;
        var schedule = scheduleResolution.Schedule;

        var expectedAreaResult = await expectedWorkAreas.ResolveAsync(employee, legalEntity, workDate, ct);
        if (!expectedAreaResult.IsSuccess || expectedAreaResult.Value is null)
            return Result<AttendanceTodayContext>.Failure(
                expectedAreaResult.Error ?? "The expected work area could not be resolved.",
                expectedAreaResult.StatusCode ?? 409);

        var expectedArea = expectedAreaResult.Value;
        var policy = await ResolvePolicyAsync(legalEntity.Id, workDate, NormalizeWorkMode(expectedArea.WorkArea), ct);

        return Result<AttendanceTodayContext>.Success(new AttendanceTodayContext(
            employee,
            legalEntity,
            timezone,
            zone,
            workDate,
            utcNow,
            localNow,
            schedule,
            expectedArea.WorkArea,
            expectedArea.Source,
            policy.Policy,
            policy.Status,
            policy.AllowedMethods,
            GetLocalDayWindow(workDate, zone)));
    }

    public async Task<Result<AttendanceTodayResponse>> GetTodayAsync(CancellationToken ct = default)
    {
        var contextResult = await ResolveContextAsync(ct);
        if (!contextResult.IsSuccess)
            return Result<AttendanceTodayResponse>.Failure(contextResult.Error!, contextResult.StatusCode ?? 400);

        var context = contextResult.Value!;
        var attendanceRecord = await attendance.GetRecordAsync(
            currentUser.TenantId, context.Employee.Id, context.WorkDate, ct);
        var breakRecords = await attendance.ListBreaksAsync(
            currentUser.TenantId,
            context.Employee.Id,
            context.LocalDayWindow.Start,
            context.LocalDayWindow.End,
            ct);
        var hasApprovedLeave = leaveRequests is not null
            && (await leaveRequests.ListApprovedCoveringAsync(
                currentUser.TenantId,
                [context.Employee.Id],
                context.WorkDate,
                context.WorkDate,
                ct)).Count != 0;
        var breakUsage = CalculateBreakUsage(breakRecords, context.LocalDayWindow, context.LocalNow);
        var breakState = ResolveBreakState(
            attendanceRecord,
            breakRecords,
            context.LegalEntity.BreakDurationMinutes,
            breakUsage);
        var attendanceState = AttendanceDayStatusResolver.Resolve(
            context.Schedule,
            context.PolicyStatus,
            attendanceRecord,
            hasApprovedLeave,
            breakState.HasOpenBreak,
            context.LegalEntity.BreakDurationMinutes,
            breakUsage,
            context.LocalNow,
            context.UtcNow);
        var actions = ResolveActions(
            attendanceRecord,
            context.Schedule,
            context.PolicyStatus,
            breakState,
            context.LegalEntity.BreakDurationMinutes);
        var shouldHaveClockedIn = attendanceState.ShouldHaveClockedIn;
        var messages = BuildMessages(context.Schedule, context.PolicyStatus, breakState);

        // Today's own row (fetched above by WorkDate) can't reveal a session left open on an
        // earlier day - clock-in/out always write to that day's own row, so a forgotten
        // clock-out from a prior day is otherwise invisible until the employee happens to open
        // that old day's history. Surface it here so it's seen before they clock in again.
        var staleOpenRecord = await attendance.GetAnyOpenRecordAsync(
            currentUser.TenantId, context.Employee.Id, ct);
        var hasStalePriorDay = staleOpenRecord is not null
            && staleOpenRecord.Date != context.WorkDate
            && staleOpenRecord.ActualStart is DateTimeOffset staleStart
            && context.UtcNow - staleStart >= AttendanceDayStatusResolver.MissingClockOutThreshold;
        var effectiveAttentionType = hasStalePriorDay ? "missing_clock_out" : attendanceState.AttentionType;
        var effectiveAttentionLabel = hasStalePriorDay
            ? $"Still shown as clocked in from {staleOpenRecord!.Date:MMM d} — confirm the actual clock-out time"
            : attendanceState.AttentionLabel;
        var effectiveAttentionSeverity = hasStalePriorDay ? "critical" : attendanceState.AttentionSeverity;
        var attentionWorkDate = hasStalePriorDay ? staleOpenRecord!.Date : (DateOnly?)null;
        var visibility = await authority.ResolveVisibilityAsync(
            new EmployeeAuthorityVisibilityRequest(
                currentUser.UserId,
                context.LegalEntity.Id,
                AttendanceReadPermission,
                IncludeSelf: true,
                EmployeeAuthorityPurpose.TimeTrackingRead), ct);

        // Once an attendance row exists, its persisted ExpectedWorkArea is the historical
        // snapshot for the day and takes precedence over today's live resolution, which may have
        // moved on (e.g. a later approval for a different date, or a policy change).
        var effectiveExpectedWorkArea = attendanceRecord?.ExpectedWorkArea ?? context.ExpectedWorkArea;
        var effectiveExpectedWorkAreaSource = attendanceRecord is not null
            ? ExpectedWorkAreaSourceAttendanceSnapshot
            : context.ExpectedWorkAreaSource;

        return Result<AttendanceTodayResponse>.Success(new AttendanceTodayResponse(
            context.Employee.Id,
            context.LegalEntity.Id,
            context.WorkDate,
            context.Timezone,
            context.Schedule.Status,
            context.PolicyStatus,
            context.Schedule.IsWorkingDay,
            IsHoliday: false,
            HolidayName: null,
            context.Schedule.Start?.ToString("HH:mm"),
            context.Schedule.End?.ToString("HH:mm"),
            context.Schedule.RequiredWorkMinutes,
            context.LegalEntity.BreakDurationMinutes,
            breakUsage,
            breakState.RemainingMinutes,
            breakState.State,
            breakRecords.Select(b => new AttendanceTodayBreakInterval(b.BreakStart, b.BreakEnd)).ToArray(),
            NormalizeWorkMode(effectiveExpectedWorkArea),
            attendanceState.Status,
            attendanceRecord?.ActualStart,
            attendanceRecord?.ActualEnd,
            CalculateWorkedMinutes(attendanceRecord, breakUsage, context.LocalNow),
            attendanceRecord?.AttendanceSource,
            actions.CanClockIn,
            actions.CanClockOut,
            actions.CanStartBreak,
            actions.CanEndBreak,
            shouldHaveClockedIn,
            visibility.EmployeeIds.Any(id => id != context.Employee.Id),
            context.AllowedClockInMethods,
            messages,
            attendanceState.StatusLabel,
            effectiveAttentionType,
            effectiveAttentionLabel,
            effectiveAttentionSeverity,
            attendanceState.BreakOverageMinutes,
            attendanceState.IsOverBreakAllowance,
            effectiveExpectedWorkAreaSource,
            attentionWorkDate));
    }

    private async Task<PolicyResolution> ResolvePolicyAsync(
        Guid legalEntityId, DateOnly workDate, string? workMode, CancellationToken ct)
    {
        var active = (await policies.ListByLegalEntityAsync(
                currentUser.TenantId, legalEntityId, includeInactive: false, ct))
            .Where(policy => policy.IsActive
                && policy.ScopeType == ClockInPolicy.ScopeFullCompany
                && policy.EffectiveFrom <= workDate
                && (policy.EffectiveTo is null || policy.EffectiveTo >= workDate))
            .ToList();

        if (active.Count == 0)
            return new PolicyResolution(
                "not_configured",
                null,
                new AllowedClockInMethods(false, false, false, false, false, null));

        if (active.Count > 1)
            return new PolicyResolution(
                "configuration_conflict",
                null,
                new AllowedClockInMethods(false, false, false, false, false, null));

        return new PolicyResolution(
            "configured",
            active[0],
            ResolveAllowedMethods(active[0], workMode));
    }

    private static ActionResolution ResolveActions(
        AttendanceRecord? record,
        AttendanceSchedule schedule,
        string policyStatus,
        BreakResolution breakState,
        int? breakAllowance)
    {
        var activeSession = record?.ActualStart is not null && record.ActualEnd is null;
        var canClockIn = record?.ActualStart is null
            && schedule.Status == "configured"
            && policyStatus == "configured";
        var canClockOut = activeSession;
        var canStartBreak = activeSession
            && !breakState.HasOpenBreak
            && breakAllowance is not null
            && breakState.RemainingMinutes > 0;
        var canEndBreak = activeSession && breakState.HasOpenBreak;
        return new ActionResolution(canClockIn, canClockOut, canStartBreak, canEndBreak);
    }

    private static BreakResolution ResolveBreakState(
        AttendanceRecord? record,
        IReadOnlyList<BreakRecord> records,
        int? allowance,
        int usedMinutes)
    {
        var hasOpenBreak = records.Any(x => x.BreakEnd is null);
        int? remaining = allowance is null ? null : Math.Max(0, allowance.Value - usedMinutes);
        var state = hasOpenBreak
            ? "active"
            : record?.ActualStart is null
                ? "not_started"
                : "ended";
        return new BreakResolution(
            state,
            remaining,
            hasOpenBreak,
            allowance is int configuredAllowance && usedMinutes > configuredAllowance
                ? usedMinutes - configuredAllowance
                : 0,
            allowance is int configured && usedMinutes > configured);
    }

    private static IReadOnlyList<string> BuildMessages(
        AttendanceSchedule schedule,
        string policyStatus,
        BreakResolution breakState)
    {
        var messages = new List<string>();
        if (schedule.Status != "configured") messages.Add("schedule_not_configured");
        if (policyStatus == "not_configured") messages.Add("clock_in_policy_not_configured");
        if (policyStatus == "configuration_conflict") messages.Add("multiple_active_company_policies");
        if (breakState.RemainingMinutes is null) messages.Add("break_allowance_not_configured");
        else if (breakState.IsOverBreakAllowance) messages.Add("break_allowance_exceeded");
        else if (breakState.RemainingMinutes == 0) messages.Add("break_allowance_used");
        return messages;
    }

    public static int CalculateBreakUsage(
        IReadOnlyList<BreakRecord> records,
        AttendanceLocalDayWindow window,
        DateTimeOffset now)
    {
        var effectiveNow = now < window.End ? now : window.End;
        return records.Sum(record =>
        {
            var start = record.BreakStart < window.Start ? window.Start : record.BreakStart;
            var rawEnd = record.BreakEnd ?? effectiveNow;
            var end = rawEnd > window.End ? window.End : rawEnd;
            if (end <= start) return 0;
            return (int)Math.Max(0, (end - start).TotalMinutes);
        });
    }

    public static int CalculateWorkedMinutes(AttendanceRecord? record, int breakUsedMinutes, DateTimeOffset now)
    {
        if (record?.ActualStart is not DateTimeOffset start || record.ActualEnd is not null)
            return record?.WorkedMinutes ?? 0;

        // Past the missing-clock-out threshold we no longer know how long the employee
        // actually worked, so stop projecting a live elapsed count that would keep growing
        // forever. Fall back to the last persisted value until a correction resolves it.
        if (now - start >= AttendanceDayStatusResolver.MissingClockOutThreshold)
            return record.WorkedMinutes;

        return Math.Max(0, (int)(now - start).TotalMinutes - breakUsedMinutes);
    }

    public static AttendanceLocalDayWindow GetLocalDayWindow(DateOnly date, TimeZoneInfo zone)
    {
        var localStart = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var localEnd = date.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var startUtc = TimeZoneInfo.ConvertTimeToUtc(localStart, zone);
        var endUtc = TimeZoneInfo.ConvertTimeToUtc(localEnd, zone);
        return new AttendanceLocalDayWindow(new DateTimeOffset(startUtc), new DateTimeOffset(endUtc));
    }

    private static string? NormalizeWorkMode(string? value)
        => string.Equals(value, "either", StringComparison.OrdinalIgnoreCase)
            ? "hybrid"
            : value?.ToLowerInvariant();

    private static AllowedClockInMethods ResolveAllowedMethods(ClockInPolicy policy, string? mode)
    {
        return mode switch
        {
            "onsite" => new(
                policy.OnsiteWebEnabled,
                policy.OnsiteTrayEnabled,
                policy.OnsiteBiometricEnabled,
                policy.OnsitePhotoRequired,
                policy.LocationVerificationRequired,
                policy.AllowedRadiusMeters),
            "remote" => new(
                policy.RemoteWebEnabled,
                policy.RemoteTrayEnabled,
                policy.RemoteBiometricEnabled,
                policy.RemotePhotoRequired,
                policy.LocationVerificationRequired,
                policy.AllowedRadiusMeters),
            "field" => new(
                policy.FieldWebEnabled,
                policy.FieldTrayEnabled,
                policy.FieldBiometricEnabled,
                policy.FieldPhotoRequirement == ClockInPolicy.FieldPhotoRequired,
                policy.LocationVerificationRequired,
                policy.AllowedRadiusMeters),
            "hybrid" => new(
                policy.EitherWebEnabled,
                policy.EitherTrayEnabled,
                policy.EitherBiometricEnabled,
                policy.EitherPhotoRequired,
                policy.EitherLocationCheckRequired || policy.LocationVerificationRequired,
                policy.AllowedRadiusMeters),
            _ => new AllowedClockInMethods(false, false, false, false, false, null)
        };
    }

    private sealed record PolicyResolution(
        string Status,
        ClockInPolicy? Policy,
        AllowedClockInMethods AllowedMethods);

    private sealed record BreakResolution(
        string State,
        int? RemainingMinutes,
        bool HasOpenBreak,
        int BreakOverageMinutes,
        bool IsOverBreakAllowance);

    private sealed record ActionResolution(bool CanClockIn, bool CanClockOut, bool CanStartBreak, bool CanEndBreak);
}
