namespace ONEVO.Application.Features.DevPlatform.Tenancy.DTOs.Requests;

public sealed record TenantValidationQueryRequest(
    string? Slug,
    string? CompanyName,
    string? EmailDomain,
    string? RegistrationNumber,
    string? Country);
