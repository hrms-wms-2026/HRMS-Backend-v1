using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetMyTaskCreationRequests;

public sealed record GetMyTaskCreationRequestsQuery() : IRequest<Result<IReadOnlyList<TaskCreationRequestResponse>>>;
