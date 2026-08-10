using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.OrgStructure.DTOs.Responses;

namespace ONEVO.Application.Features.OrgStructure.Commands.AddManualCoverageRecord;

public record AddManualCoverageRecordCommand(
    Guid LegalEntityId,
    Guid PositionId,
    string CoveredTargetType,
    Guid? CoveredPositionId,
    Guid? CoveredDepartmentId,
    int OwnerOrder) : IRequest<Result<ManagementCoverageRecordResponse>>;
