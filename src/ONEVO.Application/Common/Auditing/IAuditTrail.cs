namespace ONEVO.Application.Common.Auditing;

/// <summary>
/// Per-request holder for state a handler read before it changed it. <c>AuditingBehavior</c> opens a
/// frame for each audited command, and the handler calls <see cref="RecordBefore"/> with an explicit
/// projection (never the entity itself). Calls outside an audited command are ignored.
/// </summary>
public interface IAuditTrail
{
    void RecordBefore(object before);

    /// <summary>
    /// Target id known only inside the handler (for example a row it created when the response
    /// does not carry the id). Used when <see cref="AuditEntry.TargetId"/> is null.
    /// </summary>
    void RecordTarget(string targetId);
}
