namespace ONEVO.Application.Common.Auditing;

/// <summary>
/// Reads the audited target's state for a command, so the audit row can carry real before and
/// after values. <c>AuditingBehavior</c> calls it once before the handler and once after a
/// successful handler, inside the same transaction. Return an explicit, secret-free projection
/// (or null when the target does not exist yet). Implementations are registered automatically.
/// </summary>
public interface IAuditStateReader<in TCommand>
{
    Task<object?> ReadAsync(TCommand command, CancellationToken ct);
}
