namespace ONEVO.Application.Features.Storage.EntityAssets.ServiceInterfaces;

public interface IEntityAssetAccessPolicyResolver
{
    /// <summary>Null if no policy is registered for this owner_type - the caller must treat that as deny, not as "no restriction".</summary>
    IEntityAssetAccessPolicy? Resolve(string ownerType);
}
