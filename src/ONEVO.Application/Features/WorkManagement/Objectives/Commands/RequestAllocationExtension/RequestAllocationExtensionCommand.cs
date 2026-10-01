using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Commands.RequestAllocationExtension;

public sealed record RequestAllocationExtensionCommand(
    Guid ObjectiveId, decimal RequestedAdditionalHours, string Reason
) : IRequest<Result<ObjectiveChangeOutcomeResponse>>;
