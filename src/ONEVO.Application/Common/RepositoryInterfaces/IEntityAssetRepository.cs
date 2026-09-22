using ONEVO.Domain.Features.Storage.EntityAssets.Entities;

namespace ONEVO.Application.Common.RepositoryInterfaces;

/// <summary>Projection of an entity_assets row joined with its file_records metadata, for listing.</summary>
public sealed record EntityAssetWithFile(
    Guid Id, Guid FileRecordId, string OriginalFileName, long FileSizeBytes, string ContentType, DateTimeOffset CreatedAt, string AssetPurpose);

/// <summary>Same projection as EntityAssetWithFile but carrying OwnerId, for a batched
/// multi-owner lookup (ListByOwnersAsync) — kept as its own record so existing single-owner
/// callers of EntityAssetWithFile are unaffected.</summary>
public sealed record EntityAssetWithFileAndOwner(
    Guid OwnerId, Guid Id, Guid FileRecordId, string OriginalFileName, long FileSizeBytes, string ContentType, DateTimeOffset CreatedAt, string AssetPurpose);

public interface IEntityAssetRepository
{
    Task AddAsync(EntityAsset asset, CancellationToken ct = default);

    /// <summary>Batched lookup of each owner's primary asset file id for a given purpose (e.g. project cover images for a page of project list rows). Owners with no matching primary asset are simply absent from the result.</summary>
    Task<IReadOnlyDictionary<Guid, Guid>> GetPrimaryFileIdsByOwnerAsync(
        Guid tenantId, string ownerType, IReadOnlyCollection<Guid> ownerIds, string assetPurpose, CancellationToken ct = default);

    /// <summary>All assets for a single owner (e.g. every file attached to one objective), joined with file metadata, oldest first.</summary>
    Task<IReadOnlyList<EntityAssetWithFile>> ListByOwnerAsync(
        Guid tenantId, string ownerType, Guid ownerId, CancellationToken ct = default);

    /// <summary>Batched sibling of ListByOwnerAsync for when the caller already has many
    /// owner ids in hand (e.g. every comment on a task) and wants to avoid N+1 queries.
    /// Returns EntityAssetWithFileAndOwner (not EntityAssetWithFile) so existing single-owner
    /// callers are unaffected.</summary>
    Task<IReadOnlyList<EntityAssetWithFileAndOwner>> ListByOwnersAsync(
        Guid tenantId, string ownerType, IReadOnlyList<Guid> ownerIds, CancellationToken ct = default);

    Task<EntityAsset?> GetByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default);

    Task DeleteAsync(EntityAsset asset, CancellationToken ct = default);

    /// <summary>Finds the single asset row (if any) that links to this file record — used to
    /// check whether an uploaded file is still an unlinked "pending upload" or already
    /// attached to something.</summary>
    Task<EntityAsset?> GetByFileRecordIdAsync(Guid tenantId, Guid fileRecordId, CancellationToken ct = default);
}
