using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Sprints.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Sprints.Queries.GetObjectiveSprints;

public sealed record GetObjectiveSprintsQuery(Guid ObjectiveId, bool ActiveOnly) : IRequest<Result<IReadOnlyList<SprintResponse>>>;
