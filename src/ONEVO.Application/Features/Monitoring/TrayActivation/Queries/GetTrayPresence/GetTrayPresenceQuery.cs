using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Monitoring.TrayActivation.DTOs.Responses;

namespace ONEVO.Application.Features.Monitoring.TrayActivation.Queries.GetTrayPresence;

public sealed record GetTrayPresenceQuery : IRequest<Result<TrayPresenceResponseDto>>;
