namespace ONEVO.Application.Common.ServiceInterfaces;

public interface IWritableTenantContext : ITenantContext
{
    void Resolve(TenantRegistryEntry tenant);
    void SetAdminMode();
    void SetSystemMode();
}
