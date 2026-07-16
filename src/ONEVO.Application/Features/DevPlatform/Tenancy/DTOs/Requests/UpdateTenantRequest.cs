namespace ONEVO.Application.Features.DevPlatform.Tenancy.DTOs.Requests;

public sealed record UpdateTenantRequest(
    string? Name,
    string? Slug,
    string? IndustryProfile);
