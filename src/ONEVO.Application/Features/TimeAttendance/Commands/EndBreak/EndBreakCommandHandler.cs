using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.TimeAttendance.Commands.StartBreak;
using ONEVO.Application.Features.TimeAttendance.Context;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;

namespace ONEVO.Application.Features.TimeAttendance.Commands.EndBreak;

public sealed class EndBreakCommandHandler
    : IRequestHandler<EndBreakCommand, Result<BreakStateResponse>>
{
    private readonly IClockInContextResolver _contexts;
    private readonly ITimeAttendanceRepository _attendance;
    private readonly IOutboxWriter _outbox;
    private readonly IDateTimeProvider _clock;
    private readonly IUnitOfWork _uow;

    public EndBreakCommandHandler(
        IClockInContextResolver contexts,
        ITimeAttendanceRepository attendance,
        IOutboxWriter outbox,
        IDateTimeProvider clock,
        IUnitOfWork uow)
    {
        _contexts = contexts;
        _attendance = attendance;
        _outbox = outbox;
        _clock = clock;
        _uow = uow;
    }

    public async Task<Result<BreakStateResponse>> Handle(
        EndBreakCommand request,
        CancellationToken cancellationToken)
    {
        var contextResult = await _contexts.ResolveAsync(
            request.AgentId,
            cancellationToken);
        if (!contextResult.IsSuccess)
        {
            return Result<BreakStateResponse>.Failure(
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
            return Result<BreakStateResponse>.Conflict(
                "An active clock-in session is required.");
        }

        var record = await _attendance.GetOpenBreakAsync(
            context.EmployeeId,
            cancellationToken);
        if (record is null || record.TenantId != context.TenantId)
        {
            return Result<BreakStateResponse>.Conflict(
                "There is no active break.");
        }

        var now = _clock.UtcNow;
        record.BreakEnd = now;
        var minutes = Math.Max(
            0,
            (int)Math.Floor((now - record.BreakStart).TotalMinutes));

        var attendance = await _attendance.GetAttendanceAsync(
            context.EmployeeId,
            context.WorkDate,
            cancellationToken);
        if (attendance is not null &&
            attendance.TenantId == context.TenantId)
        {
            attendance.BreakMinutes += minutes;
            attendance.UpdatedAt = now;
        }

        var presence = await _attendance.GetPresenceAsync(
            context.EmployeeId,
            context.WorkDate,
            cancellationToken);
        if (presence is not null &&
            presence.TenantId == context.TenantId)
        {
            presence.TotalBreakMinutes += minutes;
            presence.LastSeenAt = now;
            presence.UpdatedAt = now;
        }

        await _outbox.EnqueueAsync(
            OutboxMessageTypes.PresenceBreakEnded,
            new PresenceBreakEndedEvent(
                context.TenantId,
                context.AgentId,
                context.EmployeeId,
                record.Id,
                now,
                minutes),
            context.TenantId,
            cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);

        return Result<BreakStateResponse>.Success(
            new BreakStateResponse(
                "break_ended",
                record.Id,
                record.BreakType,
                record.BreakStart,
                now,
                "running"));
    }
}

