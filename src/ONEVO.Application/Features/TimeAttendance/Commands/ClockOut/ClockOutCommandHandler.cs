using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.TimeAttendance.Context;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;

namespace ONEVO.Application.Features.TimeAttendance.Commands.ClockOut;

public sealed class ClockOutCommandHandler
    : IRequestHandler<ClockOutCommand, Result<ClockOutResponse>>
{
    private static readonly TimeSpan IdempotencyTtl = TimeSpan.FromHours(24);

    private readonly IClockInContextResolver _contexts;
    private readonly ITimeAttendanceRepository _attendance;
    private readonly IIdempotencyStore _idempotency;
    private readonly IOutboxWriter _outbox;
    private readonly IDateTimeProvider _clock;
    private readonly IUnitOfWork _uow;

    public ClockOutCommandHandler(
        IClockInContextResolver contexts,
        ITimeAttendanceRepository attendance,
        IIdempotencyStore idempotency,
        IOutboxWriter outbox,
        IDateTimeProvider clock,
        IUnitOfWork uow)
    {
        _contexts = contexts;
        _attendance = attendance;
        _idempotency = idempotency;
        _outbox = outbox;
        _clock = clock;
        _uow = uow;
    }

    public async Task<Result<ClockOutResponse>> Handle(
        ClockOutCommand request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey) ||
            request.IdempotencyKey.Length is < 8 or > 128)
        {
            return Result<ClockOutResponse>.Failure(
                "Idempotency-Key must contain 8 to 128 characters.",
                400);
        }

        var contextResult = await _contexts.ResolveAsync(
            request.AgentId,
            cancellationToken);
        if (!contextResult.IsSuccess)
        {
            return Result<ClockOutResponse>.Failure(
                contextResult.Error ?? "Clock-out context is unavailable.",
                contextResult.StatusCode ?? 400);
        }

        var context = contextResult.Value!;
        var begin = await _idempotency.TryBeginAsync(
            request.IdempotencyKey,
            "time_attendance.clock_out",
            $"agent:{request.AgentId}",
            Hash(request),
            IdempotencyTtl,
            cancellationToken);
        var beginResult = ResolveBeginOutcome(begin);
        if (beginResult is not null)
            return beginResult;

        try
        {
            var device = await _attendance.GetOpenDeviceSessionAsync(
                request.AgentId,
                cancellationToken);
            if (device is null ||
                device.TenantId != context.TenantId ||
                device.EmployeeId != context.EmployeeId ||
                device.DeviceId != context.AgentId)
            {
                return await CompleteAsync(
                    begin.RecordId,
                    new ClockOutResponse(
                        "not_clocked_in",
                        null,
                        null,
                        null,
                        0,
                        0,
                        "no_active_device_session",
                        409),
                    cancellationToken);
            }

            var attendance = await _attendance.GetAttendanceAsync(
                context.EmployeeId,
                context.WorkDate,
                cancellationToken);
            var presence = await _attendance.GetPresenceAsync(
                context.EmployeeId,
                context.WorkDate,
                cancellationToken);
            if (attendance is null ||
                presence is null ||
                attendance.TenantId != context.TenantId ||
                presence.TenantId != context.TenantId)
            {
                return await CompleteAsync(
                    begin.RecordId,
                    new ClockOutResponse(
                        "clock_out_conflict",
                        attendance?.Id,
                        presence?.Id,
                        null,
                        0,
                        0,
                        "presence_state_incomplete",
                        409),
                    cancellationToken);
            }

            var now = _clock.UtcNow;
            var breakMinutes = attendance.BreakMinutes;
            var openBreak = await _attendance.GetOpenBreakAsync(
                context.EmployeeId,
                cancellationToken);
            if (openBreak is not null &&
                openBreak.TenantId == context.TenantId)
            {
                openBreak.BreakEnd = now;
                breakMinutes += MinutesBetween(
                    openBreak.BreakStart,
                    now);
            }

            var totalMinutes = MinutesBetween(
                device.SessionStart,
                now);
            var workedMinutes = Math.Max(
                0,
                totalMinutes - breakMinutes);

            device.SessionEnd = now;
            device.ActiveMinutes = Math.Max(
                device.ActiveMinutes,
                workedMinutes);
            device.IdleMinutes = Math.Max(
                0,
                totalMinutes - device.ActiveMinutes);
            device.ActivePercentage = totalMinutes == 0
                ? 0
                : Math.Round(
                    device.ActiveMinutes * 100m / totalMinutes,
                    2);

            attendance.ActualEnd = now;
            attendance.WorkedMinutes = workedMinutes;
            attendance.BreakMinutes = breakMinutes;
            attendance.ShortMinutes = attendance.RequiredWorkMinutes.HasValue
                ? Math.Max(
                    0,
                    attendance.RequiredWorkMinutes.Value - workedMinutes)
                : null;
            if (attendance.ShortMinutes > 0)
                attendance.Status = "short_hours";
            attendance.UpdatedAt = now;

            presence.LastSeenAt = now;
            presence.TotalPresentMinutes = workedMinutes;
            presence.TotalBreakMinutes = breakMinutes;
            presence.Status = workedMinutes > 0
                ? "present"
                : "partial";
            presence.UpdatedAt = now;

            await _outbox.EnqueueAsync(
                OutboxMessageTypes.PresenceSessionEnded,
                new PresenceSessionEndedEvent(
                    context.TenantId,
                    context.AgentId,
                    context.EmployeeId,
                    attendance.Id,
                    presence.Id,
                    now,
                    workedMinutes,
                    breakMinutes),
                context.TenantId,
                cancellationToken);
            await _uow.SaveChangesAsync(cancellationToken);

            return await CompleteAsync(
                begin.RecordId,
                new ClockOutResponse(
                    "clocked_out",
                    attendance.Id,
                    presence.Id,
                    now,
                    workedMinutes,
                    breakMinutes,
                    "clocked_out",
                    200),
                cancellationToken);
        }
        catch
        {
            await _idempotency.AbandonAsync(
                begin.RecordId,
                cancellationToken);
            throw;
        }
    }

    private static Result<ClockOutResponse>? ResolveBeginOutcome(
        IdempotencyBeginResult begin)
    {
        if (begin.Outcome == IdempotencyOutcome.Started)
            return null;
        if (begin.Outcome == IdempotencyOutcome.HashMismatch)
        {
            return Result<ClockOutResponse>.Conflict(
                "Idempotency-Key was already used with a different request.");
        }
        if (begin.Outcome == IdempotencyOutcome.InFlight)
        {
            return Result<ClockOutResponse>.Conflict(
                "A clock-out request with this Idempotency-Key is still in progress.");
        }
        if (string.IsNullOrWhiteSpace(begin.ResponseBody))
        {
            return Result<ClockOutResponse>.Conflict(
                "Stored clock-out response is unavailable.");
        }

        try
        {
            var response = JsonSerializer.Deserialize<ClockOutResponse>(
                begin.ResponseBody);
            return response is null
                ? Result<ClockOutResponse>.Conflict(
                    "Stored clock-out response is invalid.")
                : Result<ClockOutResponse>.Success(
                    response with
                    {
                        HttpStatusCode =
                            begin.ResponseStatusCode ??
                            response.HttpStatusCode
                    });
        }
        catch (JsonException)
        {
            return Result<ClockOutResponse>.Conflict(
                "Stored clock-out response is invalid.");
        }
    }

    private async Task<Result<ClockOutResponse>> CompleteAsync(
        Guid recordId,
        ClockOutResponse response,
        CancellationToken ct)
    {
        await _idempotency.CompleteAsync(
            recordId,
            response.HttpStatusCode,
            JsonSerializer.Serialize(response),
            ct);
        return Result<ClockOutResponse>.Success(response);
    }

    private static string Hash(ClockOutCommand command) =>
        Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(
                $"{command.AgentId:D}|clock_out")));

    private static int MinutesBetween(
        DateTimeOffset start,
        DateTimeOffset end) =>
        Math.Max(
            0,
            (int)Math.Floor((end - start).TotalMinutes));
}

