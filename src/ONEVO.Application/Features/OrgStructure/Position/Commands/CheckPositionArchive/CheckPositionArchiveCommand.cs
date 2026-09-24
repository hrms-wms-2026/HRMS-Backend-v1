using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.OrgStructure.DTOs.Responses;

namespace ONEVO.Application.Features.OrgStructure.Commands.CheckPositionArchive;

public record CheckPositionArchiveCommand(
    Guid LegalEntityId,
    Guid PositionId) : IRequest<Result<PositionArchiveBlockers>>;
