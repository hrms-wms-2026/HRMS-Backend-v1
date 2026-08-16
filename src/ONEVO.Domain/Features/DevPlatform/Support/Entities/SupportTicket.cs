namespace ONEVO.Domain.Features.DevPlatform.Support.Entities;

public class SupportTicket
{
    public const string StatusOpen = "open";
    public const string StatusInProgress = "in_progress";
    public const string StatusResolved = "resolved";
    public const string StatusClosed = "closed";

    public static readonly IReadOnlyList<string> AllStatuses = new[]
    {
        StatusOpen,
        StatusInProgress,
        StatusResolved,
        StatusClosed,
    };

    public const string PriorityLow = "low";
    public const string PriorityMedium = "medium";
    public const string PriorityHigh = "high";
    public const string PriorityUrgent = "urgent";

    public static readonly IReadOnlyList<string> AllPriorities = new[]
    {
        PriorityLow,
        PriorityMedium,
        PriorityHigh,
        PriorityUrgent,
    };

    public Guid Id { get; set; }
    public Guid? TenantId { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Status { get; set; } = StatusOpen;
    public string Priority { get; set; } = PriorityMedium;
    public string? Category { get; set; }
    public Guid? CreatedByPlatformUserId { get; set; }
    public Guid? AssignedToPlatformUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }

    public List<SupportTicketComment> Comments { get; set; } = new();
}
