namespace ONEVO.Domain.Features.DevPlatform.Support.Entities;

/// <summary>
/// A platform-wide announcement written by platform staff (canonical `platform_announcements`).
/// Not tenant-owned: one row is shown to every tenant (audience "all" or "tenants") and/or to the
/// platform console (audience "all" or "platform_admins") while it is published.
/// </summary>
public class PlatformAnnouncement
{
    public const string SeverityInfo = "info";
    public const string SeverityWarning = "warning";
    public const string SeverityCritical = "critical";

    public const string AudienceAll = "all";
    public const string AudienceTenants = "tenants";
    public const string AudiencePlatformAdmins = "platform_admins";

    public static readonly IReadOnlyList<string> Severities = [SeverityInfo, SeverityWarning, SeverityCritical];
    public static readonly IReadOnlyList<string> Audiences = [AudienceAll, AudienceTenants, AudiencePlatformAdmins];

    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string Severity { get; set; } = SeverityInfo;
    public string Audience { get; set; } = AudienceAll;
    public bool IsPublished { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public Guid? PublishedByPlatformUserId { get; set; }

    /// <summary>Optional display window. A published announcement shows only from StartsAt (inclusive) until EndsAt (exclusive).</summary>
    public DateTimeOffset? StartsAt { get; set; }
    public DateTimeOffset? EndsAt { get; set; }

    public Guid? CreatedByPlatformUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }

    public bool IsVisibleToTenants => AudienceAll.Equals(Audience, StringComparison.Ordinal)
        || AudienceTenants.Equals(Audience, StringComparison.Ordinal);

    public bool IsVisibleToPlatformAdmins => AudienceAll.Equals(Audience, StringComparison.Ordinal)
        || AudiencePlatformAdmins.Equals(Audience, StringComparison.Ordinal);
}
