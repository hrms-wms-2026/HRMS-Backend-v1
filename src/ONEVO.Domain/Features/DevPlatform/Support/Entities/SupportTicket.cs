using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.DevPlatform.Support.Entities;

/// <summary>
/// A support ticket between one tenant and the platform support team (canonical `support_tickets`).
/// Tenant-owned: a tenant admin sees only their own tenant's tickets (query filter + RLS), platform
/// staff see every tenant's tickets in admin mode. Raised either by a tenant user from the tenant app
/// or by platform staff on the tenant's behalf - exactly one of the two creator columns is set.
/// </summary>
public class SupportTicket : ITenantOwnedEntity
{
    public const string StatusOpen = "open";
    public const string StatusInProgress = "in_progress";
    public const string StatusResolved = "resolved";
    public const string StatusClosed = "closed";

    public const string PriorityLow = "low";
    public const string PriorityMedium = "medium";
    public const string PriorityHigh = "high";
    public const string PriorityUrgent = "urgent";

    public static readonly IReadOnlyList<string> Statuses = [StatusOpen, StatusInProgress, StatusResolved, StatusClosed];
    public static readonly IReadOnlyList<string> Priorities = [PriorityLow, PriorityMedium, PriorityHigh, PriorityUrgent];

    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Status { get; set; } = StatusOpen;
    public string Priority { get; set; } = PriorityMedium;
    public string? Category { get; set; }

    /// <summary>Set when platform staff raised the ticket on the tenant's behalf.</summary>
    public Guid? CreatedByPlatformUserId { get; set; }

    /// <summary>Set when a tenant user raised the ticket from the tenant app.</summary>
    public Guid? CreatedByUserId { get; set; }

    /// <summary>Display name of the creator, captured at write time so either side can show it.</summary>
    public string CreatedByName { get; set; } = string.Empty;

    public Guid? AssignedToPlatformUserId { get; set; }

    /// <summary>
    /// True while the tenant is waiting on support: set when the tenant raises the ticket or replies, cleared when
    /// support replies (not an internal note) or resolves/closes the ticket. Drives the admin console's attention count.
    /// </summary>
    public bool AwaitingSupport { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }

    public ICollection<SupportTicketComment> Comments { get; set; } = new List<SupportTicketComment>();
    public ICollection<SupportTicketAttachment> Attachments { get; set; } = new List<SupportTicketAttachment>();
}
