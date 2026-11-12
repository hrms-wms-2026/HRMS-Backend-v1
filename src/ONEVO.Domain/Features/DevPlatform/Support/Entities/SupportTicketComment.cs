using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.DevPlatform.Support.Entities;

/// <summary>
/// A reply or internal note on a support ticket (canonical `support_ticket_comments`). Internal notes
/// are written only by platform staff and are never returned to the tenant. tenant_id repeats the
/// ticket's tenant so the row carries its own tenant isolation.
/// </summary>
public class SupportTicketComment : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid TicketId { get; set; }
    public Guid? AuthorPlatformUserId { get; set; }
    public Guid? AuthorUserId { get; set; }
    public string AuthorName { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public bool IsInternal { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public SupportTicket Ticket { get; set; } = null!;
}
