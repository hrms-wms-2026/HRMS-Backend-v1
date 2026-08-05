using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.OrgStructure.DTOs.Responses;

namespace ONEVO.Application.Features.OrgStructure.Commands.CreateDepartment;

public record CreateDepartmentCommand(
    Guid LegalEntityId,
    string Name,
    string? Code,
    Guid? ParentDepartmentId,
    Guid? HeadPositionId) : IRequest<Result<DepartmentResponse>>;
