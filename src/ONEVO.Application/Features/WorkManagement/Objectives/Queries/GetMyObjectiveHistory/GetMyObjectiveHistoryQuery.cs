using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Queries.GetMyObjectiveHistory;

public sealed record GetMyObjectiveHistoryQuery() : IRequest<Result<IReadOnlyList<ObjectiveHistoryItemResponse>>>;
