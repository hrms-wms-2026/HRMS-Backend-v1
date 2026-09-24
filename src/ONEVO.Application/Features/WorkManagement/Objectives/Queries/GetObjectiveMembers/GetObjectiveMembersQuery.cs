using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Queries.GetObjectiveMembers;

public sealed record GetObjectiveMembersQuery(Guid ObjectiveId) : IRequest<Result<ObjectiveMemberListResponse>>;
