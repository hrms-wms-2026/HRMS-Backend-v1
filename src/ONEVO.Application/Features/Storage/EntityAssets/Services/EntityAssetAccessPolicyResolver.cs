using ONEVO.Application.Features.Storage.EntityAssets.ServiceInterfaces;

namespace ONEVO.Application.Features.Storage.EntityAssets.Services;

public sealed class EntityAssetAccessPolicyResolver : IEntityAssetAccessPolicyResolver
{
    private readonly IReadOnlyDictionary<string, IEntityAssetAccessPolicy> _policiesByOwnerType;

    public EntityAssetAccessPolicyResolver(IReadOnlyDictionary<string, IEntityAssetAccessPolicy> policiesByOwnerType)
        => _policiesByOwnerType = policiesByOwnerType;

    public IEntityAssetAccessPolicy? Resolve(string ownerType)
        => _policiesByOwnerType.TryGetValue(ownerType, out var policy) ? policy : null;
}
