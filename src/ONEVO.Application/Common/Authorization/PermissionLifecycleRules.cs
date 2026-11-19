namespace ONEVO.Application.Common.Authorization;

/// <summary>
/// Old code becomes new code. The permissions row is updated in place, so role grants and user
/// overrides, which point at the row id, follow it. Keep an entry for as long as any database may
/// still hold the old code; leaving it in is harmless.
/// </summary>
public sealed record PermissionRename(string From, string To, DateOnly Date, string Reason)
{
}

public enum DefaultGrantTargetKind
{
    /// <summary>Each tenant's built-in "Owner" role (DefaultRoleSeeder).</summary>
    OwnerRole,

    /// <summary>Roles created from one role template (roles.source_template_id).</summary>
    RoleTemplate,

    /// <summary>Roles that already hold another code, for a code split out of an existing one.</summary>
    HoldersOf,
}

public sealed record DefaultGrantTarget(DefaultGrantTargetKind Kind, Guid? TemplateId = null, string? Code = null)
{
    public static DefaultGrantTarget Owner { get; } = new(DefaultGrantTargetKind.OwnerRole);

    public static DefaultGrantTarget FromTemplate(Guid templateId) =>
        new(DefaultGrantTargetKind.RoleTemplate, TemplateId: templateId);

    public static DefaultGrantTarget HoldersOfCode(string code) =>
        new(DefaultGrantTargetKind.HoldersOf, Code: code);

    public override string ToString() => Kind switch
    {
        DefaultGrantTargetKind.OwnerRole => "owner",
        DefaultGrantTargetKind.RoleTemplate => $"template:{TemplateId}",
        _ => $"holders-of:{Code}",
    };
}

/// <summary>
/// A one-time backfill of a new code to matching roles of existing tenants. It runs once per
/// database (ledger key <c>default-grant:&lt;code&gt;</c>), so an admin who later takes the code away
/// from a role is not overruled at the next start. New tenants get their roles from
/// DefaultRoleSeeder and role templates, not from this.
/// </summary>
public sealed record PermissionDefaultGrant(
    string Code, IReadOnlyList<DefaultGrantTarget> Targets, DateOnly Date, string Reason)
{
}

public enum RetirementStage
{
    /// <summary>The row stays with retired_at set. The code stops working and is hidden from the role
    /// screen; grants stay in the table, so taking the entry out again restores them.</summary>
    SoftRetired,

    /// <summary>The row and every grant and override of it are deleted. Use in a release after
    /// SoftRetired.</summary>
    Removed,
}

/// <summary>
/// Retires a code. Built from the catalogue entries of kind SoftRetired and Retired. The sync refuses
/// when roles or overrides still hold the code, unless <see cref="DropGrants"/> says losing that access
/// is intended. To keep the access under another code, declare a <see cref="PermissionRename"/> instead.
/// </summary>
public sealed record PermissionRetirement(string Code, RetirementStage Stage, string Reason, bool DropGrants = false)
{
}

public sealed record PermissionLifecycleRules(
    IReadOnlyList<PermissionRename> Renames,
    IReadOnlyList<PermissionDefaultGrant> DefaultGrants,
    IReadOnlyList<PermissionRetirement> Retirements)
{
    public static PermissionLifecycleRules Empty { get; } = new([], [], []);
}
