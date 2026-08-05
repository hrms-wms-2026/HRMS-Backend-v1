using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.OrgStructure.DTOs.Responses;

namespace ONEVO.Application.Features.OrgStructure.Queries.CheckDepartmentArchiveDependencies;

public record CheckDepartmentArchiveDependenciesQuery(
    Guid LegalEntityId,
    Guid DepartmentId) : IRequest<Result<DepartmentArchiveDependencyResponse>>;
