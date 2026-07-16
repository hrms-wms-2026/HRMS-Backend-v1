using ONEVO.Domain.Features.InfrastructureModule.Entities;

namespace ONEVO.Application.Common.ServiceInterfaces;

public sealed record TenantRegistryEntry(
    Guid TenantId,
    string Slug,
    TenantStatus Status,
    string? PlanCode
);
