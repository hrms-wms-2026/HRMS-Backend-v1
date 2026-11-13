using System.Text.Json;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.DTOs.Responses;
using ONEVO.Application.Common.Auditing;

namespace ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.Commands.UpdateConfigurationTemplateMetadata;

public sealed record UpdateConfigurationTemplateMetadataCommand(
    Guid TemplateId,
    string? Name,
    string? Description,
    IReadOnlyList<string>? ModuleKeys,
    string? IndustryProfileTag,
    JsonElement? PayloadJson) : IRequest<Result<ConfigurationTemplateDto>>, IAuditedCommand<Result<ConfigurationTemplateDto>>
{
    public AuditEntry DescribeAudit(Result<ConfigurationTemplateDto>? response)
        => AuditEntry.For("configuration_template.updated", "configuration_template", TemplateId, null, new { Name, Description, ModuleKeys, IndustryProfileTag, PayloadChanged = PayloadJson.HasValue });
}
