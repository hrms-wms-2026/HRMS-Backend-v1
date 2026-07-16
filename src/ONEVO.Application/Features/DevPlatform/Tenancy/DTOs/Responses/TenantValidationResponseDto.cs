namespace ONEVO.Application.Features.DevPlatform.Tenancy.DTOs.Responses;

public sealed record TenantValidationResponseDto(
    bool Valid,
    IReadOnlyList<TenantValidationConflictDto> Conflicts,
    IReadOnlyList<TenantValidationWarningDto> Warnings);

public sealed record TenantValidationConflictDto(string Field, string Message);

public sealed record TenantValidationWarningDto(string Field, string Message);
