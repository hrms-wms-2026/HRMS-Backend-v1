using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Leave.Entitlement.DTOs.Responses;

namespace ONEVO.Application.Features.Leave.Entitlement.Commands.RecalculateEntitlement;

public record RecalculateEntitlementCommand(
    Guid EntitlementId,
    bool ConfirmNegativeRemaining) : IRequest<Result<LeaveEntitlementResponse>>;
