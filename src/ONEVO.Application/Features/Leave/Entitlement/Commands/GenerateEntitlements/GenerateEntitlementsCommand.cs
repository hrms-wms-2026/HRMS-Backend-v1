using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Leave.Entitlement.DTOs.Responses;

namespace ONEVO.Application.Features.Leave.Entitlement.Commands.GenerateEntitlements;

public record GenerateEntitlementsCommand(
    int Year,
    Guid? LegalEntityId) : IRequest<Result<LeaveEntitlementGenerationResultResponse>>;
