namespace ONEVO.Application.Common.ServiceInterfaces;

/// <summary>
/// Request metadata recorded on audit rows. Every value is client-influenced (the IP after the
/// trusted forwarded-headers step, the User-Agent and correlation id as sent), so treat them as
/// untrusted text: implementations truncate them to the audit column lengths. All are null
/// outside an HTTP request.
/// </summary>
public interface IRequestAuditContext
{
    string? IpAddress { get; }
    string? UserAgent { get; }
    string? CorrelationId { get; }
}
