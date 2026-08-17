using ONEVO.Domain.Features.Storage.EntityAssets.Entities;

namespace ONEVO.Application.Common.RepositoryInterfaces;

public interface IEntityAssetRepository
{
    Task AddAsync(EntityAsset asset, CancellationToken ct = default);

    /// <summary>Batched lookup of each owner's primary asset file id for a given purpose (e.g. project cover images for a page of project list rows). Owners with no matching primary asset are simply absent from the result.</summary>
    Task<IReadOnlyDictionary<Guid, Guid>> GetPrimaryFileIdsByOwnerAsync(
        Guid tenantId, string ownerType, IReadOnlyCollection<Guid> ownerIds, string assetPurpose, CancellationToken ct = default);
}
