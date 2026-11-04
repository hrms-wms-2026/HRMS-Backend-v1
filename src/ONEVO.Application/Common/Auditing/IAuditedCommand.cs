namespace ONEVO.Application.Common.Auditing;

/// <summary>
/// Marks a command as a privileged change that must leave an audit row. Do not implement this
/// directly: implement <see cref="IPlatformAuditedCommand{TResponse}"/> (Developer Platform action,
/// written to platform_audit_logs) or <see cref="ITenantAuditedCommand{TResponse}"/> (tenant action,
/// written to the tenant's audit_logs). <c>AuditingBehavior</c> writes the row in the same database
/// transaction as the change, and only when the command succeeds.
/// </summary>
public interface IAuditedCommand
{
}

/// <summary>Shared shape of the two audited-command markers.</summary>
public interface IAuditedCommand<in TResponse> : IAuditedCommand
{
    /// <summary>
    /// Describes the change after it succeeded. Build <see cref="AuditEntry.After"/> from explicit,
    /// named values only: never pass the command, an entity, a DTO graph or any secret. A
    /// credential rotation records that it happened (and safe metadata), never the value.
    /// </summary>
    AuditEntry DescribeAudit(TResponse response);
}

/// <summary>Developer Platform privileged action, recorded in platform_audit_logs.</summary>
public interface IPlatformAuditedCommand<in TResponse> : IAuditedCommand<TResponse>
{
}

/// <summary>Tenant privileged action, recorded in the current tenant's audit_logs.</summary>
public interface ITenantAuditedCommand<in TResponse> : IAuditedCommand<TResponse>
{
}
