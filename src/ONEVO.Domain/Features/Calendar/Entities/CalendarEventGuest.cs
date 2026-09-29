using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Calendar.Entities;

/// <summary>An external invitee identified only by email (not an employee). Guests get the invite
/// email but have no login, so there is no RSVP. Removal is a real delete, so the unique
/// (tenant, event, email) index never blocks re-inviting the same address.</summary>
public class CalendarEventGuest : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid EventId { get; set; }
    public string Email { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
