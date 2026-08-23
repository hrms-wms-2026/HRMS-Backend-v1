using MediatR;
using ONEVO.Application.Common.Exceptions;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Services;
using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Application.Features.TimeAttendance.Commands.StartBreak;

public sealed class StartBreakCommandHandler(
    IAttendanceTodayStateService todayState,
    IAttendanceReadRepository attendance,
    IUnitOfWork unitOfWork)
    : IRequestHandler<StartBreakCommand, Result<AttendanceTodayResponse>>
{
    public async Task<Result<AttendanceTodayResponse>> Handle(
        StartBreakCommand _, CancellationToken ct)
    {
        var contextResult = await todayState.ResolveContextAsync(ct);
        if (!contextResult.IsSuccess)
            return ToTodayFailure(contextResult);

        var context = contextResult.Value!;
        if (context.Schedule.Status != "configured")
            return Result<AttendanceTodayResponse>.Conflict("schedule_not_configured");

        if (!context.Schedule.IsWorkingDay)
            return Result<AttendanceTodayResponse>.Conflict("off_day");

        try
        {
            var mutation = await unitOfWork.ExecuteInTransactionAsync(
                transactionCt => MutateAsync(context, transactionCt), ct);

            if (!mutation.IsSuccess)
                return Result<AttendanceTodayResponse>.Failure(
                    mutation.Error!, mutation.StatusCode ?? 400);
        }
        catch (UniqueConstraintConflictException)
        {
            return Result<AttendanceTodayResponse>.Conflict("break_already_active");
        }
        catch (ConcurrencyConflictException)
        {
            return Result<AttendanceTodayResponse>.Conflict(
                "Break state was updated by another request. Please refresh and try again.");
        }

        return await todayState.GetTodayAsync(ct);
    }

    private async Task<Result<bool>> MutateAsync(
        AttendanceTodayContext context,
        CancellationToken ct)
    {
        var allowance = context.LegalEntity.BreakDurationMinutes;
        if (allowance is null)
            return Result<bool>.Conflict("break_allowance_not_configured");

        if (allowance <= 0)
            return Result<bool>.Conflict("break_allowance_used");

        var usedMinutes = await attendance.SumCompletedBreakMinutesAsync(
            context.Employee.TenantId,
            context.Employee.Id,
            context.LocalDayWindow.Start,
            context.LocalDayWindow.End,
            ct);
        if (usedMinutes >= allowance.Value)
            return Result<bool>.Conflict("break_allowance_used");

        var record = await attendance.GetTrackedRecordAsync(
            context.Employee.TenantId,
            context.Employee.Id,
            context.WorkDate,
            ct);

        if (record is null || record.ActualStart is null)
            return Result<bool>.Conflict("not_clocked_in");

        if (record.ActualEnd is not null)
            return Result<bool>.Conflict("already_clocked_out");

        var currentDayOpenBreak = await attendance.GetOpenBreakTrackedAsync(
            context.Employee.TenantId,
            context.Employee.Id,
            context.LocalDayWindow.Start,
            context.LocalDayWindow.End,
            ct);
        if (currentDayOpenBreak is not null)
            return Result<bool>.Conflict("break_already_active");

        var historicalOpenBreak = await attendance.GetAnyOpenBreakTrackedAsync(
            context.Employee.TenantId,
            context.Employee.Id,
            ct);
        if (historicalOpenBreak is not null)
            return Result<bool>.Conflict("break_already_active");

        var breakRecord = new BreakRecord
        {
            Id = Guid.NewGuid(),
            TenantId = context.Employee.TenantId,
            EmployeeId = context.Employee.Id,
            BreakStart = context.UtcNow,
            BreakEnd = null,
            BreakType = null,
            AutoDetected = false,
            CreatedAt = context.UtcNow
        };

        await attendance.AddBreakAsync(breakRecord, ct);
        await attendance.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }

    private static Result<AttendanceTodayResponse> ToTodayFailure(
        Result<AttendanceTodayContext> contextResult)
        => Result<AttendanceTodayResponse>.Failure(
            contextResult.Error!, contextResult.StatusCode ?? 400);
}
