using MediatR;
using ONEVO.Application.Common.Exceptions;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Services;
using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Application.Features.TimeAttendance.Commands.ClockIn;

public sealed class ClockInCommandHandler(
    IAttendanceTodayStateService todayState,
    IAttendanceReadRepository attendance,
    IUnitOfWork unitOfWork)
    : IRequestHandler<ClockInCommand, Result<AttendanceTodayResponse>>
{
    public async Task<Result<AttendanceTodayResponse>> Handle(
        ClockInCommand request, CancellationToken ct)
    {
        var contextResult = await todayState.ResolveContextAsync(ct);
        if (!contextResult.IsSuccess)
            return ToTodayFailure(contextResult);

        return await HandleForContextAsync(contextResult.Value!, request.Source, ct);
    }

    public async Task<Result<AttendanceTodayResponse>> HandleForContextAsync(
        AttendanceTodayContext context, string sourceRaw, CancellationToken ct)
    {
        if (context.Schedule.Status != "configured")
            return Result<AttendanceTodayResponse>.Conflict("schedule_not_configured");

        if (context.PolicyStatus == "not_configured")
            return Result<AttendanceTodayResponse>.Conflict("clock_in_policy_not_configured");

        if (context.PolicyStatus == "configuration_conflict")
            return Result<AttendanceTodayResponse>.Conflict("multiple_active_company_policies");

        var source = sourceRaw.Trim().ToLowerInvariant();
        var allowed = source == AttendanceRecord.SourceWeb
            ? context.AllowedClockInMethods.Web
            : context.AllowedClockInMethods.DesktopTray;
        if (!allowed)
            return Result<AttendanceTodayResponse>.Forbidden(
                $"Clock-in source {source} is not allowed by the active policy.");

        try
        {
            var mutation = await unitOfWork.ExecuteInTransactionAsync(
                async transactionCt => await MutateAsync(context, source, transactionCt), ct);

            if (!mutation.IsSuccess)
                return Result<AttendanceTodayResponse>.Failure(
                    mutation.Error!, mutation.StatusCode ?? 400);
        }
        catch (UniqueConstraintConflictException)
        {
            return Result<AttendanceTodayResponse>.Conflict(
                "Attendance for this work day was just created by another request. Please refresh and try again.");
        }
        catch (ConcurrencyConflictException)
        {
            return Result<AttendanceTodayResponse>.Conflict(
                "Attendance for this work day was just updated by another request. Please refresh and try again.");
        }

        return await todayState.GetTodayAsync(context.Employee.TenantId, context.Employee.UserId, ct);
    }

    private async Task<Result<bool>> MutateAsync(
        AttendanceTodayContext context,
        string source,
        CancellationToken ct)
    {
        var existing = await attendance.GetTrackedRecordAsync(
            context.Employee.TenantId,
            context.Employee.Id,
            context.WorkDate,
            ct);

        if (existing?.ActualEnd is not null)
            return Result<bool>.Conflict("already_clocked_out");

        if (existing?.ActualStart is not null)
            return Result<bool>.Conflict("already_clocked_in");

        if (existing is null)
        {
            var record = new AttendanceRecord
            {
                Id = Guid.NewGuid(),
                TenantId = context.Employee.TenantId,
                EmployeeId = context.Employee.Id,
                Date = context.WorkDate,
                WorkedMinutes = 0,
                BreakMinutes = 0,
                CreatedAt = context.UtcNow,
                UpdatedAt = context.UtcNow
            };
            ApplyClockInState(record, context, source);
            await attendance.AddRecordAsync(record, ct);
        }
        else
        {
            ApplyClockInState(existing, context, source);
        }

        await attendance.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }

    private static void ApplyClockInState(
        AttendanceRecord record,
        AttendanceTodayContext context,
        string source)
    {
        var lateMinutes = context.Schedule.Start is TimeOnly scheduledStart
            ? Math.Max(0, (int)(context.LocalNow.TimeOfDay - scheduledStart.ToTimeSpan()).TotalMinutes)
            : 0;

        record.TenantId = context.Employee.TenantId;
        record.EmployeeId = context.Employee.Id;
        record.Date = context.WorkDate;
        record.ExpectedWorkingDay = context.Schedule.IsWorkingDay;
        record.WorkTimeType = AttendanceRecord.WorkTimeTypeFixed;
        record.ScheduledStart = context.Schedule.Start;
        record.ScheduledEnd = context.Schedule.End;
        record.RequiredWorkMinutes = context.Schedule.RequiredWorkMinutes;
        record.ExpectedWorkArea = context.ExpectedWorkArea;
        record.ScheduleTimezone = context.Timezone;
        record.IsHoliday = false;
        record.HolidayName = null;
        record.ActualStart = context.UtcNow;
        record.ActualEnd = null;
        record.WorkedMinutes = 0;
        record.LateMinutes = lateMinutes;
        record.AttendanceSource = source;
        record.Status = lateMinutes > 0
            ? AttendanceRecord.StatusLate
            : AttendanceRecord.StatusOnTime;
        record.UpdatedAt = context.UtcNow;
    }

    private static Result<AttendanceTodayResponse> ToTodayFailure(
        Result<AttendanceTodayContext> contextResult)
        => Result<AttendanceTodayResponse>.Failure(
            contextResult.Error!, contextResult.StatusCode ?? 400);
}
