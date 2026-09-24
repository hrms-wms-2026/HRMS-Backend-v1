using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Queries.GetObjectiveById;

public sealed record GetObjectiveByIdQuery(Guid ObjectiveId) : IRequest<Result<ObjectiveDetailResponse>>;
