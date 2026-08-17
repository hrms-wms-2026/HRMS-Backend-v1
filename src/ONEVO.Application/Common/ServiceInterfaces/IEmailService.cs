namespace ONEVO.Application.Common.ServiceInterfaces;

public interface IEmailService
{
    Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default);
    Task SendTemplateAsync(string to, string templateId, object templateData, CancellationToken ct = default);
    Task SendPasswordResetAsync(string to, string resetToken, string? tenantSlug = null, CancellationToken ct = default);
    Task SendAdminPasswordResetAsync(string to, string resetToken, CancellationToken ct = default);
    Task SendAdminPasswordChangedAsync(string to, CancellationToken ct = default);
    Task SendPlatformManagerInviteAsync(string to, string fullName, string inviteToken, CancellationToken ct = default);
    Task SendEmployeeOnboardingInviteAsync(string to, string firstName, string lastName, string inviteToken, string? tenantSlug = null, CancellationToken ct = default);
    Task SendInvoiceEmailAsync(string to, object templateData, CancellationToken ct = default);
    Task SendPositionChangeApprovalRequestAsync(string to, string employeeName, string positionName, string? changeReason, CancellationToken ct = default);
}
