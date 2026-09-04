using MediatR;
using ONEVO.Application.Common.Exceptions;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Application.Features.TimeAttendance.Commands.ClockOut;

public sealed class ClockOutCommandHandler(
    IAttendanceTodayStateService todayState,
    IAttendanceReadRepository attendance,
    IUnitOfWork unitOfWork,
    ITaskClockingSessionRepository taskSessions)
    : IRequestHandler<ClockOutCommand, Result<AttendanceTodayResponse>>
{
    public async Task<Result<AttendanceTodayResponse>> Handle(
        ClockOutCommand _, CancellationToken ct)
    {
        var contextResult = await todayState.ResolveContextAsync(ct);
        if (!contextResult.IsSuccess)
            return ToTodayFailure(contextResult);

        return await HandleForContextAsync(contextResult.Value!, ct);
    }

    public async Task<Result<AttendanceTodayResponse>> HandleForContextAsync(
        AttendanceTodayContext context, CancellationToken ct)
    {
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
            return Result<AttendanceTodayResponse>.Conflict(
                "Attendance for this work day was just updated by another request. Please refresh and try again.");
        }

        return await todayState.GetTodayAsync(context.Employee.TenantId, context.Employee.UserId, ct);
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

        var hasOpenBreak = await attendance.HasOpenBreakAsync(
            context.Employee.TenantId,
            context.Employee.Id,
            context.LocalDayWindow.Start,
            context.LocalDayWindow.End,
            ct);
        if (hasOpenBreak)
            return Result<bool>.Conflict("open_break_must_be_ended_before_clock_out");

        var openTaskSessions = await taskSessions.GetOpenSessionsForEmployeeAsync(
            context.Employee.TenantId, context.Employee.Id, ct);
        if (openTaskSessions.Count > 0)
            return Result<bool>.Conflict(BuildOpenTaskSessionMessage(openTaskSessions));

        var completedBreakMinutes = await attendance.SumCompletedBreakMinutesAsync(
            context.Employee.TenantId,
            context.Employee.Id,
            context.LocalDayWindow.Start,
            context.LocalDayWindow.End,
            ct);
        var workedMinutes = Math.Max(
            0,
            (int)(context.UtcNow - record.ActualStart.Value).TotalMinutes - completedBreakMinutes);

        record.ActualEnd = context.UtcNow;
        record.BreakMinutes = completedBreakMinutes;
        record.WorkedMinutes = workedMinutes;
        record.Status = context.Schedule.RequiredWorkMinutes is int requiredMinutes
            && workedMinutes < requiredMinutes
            ? AttendanceRecord.StatusShortHours
            : AttendanceRecord.StatusClockedOut;
        record.UpdatedAt = context.UtcNow;

        await attendance.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }

    private static string BuildOpenTaskSessionMessage(IReadOnlyList<OpenEmployeeTaskSession> openTaskSessions)
    {
        var taskNames = string.Join(", ", openTaskSessions.Select(session => $"'{session.TaskTitle}'"));
        return openTaskSessions.Count == 1
            ? $"Task {taskNames} is still running. Push it before clocking out for the day."
            : $"Tasks {taskNames} are still running. Push them before clocking out for the day.";
    }

    private static Result<AttendanceTodayResponse> ToTodayFailure(
        Result<AttendanceTodayContext> contextResult)
        => Result<AttendanceTodayResponse>.Failure(
            contextResult.Error!, contextResult.StatusCode ?? 400);
}
