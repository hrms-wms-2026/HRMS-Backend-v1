namespace ONEVO.Application.Common.ServiceInterfaces;

public interface IEmailService
{
    Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default);
    Task SendTemplateAsync(string to, string templateId, object templateData, CancellationToken ct = default);
    Task SendPasswordResetAsync(string to, string resetToken, string? tenantSlug = null, CancellationToken ct = default);
}
