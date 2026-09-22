namespace ONEVO.Application.Features.Storage.EntityAssets.ServiceInterfaces;

/// <summary>
/// Decides whether the current caller may read a file already linked (via entity_assets) to a
/// specific owner. One implementation per owner_type, registered in
/// EntityAssetAccessPolicyResolver - GetFileQueryHandler default-denies any owner_type with no
/// registered policy, so a new owner type is unreadable through the generic resolve endpoint
/// until it explicitly opts in here.
/// </summary>
public interface IEntityAssetAccessPolicy
{
    Task<bool> CanReadAsync(Guid tenantId, Guid ownerId, CancellationToken ct = default);
}
