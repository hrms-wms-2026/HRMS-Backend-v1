using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Queries.GetObjectiveSubtree;

public sealed record GetObjectiveSubtreeQuery(Guid ObjectiveId) : IRequest<Result<ObjectiveSubtreeResponse>>;
