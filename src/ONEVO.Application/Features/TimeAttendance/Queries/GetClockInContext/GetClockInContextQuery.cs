using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.TimeAttendance.Context;

namespace ONEVO.Application.Features.TimeAttendance.Queries.GetClockInContext;

public sealed record GetClockInContextQuery(Guid AgentId)
    : IRequest<Result<ResolvedClockInContext>>;

