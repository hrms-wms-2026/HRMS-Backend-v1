using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Queries.GetMyProjectMilestones;

public sealed record GetMyProjectMilestonesQuery(Guid ProjectId) : IRequest<Result<IReadOnlyList<MyProjectMilestoneResponse>>>;
