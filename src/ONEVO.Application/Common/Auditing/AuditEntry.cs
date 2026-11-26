namespace ONEVO.Application.Common.Auditing;

/// <summary>
/// What one audited command did. <paramref name="Details"/> is serialized as the row's "after"
/// value unless an <see cref="IAuditStateReader{TCommand}"/> supplies real before/after state. It
/// must be an explicit, secret-free projection (an anonymous object listing the fields to keep).
/// </summary>
public sealed record AuditEntry(
    string Action,
    string TargetType,
    string? TargetId,
    Guid? TargetTenantId = null,
    object? Details = null)
{
    public static AuditEntry For(
        string action,
        string targetType,
        Guid? targetId,
        Guid? targetTenantId = null,
        object? details = null)
        => new(action, targetType, targetId?.ToString(), targetTenantId, details);
}
