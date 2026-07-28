using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.TimeAttendance.Context;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;

namespace ONEVO.Application.Features.TimeAttendance.Queries.GetCurrentPresence;

public sealed class GetCurrentPresenceQueryHandler
    : IRequestHandler<GetCurrentPresenceQuery, Result<CurrentPresenceDto>>
{
    private readonly IClockInContextResolver _contexts;
    private readonly ITimeAttendanceRepository _attendance;

    public GetCurrentPresenceQueryHandler(
        IClockInContextResolver contexts,
        ITimeAttendanceRepository attendance)
    {
        _contexts = contexts;
        _attendance = attendance;
    }

    public async Task<Result<CurrentPresenceDto>> Handle(
        GetCurrentPresenceQuery request,
        CancellationToken cancellationToken)
    {
        var contextResult = await _contexts.ResolveAsync(
            request.AgentId,
            cancellationToken);
        if (!contextResult.IsSuccess)
        {
            return Result<CurrentPresenceDto>.Failure(
                contextResult.Error ?? "Presence context is unavailable.",
                contextResult.StatusCode ?? 400);
        }

        var context = contextResult.Value!;
        var device = await _attendance.GetOpenDeviceSessionAsync(
            request.AgentId,
            cancellationToken);
        if (device is null ||
            device.TenantId != context.TenantId ||
            device.EmployeeId != context.EmployeeId ||
            device.DeviceId != context.AgentId)
        {
            return Result<CurrentPresenceDto>.Success(
                new CurrentPresenceDto(
                    "not_clocked_in",
                    "stopped",
                    null,
                    null,
                    null,
                    context.MonitoringHardStopAt));
        }

        var openBreak = await _attendance.GetOpenBreakAsync(
            context.EmployeeId,
            cancellationToken);
        var onBreak =
            openBreak is not null &&
            openBreak.TenantId == context.TenantId;
        return Result<CurrentPresenceDto>.Success(
            new CurrentPresenceDto(
                onBreak ? "on_break" : "working",
                onBreak ? "paused" : "running",
                device.SessionStart,
                onBreak ? openBreak!.Id : null,
                onBreak ? openBreak!.BreakStart : null,
                context.MonitoringHardStopAt));
    }
}

