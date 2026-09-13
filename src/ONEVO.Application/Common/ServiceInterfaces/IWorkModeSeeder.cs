namespace ONEVO.Application.Common.ServiceInterfaces;

public interface IWorkModeSeeder
{
    Task SeedDefaultsAsync(Guid tenantId, Guid legalEntityId, CancellationToken ct = default);
}
