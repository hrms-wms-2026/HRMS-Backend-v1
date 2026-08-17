using ONEVO.Domain.Features.Auth.Entities;
using AuthPermission = ONEVO.Domain.Features.Auth.Entities.Permission;

namespace ONEVO.Application.Features.Auth.Permission.RepositoryInterfaces;

public interface IPermissionRepository
{
    Task<AuthPermission?> GetByCodeAsync(string code, CancellationToken ct = default);
    Task<IReadOnlyList<AuthPermission>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct = default);
    Task<IReadOnlyList<AuthPermission>> GetByCodesAsync(IEnumerable<string> codes, CancellationToken ct = default);
    Task<bool> UserHasPermissionCodeAsync(
        Guid userId,
        string permissionCode,
        DateTimeOffset now,
        CancellationToken ct = default);
    Task<IReadOnlyList<string>> ListRolePermissionCodesAsync(
        Guid userId,
        DateTimeOffset now,
        CancellationToken ct = default);
    /// <summary>Role-derived permission codes for a user, filtered to grants effective now.
    /// When activeLegalEntityId is not null, a UserRole row is only included if it has no
    /// SourcePositionId (not entity-scoped - e.g. a manually assigned admin role) OR its
    /// SourcePositionId's Position belongs to activeLegalEntityId. Pass null to skip entity
    /// filtering entirely (returns every role grant regardless of entity).</summary>
    Task<IReadOnlyList<PermissionCodeWithModule>> ListRolePermissionCodesWithModulesAsync(
        Guid userId,
        DateTimeOffset now,
        Guid? activeLegalEntityId,
        CancellationToken ct = default);

    /// <summary>Every user in the tenant currently holding permissionCode via an
    /// unexpired UserRole, regardless of which role grants it. Used to resolve who can
    /// approve a sensitive AccessGrantRequest (roles:manage) - the inverse of
    /// ListRolePermissionCodesWithModulesAsync, which goes user -> permissions.</summary>
    Task<IReadOnlyList<Guid>> ListUserIdsWithPermissionCodeAsync(
        Guid tenantId, string permissionCode, DateTimeOffset now, CancellationToken ct = default);
}
