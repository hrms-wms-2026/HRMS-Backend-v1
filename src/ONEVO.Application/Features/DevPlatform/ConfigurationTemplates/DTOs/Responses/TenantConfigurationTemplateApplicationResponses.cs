using System.Text.Json;

namespace ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.DTOs.Responses;

public sealed record TenantConfigurationTemplateApplicationDto(
    Guid Id,
    Guid TenantId,
    Guid ConfigurationTemplateId,
    string TemplateType,
    int AppliedVersion,
    JsonElement AppliedPayloadJson,
    JsonElement? CustomPayloadJson,
    IReadOnlyList<string> Warnings,
    string Status,
    Guid AppliedById,
    DateTimeOffset AppliedAt);

public sealed record TenantConfigurationTemplateApplicationListResponseDto(
    IReadOnlyList<TenantConfigurationTemplateApplicationDto> Items,
    int TotalCount,
    int Page,
    int PageSize);

public sealed record ApplyConfigurationTemplateResultDto(
    Guid ApplicationId,
    int AppliedVersion,
    IReadOnlyList<string> Warnings);
