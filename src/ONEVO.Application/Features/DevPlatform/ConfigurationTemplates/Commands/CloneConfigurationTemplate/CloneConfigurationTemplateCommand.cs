using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.DTOs.Responses;
using ONEVO.Application.Common.Auditing;

namespace ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.Commands.CloneConfigurationTemplate;

public sealed record CloneConfigurationTemplateCommand(Guid TemplateId, Guid CreatedById)
    : IRequest<Result<ConfigurationTemplateDto>>, IPlatformAuditedCommand<Result<ConfigurationTemplateDto>>
{
    public AuditEntry DescribeAudit(Result<ConfigurationTemplateDto> response) => new()
    {
        Action = "configuration_template.cloned",
        TargetType = "configuration_template",
        TargetId = response.Value?.Id.ToString(),
        After = new { sourceTemplateId = TemplateId, templateKey = response.Value?.TemplateKey },
    };
}
