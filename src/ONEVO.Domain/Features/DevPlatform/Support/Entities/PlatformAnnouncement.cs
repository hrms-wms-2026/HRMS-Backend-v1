namespace ONEVO.Domain.Features.DevPlatform.Support.Entities;

public class PlatformAnnouncement
{
    public const string SeverityInfo = "info";
    public const string SeverityWarning = "warning";
    public const string SeverityCritical = "critical";

    public static readonly IReadOnlyList<string> AllSeverities = new[]
    {
        SeverityInfo,
        SeverityWarning,
        SeverityCritical,
    };

    public const string AudienceAll = "all";
    public const string AudienceTenants = "tenants";
    public const string AudiencePlatformAdmins = "platform_admins";

    public static readonly IReadOnlyList<string> AllAudiences = new[]
    {
        AudienceAll,
        AudienceTenants,
        AudiencePlatformAdmins,
    };

    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string Severity { get; set; } = SeverityInfo;
    public string Audience { get; set; } = AudienceAll;
    public bool IsPublished { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
}
