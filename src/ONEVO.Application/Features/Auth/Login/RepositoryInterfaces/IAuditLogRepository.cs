using ONEVO.Domain.Features.Auth.Entities;

namespace ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;

public interface IAuditLogRepository
{
    Task AddAsync(AuditLog auditLog, CancellationToken ct = default);

    Task<(IReadOnlyList<AuditLog> Items, int Total)> ListByTenantIdAsync(
        Guid tenantId,
        int page,
        int pageSize,
        CancellationToken ct = default);
}
