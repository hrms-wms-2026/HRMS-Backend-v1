using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.OrgStructure.Commands.ArchiveDepartment;

public record ArchiveDepartmentCommand(
    Guid LegalEntityId,
    Guid DepartmentId) : IRequest<Result<bool>>;
