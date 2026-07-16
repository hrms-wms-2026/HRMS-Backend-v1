namespace ONEVO.Application.Features.DevPlatform.Provisioning.RepositoryInterfaces;

public interface ITenantSetupSelectionRepository
{
    Task AddRangeAsync(
        Guid tenantId,
        IReadOnlyList<string> setupOptionKeys,
        DateTimeOffset createdAt,
        CancellationToken ct = default);
}
