using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.OrgStructure.DTOs.Responses;

namespace ONEVO.Application.Features.OrgStructure.Queries.GetPositionCoverage;

public record GetPositionCoverageQuery(
    Guid LegalEntityId,
    Guid PositionId) : IRequest<Result<IReadOnlyList<ManagementCoverageRecordResponse>>>;
