using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.OrgStructure.DTOs.Responses;

namespace ONEVO.Application.Features.OrgStructure.Queries.GetDepartment;

public record GetDepartmentQuery(
    Guid LegalEntityId,
    Guid DepartmentId) : IRequest<Result<DepartmentResponse>>;
