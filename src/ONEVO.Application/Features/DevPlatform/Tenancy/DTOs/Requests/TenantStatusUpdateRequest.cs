namespace ONEVO.Application.Features.DevPlatform.Tenancy.DTOs.Requests;

public sealed record TenantStatusUpdateRequest(
    string Action,
    string? Reason);
