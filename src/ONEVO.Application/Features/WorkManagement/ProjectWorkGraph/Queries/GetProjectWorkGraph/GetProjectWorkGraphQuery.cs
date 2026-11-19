using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.ProjectWorkGraph.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.ProjectWorkGraph.Queries.GetProjectWorkGraph;

public sealed record GetProjectWorkGraphQuery(Guid ProjectId) : IRequest<Result<ProjectWorkGraphResponse>>;
