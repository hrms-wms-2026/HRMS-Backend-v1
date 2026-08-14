using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Commands.TransferObjectiveHead;

public sealed record TransferObjectiveHeadCommand(Guid ObjectiveId, Guid NewHeadEmployeeId) : IRequest<Result<ObjectiveChangeOutcomeResponse>>;
