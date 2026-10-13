namespace ONEVO.Domain.Features.DevPlatform.AppCatalog.Entities;

public static class GlobalApplicationCategories
{
    public const string Meeting = "meeting";
    public const string Documentation = "documentation";
    public const string Browser = "browser";
    public const string Development = "development";
    public const string Communication = "communication";
    public const string Office = "office";
    public const string Design = "design";
    public const string Productivity = "productivity";
    public const string Other = "other";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Meeting, Documentation, Browser, Development, Communication,
        Office, Design, Productivity, Other
    };
}

/// <summary>
/// Platform-wide identity for an application observed by any tenant. ProcessName is
/// normalized to lower-case and is the authoritative cross-tenant identity key.
/// Category null means that the application is still unallocated.
/// </summary>
public sealed class GlobalApplication
{
    public Guid Id { get; set; }
    public string AppName { get; set; } = string.Empty;
    public string ProcessName { get; set; } = string.Empty;
    public string? Category { get; set; }
    public string? Publisher { get; set; }
    public byte[]? Logo { get; set; }
    public string? LogoContentType { get; set; }
    public bool IsPublic { get; set; } = true;
    public bool IsProductiveDefault { get; set; }
    public Guid? CreatedById { get; set; }
    public DateTimeOffset FirstSeenAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }

    public bool IsAllocated => Category is not null;

    public static string NormalizeProcessName(string processName) => processName.Trim().ToLowerInvariant();

    public static string DefaultDisplayName(string processName)
    {
        var normalized = NormalizeProcessName(processName);
        return normalized.EndsWith(".exe", StringComparison.Ordinal)
            ? normalized[..^4]
            : normalized;
    }
}
