using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.OrgStructure.Commands.DeleteLegalEntity;

public record DeleteLegalEntityCommand(Guid LegalEntityId, string ConfirmName) : IRequest<Result>;
