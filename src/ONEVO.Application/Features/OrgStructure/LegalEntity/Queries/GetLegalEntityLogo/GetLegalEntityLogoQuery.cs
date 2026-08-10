using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Storage.File.DTOs.Responses;

namespace ONEVO.Application.Features.OrgStructure.Queries.GetLegalEntityLogo;

public record GetLegalEntityLogoQuery(Guid LegalEntityId) : IRequest<Result<FileStreamDto>>;
