using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.DTOs.Responses;
using ONEVO.Application.Common.Auditing;

namespace ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.Commands.ApplyConfigurationTemplateToTenant;

public sealed record ApplyConfigurationTemplateToTenantCommand(
    Guid TenantId,
    Guid TemplateId,
    bool ForceUpdate,
    Guid AppliedById) : IRequest<Result<ApplyConfigurationTemplateResultDto>>, IAuditedCommand<Result<ApplyConfigurationTemplateResultDto>>
{
    public AuditEntry DescribeAudit(Result<ApplyConfigurationTemplateResultDto>? response)
        => AuditEntry.For("configuration_template.applied_to_tenant", "configuration_template", TemplateId, TenantId, new { ForceUpdate, AppliedVersion = response?.Value?.AppliedVersion, ApplicationId = response?.Value?.ApplicationId });
}
