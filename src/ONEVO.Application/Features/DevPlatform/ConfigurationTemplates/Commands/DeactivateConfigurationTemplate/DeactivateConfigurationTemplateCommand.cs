using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.DTOs.Responses;
using ONEVO.Application.Common.Auditing;

namespace ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.Commands.DeactivateConfigurationTemplate;

public sealed record DeactivateConfigurationTemplateCommand(Guid TemplateId)
    : IRequest<Result<ConfigurationTemplateDto>>, IAuditedCommand<Result<ConfigurationTemplateDto>>
{
    public AuditEntry DescribeAudit(Result<ConfigurationTemplateDto>? response)
        => AuditEntry.For("configuration_template.deactivated", "configuration_template", TemplateId);
}
