namespace ONEVO.Application.Features.OrgStructure.PositionTemplatePacks.DTOs;

public sealed record PositionTemplatePackPositionDto(
    string PositionKey,
    string PositionName,
    string DepartmentName,
    string? ReportsToPositionKey,
    Guid? LinkedRoleTemplateId);

public sealed record PositionTemplatePackDto(
    Guid Id,
    string TemplateKey,
    string Name,
    string? Description,
    string? IndustryProfileTag,
    string EmployeeCountRangeKey,
    int EmployeeCountMin,
    int? EmployeeCountMax,
    IReadOnlyList<PositionTemplatePackPositionDto> Positions);

public sealed record PositionTemplatePackListResponseDto(IReadOnlyList<PositionTemplatePackDto> Items);
