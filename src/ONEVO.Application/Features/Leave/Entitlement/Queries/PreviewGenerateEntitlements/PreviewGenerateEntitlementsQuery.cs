using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Leave.Entitlement.DTOs.Responses;

namespace ONEVO.Application.Features.Leave.Entitlement.Queries.PreviewGenerateEntitlements;

public record PreviewGenerateEntitlementsQuery(
    int Year,
    Guid? LegalEntityId) : IRequest<Result<LeaveEntitlementGenerationPreviewResponse>>;
