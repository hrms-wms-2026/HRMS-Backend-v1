using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.Tenancy.DTOs.Responses;
using ONEVO.Application.Common.Auditing;
using ONEVO.Application.Features.DevPlatform.Validation;

namespace ONEVO.Application.Features.DevPlatform.Tenancy.Commands.UpdateRoleTemplate;

public sealed record UpdateRoleTemplateCommand(
    Guid TemplateId,
    string Name,
    string? Description,
    IReadOnlyList<string> ModuleKeys,
    IReadOnlyList<string> PermissionCodes,
    bool IsActive) : IRequest<Result<RoleTemplateDto>>, IAuditedCommand<Result<RoleTemplateDto>>, IRoleTemplateInput
{
    public AuditEntry DescribeAudit(Result<RoleTemplateDto>? response)
        => AuditEntry.For("role_template.updated", "role_template", TemplateId, null, new { Name, Description, ModuleKeys, PermissionCodes, IsActive });
}
