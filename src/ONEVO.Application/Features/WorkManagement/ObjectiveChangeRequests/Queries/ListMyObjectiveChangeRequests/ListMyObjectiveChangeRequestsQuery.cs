using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.Queries.ListMyObjectiveChangeRequests;

public sealed record ListMyObjectiveChangeRequestsQuery() : IRequest<Result<IReadOnlyList<ObjectiveChangeRequestResponse>>>;
