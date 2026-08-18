using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Commands.CreateObjective;

public sealed record CreateObjectiveCommand(
    Guid ParentObjectiveId,
    string Title,
    string? Description,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal AllocatedHours,
    Guid? HeadEmployeeId,
    IReadOnlyList<(Guid EmployeeId, string Type)>? MemberInvitations
) : IRequest<Result<ObjectiveDetailResponse>>;
