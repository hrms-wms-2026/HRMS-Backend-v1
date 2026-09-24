using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.OrgStructure.DTOs.Responses;

namespace ONEVO.Application.Features.OrgStructure.Commands.LinkLegalEntityLogo;

public sealed record LinkLegalEntityLogoCommand(Guid LegalEntityId, Guid FileId)
    : IRequest<Result<LegalEntityLogoResponse>>;
