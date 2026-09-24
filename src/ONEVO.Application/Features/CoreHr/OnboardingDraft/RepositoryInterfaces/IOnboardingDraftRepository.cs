using ONEVO.Application.Features.CoreHr.OnboardingDrafts.DTOs.Responses;

namespace ONEVO.Application.Features.CoreHr.OnboardingDrafts.RepositoryInterfaces;

public interface IOnboardingDraftRepository
{
    Task<ONEVO.Domain.Features.CoreHr.Entities.OnboardingDraft?> GetTrackedAsync(
        Guid tenantId, Guid id, CancellationToken ct = default);

    Task<OnboardingDraftResponse?> GetResponseByIdAsync(
        Guid tenantId, Guid id, CancellationToken ct = default);

    Task<(IReadOnlyList<OnboardingDraftResponse> Items, int TotalCount)> ListAsync(
        Guid tenantId, Guid? startedById, int page, int pageSize, CancellationToken ct = default);

    /// <summary>List view with resolved position/department/started-by names for display.</summary>
    Task<(IReadOnlyList<DraftListItemResponse> Items, int TotalCount)> ListWithNamesAsync(
        Guid tenantId, Guid? startedById, int page, int pageSize, CancellationToken ct = default);

    Task AddAsync(ONEVO.Domain.Features.CoreHr.Entities.OnboardingDraft draft, CancellationToken ct = default);

    /// <summary>
    /// Sets the tracked entity's original xmin value to the caller-supplied expected version
    /// before SaveChangesAsync, so EF Core's optimistic concurrency check runs against it
    /// rather than whatever was last read - required for a correct If-Match/409 semantics
    /// when the entity was loaded fresh rather than carried across requests in memory.
    /// </summary>
    void SetExpectedVersion(ONEVO.Domain.Features.CoreHr.Entities.OnboardingDraft draft, string expectedVersion);

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
