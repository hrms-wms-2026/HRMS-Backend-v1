using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Queries.GetObjectiveTree;

public sealed record GetObjectiveTreeQuery(Guid ProjectId) : IRequest<Result<IReadOnlyList<ObjectiveTreeItemResponse>>>;
