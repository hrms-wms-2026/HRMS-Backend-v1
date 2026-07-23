using System.Text.Json;

namespace ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.DTOs.Responses;

public sealed record ConfigurationTemplateDto(
    Guid Id,
    string TemplateKey,
    string TemplateType,
    string Name,
    string? Description,
    int Version,
    IReadOnlyList<string> ModuleKeys,
    string? IndustryProfileTag,
    JsonElement PayloadJson,
    bool IsSystem,
    bool IsActive,
    Guid CreatedById,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

public sealed record ConfigurationTemplateListResponseDto(
    IReadOnlyList<ConfigurationTemplateDto> Items,
    int TotalCount,
    int Page,
    int PageSize);

public sealed record ConfigurationTemplateDetailDto(
    ConfigurationTemplateDto Template,
    IReadOnlyList<TenantConfigurationTemplateApplicationDto> ApplyHistory);
