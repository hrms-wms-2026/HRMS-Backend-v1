using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.OrgStructure.DTOs.Responses;

namespace ONEVO.Application.Features.OrgStructure.Queries.GetCoverageResolution;

public record GetCoverageResolutionQuery(
    Guid LegalEntityId,
    string CoveredTargetType,
    Guid? CoveredPositionId,
    Guid? CoveredDepartmentId) : IRequest<Result<IReadOnlyList<CoverageResolutionLevelResponse>>>;
