using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.OrgStructure.DTOs.Responses;

namespace ONEVO.Application.Features.OrgStructure.Queries.GetPositionById;

public record GetPositionByIdQuery(
    Guid LegalEntityId,
    Guid PositionId) : IRequest<Result<PositionResponse>>;
