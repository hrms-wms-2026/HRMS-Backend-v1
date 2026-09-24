using ONEVO.Domain.Features.SharedPlatform.Entities;

namespace ONEVO.Application.Features.DevPlatform.Billing.RepositoryInterfaces;

public interface IBillingAuditLogRepository
{
    Task AddAsync(BillingAuditLog auditLog, CancellationToken ct = default);

    Task<IReadOnlyList<BillingAuditLog>> ListByInvoiceAsync(Guid invoiceId, CancellationToken ct = default);

    Task<IReadOnlyList<BillingAuditLog>> ListByTenantAsync(Guid tenantId, CancellationToken ct = default);
}
