namespace ONEVO.Application.Common.ServiceInterfaces;

/// <summary>Who is calling, from where: the current HTTP request's client details, for audit rows.</summary>
public interface IRequestClientInfo
{
    string? IpAddress { get; }
    string? UserAgent { get; }
    string? CorrelationId { get; }
}
