using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Application.Features.CoreHr.BulkOnboarding.RepositoryInterfaces;

public interface IBulkOnboardingBatchRepository
{
    Task<BulkOnboardingBatch?> GetTrackedAsync(Guid tenantId, Guid id, CancellationToken ct = default);
    Task<BulkOnboardingBatch?> GetAsync(Guid tenantId, Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<BulkOnboardingBatchRow>> ListRowsAsync(Guid tenantId, Guid batchId, CancellationToken ct = default);
    Task<IReadOnlyList<BulkOnboardingBatchRow>> ListTrackedRowsAsync(Guid tenantId, Guid batchId, CancellationToken ct = default);

    /// <summary>Cross-tenant lookup for the background worker only - it does not yet have a
    /// resolved tenant context when picking the next batch to process.</summary>
    Task<BulkOnboardingBatch?> GetOldestPendingAsync(string status, CancellationToken ct = default);

    Task AddAsync(BulkOnboardingBatch batch, IReadOnlyList<BulkOnboardingBatchRow> rows, CancellationToken ct = default);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
