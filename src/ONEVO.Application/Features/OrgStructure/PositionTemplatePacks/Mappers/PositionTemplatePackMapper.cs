using System.Text.Json;
using ONEVO.Application.Features.OrgStructure.PositionTemplatePacks.DTOs;
using ONEVO.Domain.Features.DevPlatform.ConfigurationTemplates.Entities;

namespace ONEVO.Application.Features.OrgStructure.PositionTemplatePacks.Mappers;

internal static class PositionTemplatePackMapper
{
    private static readonly HashSet<string> AllowedEmployeeCountRangeKeys = new(StringComparer.Ordinal)
    {
        "1-10", "11-50", "51-100", "101-500", "501-1000", "1001+"
    };

    /// <summary>Deserializes and validates a configuration_templates row against the documented
    /// position_template payload schema. Returns false (no dto) for any structurally invalid or
    /// incomplete payload instead of throwing - malformed system/global template configuration
    /// must never leak a raw parse exception to the tenant-facing caller.</summary>
    internal static bool TryMap(ConfigurationTemplate template, out PositionTemplatePackDto? dto)
    {
        dto = null;

        PositionTemplatePackPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<PositionTemplatePackPayload>(template.PayloadJson);
        }
        catch (JsonException)
        {
            return false;
        }

        if (payload is null)
            return false;

        if (string.IsNullOrWhiteSpace(payload.PackName))
            return false;

        if (string.IsNullOrWhiteSpace(payload.EmployeeCountRangeKey)
            || !AllowedEmployeeCountRangeKeys.Contains(payload.EmployeeCountRangeKey))
            return false;

        if (payload.EmployeeCountMin is null || payload.EmployeeCountMin < 0)
            return false;

        if (payload.Positions is null || payload.Positions.Count == 0)
            return false;

        var positions = new List<PositionTemplatePackPositionDto>(payload.Positions.Count);
        foreach (var position in payload.Positions)
        {
            if (string.IsNullOrWhiteSpace(position.PositionKey)
                || string.IsNullOrWhiteSpace(position.PositionName)
                || string.IsNullOrWhiteSpace(position.DepartmentName))
            {
                return false;
            }

            positions.Add(new PositionTemplatePackPositionDto(
                position.PositionKey,
                position.PositionName,
                position.DepartmentName,
                position.ReportsToPositionKey,
                position.LinkedRoleTemplateId));
        }

        dto = new PositionTemplatePackDto(
            template.Id,
            template.TemplateKey,
            template.Name,
            template.Description,
            payload.Industry,
            payload.EmployeeCountRangeKey!,
            payload.EmployeeCountMin!.Value,
            payload.EmployeeCountMax,
            positions);
        return true;
    }
}
