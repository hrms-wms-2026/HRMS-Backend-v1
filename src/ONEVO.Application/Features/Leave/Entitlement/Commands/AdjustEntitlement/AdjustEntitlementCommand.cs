using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Leave.Entitlement.DTOs.Responses;

namespace ONEVO.Application.Features.Leave.Entitlement.Commands.AdjustEntitlement;

public record AdjustEntitlementCommand(
    Guid EntitlementId,
    decimal TotalHours,
    decimal CarriedForwardHours,
    string Reason,
    bool ConfirmNegativeRemaining) : IRequest<Result<LeaveEntitlementResponse>>;
