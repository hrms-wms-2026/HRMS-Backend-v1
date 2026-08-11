using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Commands.AchieveObjective;

public sealed record AchieveObjectiveCommand(Guid ObjectiveId) : IRequest<Result<ObjectiveChangeOutcomeResponse>>;
