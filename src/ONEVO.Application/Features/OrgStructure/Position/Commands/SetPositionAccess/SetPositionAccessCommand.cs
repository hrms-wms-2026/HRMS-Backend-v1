using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.OrgStructure.DTOs.Responses;

namespace ONEVO.Application.Features.OrgStructure.Commands.SetPositionAccess;

public record SetPositionAccessCommand(
    Guid LegalEntityId,
    Guid PositionId,
    Guid RoleId,
    bool RequiresApproval) : IRequest<Result<PositionAccessTemplateResponse>>;
