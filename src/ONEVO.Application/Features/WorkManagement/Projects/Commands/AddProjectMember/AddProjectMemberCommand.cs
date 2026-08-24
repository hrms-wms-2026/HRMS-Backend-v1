using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Projects.Commands.AddProjectMember;

public sealed record AddProjectMemberCommand(Guid ProjectId, Guid EmployeeId)
    : IRequest<Result<AddObjectiveMemberOutcomeResponse>>;
