using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.Tenancy.DTOs.Responses;
using ONEVO.Application.Common.Auditing;

namespace ONEVO.Application.Features.DevPlatform.Tenancy.Commands.UpdateRoleTemplate;

public sealed record UpdateRoleTemplateCommand(
    Guid TemplateId,
    string Name,
    string? Description,
    IReadOnlyList<string> ModuleKeys,
    IReadOnlyList<string> PermissionCodes,
    bool IsActive) : IRequest<Result<RoleTemplateDto>>, IPlatformAuditedCommand<Result<RoleTemplateDto>>
{
    // The previous name, modules, permissions and active flag are recorded by the handler.
    public AuditEntry DescribeAudit(Result<RoleTemplateDto> response) => new()
    {
        Action = "role_template.updated",
        TargetType = "role_template",
        TargetId = TemplateId.ToString(),
        After = new
        {
            name = Name,
            description = Description,
            moduleKeys = ModuleKeys,
            permissionCodes = PermissionCodes,
            isActive = IsActive,
            version = response.Value?.Version,
        },
    };
}
