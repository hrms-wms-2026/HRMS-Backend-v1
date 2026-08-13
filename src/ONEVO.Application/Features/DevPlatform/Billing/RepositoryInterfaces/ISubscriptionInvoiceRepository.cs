using ONEVO.Domain.Features.SharedPlatform.Entities;

namespace ONEVO.Application.Features.DevPlatform.Billing.RepositoryInterfaces;

public sealed record SubscriptionInvoiceListFilter(
    Guid? TenantId,
    string? Status,
    DateTimeOffset? From,
    DateTimeOffset? To,
    int Skip,
    int Take);

public interface ISubscriptionInvoiceRepository
{
    Task AddAsync(SubscriptionInvoice invoice, CancellationToken ct = default);

    Task UpdateAsync(SubscriptionInvoice invoice, CancellationToken ct = default);

    Task<SubscriptionInvoice?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<SubscriptionInvoice?> GetByInvoiceNumberAsync(string invoiceNumber, CancellationToken ct = default);

    Task<SubscriptionInvoice?> GetByExternalInvoiceIdAsync(string externalInvoiceId, CancellationToken ct = default);

    Task<IReadOnlyList<SubscriptionInvoice>> ListAsync(
        SubscriptionInvoiceListFilter filter,
        CancellationToken ct = default);

    Task<int> CountAsync(SubscriptionInvoiceListFilter filter, CancellationToken ct = default);

    Task<IReadOnlyList<SubscriptionInvoice>> ListByTenantAsync(Guid tenantId, CancellationToken ct = default);
}
