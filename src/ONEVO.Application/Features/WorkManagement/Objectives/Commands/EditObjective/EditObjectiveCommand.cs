using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Commands.EditObjective;

public sealed record EditObjectiveCommand(
    Guid ObjectiveId,
    string Title,
    string? Description,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal AllocatedHours
) : IRequest<Result<ObjectiveEditOutcomeResponse>>;
