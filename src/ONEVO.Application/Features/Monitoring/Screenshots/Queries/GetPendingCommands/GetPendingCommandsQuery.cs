using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Monitoring.Screenshots.DTOs.Responses;

namespace ONEVO.Application.Features.Monitoring.Screenshots.Queries.GetPendingCommands;

public record GetPendingCommandsQuery : IRequest<Result<List<AgentCommandDto>>>;
