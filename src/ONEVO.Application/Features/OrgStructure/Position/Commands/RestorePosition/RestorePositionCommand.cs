using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.OrgStructure.Commands.RestorePosition;

public record RestorePositionCommand(
    Guid LegalEntityId,
    Guid PositionId) : IRequest<Result<bool>>;
