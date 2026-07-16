namespace ONEVO.Application.Features.DevPlatform.Tenancy.Provisioning;

public interface ITenantModuleStatusReader
{
    Task<ProvisioningSectionStatus> GetAsync(Guid tenantId, CancellationToken ct = default);
}
