using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Commands.UnachieveObjective;

public sealed record UnachieveObjectiveCommand(Guid ObjectiveId) : IRequest<Result<ObjectiveChangeOutcomeResponse>>;
