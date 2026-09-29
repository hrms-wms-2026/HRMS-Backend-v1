using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Storage.EntityAssets.ServiceInterfaces;

/// <summary>
/// Links an already-uploaded pending file as an owner's single primary asset. Owning features
/// remain responsible for authorizing access to the owner before calling this service.
/// </summary>
public interface IPrimaryEntityAssetLinker
{
    Task<Result<Guid?>> LinkAsync(
        Guid tenantId,
        Guid userId,
        string ownerType,
        Guid ownerId,
        string purpose,
        Guid fileId,
        CancellationToken ct = default);

    Task<Result> RemoveAsync(
        Guid tenantId,
        Guid userId,
        string ownerType,
        Guid ownerId,
        string purpose,
        CancellationToken ct = default);
}
