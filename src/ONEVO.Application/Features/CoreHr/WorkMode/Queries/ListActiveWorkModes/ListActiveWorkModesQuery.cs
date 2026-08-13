using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.CoreHr.WorkModes.DTOs.Responses;

namespace ONEVO.Application.Features.CoreHr.WorkModes.Queries.ListActiveWorkModes;

public sealed record ListActiveWorkModesQuery : IRequest<Result<List<WorkModeDto>>>;
