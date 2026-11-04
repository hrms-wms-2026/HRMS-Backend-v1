namespace ONEVO.Application.Features.Dashboard.Layout.Registry;

public static class DashboardKeys
{
    public const string MyWork = "my-work";
    public const string MyTeam = "my-team";

    public static string Title(string dashboardKey) => dashboardKey switch
    {
        MyWork => "My Work",
        MyTeam => "My Team",
        _ => dashboardKey,
    };
}

public static class Breakpoints
{
    public const string Desktop = "desktop";
    public const string Tablet = "tablet";
    public const string Mobile = "mobile";

    public static readonly string[] All = [Desktop, Tablet, Mobile];

    public static int Columns(string breakpoint) => breakpoint switch
    {
        Desktop => 12,
        Tablet => 6,
        Mobile => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(breakpoint), breakpoint, "Unknown breakpoint."),
    };
}

public sealed record GridRect(int X, int Y, int W, int H);

/// <summary>Sizes are desktop; tablet/mobile clamp W to the breakpoint column count. ShownOnWeb = false for a widget
/// the web app keeps in the saved layout but never renders: the bootstrap loads no data for it.</summary>
public sealed record WidgetDefinition(
    string Key,
    string DashboardKey,
    string Title,
    string Description,
    int MinW,
    int MinH,
    int MaxW,
    int MaxH,
    bool HasProvider,
    bool ShownOnWeb = true);
