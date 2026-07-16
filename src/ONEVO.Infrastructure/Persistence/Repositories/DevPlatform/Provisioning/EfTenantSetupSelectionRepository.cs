using ONEVO.Application.Features.DevPlatform.Billing.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Provisioning.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Subscription.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Domain.Features.SharedPlatform.Entities;
using ONEVO.Infrastructure.Persistence;

namespace ONEVO.Infrastructure.Persistence.Repositories.DevPlatform.Provisioning;

public sealed class EfTenantSetupSelectionRepository : ITenantSetupSelectionRepository
{
    private readonly ApplicationDbContext _db;

    public EfTenantSetupSelectionRepository(ApplicationDbContext db) => _db = db;

    public async Task AddRangeAsync(
        Guid tenantId,
        IReadOnlyList<string> setupOptionKeys,
        DateTimeOffset createdAt,
        CancellationToken ct = default)
    {
        var selections = setupOptionKeys
            .Distinct()
            .Select(key => new TenantSetupSelection
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                SetupOptionKey = key,
                Status = "pending",
                CreatedAt = createdAt
            });

        await _db.TenantSetupSelections.AddRangeAsync(selections, ct);
    }
}
