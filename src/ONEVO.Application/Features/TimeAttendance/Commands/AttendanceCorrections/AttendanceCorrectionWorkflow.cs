using System.Text.Json;
using ONEVO.Application.Common.Exceptions;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.Models;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Request.RepositoryInterfaces;
using MediatR;
using CoreEmployeeRepository = ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;
using ONEVO.Application.Features.TimeAttendance.Queries.AttendanceCorrections;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Services;
using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Application.Features.TimeAttendance.Commands.AttendanceCorrections;

public sealed class AttendanceCorrectionWorkflow(
    ICurrentUser currentUser,
    IDateTimeProvider dateTime,
    CoreEmployeeRepository employees,
    ILegalEntityRepository legalEntities,
    IClockInPolicyRepository policies,
    IAttendanceReadRepository attendance,
    IAttendanceCorrectionRepository corrections,
    IEmployeeAuthorityResolver authority,
    IPositionRepository positions,
    INotificationDispatcher notifications,
    IUnitOfWork unitOfWork,
    ILeaveRequestReadRepository? leaveRequests = null)
{
    private const string ApprovalPermission = "attendance:approve";
    private const string RelatedType = "attendance_correction";
    private const string CreatedTemplate = "attendance_correction_request_created";
    private const string DecidedTemplate = "attendance_correction_request_decided";
    private const string CancelledTemplate = "attendance_correction_request_cancelled";

    public async Task<Result<AttendanceCorrectionPreviewResponse>> PreviewAsync(
        PreviewAttendanceCorrectionCommand request, CancellationToken ct)
    {
        var prepared = await PrepareAsync(request.WorkDate, request.CorrectionType,
            request.RequestedClockInAt, request.RequestedClockOutAt, request.RequestedBreaks,
            request.Reason, request.Notes, createRecord: false, ct);
        if (!prepared.IsSuccess)
            return Result<AttendanceCorrectionPreviewResponse>.Failure(prepared.Error!, prepared.StatusCode ?? 400);

        var value = prepared.Value!;
        return Result<AttendanceCorrectionPreviewResponse>.Success(
            new(value.Policy.CorrectionRequiresApproval, value.Approver));
    }

    public async Task<Result<AttendanceCorrectionResponse>> RequestAsync(
        RequestAttendanceCorrectionCommand request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result<AttendanceCorrectionResponse>.Forbidden("Authentication is required.");

        try
        {
            return await unitOfWork.ExecuteInTransactionAsync(async transactionCt =>
            {
                var prepared = await PrepareAsync(request.WorkDate, request.CorrectionType,
                    request.RequestedClockInAt, request.RequestedClockOutAt, request.RequestedBreaks,
                    request.Reason, request.Notes, createRecord: true, transactionCt);
                if (!prepared.IsSuccess)
                    return Result<AttendanceCorrectionResponse>.Failure(prepared.Error!, prepared.StatusCode ?? 400);

                var value = prepared.Value!;
                var correction = BuildCorrection(value, request, transactionCt);
                await corrections.AddAsync(correction, transactionCt);

                if (value.Policy.CorrectionRequiresApproval)
                {
                    await notifications.SendTemplatedAsync(
                        currentUser.TenantId,
                        value.Approver!.UserId,
                        CreatedTemplate,
                        new Dictionary<string, string>
                        {
                            ["employeeName"] = DisplayName(value.Employee)
                        },
                        RelatedType,
                        correction.Id,
                        transactionCt);
                }
                else
                {
                    await ApplyCorrectionAsync(correction, value.Record!, value.Schedule, value.TimeZone,
                        value.LegalEntity.BreakDurationMinutes, value.PolicyStatus, transactionCt);
                    correction.Status = AttendanceCorrection.StatusApproved;
                    correction.ReviewedById = currentUser.UserId;
                    correction.ReviewedAt = dateTime.UtcNow;
                    await notifications.SendTemplatedAsync(
                        currentUser.TenantId,
                        currentUser.UserId,
                        DecidedTemplate,
                        new Dictionary<string, string>
                        {
                            ["decision"] = "approved"
                        },
                        RelatedType,
                        correction.Id,
                        transactionCt);
                }

                await corrections.SaveChangesAsync(transactionCt);
                return Result<AttendanceCorrectionResponse>.Success(
                    ToResponse(correction, value.Employee, value.TimeZone.Id, value.Approver));
            }, ct);
        }
        catch (UniqueConstraintConflictException)
        {
            return Result<AttendanceCorrectionResponse>.Conflict(
                "A pending correction for this attendance day and correction type already exists.");
        }
        catch (ConcurrencyConflictException)
        {
            return Result<AttendanceCorrectionResponse>.Conflict(
                "This attendance correction was changed by another request. Please refresh and try again.");
        }
    }

    public async Task<Result<AttendanceCorrectionResponse>> ApproveAsync(
        ApproveAttendanceCorrectionCommand request, CancellationToken ct)
        => await DecideAsync(request.Id, AttendanceCorrection.StatusApproved, request.ReviewComment, ct);

    public async Task<Result<AttendanceCorrectionResponse>> RejectAsync(
        RejectAttendanceCorrectionCommand request, CancellationToken ct)
        => await DecideAsync(request.Id, AttendanceCorrection.StatusRejected, request.ReviewComment, ct);

    public async Task<Result<AttendanceCorrectionResponse>> CancelAsync(
        CancelAttendanceCorrectionCommand request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result<AttendanceCorrectionResponse>.Forbidden("Authentication is required.");

        try
        {
            return await unitOfWork.ExecuteInTransactionAsync(async transactionCt =>
            {
                var correction = await corrections.GetTrackedByIdAsync(currentUser.TenantId, request.Id, transactionCt);
                if (correction is null)
                    return Result<AttendanceCorrectionResponse>.NotFound("Attendance correction was not found.");
                if (correction.RequestedById != currentUser.UserId || correction.EmployeeId == Guid.Empty)
                    return Result<AttendanceCorrectionResponse>.Forbidden("Only the requester can cancel this correction.");
                if (correction.Status != AttendanceCorrection.StatusPending)
                    return Result<AttendanceCorrectionResponse>.Conflict("Only a pending correction can be cancelled.");

                correction.Status = AttendanceCorrection.StatusCancelled;
                correction.ReviewedById = currentUser.UserId;
                correction.ReviewedAt = dateTime.UtcNow;
                correction.UpdatedAt = dateTime.UtcNow;
                await notifications.SendTemplatedAsync(
                    currentUser.TenantId,
                    correction.RequestedById,
                    CancelledTemplate,
                    new Dictionary<string, string>(),
                    RelatedType,
                    correction.Id,
                    transactionCt);
                await corrections.SaveChangesAsync(transactionCt);

                var employee = await employees.GetByIdAsync(currentUser.TenantId, correction.EmployeeId, transactionCt);
                var legalEntity = await legalEntities.GetByIdForTenantAsync(
                    currentUser.TenantId, correction.LegalEntityId, transactionCt);
                return Result<AttendanceCorrectionResponse>.Success(
                    ToResponse(correction, employee, ResolveTimezone(legalEntity, correction.WorkDate)));
            }, ct);
        }
        catch (ConcurrencyConflictException)
        {
            return Result<AttendanceCorrectionResponse>.Conflict(
                "This attendance correction was changed by another request. Please refresh and try again.");
        }
    }

    public async Task<Result<IReadOnlyList<AttendanceCorrectionResponse>>> ListMyAsync(
        ListMyAttendanceCorrectionsQuery request, CancellationToken ct)
    {
        var context = await ResolveEmployeeContextAsync(ct);
        if (!context.IsSuccess)
            return Result<IReadOnlyList<AttendanceCorrectionResponse>>.Failure(context.Error!, context.StatusCode ?? 400);

        var value = context.Value!;
        var rows = await corrections.ListMyAsync(currentUser.TenantId, value.Employee.Id,
            request.From, request.To, request.Status, ct);
        return Result<IReadOnlyList<AttendanceCorrectionResponse>>.Success(
            rows.Select(row => ToResponse(row, value.Employee,
                ResolveTimezone(value.LegalEntity, row.WorkDate))).ToArray());
    }

    public async Task<Result<IReadOnlyList<AttendanceCorrectionResponse>>> ListApprovalsAsync(
        ListAttendanceCorrectionApprovalsQuery request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result<IReadOnlyList<AttendanceCorrectionResponse>>.Forbidden("Authentication is required.");
        if (!currentUser.HasPermission(ApprovalPermission))
            return Result<IReadOnlyList<AttendanceCorrectionResponse>>.Forbidden("You do not have permission to approve attendance corrections.");

        var context = await ResolveEmployeeContextAsync(ct);
        if (!context.IsSuccess)
            return Result<IReadOnlyList<AttendanceCorrectionResponse>>.Failure(context.Error!, context.StatusCode ?? 400);
        var value = context.Value!;
        var visibility = await authority.ResolveVisibilityAsync(new EmployeeAuthorityVisibilityRequest(
            currentUser.UserId, value.Employee.LegalEntityId!.Value, ApprovalPermission,
            IncludeSelf: false, EmployeeAuthorityPurpose.AttendanceCorrectionApproval), ct);
        var rows = await corrections.ListApprovalInboxAsync(currentUser.TenantId,
            value.Employee.LegalEntityId.Value, visibility.EmployeeIds,
            request.From, request.To, request.Status ?? AttendanceCorrection.StatusPending, ct);
        var identities = await attendance.ListEmployeeIdentitiesAsync(currentUser.TenantId,
            value.Employee.LegalEntityId.Value, rows.Select(x => x.EmployeeId).Distinct().ToArray(), ct);
        return Result<IReadOnlyList<AttendanceCorrectionResponse>>.Success(
            rows.Select(row => identities.TryGetValue(row.EmployeeId, out var identity)
                ? ToResponse(row, null, ResolveTimezone(value.LegalEntity, row.WorkDate), null, identity.DisplayName)
                : ToResponse(row, null, ResolveTimezone(value.LegalEntity, row.WorkDate))).ToArray());
    }

    private async Task<Result<AttendanceCorrectionResponse>> DecideAsync(
        Guid id, string decision, string? reviewComment, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result<AttendanceCorrectionResponse>.Forbidden("Authentication is required.");
        if (!currentUser.HasPermission(ApprovalPermission))
            return Result<AttendanceCorrectionResponse>.Forbidden("You do not have permission to approve attendance corrections.");

        try
        {
            return await unitOfWork.ExecuteInTransactionAsync(async transactionCt =>
            {
                var correction = await corrections.GetTrackedByIdAsync(currentUser.TenantId, id, transactionCt);
                if (correction is null)
                    return Result<AttendanceCorrectionResponse>.NotFound("Attendance correction was not found.");
                if (correction.Status != AttendanceCorrection.StatusPending)
                    return Result<AttendanceCorrectionResponse>.Conflict("Only a pending correction can be reviewed.");

                var route = await authority.ResolveApproverAsync(new EmployeeApprovalRouteRequest(
                    correction.EmployeeId, correction.LegalEntityId, ApprovalPermission,
                    EmployeeAuthorityPurpose.AttendanceCorrectionApproval), transactionCt);
                if (!route.IsSuccess || route.Value is null)
                    return Result<AttendanceCorrectionResponse>.Conflict(
                        "No eligible attendance approver is configured for this employee.");
                if (route.Value.ApproverUserId != currentUser.UserId)
                    return Result<AttendanceCorrectionResponse>.Forbidden("You are not an eligible approver for this correction.");

                var employee = await employees.GetByIdAsync(currentUser.TenantId, correction.EmployeeId, transactionCt);
                if (employee is null)
                    return Result<AttendanceCorrectionResponse>.NotFound("The correction requester was not found.");
                var legalEntity = await legalEntities.GetByIdForTenantAsync(
                    currentUser.TenantId, correction.LegalEntityId, transactionCt);
                if (legalEntity is null)
                    return Result<AttendanceCorrectionResponse>.NotFound("The correction company was not found.");
                var scheduleResolution = AttendanceScheduleResolver.ResolveForDate(
                    legalEntity, correction.WorkDate, dateTime.UtcNow);
                var record = correction.AttendanceRecordId is Guid
                    ? await attendance.GetTrackedRecordAsync(currentUser.TenantId, correction.EmployeeId,
                        correction.WorkDate, transactionCt)
                    : null;
                if (decision == AttendanceCorrection.StatusApproved)
                {
                    if (record is null)
                        return Result<AttendanceCorrectionResponse>.Conflict("The attendance day no longer exists.");
                    await ApplyCorrectionAsync(correction, record, scheduleResolution.Schedule,
                        scheduleResolution.TimeZone, legalEntity.BreakDurationMinutes, "configured", transactionCt);
                }

                correction.Status = decision;
                correction.ReviewedById = currentUser.UserId;
                correction.ReviewedAt = dateTime.UtcNow;
                correction.ReviewComment = string.IsNullOrWhiteSpace(reviewComment) ? null : reviewComment.Trim();
                correction.UpdatedAt = dateTime.UtcNow;
                await notifications.SendTemplatedAsync(
                    currentUser.TenantId,
                    correction.RequestedById,
                    DecidedTemplate,
                    new Dictionary<string, string> { ["decision"] = decision },
                    RelatedType,
                    correction.Id,
                    transactionCt);
                await corrections.SaveChangesAsync(transactionCt);
                return Result<AttendanceCorrectionResponse>.Success(
                    ToResponse(correction, employee, scheduleResolution.TimeZone.Id));
            }, ct);
        }
        catch (ConcurrencyConflictException)
        {
            return Result<AttendanceCorrectionResponse>.Conflict(
                "This attendance correction was changed by another request. Please refresh and try again.");
        }
    }

    private async Task<Result<PreparedCorrection>> PrepareAsync(
        DateOnly workDate, string correctionType, DateTimeOffset? requestedClockIn,
        DateTimeOffset? requestedClockOut, IReadOnlyList<AttendanceCorrectionInputBreak>? requestedBreaks,
        string reason, string? notes, bool createRecord, CancellationToken ct)
    {
        var context = await ResolveEmployeeContextAsync(ct);
        if (!context.IsSuccess)
            return Result<PreparedCorrection>.Failure(context.Error!, context.StatusCode ?? 400);
        var employee = context.Value!.Employee;
        var legalEntity = context.Value.LegalEntity;
        var scheduleResolution = AttendanceScheduleResolver.ResolveForDate(legalEntity, workDate, dateTime.UtcNow);
        var normalizedType = correctionType?.Trim().ToLowerInvariant();
        if (workDate > DateOnly.FromDateTime(scheduleResolution.LocalNow.DateTime))
            return Result<PreparedCorrection>.Failure("Attendance corrections can only be requested for today or a previous work date.");
        if (normalizedType is not (AttendanceCorrection.TypeClockIn or AttendanceCorrection.TypeClockOut
            or AttendanceCorrection.TypeBreak or AttendanceCorrection.TypeFullDay))
            return Result<PreparedCorrection>.Failure("The correction type is not supported.");
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 255)
            return Result<PreparedCorrection>.Failure("A reason is required and must be 255 characters or fewer.");
        if (notes?.Length > 2000)
            return Result<PreparedCorrection>.Failure("Notes must be 2000 characters or fewer.");
        if (scheduleResolution.Schedule.Status != "configured")
            return Result<PreparedCorrection>.Conflict("The company work schedule is not configured.");
        var policyResult = await ResolvePolicyAsync(legalEntity.Id, workDate, ct);
        if (!policyResult.IsSuccess)
            return Result<PreparedCorrection>.Failure(policyResult.Error!, policyResult.StatusCode ?? 409);
        var policy = policyResult.Value!;
        var times = ValidateRequestedTimes(normalizedType!, workDate, scheduleResolution.TimeZone,
            requestedClockIn, requestedClockOut, requestedBreaks);
        if (times is not null)
            return Result<PreparedCorrection>.Failure(times);

        var record = await attendance.GetTrackedRecordAsync(currentUser.TenantId, employee.Id, workDate, ct);
        if (record is null && createRecord)
        {
            record = NewAttendanceRecord(employee.Id, currentUser.TenantId, workDate, scheduleResolution, dateTime.UtcNow);
            await attendance.AddRecordAsync(record, ct);
        }
        if (record is null)
            record = NewAttendanceRecord(employee.Id, currentUser.TenantId, workDate, scheduleResolution, dateTime.UtcNow);

        var breaks = await attendance.ListBreaksAsync(currentUser.TenantId, employee.Id,
            GetLocalDayWindow(workDate, scheduleResolution.TimeZone).Start,
            GetLocalDayWindow(workDate, scheduleResolution.TimeZone).End, ct);
        if (createRecord && await corrections.HasPendingForRecordAsync(currentUser.TenantId, employee.Id,
            record.Id, normalizedType!, ct))
            return Result<PreparedCorrection>.Conflict("A pending correction for this attendance day and correction type already exists.");

        EmployeeApprovalRoute? route = null;
        AttendanceCorrectionApproverResponse? approver = null;
        if (policy.CorrectionRequiresApproval)
        {
            var routeResult = await authority.ResolveApproverAsync(new EmployeeApprovalRouteRequest(
                employee.Id, legalEntity.Id, ApprovalPermission,
                EmployeeAuthorityPurpose.AttendanceCorrectionApproval), ct);
            if (!routeResult.IsSuccess || routeResult.Value is null)
                return Result<PreparedCorrection>.Conflict("No eligible attendance approver is configured for this employee.");
            route = routeResult.Value;
            var approverEmployee = await employees.GetByIdAsync(currentUser.TenantId,
                route.ApproverEmployeeId, ct);
            var position = await positions.GetByIdAsync(currentUser.TenantId, route.ApproverPositionId, ct);
            approver = new AttendanceCorrectionApproverResponse(
                route.ApproverEmployeeId, route.ApproverUserId,
                approverEmployee is null ? "Approver" : DisplayName(approverEmployee),
                position?.Name);
        }

        return Result<PreparedCorrection>.Success(new PreparedCorrection(
            employee, legalEntity, scheduleResolution.Schedule, scheduleResolution.TimeZone,
            policy, "configured", record, breaks, route, approver, workDate));
    }

    private async Task<Result<EmployeeContext>> ResolveEmployeeContextAsync(CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result<EmployeeContext>.Forbidden("Authentication is required.");
        if (currentUser.TenantId == Guid.Empty)
            return Result<EmployeeContext>.Forbidden("Tenant context is missing.");
        var employee = await employees.GetDefaultForUserAsync(currentUser.TenantId, currentUser.UserId, ct);
        if (employee?.LegalEntityId is null)
            return Result<EmployeeContext>.NotFound("Current employee record was not found.");
        var legalEntity = await legalEntities.GetByIdForTenantAsync(
            currentUser.TenantId, employee.LegalEntityId.Value, ct);
        return legalEntity is null
            ? Result<EmployeeContext>.NotFound("Company was not found.")
            : Result<EmployeeContext>.Success(new EmployeeContext(employee, legalEntity));
    }

    private async Task<Result<ClockInPolicy>> ResolvePolicyAsync(Guid legalEntityId, DateOnly workDate, CancellationToken ct)
    {
        var active = (await policies.ListByLegalEntityAsync(currentUser.TenantId, legalEntityId, false, ct))
            .Where(x => x.IsActive && x.ScopeType == ClockInPolicy.ScopeFullCompany
                && x.EffectiveFrom <= workDate && (x.EffectiveTo is null || x.EffectiveTo >= workDate))
            .ToList();
        if (active.Count == 0)
            return Result<ClockInPolicy>.Conflict("No active company clock-in policy is configured for this date.");
        if (active.Count > 1)
            return Result<ClockInPolicy>.Conflict("More than one active company clock-in policy is configured for this date.");
        return Result<ClockInPolicy>.Success(active[0]);
    }

    private AttendanceCorrection BuildCorrection(PreparedCorrection value,
        RequestAttendanceCorrectionCommand request, CancellationToken ct)
    {
        var requestedBreaks = request.RequestedBreaks?.Select(x => new AttendanceCorrectionBreak(
            x.BreakStart, x.BreakEnd, x.BreakType.Trim())).ToArray();
        var originalBreaks = value.Breaks
            .Where(x => x.BreakEnd is not null)
            .Select(x => new AttendanceCorrectionBreak(
                x.BreakStart, x.BreakEnd!.Value, x.BreakType ?? "other"))
            .ToArray();
        return new AttendanceCorrection
        {
            Id = Guid.NewGuid(), TenantId = currentUser.TenantId, EmployeeId = value.Employee.Id,
            LegalEntityId = value.LegalEntity.Id, AttendanceRecordId = value.Record.Id,
            WorkDate = value.WorkDate,
            CorrectionType = request.CorrectionType.Trim().ToLowerInvariant(),
            OriginalClockInAt = value.Record.ActualStart, OriginalClockOutAt = value.Record.ActualEnd,
            RequestedClockInAt = request.RequestedClockInAt, RequestedClockOutAt = request.RequestedClockOutAt,
            OriginalBreakJson = JsonSerializer.Serialize(originalBreaks),
            RequestedBreakJson = requestedBreaks is null ? null : JsonSerializer.Serialize(requestedBreaks),
            Reason = request.Reason.Trim(), Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            Status = value.Policy.CorrectionRequiresApproval ? AttendanceCorrection.StatusPending : AttendanceCorrection.StatusApproved,
            ApprovalRequired = value.Policy.CorrectionRequiresApproval,
            RequestedById = currentUser.UserId,
            CreatedAt = dateTime.UtcNow, UpdatedAt = dateTime.UtcNow
        };
    }

    private async Task ApplyCorrectionAsync(AttendanceCorrection correction, AttendanceRecord record,
        AttendanceSchedule schedule, TimeZoneInfo timeZone, int? breakAllowanceMinutes,
        string policyStatus, CancellationToken ct)
    {
        if (correction.CorrectionType == AttendanceCorrection.TypeClockIn
            && correction.RequestedClockInAt is null)
            throw new InvalidOperationException("Clock-in correction is missing its requested time.");
        if (correction.CorrectionType == AttendanceCorrection.TypeClockOut
            && record.ActualStart is null)
            throw new InvalidOperationException("A clock-out correction requires an existing clock-in.");
        if (correction.CorrectionType == AttendanceCorrection.TypeClockIn)
            record.ActualStart = correction.RequestedClockInAt;
        if (correction.CorrectionType == AttendanceCorrection.TypeClockOut)
            record.ActualEnd = correction.RequestedClockOutAt;
        if (correction.CorrectionType == AttendanceCorrection.TypeFullDay)
        {
            record.ActualStart = correction.RequestedClockInAt;
            record.ActualEnd = correction.RequestedClockOutAt;
        }
        if (correction.CorrectionType == AttendanceCorrection.TypeBreak)
        {
            var requested = JsonSerializer.Deserialize<AttendanceCorrectionBreak[]>(correction.RequestedBreakJson ?? "[]") ?? [];
            var localDay = GetLocalDayWindow(record.Date, timeZone);
            var existing = await attendance.ListBreaksAsync(currentUser.TenantId, record.EmployeeId,
                localDay.Start, localDay.End, ct);
            foreach (var item in existing)
                await DeleteBreakAsync(item.Id, ct);
            foreach (var item in requested)
                await attendance.AddBreakAsync(new BreakRecord
                {
                    Id = Guid.NewGuid(), TenantId = currentUser.TenantId, EmployeeId = record.EmployeeId,
                    BreakStart = item.BreakStart, BreakEnd = item.BreakEnd, BreakType = item.BreakType,
                    AutoDetected = false, CreatedAt = dateTime.UtcNow
                }, ct);
            record.BreakMinutes = requested.Sum(x => Math.Max(0, (int)(x.BreakEnd - x.BreakStart).TotalMinutes));
        }
        var breakMinutes = record.BreakMinutes;
        record.WorkedMinutes = record.ActualStart is DateTimeOffset start && record.ActualEnd is DateTimeOffset end
            ? Math.Max(0, (int)(end - start).TotalMinutes - breakMinutes) : 0;
        record.LateMinutes = schedule.Start is TimeOnly scheduledStart && record.ActualStart is DateTimeOffset actualStart
            ? Math.Max(0, (int)(TimeZoneInfo.ConvertTime(actualStart, timeZone).TimeOfDay - scheduledStart.ToTimeSpan()).TotalMinutes)
            : 0;
        var hasApprovedLeave = leaveRequests is not null
            && (await leaveRequests.ListApprovedCoveringAsync(currentUser.TenantId,
                [record.EmployeeId], record.Date, record.Date, ct)).Count > 0;
        var resolved = AttendanceDayStatusResolver.Resolve(schedule, policyStatus, record, hasApprovedLeave,
            breakAllowanceMinutes, record.BreakMinutes, dateTime.UtcNow);
        record.Status = resolved.Status;
        record.UpdatedAt = dateTime.UtcNow;
    }

    private Task DeleteBreakAsync(Guid id, CancellationToken ct)
        => attendance.DeleteBreakAsync(id, ct);

    private static AttendanceRecord NewAttendanceRecord(Guid employeeId, Guid tenantId, DateOnly date,
        AttendanceScheduleResolution resolution, DateTimeOffset now)
        => new()
        {
            Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId, Date = date,
            ExpectedWorkingDay = resolution.Schedule.IsWorkingDay, WorkTimeType = AttendanceRecord.WorkTimeTypeFixed,
            ScheduledStart = resolution.Schedule.Start, ScheduledEnd = resolution.Schedule.End,
            RequiredWorkMinutes = resolution.Schedule.RequiredWorkMinutes, ScheduleTimezone = resolution.Timezone,
            WorkedMinutes = 0, BreakMinutes = 0, Status = AttendanceRecord.StatusNotClockedIn,
            CreatedAt = now, UpdatedAt = now
        };

    private static string? ValidateRequestedTimes(string type, DateOnly workDate, TimeZoneInfo zone,
        DateTimeOffset? clockIn, DateTimeOffset? clockOut, IReadOnlyList<AttendanceCorrectionInputBreak>? breaks)
    {
        bool InDate(DateTimeOffset value) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(value, zone).DateTime) == workDate;
        if (type is AttendanceCorrection.TypeClockIn or AttendanceCorrection.TypeFullDay && clockIn is null)
            return "A requested clock-in time is required for this correction type.";
        if (type is AttendanceCorrection.TypeClockOut or AttendanceCorrection.TypeFullDay && clockOut is null)
            return "A requested clock-out time is required for this correction type.";
        if (clockIn is DateTimeOffset ci && !InDate(ci) || clockOut is DateTimeOffset co && !InDate(co))
            return "Requested attendance times must belong to the selected local work date.";
        if (clockIn is DateTimeOffset start && clockOut is DateTimeOffset end && end <= start)
            return "The requested clock-out time must be after the requested clock-in time.";
        if (type == AttendanceCorrection.TypeBreak)
        {
            if (breaks is null || breaks.Count == 0)
                return "At least one completed break interval is required.";
            var ordered = breaks.OrderBy(x => x.BreakStart).ToArray();
            for (var i = 0; i < ordered.Length; i++)
            {
                if (ordered[i].BreakStart >= ordered[i].BreakEnd)
                    return "Each break interval must end after it starts.";
                if (!InDate(ordered[i].BreakStart) || !InDate(ordered[i].BreakEnd))
                    return "Break intervals must belong to the selected local work date.";
                if (i > 0 && ordered[i - 1].BreakEnd > ordered[i].BreakStart)
                    return "Break intervals must not overlap.";
            }
        }
        return null;
    }

    private static AttendanceLocalDayWindow GetLocalDayWindow(DateOnly date, TimeZoneInfo zone)
    {
        var start = TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(TimeOnly.MinValue), zone);
        var end = TimeZoneInfo.ConvertTimeToUtc(date.AddDays(1).ToDateTime(TimeOnly.MinValue), zone);
        return new AttendanceLocalDayWindow(new DateTimeOffset(start), new DateTimeOffset(end));
    }

    private static string DisplayName(ONEVO.Domain.Features.CoreHr.Entities.Employee employee)
        => $"{employee.FirstName} {employee.LastName}".Trim();

    private static AttendanceCorrectionResponse ToResponse(AttendanceCorrection correction,
        ONEVO.Domain.Features.CoreHr.Entities.Employee? employee,
        string timezone,
        AttendanceCorrectionApproverResponse? approver = null, string? requesterDisplayName = null)
    {
        var requestedBreaks = correction.RequestedBreakJson is null
            ? null
            : JsonSerializer.Deserialize<AttendanceCorrectionBreakResponse[]>(correction.RequestedBreakJson);
        return new(correction.Id, correction.EmployeeId, correction.LegalEntityId, timezone,
            correction.WorkDate,
            correction.CorrectionType, correction.RequestedClockInAt, correction.RequestedClockOutAt,
            requestedBreaks, correction.Reason, correction.Notes, correction.Status,
            correction.ApprovalRequired,
            correction.RequestedById, correction.ReviewedById, correction.ReviewedAt,
            correction.ReviewComment, approver,
            requesterDisplayName ?? (employee is null ? null : DisplayName(employee)));
    }

    private string ResolveTimezone(
        ONEVO.Domain.Features.OrgStructure.Entities.LegalEntity? legalEntity,
        DateOnly workDate)
        => legalEntity is null
            ? TimeZoneInfo.Utc.Id
            : AttendanceScheduleResolver.ResolveForDate(
                legalEntity, workDate, dateTime.UtcNow).TimeZone.Id;

    private sealed record EmployeeContext(
        ONEVO.Domain.Features.CoreHr.Entities.Employee Employee,
        ONEVO.Domain.Features.OrgStructure.Entities.LegalEntity LegalEntity);

    private sealed record PreparedCorrection(
        ONEVO.Domain.Features.CoreHr.Entities.Employee Employee,
        ONEVO.Domain.Features.OrgStructure.Entities.LegalEntity LegalEntity,
        AttendanceSchedule Schedule,
        TimeZoneInfo TimeZone,
        ClockInPolicy Policy,
        string PolicyStatus,
        AttendanceRecord Record,
        IReadOnlyList<BreakRecord> Breaks,
        EmployeeApprovalRoute? Route,
        AttendanceCorrectionApproverResponse? Approver,
        DateOnly WorkDate);
}

public sealed class PreviewAttendanceCorrectionCommandHandler(AttendanceCorrectionWorkflow workflow)
    : IRequestHandler<PreviewAttendanceCorrectionCommand, Result<AttendanceCorrectionPreviewResponse>>
{
    public Task<Result<AttendanceCorrectionPreviewResponse>> Handle(PreviewAttendanceCorrectionCommand request, CancellationToken ct)
        => workflow.PreviewAsync(request, ct);
}

public sealed class RequestAttendanceCorrectionCommandHandler(AttendanceCorrectionWorkflow workflow)
    : IRequestHandler<RequestAttendanceCorrectionCommand, Result<AttendanceCorrectionResponse>>
{
    public Task<Result<AttendanceCorrectionResponse>> Handle(RequestAttendanceCorrectionCommand request, CancellationToken ct)
        => workflow.RequestAsync(request, ct);
}

public sealed class ApproveAttendanceCorrectionCommandHandler(AttendanceCorrectionWorkflow workflow)
    : IRequestHandler<ApproveAttendanceCorrectionCommand, Result<AttendanceCorrectionResponse>>
{
    public Task<Result<AttendanceCorrectionResponse>> Handle(ApproveAttendanceCorrectionCommand request, CancellationToken ct)
        => workflow.ApproveAsync(request, ct);
}

public sealed class RejectAttendanceCorrectionCommandHandler(AttendanceCorrectionWorkflow workflow)
    : IRequestHandler<RejectAttendanceCorrectionCommand, Result<AttendanceCorrectionResponse>>
{
    public Task<Result<AttendanceCorrectionResponse>> Handle(RejectAttendanceCorrectionCommand request, CancellationToken ct)
        => workflow.RejectAsync(request, ct);
}

public sealed class CancelAttendanceCorrectionCommandHandler(AttendanceCorrectionWorkflow workflow)
    : IRequestHandler<CancelAttendanceCorrectionCommand, Result<AttendanceCorrectionResponse>>
{
    public Task<Result<AttendanceCorrectionResponse>> Handle(CancelAttendanceCorrectionCommand request, CancellationToken ct)
        => workflow.CancelAsync(request, ct);
}

public sealed class ListMyAttendanceCorrectionsQueryHandler(AttendanceCorrectionWorkflow workflow)
    : IRequestHandler<ListMyAttendanceCorrectionsQuery, Result<IReadOnlyList<AttendanceCorrectionResponse>>>
{
    public Task<Result<IReadOnlyList<AttendanceCorrectionResponse>>> Handle(ListMyAttendanceCorrectionsQuery request, CancellationToken ct)
        => workflow.ListMyAsync(request, ct);
}

public sealed class ListAttendanceCorrectionApprovalsQueryHandler(AttendanceCorrectionWorkflow workflow)
    : IRequestHandler<ListAttendanceCorrectionApprovalsQuery, Result<IReadOnlyList<AttendanceCorrectionResponse>>>
{
    public Task<Result<IReadOnlyList<AttendanceCorrectionResponse>>> Handle(ListAttendanceCorrectionApprovalsQuery request, CancellationToken ct)
        => workflow.ListApprovalsAsync(request, ct);
}
