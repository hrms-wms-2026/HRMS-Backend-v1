using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Dashboard.Team.DTOs;

namespace ONEVO.Application.Features.Dashboard.Team.Queries;

public sealed record GetMyTeamCapabilitiesQuery : IRequest<Result<MyTeamCapabilitiesResponse>>;
