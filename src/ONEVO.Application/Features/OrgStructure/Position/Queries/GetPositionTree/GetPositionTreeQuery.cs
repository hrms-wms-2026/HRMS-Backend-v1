using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.OrgStructure.DTOs.Responses;

namespace ONEVO.Application.Features.OrgStructure.Queries.GetPositionTree;

public record GetPositionTreeQuery(
    Guid LegalEntityId,
    bool IncludeInactive) : IRequest<Result<IReadOnlyList<PositionTreeNodeResponse>>>;
