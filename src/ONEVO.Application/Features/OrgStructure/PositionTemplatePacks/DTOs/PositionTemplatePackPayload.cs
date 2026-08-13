using System.Text.Json.Serialization;

namespace ONEVO.Application.Features.OrgStructure.PositionTemplatePacks.DTOs;

/// <summary>Deserialization shape for configuration_templates.payload_json when template_type = position_template.
/// Field names mirror the documented snake_case payload schema exactly.</summary>
internal sealed record PositionTemplatePackPayload(
    [property: JsonPropertyName("pack_name")] string? PackName,
    [property: JsonPropertyName("employee_count_range_key")] string? EmployeeCountRangeKey,
    [property: JsonPropertyName("employee_count_min")] int? EmployeeCountMin,
    [property: JsonPropertyName("employee_count_max")] int? EmployeeCountMax,
    [property: JsonPropertyName("industry")] string? Industry,
    [property: JsonPropertyName("positions")] List<PositionTemplatePackPositionPayload>? Positions);

internal sealed record PositionTemplatePackPositionPayload(
    [property: JsonPropertyName("position_key")] string? PositionKey,
    [property: JsonPropertyName("position_name")] string? PositionName,
    [property: JsonPropertyName("department_name")] string? DepartmentName,
    [property: JsonPropertyName("reports_to_position_key")] string? ReportsToPositionKey,
    [property: JsonPropertyName("linked_role_template_id")] Guid? LinkedRoleTemplateId);
