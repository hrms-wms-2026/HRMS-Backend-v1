using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.OrgStructure.DTOs.Responses;

namespace ONEVO.Application.Features.OrgStructure.Queries.GetLegalEntityGeneralSettings;

public record GetLegalEntityGeneralSettingsQuery(Guid LegalEntityId)
    : IRequest<Result<LegalEntityGeneralSettingsResponse>>;
