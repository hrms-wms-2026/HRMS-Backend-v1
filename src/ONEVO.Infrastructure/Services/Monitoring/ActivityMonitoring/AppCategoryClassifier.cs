namespace ONEVO.Infrastructure.Services.Monitoring.ActivityMonitoring;

public enum AppCategory { Productive, Personal, Unknown }

/// <summary>
/// Hardcoded process-name -> category lookup for productivity reporting.
/// Not tenant-configurable (YAGNI per 2026-08-17 Reports &amp; Analytics design spec) -
/// extend this list or add tenant overrides later if requested.
/// </summary>
public static class AppCategoryClassifier
{
    private static readonly HashSet<string> ProductiveProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "code.exe", "devenv.exe", "excel.exe", "winword.exe", "powerpnt.exe",
        "outlook.exe", "teams.exe", "slack.exe", "figma.exe", "postman.exe",
        "notepad++.exe", "sourcetree.exe", "dbeaver.exe"
    };

    private static readonly HashSet<string> PersonalProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "spotify.exe", "steam.exe", "discord.exe", "netflix.exe", "whatsapp.exe", "epicgameslauncher.exe"
    };

    public static AppCategory Classify(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return AppCategory.Unknown;
        if (ProductiveProcesses.Contains(processName)) return AppCategory.Productive;
        if (PersonalProcesses.Contains(processName)) return AppCategory.Personal;
        return AppCategory.Unknown;
    }
}
