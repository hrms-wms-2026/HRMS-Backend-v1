namespace ONEVO.Application.Features.InfrastructureModule.User.DTOs.Responses;

public sealed record UserDto(
    Guid Id,
    Guid TenantId,
    string Email,
    string FirstName,
    string LastName,
    bool IsActive,
    bool EmailVerified);
