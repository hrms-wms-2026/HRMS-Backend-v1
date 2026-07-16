namespace ONEVO.Application.Features.DevPlatform.Tenancy.Provisioning;

public interface ITenantSubscriptionStatusReader
{
    Task<ProvisioningSectionStatus> GetAsync(Guid tenantId, CancellationToken ct = default);
}
