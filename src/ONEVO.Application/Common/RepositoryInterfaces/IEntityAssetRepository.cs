using ONEVO.Domain.Features.Storage.EntityAssets.Entities;

namespace ONEVO.Application.Common.RepositoryInterfaces;

/// <summary>Projection of an entity_assets row joined with its file_records metadata, for listing.</summary>
public sealed record EntityAssetWithFile(
    Guid Id, Guid FileRecordId, string OriginalFileName, long FileSizeBytes, string ContentType, DateTimeOffset CreatedAt);

public interface IEntityAssetRepository
{
    Task AddAsync(EntityAsset asset, CancellationToken ct = default);

    /// <summary>Batched lookup of each owner's primary asset file id for a given purpose (e.g. project cover images for a page of project list rows). Owners with no matching primary asset are simply absent from the result.</summary>
    Task<IReadOnlyDictionary<Guid, Guid>> GetPrimaryFileIdsByOwnerAsync(
        Guid tenantId, string ownerType, IReadOnlyCollection<Guid> ownerIds, string assetPurpose, CancellationToken ct = default);

    /// <summary>All assets for a single owner (e.g. every file attached to one objective), joined with file metadata, oldest first.</summary>
    Task<IReadOnlyList<EntityAssetWithFile>> ListByOwnerAsync(
        Guid tenantId, string ownerType, Guid ownerId, CancellationToken ct = default);

    Task<EntityAsset?> GetByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default);

    Task DeleteAsync(EntityAsset asset, CancellationToken ct = default);
}
