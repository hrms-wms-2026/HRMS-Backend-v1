using System.Text.Json;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.DTOs.Responses;
using ONEVO.Application.Common.Auditing;

namespace ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.Commands.CreateConfigurationTemplate;

public sealed record CreateConfigurationTemplateCommand(
    string TemplateKey,
    string TemplateType,
    string Name,
    string? Description,
    IReadOnlyList<string> ModuleKeys,
    string? IndustryProfileTag,
    JsonElement PayloadJson,
    bool IsSystem,
    Guid CreatedById) : IRequest<Result<ConfigurationTemplateDto>>, IPlatformAuditedCommand<Result<ConfigurationTemplateDto>>
{
    // The template payload is not copied into the audit row.
    public AuditEntry DescribeAudit(Result<ConfigurationTemplateDto> response) => new()
    {
        Action = "configuration_template.created",
        TargetType = "configuration_template",
        TargetId = response.Value?.Id.ToString(),
        After = new
        {
            templateKey = TemplateKey,
            templateType = TemplateType,
            name = Name,
            moduleKeys = ModuleKeys,
            industryProfileTag = IndustryProfileTag,
            isSystem = IsSystem,
            version = response.Value?.Version,
        },
    };
}
