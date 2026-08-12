using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Monitoring.WorkSessions.DTOs.Responses;

namespace ONEVO.Application.Features.Monitoring.WorkSessions.Commands.SubmitWorkSession;

public record SubmitWorkSessionCommand(
    Guid SessionId,
    DateTimeOffset ClockInAt,
    DateTimeOffset ClockOutAt,
    int AccumulatedBreakSeconds,
    int AccumulatedWorkSeconds,
    int BreakSessionCount,
    string? ScheduleDisplay
) : IRequest<Result<WorkSessionResponseDto>>;
