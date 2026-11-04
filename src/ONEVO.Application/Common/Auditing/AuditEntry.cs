namespace ONEVO.Application.Common.Auditing;

/// <summary>
/// What an audited command did, as an explicit projection. <see cref="After"/> (and the
/// before-state a handler records through <see cref="IAuditTrail"/>) is serialized to JSON, so it
/// must be a small anonymous object or dictionary of primitive values written out field by field.
/// </summary>
public sealed record AuditEntry
{
    public required string Action { get; init; }
    public required string TargetType { get; init; }
    public string? TargetId { get; init; }

    /// <summary>Tenant the action targeted. Required for tenant-scoped commands if the current user has none.</summary>
    public Guid? TargetTenantId { get; init; }

    public object? After { get; init; }
}
