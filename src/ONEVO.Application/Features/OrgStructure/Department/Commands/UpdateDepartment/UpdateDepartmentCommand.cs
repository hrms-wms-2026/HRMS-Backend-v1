using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.OrgStructure.DTOs.Responses;

namespace ONEVO.Application.Features.OrgStructure.Commands.UpdateDepartment;

public record UpdateDepartmentCommand(
    Guid LegalEntityId,
    Guid DepartmentId,
    string Name,
    string? Code,
    Guid? ParentDepartmentId,
    Guid? HeadPositionId) : IRequest<Result<DepartmentResponse>>;
