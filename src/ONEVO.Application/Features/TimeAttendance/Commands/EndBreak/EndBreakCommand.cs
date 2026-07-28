using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.TimeAttendance.Commands.StartBreak;

namespace ONEVO.Application.Features.TimeAttendance.Commands.EndBreak;

public sealed record EndBreakCommand(Guid AgentId)
    : IRequest<Result<BreakStateResponse>>;

