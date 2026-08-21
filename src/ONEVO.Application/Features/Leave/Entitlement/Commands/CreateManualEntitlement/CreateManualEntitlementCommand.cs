using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Leave.Entitlement.DTOs.Responses;

namespace ONEVO.Application.Features.Leave.Entitlement.Commands.CreateManualEntitlement;

public record CreateManualEntitlementCommand(
    Guid EmployeeId,
    Guid LeaveTypeId,
    int Year,
    decimal TotalDays,
    decimal CarriedForwardDays,
    string Reason) : IRequest<Result<LeaveEntitlementResponse>>;
