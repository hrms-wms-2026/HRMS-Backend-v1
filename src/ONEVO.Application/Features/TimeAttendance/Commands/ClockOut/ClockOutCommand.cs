using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.TimeAttendance.Commands.ClockOut;

public sealed record ClockOutCommand(
    Guid AgentId,
    string IdempotencyKey)
    : IRequest<Result<ClockOutResponse>>;

