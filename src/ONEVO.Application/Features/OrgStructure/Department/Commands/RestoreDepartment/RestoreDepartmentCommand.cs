using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.OrgStructure.Commands.RestoreDepartment;

public record RestoreDepartmentCommand(
    Guid LegalEntityId,
    Guid DepartmentId) : IRequest<Result<bool>>;
