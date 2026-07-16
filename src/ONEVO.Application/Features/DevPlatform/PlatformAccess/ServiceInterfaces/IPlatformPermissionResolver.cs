namespace ONEVO.Application.Features.DevPlatform.PlatformAccess.ServiceInterfaces;

/// <summary>
/// Result of resolving a platform user's effective access from the database:
/// platform_users -> platform_user_roles -> platform_role_permissions -> platform_permissions.
/// </summary>
public sealed class PlatformAccessProfile
{
    public Guid UserId { get; init; }
    public string Email { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public List<string> RoleNames { get; init; } = new();
    public HashSet<string> PermissionCodes { get; init; } = new(StringComparer.Ordinal);
}

public interface IPlatformPermissionResolver
{
    /// <summary>
    /// Loads the platform user, checks status, loads assigned roles and role permissions,
    /// and returns the effective permission set. Returns null when the user does not exist
    /// or is not active.
    /// </summary>
    Task<PlatformAccessProfile?> ResolveActiveUserAsync(Guid platformUserId, CancellationToken ct = default);
}
