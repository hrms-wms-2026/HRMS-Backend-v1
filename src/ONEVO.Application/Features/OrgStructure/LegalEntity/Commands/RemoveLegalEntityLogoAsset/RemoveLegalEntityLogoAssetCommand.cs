using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.OrgStructure.Commands.RemoveLegalEntityLogoAsset;

public sealed record RemoveLegalEntityLogoAssetCommand(Guid LegalEntityId) : IRequest<Result>;
