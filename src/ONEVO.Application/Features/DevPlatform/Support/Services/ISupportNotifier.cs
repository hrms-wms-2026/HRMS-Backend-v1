using ONEVO.Domain.Features.DevPlatform.Support.Entities;

namespace ONEVO.Application.Features.DevPlatform.Support.Services;

public enum SupportAlertKind
{
    TicketRaised,
    TenantReplied,
}

/// <summary>
/// Support notifications. Tenant notices go through the tenant notification system (in-app by default; the tenant
/// can turn on email and tray in Notification Settings). The support team is alerted by email: the assignee, or the
/// configured support inbox (Support:AlertEmail) while the ticket is unassigned.
///
/// None of these methods throw: a notification failure is logged and never fails the support action itself.
/// </summary>
public interface ISupportNotifier
{
    /// <summary>Queues tenant notices about a support reply. Written in the caller's unit of work: call before SaveChanges.</summary>
    Task QueueTenantReplyAsync(SupportTicket ticket, SupportTicketComment comment, CancellationToken ct);

    /// <summary>Queues tenant notices about a status change. Written in the caller's unit of work: call before SaveChanges.</summary>
    Task QueueTenantStatusChangeAsync(SupportTicket ticket, CancellationToken ct);

    /// <summary>Emails the support team about tenant activity. An external side effect: call after SaveChanges.</summary>
    Task AlertSupportAsync(SupportTicket ticket, SupportAlertKind kind, string actorName, string text, CancellationToken ct);
}
