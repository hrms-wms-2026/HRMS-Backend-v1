using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Sprints.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Sprints.Queries.GetProjectSprints;

public sealed record GetProjectSprintsQuery(Guid ProjectId) : IRequest<Result<IReadOnlyList<SprintResponse>>>;
