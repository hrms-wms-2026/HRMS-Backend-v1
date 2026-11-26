namespace ONEVO.Application.Common.Auditing;

/// <summary>
/// Marker for a command that is a privileged change and must leave an audit row (security audit
/// finding F-07). Implement <see cref="IAuditedCommand{TResponse}"/>, never this interface alone:
/// <c>AuditingBehavior</c> only recognises the generic form, and an architecture test checks that
/// its type argument matches the command's response type.
/// </summary>
public interface IAuditedCommand
{
}

/// <summary>
/// A command whose successful (and failed) execution is written to the audit trail by
/// <c>AuditingBehavior</c>. <see cref="DescribeAudit"/> is an explicit, hand-written projection:
/// never serialize the command, the request body or an entity by reflection, and never include a
/// secret, password, API key, token or private key. Record the fact of a rotation, not the value.
/// </summary>
/// <typeparam name="TResponse">The command's MediatR response type (<c>Unit</c> for <c>IRequest</c>).</typeparam>
public interface IAuditedCommand<in TResponse> : IAuditedCommand
{
    /// <param name="response">
    /// The handler's response, or <c>default</c> when the handler threw. On a failed
    /// <c>Result</c> its value is null, so read ids from it null-safely.
    /// </param>
    AuditEntry DescribeAudit(TResponse? response);
}
