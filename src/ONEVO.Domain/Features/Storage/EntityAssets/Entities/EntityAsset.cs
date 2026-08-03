using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Storage.EntityAssets.Entities;

/// <summary>
/// Generic link from a product entity to a file. Scoped to owner_type "project"
/// only for now (Work Management project cover/logo) — see EntityAssetOwnerTypes.
///
/// TenantId is non-nullable here (unlike the nullable column documented for
/// platform-level assets in phase1-table-inventory.md) because every
/// ITenantOwnedEntity in this codebase's shared tenant query-filter builder
/// (ApplicationDbContext.BuildGenericTenantAndSoftDeleteFilterBody) reflects on
/// a plain Guid TenantId property; this task never creates a platform-level
/// (null-tenant) row anyway. Revisit if/when a platform-level owner type is added.
/// </summary>
public class EntityAsset : BaseEntity
{
    public string OwnerType { get; set; } = string.Empty;
    public Guid OwnerId { get; set; }
    public string AssetPurpose { get; set; } = string.Empty;
    public Guid FileRecordId { get; set; }
    public bool IsPrimary { get; set; }
    public int? SortOrder { get; set; }
    public string? MetadataJson { get; set; }
    public string CreatedByType { get; set; } = string.Empty;
}
