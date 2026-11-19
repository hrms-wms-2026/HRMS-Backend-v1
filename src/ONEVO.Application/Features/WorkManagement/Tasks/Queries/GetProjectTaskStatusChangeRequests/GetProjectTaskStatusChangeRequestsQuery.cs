using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetProjectTaskStatusChangeRequests;

public sealed record GetProjectTaskStatusChangeRequestsQuery(Guid ProjectId)
    : IRequest<Result<ProjectTaskStatusChangeRequestsResponse>>;
