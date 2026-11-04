using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.DTOs.Responses;
using ONEVO.Application.Common.Auditing;

namespace ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.Commands.ApplyConfigurationTemplateToTenant;

public sealed record ApplyConfigurationTemplateToTenantCommand(
    Guid TenantId,
    Guid TemplateId,
    bool ForceUpdate,
    Guid AppliedById) : IRequest<Result<ApplyConfigurationTemplateResultDto>>, IPlatformAuditedCommand<Result<ApplyConfigurationTemplateResultDto>>
{
    public AuditEntry DescribeAudit(Result<ApplyConfigurationTemplateResultDto> response) => new()
    {
        Action = "configuration_template.applied_to_tenant",
        TargetType = "configuration_template",
        TargetId = TemplateId.ToString(),
        TargetTenantId = TenantId,
        After = new
        {
            forceUpdate = ForceUpdate,
            applicationId = response.Value?.ApplicationId,
            appliedVersion = response.Value?.AppliedVersion,
        },
    };
}
