using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.OrgStructure.DTOs.Responses;

namespace ONEVO.Application.Features.OrgStructure.Commands.SetLegalEntityLogo;

public record SetLegalEntityLogoCommand(Guid LegalEntityId, Guid FileId) : IRequest<Result<LegalEntityLogoResponse>>;
