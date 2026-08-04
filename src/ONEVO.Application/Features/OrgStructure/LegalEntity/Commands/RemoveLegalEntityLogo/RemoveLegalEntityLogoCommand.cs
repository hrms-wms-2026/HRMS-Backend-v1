using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.OrgStructure.Commands.RemoveLegalEntityLogo;

public record RemoveLegalEntityLogoCommand(Guid LegalEntityId) : IRequest<Result>;
