using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Auth.ActiveCompany.Commands.SwitchActiveCompany;

public sealed record SwitchActiveCompanyCommand(Guid LegalEntityId) : IRequest<Result<Unit>>;
