using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.ProjectInvitations.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.ProjectInvitations.Queries.GetMyObjectiveInvitations;

public sealed record GetMyObjectiveInvitationsQuery : IRequest<Result<IReadOnlyList<ProjectMemberInvitationResponse>>>;
