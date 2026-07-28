using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.TimeAttendance.Context;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Application.Features.TimeAttendance.Commands.StartBreak;

public sealed class StartBreakCommandHandler
    : IRequestHandler<StartBreakCommand, Result<BreakStateResponse>>
{
    private static readonly HashSet<string> SupportedBreakTypes =
        new(StringComparer.Ordinal)
        {
            "lunch",
            "prayer",
            "smoke",
            "personal",
            "other"
        };

    private readonly IClockInContextResolver _contexts;
    private readonly ITimeAttendanceRepository _attendance;
    private readonly IOutboxWriter _outbox;
    private readonly IDateTimeProvider _clock;
    private readonly IUnitOfWork _uow;

    public StartBreakCommandHandler(
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
        StartBreakCommand request,
        CancellationToken cancellationToken)
    {
        var breakType =
            request.BreakType.Trim().ToLowerInvariant();
        if (!SupportedBreakTypes.Contains(breakType))
        {
            return Result<BreakStateResponse>.Failure(
                "Unsupported break type.",
                400);
        }

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

        var existing = await _attendance.GetOpenBreakAsync(
            context.EmployeeId,
            cancellationToken);
        if (existing is not null)
        {
            return Result<BreakStateResponse>.Success(
                new BreakStateResponse(
                    "break_already_started",
                    existing.Id,
                    existing.BreakType,
                    existing.BreakStart,
                    null,
                    "paused"));
        }

        var now = _clock.UtcNow;
        var record = new BreakRecord
        {
            Id = Guid.NewGuid(),
            TenantId = context.TenantId,
            EmployeeId = context.EmployeeId,
            BreakStart = now,
            BreakType = breakType,
            AutoDetected = false,
            CreatedAt = now
        };
        await _attendance.AddBreakAsync(
            record,
            cancellationToken);
        await _outbox.EnqueueAsync(
            OutboxMessageTypes.PresenceBreakStarted,
            new PresenceBreakStartedEvent(
                context.TenantId,
                context.AgentId,
                context.EmployeeId,
                record.Id,
                now,
                breakType),
            context.TenantId,
            cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);

        return Result<BreakStateResponse>.Success(
            new BreakStateResponse(
                "break_started",
                record.Id,
                record.BreakType,
                record.BreakStart,
                null,
                "paused"));
    }
}

