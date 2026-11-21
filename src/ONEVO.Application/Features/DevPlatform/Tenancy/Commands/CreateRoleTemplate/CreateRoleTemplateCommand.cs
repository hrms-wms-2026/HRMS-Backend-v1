using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.Tenancy.DTOs.Responses;
using ONEVO.Application.Common.Auditing;
using ONEVO.Application.Features.DevPlatform.Validation;

namespace ONEVO.Application.Features.DevPlatform.Tenancy.Commands.CreateRoleTemplate;

public sealed record CreateRoleTemplateCommand(
    string Name,
    string? Description,
    IReadOnlyList<string> ModuleKeys,
    IReadOnlyList<string> PermissionCodes) : IRequest<Result<RoleTemplateDto>>, IAuditedCommand<Result<RoleTemplateDto>>, IRoleTemplateInput
{
    public AuditEntry DescribeAudit(Result<RoleTemplateDto>? response)
        => AuditEntry.For("role_template.created", "role_template", response?.Value?.Id, null, new { Name, Description, ModuleKeys, PermissionCodes });
}
