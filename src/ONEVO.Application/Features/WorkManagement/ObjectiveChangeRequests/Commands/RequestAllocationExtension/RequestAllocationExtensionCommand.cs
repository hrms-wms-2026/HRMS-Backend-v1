using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.Commands.RequestAllocationExtension;

public sealed record RequestAllocationExtensionCommand(
    Guid ObjectiveId, decimal RequestedAdditionalHours, string Reason
) : IRequest<Result<ObjectiveChangeRequestResponse>>;
