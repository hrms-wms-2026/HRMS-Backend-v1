namespace ONEVO.Application.Common.ServiceInterfaces;

/// <summary>
/// Request to send one transactional/system email (invite, password reset, system notice).
/// Carries no secret material — provider credentials are resolved server-side from
/// platform_service_keys by the Infrastructure sender.
/// </summary>
public sealed record TransactionalEmailRequest(
    Guid? TenantId,
    string RecipientEmail,
    string Subject,
    string HtmlBody,
    string? TextBody,
    Guid? EmailDeliveryLogId = null,
    Guid? NotificationTemplateId = null,
    Guid? OutboxMessageId = null);

/// <summary>
/// Outcome of a transactional email send attempt. Contains no secret fields:
/// SafeError is a sanitized message that never includes key material, auth headers,
/// or raw provider request/response bodies.
/// </summary>
public sealed record TransactionalEmailResult(
    bool Success,
    string Provider,
    string? ProviderMessageId,
    string? SafeError,
    DateTimeOffset? SentAt)
{
    public static TransactionalEmailResult Sent(string provider, string? providerMessageId, DateTimeOffset sentAt)
        => new(true, provider, providerMessageId, null, sentAt);

    public static TransactionalEmailResult Failed(string provider, string safeError)
        => new(false, provider, null, safeError, null);
}

/// <summary>
/// Sends transactional/system email using the ONEVO-owned provider key resolved from
/// platform_service_keys. The provider selector (Email:Provider) is non-secret config;
/// the API key always comes from IPlatformServiceKeyResolver and never from appsettings.
/// </summary>
public interface ITransactionalEmailSender
{
    Task<TransactionalEmailResult> SendAsync(TransactionalEmailRequest request, CancellationToken ct = default);
}
