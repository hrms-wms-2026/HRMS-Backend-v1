using MediatR;
using ONEVO.Application.Common.Exceptions;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Services;

namespace ONEVO.Application.Features.TimeAttendance.Commands.EndBreak;

public sealed class EndBreakCommandHandler(
    IAttendanceTodayStateService todayState,
    IAttendanceReadRepository attendance,
    IUnitOfWork unitOfWork)
    : IRequestHandler<EndBreakCommand, Result<AttendanceTodayResponse>>
{
    public async Task<Result<AttendanceTodayResponse>> Handle(
        EndBreakCommand _, CancellationToken ct)
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
        catch (ConcurrencyConflictException)
        {
            return Result<AttendanceTodayResponse>.Conflict("break_already_ended");
        }

        return await todayState.GetTodayAsync(ct);
    }

    private async Task<Result<bool>> MutateAsync(
        AttendanceTodayContext context,
        CancellationToken ct)
    {
        var record = await attendance.GetTrackedRecordAsync(
            context.Employee.TenantId,
            context.Employee.Id,
            context.WorkDate,
            ct);

        if (record is null || record.ActualStart is null)
            return Result<bool>.Conflict("not_clocked_in");

        if (record.ActualEnd is not null)
            return Result<bool>.Conflict("already_clocked_out");

        var openBreak = await attendance.GetOpenBreakTrackedAsync(
            context.Employee.TenantId,
            context.Employee.Id,
            context.LocalDayWindow.Start,
            context.LocalDayWindow.End,
            ct);
        if (openBreak is null)
        {
            var historicalOpenBreak = await attendance.GetAnyOpenBreakTrackedAsync(
                context.Employee.TenantId,
                context.Employee.Id,
                ct);
            return historicalOpenBreak is null
                ? Result<bool>.Conflict("no_active_break")
                : Result<bool>.Conflict("no_active_break");
        }

        if (context.UtcNow < openBreak.BreakStart)
            return Result<bool>.Conflict("invalid_break_time");

        openBreak.BreakEnd = context.UtcNow;
        var completedBreakMinutes = await attendance.SumCompletedBreakMinutesAsync(
            context.Employee.TenantId,
            context.Employee.Id,
            context.LocalDayWindow.Start,
            context.LocalDayWindow.End,
            ct);
        var currentBreakMinutes = CalculateBreakMinutes(
            openBreak.BreakStart,
            openBreak.BreakEnd.Value,
            context.LocalDayWindow);
        record.BreakMinutes = completedBreakMinutes + currentBreakMinutes;
        await attendance.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }

    private static int CalculateBreakMinutes(
        DateTimeOffset start,
        DateTimeOffset end,
        AttendanceLocalDayWindow window)
    {
        var clippedStart = start < window.Start ? window.Start : start;
        var clippedEnd = end > window.End ? window.End : end;
        return clippedEnd <= clippedStart
            ? 0
            : (int)Math.Max(0, (clippedEnd - clippedStart).TotalMinutes);
    }

    private static Result<AttendanceTodayResponse> ToTodayFailure(
        Result<AttendanceTodayContext> contextResult)
        => Result<AttendanceTodayResponse>.Failure(
            contextResult.Error!, contextResult.StatusCode ?? 400);
}
