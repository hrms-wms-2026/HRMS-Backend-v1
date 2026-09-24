using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetMyTaskEditRequests;

public sealed record GetMyTaskEditRequestsQuery
    : IRequest<Result<IReadOnlyList<TaskEditRequestResponse>>>;
