namespace ONEVO.Application.Features.DevPlatform.Tenancy.Provisioning;

public interface ITenantSettingsStatusReader
{
    Task<ProvisioningSectionStatus> GetAsync(Guid tenantId, CancellationToken ct = default);
}
