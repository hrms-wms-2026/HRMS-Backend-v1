using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.Tenancy.DTOs.Responses;
using ONEVO.Application.Common.Auditing;

namespace ONEVO.Application.Features.DevPlatform.Tenancy.Commands.CreateRoleTemplate;

public sealed record CreateRoleTemplateCommand(
    string Name,
    string? Description,
    IReadOnlyList<string> ModuleKeys,
    IReadOnlyList<string> PermissionCodes) : IRequest<Result<RoleTemplateDto>>, IPlatformAuditedCommand<Result<RoleTemplateDto>>
{
    public AuditEntry DescribeAudit(Result<RoleTemplateDto> response) => new()
    {
        Action = "role_template.created",
        TargetType = "role_template",
        TargetId = response.Value?.Id.ToString(),
        After = new
        {
            name = Name,
            description = Description,
            moduleKeys = ModuleKeys,
            permissionCodes = PermissionCodes,
        },
    };
}
