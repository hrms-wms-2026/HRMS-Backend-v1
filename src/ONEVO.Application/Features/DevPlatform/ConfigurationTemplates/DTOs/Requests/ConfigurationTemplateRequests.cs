using System.Text.Json;

namespace ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.DTOs.Requests;

public sealed record CreateConfigurationTemplateRequest(
    string TemplateKey,
    string TemplateType,
    string Name,
    string? Description,
    IReadOnlyList<string> ModuleKeys,
    string? IndustryProfileTag,
    JsonElement PayloadJson,
    bool IsSystem);

public sealed record UpdateConfigurationTemplateRequest(
    string? Name,
    string? Description,
    IReadOnlyList<string>? ModuleKeys,
    string? IndustryProfileTag,
    JsonElement? PayloadJson);

public sealed record ApplyConfigurationTemplateRequest(bool ForceUpdate);
