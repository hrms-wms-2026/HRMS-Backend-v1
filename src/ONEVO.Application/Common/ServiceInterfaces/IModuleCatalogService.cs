using ONEVO.Domain.Features.SharedPlatform.Entities;

namespace ONEVO.Application.Common.ServiceInterfaces;

public interface IModuleCatalogService
{
    Task<IReadOnlySet<string>> GetActiveModuleKeysAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ModuleCatalogItem>> GetByCatalogKeysAsync(IReadOnlyList<string> keys, CancellationToken ct = default);
}
