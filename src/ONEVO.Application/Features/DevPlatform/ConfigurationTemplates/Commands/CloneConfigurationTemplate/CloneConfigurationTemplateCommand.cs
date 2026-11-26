using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.DTOs.Responses;
using ONEVO.Application.Common.Auditing;

namespace ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.Commands.CloneConfigurationTemplate;

public sealed record CloneConfigurationTemplateCommand(Guid TemplateId, Guid CreatedById)
    : IRequest<Result<ConfigurationTemplateDto>>, IAuditedCommand<Result<ConfigurationTemplateDto>>
{
    public AuditEntry DescribeAudit(Result<ConfigurationTemplateDto>? response)
        => AuditEntry.For("configuration_template.cloned", "configuration_template", response?.Value?.Id, null, new { SourceTemplateId = TemplateId });
}
