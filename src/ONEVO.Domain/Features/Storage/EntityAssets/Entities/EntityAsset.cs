using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Storage.EntityAssets.Entities;

/// <summary>
/// Generic link from a product entity to a file. Scoped to owner_type "project"
/// only for now (Work Management project cover/logo) — see EntityAssetOwnerTypes.
/// </summary>
public class EntityAsset : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid? TenantId { get; set; }
    public string OwnerType { get; set; } = string.Empty;
    public Guid OwnerId { get; set; }
    public string AssetPurpose { get; set; } = string.Empty;
    public Guid FileRecordId { get; set; }
    public bool IsPrimary { get; set; }
    public int? SortOrder { get; set; }
    public string? MetadataJson { get; set; }
    public string CreatedByType { get; set; } = string.Empty;
    public Guid CreatedById { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }

    Guid ITenantOwnedEntity.TenantId => TenantId ?? Guid.Empty;
}
