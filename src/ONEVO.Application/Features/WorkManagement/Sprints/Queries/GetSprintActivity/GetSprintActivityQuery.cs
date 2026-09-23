using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Sprints.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Sprints.Queries.GetSprintActivity;

public sealed record GetSprintActivityQuery(Guid SprintId) : IRequest<Result<IReadOnlyList<SprintActivityResponse>>>;
