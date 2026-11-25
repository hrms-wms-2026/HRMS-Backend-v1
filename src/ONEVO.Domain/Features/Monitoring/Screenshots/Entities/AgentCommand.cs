using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Monitoring.Screenshots.Entities;

public class AgentCommand : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid AgentDeviceId { get; set; }
    public Guid RequestedById { get; set; }
    public string CommandType { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = "{}";
    public string Status { get; set; } = AgentCommandStatuses.Pending;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DeliveredAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? ResultJson { get; set; }
}

/// <summary>Values of <see cref="AgentCommand.CommandType"/> (task 39f: a check constraint reads them).</summary>
public static class AgentCommandTypes
{
    public const string CaptureScreenshot = "capture_screenshot";
}

/// <summary>Values of <see cref="AgentCommand.Status"/> (task 39f: a check constraint reads them).</summary>
public static class AgentCommandStatuses
{
    public const string Pending = "pending";
    public const string Delivered = "delivered";
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string Expired = "expired";
}
