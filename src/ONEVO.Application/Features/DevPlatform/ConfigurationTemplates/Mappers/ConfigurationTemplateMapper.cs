using System.Text.Json;
using ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.DTOs.Responses;
using ONEVO.Domain.Features.DevPlatform.ConfigurationTemplates.Entities;

namespace ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.Mappers;

internal static class ConfigurationTemplateMapper
{
    internal static ConfigurationTemplateDto ToDto(ConfigurationTemplate t) =>
        new(
            t.Id,
            t.TemplateKey,
            t.TemplateType,
            t.Name,
            t.Description,
            t.Version,
            DeserializeStringList(t.ModuleKeysJson),
            t.IndustryProfileTag,
            JsonDocument.Parse(t.PayloadJson).RootElement.Clone(),
            t.IsSystem,
            t.IsActive,
            t.CreatedById,
            t.CreatedAt,
            t.UpdatedAt);

    internal static ConfigurationTemplateListResponseDto ToListResponseDto(
        IReadOnlyList<ConfigurationTemplateDto> items, int totalCount, int page, int pageSize) =>
        new(items, totalCount, page, pageSize);

    internal static TenantConfigurationTemplateApplicationDto ToDto(TenantConfigurationTemplateApplication a) =>
        new(
            a.Id,
            a.TenantId,
            a.ConfigurationTemplateId,
            a.TemplateType,
            a.AppliedVersion,
            JsonDocument.Parse(a.AppliedPayloadJson).RootElement.Clone(),
            a.CustomPayloadJson is null ? null : JsonDocument.Parse(a.CustomPayloadJson).RootElement.Clone(),
            a.WarningsJson is null ? new List<string>() : DeserializeStringList(a.WarningsJson),
            a.Status,
            a.AppliedById,
            a.AppliedAt);

    internal static TenantConfigurationTemplateApplicationListResponseDto ToListResponseDto(
        IReadOnlyList<TenantConfigurationTemplateApplicationDto> items, int totalCount, int page, int pageSize) =>
        new(items, totalCount, page, pageSize);

    internal static string SerializeStringList(IReadOnlyList<string> values) =>
        JsonSerializer.Serialize(values);

    internal static List<string> DeserializeStringList(string json) =>
        string.IsNullOrWhiteSpace(json)
            ? new List<string>()
            : JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
}
