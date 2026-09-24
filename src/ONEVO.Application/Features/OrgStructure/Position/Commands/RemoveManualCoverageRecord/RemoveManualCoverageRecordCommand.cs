using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.OrgStructure.Commands.RemoveManualCoverageRecord;

public record RemoveManualCoverageRecordCommand(
    Guid LegalEntityId,
    Guid PositionId,
    Guid CoverageId) : IRequest<Result<bool>>;
