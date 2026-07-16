using ONEVO.Application.Common.ServiceInterfaces;

namespace ONEVO.Infrastructure.ExternalServices.Email;

/// <summary>
/// Provider-specific HTTP adapter for one transactional email provider.
/// SECURITY: the decrypted apiKey exists only in memory for the duration of the call.
/// Implementations build the Authorization header locally and must never log it,
/// never log the request body, and never include key material in returned errors.
/// </summary>
public interface IEmailProviderAdapter
{
    /// <summary>Platform service key slug this adapter serves ("sendgrid" or "resend").</summary>
    string Provider { get; }

    Task<TransactionalEmailResult> SendAsync(
        string apiKey,
        EmailOptions options,
        TransactionalEmailRequest request,
        CancellationToken ct);
}
