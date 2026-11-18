using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.DevPlatform.Support.Entities;

/// <summary>
/// A file attached to a support ticket (canonical `support_ticket_attachments`). The bytes live in
/// object storage under <see cref="ObjectKey"/>, which is never exposed to clients; downloads stream
/// through the API after the caller's access to the ticket is checked. Internal attachments are
/// uploaded by platform staff and are never returned to the tenant.
/// </summary>
public class SupportTicketAttachment : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid TicketId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string ObjectKey { get; set; } = string.Empty;
    public bool IsInternal { get; set; }
    public Guid? UploadedByPlatformUserId { get; set; }
    public Guid? UploadedByUserId { get; set; }
    public string UploadedByName { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }

    public SupportTicket Ticket { get; set; } = null!;
}
