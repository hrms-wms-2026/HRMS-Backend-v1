namespace ONEVO.Domain.Features.DevPlatform.Support.Entities;

public class SupportTicketComment
{
    public Guid Id { get; set; }
    public Guid TicketId { get; set; }
    public Guid? AuthorPlatformUserId { get; set; }
    public string Body { get; set; } = string.Empty;
    public bool IsInternal { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public SupportTicket? Ticket { get; set; }
}
