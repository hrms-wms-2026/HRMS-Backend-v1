using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.OrgStructure.DTOs.Responses;

namespace ONEVO.Application.Features.OrgStructure.Queries.GetCoverageByTarget;

// Every active coverage record for one covered target, regardless of which position owns it -
// used by the "add coverage" UI to show which responsibility levels are already claimed by other
// owner positions before submit, instead of only finding out via a 409 from AddManualCoverageRecord.
public record GetCoverageByTargetQuery(
    Guid LegalEntityId,
    string CoveredTargetType,
    Guid? CoveredPositionId,
    Guid? CoveredDepartmentId,
    Guid? ExcludingRecordId) : IRequest<Result<IReadOnlyList<ManagementCoverageRecordResponse>>>;
