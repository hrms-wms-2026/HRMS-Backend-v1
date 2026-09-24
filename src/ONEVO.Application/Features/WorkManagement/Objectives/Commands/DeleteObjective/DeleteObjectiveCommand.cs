using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Commands.DeleteObjective;

public sealed record DeleteObjectiveCommand(Guid ObjectiveId) : IRequest<Result<ObjectiveChangeOutcomeResponse>>;
