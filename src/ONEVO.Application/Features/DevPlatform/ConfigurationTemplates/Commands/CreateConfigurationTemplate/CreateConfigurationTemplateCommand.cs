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
    Guid CreatedById) : IRequest<Result<ConfigurationTemplateDto>>, IAuditedCommand<Result<ConfigurationTemplateDto>>
{
    public AuditEntry DescribeAudit(Result<ConfigurationTemplateDto>? response)
        => AuditEntry.For("configuration_template.created", "configuration_template", response?.Value?.Id, null, new { TemplateKey, TemplateType, Name, Description, ModuleKeys, IndustryProfileTag, IsSystem });
}
