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
    JsonElement? PayloadJson) : IRequest<Result<ConfigurationTemplateDto>>, IPlatformAuditedCommand<Result<ConfigurationTemplateDto>>
{
    // The template payload is not copied into the audit row; only whether it changed.
    public AuditEntry DescribeAudit(Result<ConfigurationTemplateDto> response) => new()
    {
        Action = "configuration_template.updated",
        TargetType = "configuration_template",
        TargetId = TemplateId.ToString(),
        After = new
        {
            name = Name,
            description = Description,
            moduleKeys = ModuleKeys,
            industryProfileTag = IndustryProfileTag,
            payloadReplaced = PayloadJson.HasValue,
            version = response.Value?.Version,
        },
    };
}
