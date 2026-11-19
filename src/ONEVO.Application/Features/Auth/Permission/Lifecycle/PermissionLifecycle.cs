namespace ONEVO.Application.Features.Auth.Permission.Lifecycle;

/// <summary>
/// The declared history of the permission catalogue: renames, one-time default grants for new codes,
/// and retirements. PermissionCatalogSynchronizer applies it, idempotently, at every sync. Every
/// change here also gets a line in docs/api/permission-changelog.md. How-to:
/// docs/development/permission-lifecycle-checklists.md.
/// </summary>
public static class PermissionLifecycle
{
    private static readonly DateOnly LegacyCutover = new(2026, 11, 19);

    public static IReadOnlyList<PermissionRename> Renames { get; } =
    [
    ];

    public static IReadOnlyList<PermissionDefaultGrant> DefaultGrants { get; } =
    [
    ];

    public static IReadOnlyList<PermissionRetirement> Retirements { get; } =
    [
        // Retired before task 33 by the old RetiredPermissionCodes list, which deleted the rows and their
        // grants at start-up. Kept as Removed + DropGrants so an old database still converges the same way.
        .. Legacy("Work Management moved to module entitlement plus project relationships.",
            "agent:command", "agent:manage", "agent:register", "agent:view-health",
            "projects:access", "projects:read",
            "tasks:read", "tasks:read-own", "tasks:write", "tasks:approve", "tasks:delete",
            "time:read", "time:write", "time:approve",
            "okr:read", "okr:write", "wiki:read", "wiki:write",
            "sprints:read", "sprints:manage",
            "workspaces:read", "workspaces:create", "workspaces:manage",
            "resources:read", "resources:manage", "roadmaps:read", "roadmaps:write"),
        .. Legacy("Never enforced. settings:billing duplicated billing:manage, settings:branding is covered by " +
                  "legal_entity:update, settings:read by legal_entity:read.",
            "settings:read", "settings:admin", "settings:billing", "settings:branding",
            "settings:integrations", "settings:alerts", "settings:system"),
        .. Legacy("Employee visibility is relationship-based (management coverage), not a permission.",
            "employees:read", "employees:read-team", "attendance:read-team"),
        .. Legacy("Invitation resend/revoke is allowed for the person who added the employee, or users:manage.",
            "invitations:manage"),
    ];

    public static PermissionLifecycleRules Rules { get; } = new(Renames, DefaultGrants, Retirements);

    private static IEnumerable<PermissionRetirement> Legacy(string reason, params string[] codes) =>
        codes.Select(code => new PermissionRetirement(
            code, RetirementStage.Removed, LegacyCutover, reason, DropGrants: true));
}
