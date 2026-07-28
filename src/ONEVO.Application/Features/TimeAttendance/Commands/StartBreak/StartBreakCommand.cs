using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.TimeAttendance.Commands.StartBreak;

public sealed record StartBreakCommand(
    Guid AgentId,
    string BreakType)
    : IRequest<Result<BreakStateResponse>>;

