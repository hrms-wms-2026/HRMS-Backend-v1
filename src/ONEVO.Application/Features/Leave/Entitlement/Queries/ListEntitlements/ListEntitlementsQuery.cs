using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Leave.Entitlement.DTOs.Responses;

namespace ONEVO.Application.Features.Leave.Entitlement.Queries.ListEntitlements;

public record ListEntitlementsQuery(
    int Year,
    Guid? LegalEntityId,
    Guid? DepartmentId,
    Guid? LeaveTypeId,
    string? Search) : IRequest<Result<IReadOnlyList<LeaveEntitlementResponse>>>;
