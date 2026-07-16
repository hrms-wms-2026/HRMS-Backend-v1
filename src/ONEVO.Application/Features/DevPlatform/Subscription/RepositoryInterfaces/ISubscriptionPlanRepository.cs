using ONEVO.Domain.Features.SharedPlatform.Entities;

namespace ONEVO.Application.Features.DevPlatform.Subscription.RepositoryInterfaces;

public interface ISubscriptionPlanRepository
{
    Task<SubscriptionPlan?> GetByIdAsync(Guid planId, CancellationToken ct = default);
    Task<IReadOnlyList<SubscriptionPlan>> ListAsync(CancellationToken ct = default);
    Task<bool> ExistsByCodeAsync(string code, CancellationToken ct = default);
    Task AddAsync(SubscriptionPlan plan, CancellationToken ct = default);
}
