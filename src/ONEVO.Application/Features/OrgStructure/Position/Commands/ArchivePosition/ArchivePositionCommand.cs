using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.OrgStructure.Commands.ArchivePosition;

public record ArchivePositionCommand(
    Guid LegalEntityId,
    Guid PositionId) : IRequest<Result<bool>>;
