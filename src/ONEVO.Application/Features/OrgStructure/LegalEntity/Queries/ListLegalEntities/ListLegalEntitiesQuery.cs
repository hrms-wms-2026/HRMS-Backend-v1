using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.OrgStructure.DTOs.Responses;

namespace ONEVO.Application.Features.OrgStructure.Queries.ListLegalEntities;

public record ListLegalEntitiesQuery(bool IncludeInactive = false)
    : IRequest<Result<IReadOnlyList<LegalEntityListItemResponse>>>;
