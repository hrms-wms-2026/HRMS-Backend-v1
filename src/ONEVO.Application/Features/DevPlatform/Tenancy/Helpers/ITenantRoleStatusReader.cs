namespace ONEVO.Application.Features.DevPlatform.Tenancy.Provisioning;

public interface ITenantRoleStatusReader
{
    Task<ProvisioningSectionStatus> GetAsync(Guid tenantId, CancellationToken ct = default);
}
