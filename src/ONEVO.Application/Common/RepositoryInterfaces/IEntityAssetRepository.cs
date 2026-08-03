using ONEVO.Domain.Features.Storage.EntityAssets.Entities;

namespace ONEVO.Application.Common.RepositoryInterfaces;

public interface IEntityAssetRepository
{
    Task AddAsync(EntityAsset asset, CancellationToken ct = default);
}
