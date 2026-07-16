using ONEVO.Domain.Features.InfrastructureModule.Entities;

namespace ONEVO.Application.Common.ServiceInterfaces;

public interface ITenantContext
{
    Guid TenantId { get; }
    string? Slug { get; }
    TenantStatus? Status { get; }
    bool IsResolved { get; }
    TenantContextMode ContextMode { get; }
}
