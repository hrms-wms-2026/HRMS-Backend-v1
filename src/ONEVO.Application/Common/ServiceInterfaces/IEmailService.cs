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
    Task SendPositionChangeApprovalRequestAsync(string to, string employeeName, string positionName, string? changeReason, CancellationToken ct = default, string? tenantSlug = null);
    Task SendCalendarEventInviteAsync(string to, string recipientName, string eventTitle, DateTimeOffset startDateUtc, string? location, string organizerName, string? meetingLink = null, CancellationToken ct = default);

    /// <summary>A calendar invite, update, cancellation or removal email with its .ics (plan C1 P1-3 / P2-1). The default
    /// sends the plain invite template without an attachment, for implementations that predate it.</summary>
    Task SendCalendarEventEmailAsync(CalendarEventEmailMessage message, CancellationToken ct = default)
        => SendCalendarEventInviteAsync(message.To, message.RecipientName, message.EventTitle, message.StartDateUtc, message.Location,
            message.OrganizerName, message.MeetingLink, ct);
}

/// <summary>One calendar email. <see cref="Kind"/> is invite / updated / cancelled / removed; <see cref="IcsContent"/> is the
/// iCalendar text to attach (null = no attachment) and <see cref="IcsMethod"/> its METHOD (REQUEST or CANCEL).</summary>
public sealed record CalendarEventEmailMessage(
    string To, string RecipientName, string Kind, string EventTitle, DateTimeOffset StartDateUtc, string? Location, string OrganizerName,
    string? MeetingLink, string? IcsContent = null, string? IcsMethod = null);
