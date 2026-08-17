using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.Exceptions;
using ONEVO.Application.Common.ServiceInterfaces;

namespace ONEVO.Infrastructure.ExternalServices.Email;

/// <summary>
/// IEmailService implementation for transactional/system email (invites, password
/// resets). Renders templates locally and delegates delivery to
/// ITransactionalEmailSender, which resolves the ONEVO-owned provider key from
/// platform_providers plus platform_service_keys. A failed send throws with the sanitized error so the outbox
/// retry model (attempt_count / next_attempt_at / last_error) applies unchanged; the
/// thrown message never contains key material.
/// </summary>
public class TransactionalEmailService : IEmailService
{
    private readonly ITransactionalEmailSender _sender;
    private readonly IEmailTemplateRenderer _renderer;
    private readonly ILogger<TransactionalEmailService> _logger;

    public TransactionalEmailService(
        ITransactionalEmailSender sender,
        IEmailTemplateRenderer renderer,
        ILogger<TransactionalEmailService> logger)
    {
        _sender = sender;
        _renderer = renderer;
        _logger = logger;
    }

    public Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
        => SendInternalAsync(to, subject, htmlBody, textBody: null, ct);

    public Task SendTemplateAsync(string to, string templateId, object templateData, CancellationToken ct = default)
    {
        var rendered = _renderer.Render(templateId, templateData);
        return SendInternalAsync(to, rendered.Subject, rendered.HtmlBody, rendered.TextBody, ct);
    }

    public Task SendPasswordResetAsync(string to, string resetToken, string? tenantSlug = null, CancellationToken ct = default)
        => SendTemplateAsync(to, "password_reset", new { reset_token = resetToken, tenant_slug = tenantSlug }, ct);

    public Task SendAdminPasswordResetAsync(string to, string resetToken, CancellationToken ct = default)
        => SendTemplateAsync(to, "admin_password_reset", new { reset_token = resetToken }, ct);

    public Task SendAdminPasswordChangedAsync(string to, CancellationToken ct = default)
        => SendTemplateAsync(to, "admin_password_changed", new { }, ct);

    public Task SendPlatformManagerInviteAsync(string to, string fullName, string inviteToken, CancellationToken ct = default)
        => SendTemplateAsync(to, "platform_manager_invite", new { full_name = fullName, invite_token = inviteToken }, ct);

    public Task SendEmployeeOnboardingInviteAsync(string to, string firstName, string lastName, string inviteToken, string? tenantSlug = null, CancellationToken ct = default)
        => SendTemplateAsync(to, "employee_onboarding_invite", new { first_name = firstName, last_name = lastName, invite_token = inviteToken, tenant_slug = tenantSlug }, ct);

    public Task SendInvoiceEmailAsync(string to, object templateData, CancellationToken ct = default)
        => SendTemplateAsync(to, "invoice_email", templateData, ct);

    private async Task SendInternalAsync(
        string to,
        string subject,
        string htmlBody,
        string? textBody,
        CancellationToken ct)
    {
        var request = new TransactionalEmailRequest(
            TenantId: null,
            RecipientEmail: to,
            Subject: subject,
            HtmlBody: htmlBody,
            TextBody: textBody);

        var result = await _sender.SendAsync(request, ct);
        if (result.Success)
            return;

        var safeError = string.IsNullOrWhiteSpace(result.SafeError)
            ? $"Email send failed via provider '{result.Provider}'."
            : result.SafeError;

        _logger.LogWarning("Email to {To} via {Provider} failed: {SafeError}",
            to, result.Provider, safeError);

        if (safeError is TransactionalEmailFailureCodes.ProviderUnavailable or
            TransactionalEmailFailureCodes.ProviderAmbiguous)
        {
            throw new ServiceUnavailableException(safeError);
        }

        throw new InvalidOperationException(safeError);
    }
}
