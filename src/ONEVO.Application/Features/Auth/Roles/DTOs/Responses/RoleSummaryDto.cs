using ONEVO.Application.Features.Auth.Roles.DTOs.Responses;
namespace ONEVO.Application.Features.Auth.Roles.DTOs.Responses;

public record RoleSummaryDto(
    Guid Id,
    string Name,
    string Description,
    bool IsSystem,
    int PermissionCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    Guid? SourceTemplateId = null);
