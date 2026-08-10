using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.OrgStructure.DTOs.Responses;

namespace ONEVO.Application.Features.OrgStructure.Commands.UpdateManualCoverageRecord;

public record UpdateManualCoverageRecordCommand(
    Guid LegalEntityId,
    Guid PositionId,
    Guid CoverageId,
    int OwnerOrder) : IRequest<Result<ManagementCoverageRecordResponse>>;
