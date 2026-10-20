using ONEVO.Application.Features.Dashboard.Layout.Grid;

namespace ONEVO.Application.Features.Dashboard.Layout.Registry;

/// <summary>Code-defined widget catalog. Sizes are desktop (12 columns).</summary>
public static class DashboardWidgetRegistry
{
    private static readonly WidgetDefinition[] Definitions =
    [
        new("today-at-a-glance", DashboardKeys.MyWork, "Today at a glance", "Your day at a glance: attendance, leave and key counts.", 3, 3, 6, 5, true),
        new("action-center", DashboardKeys.MyWork, "Action center", "Items waiting for your action.", 4, 3, 12, 6, false),
        new("quick-actions", DashboardKeys.MyWork, "Quick actions", "Shortcuts to common tasks.", 3, 3, 6, 4, false),
        new("todays-schedule", DashboardKeys.MyWork, "Today's schedule", "Your shifts and events for today.", 3, 3, 12, 6, true),
        new("today-total-time", DashboardKeys.MyWork, "Today's total time", "Time worked so far today.", 3, 3, 6, 5, true),
        new("active-projects", DashboardKeys.MyWork, "Active projects", "Projects you are currently working on.", 3, 3, 6, 6, true),
        new("my-tasks", DashboardKeys.MyWork, "My tasks", "Tasks assigned to you.", 3, 3, 12, 6, true),
        new("work-pattern", DashboardKeys.MyWork, "Work pattern", "Your recent working pattern.", 6, 3, 12, 5, true),
        new("notifications", DashboardKeys.MyWork, "Notifications", "Your latest notifications.", 3, 3, 6, 6, false),
        new("wellbeing", DashboardKeys.MyWork, "Wellbeing", "Wellbeing insights for you.", 3, 3, 6, 4, true),
        new("team-status", DashboardKeys.MyTeam, "Team status", "Who in your team is working, away or off.", 6, 4, 12, 8, true),
        new("team-quick-actions", DashboardKeys.MyTeam, "Team quick actions", "Shortcuts for managing your team.", 3, 3, 6, 4, false),
        new("team-progress", DashboardKeys.MyTeam, "Team progress", "Estimated hours progress across your team.", 4, 4, 12, 8, true),
    ];

    private static readonly Dictionary<string, WidgetDefinition> ByKey =
        Definitions.ToDictionary(d => d.Key, StringComparer.Ordinal);

    public static WidgetDefinition? Get(string key) =>
        key is not null && ByKey.TryGetValue(key, out var def) ? def : null;

    public static IReadOnlyList<WidgetDefinition> ForDashboard(string dashboardKey) =>
        Definitions.Where(d => d.DashboardKey == dashboardKey).ToList();

    // Desktop default placements. Widgets absent here are in the catalog but not placed by default.
    private static readonly (string Key, GridRect Rect)[] MyWorkDefaults =
    [
        ("today-at-a-glance", new GridRect(0, 0, 3, 4)),
        ("action-center", new GridRect(3, 0, 6, 4)),
        ("quick-actions", new GridRect(9, 0, 3, 4)),
        ("todays-schedule", new GridRect(0, 4, 6, 4)),
        ("today-total-time", new GridRect(6, 4, 3, 4)),
        ("active-projects", new GridRect(9, 4, 3, 4)),
        ("my-tasks", new GridRect(0, 8, 6, 4)),
        ("work-pattern", new GridRect(0, 12, 12, 4)),
    ];

    private static readonly (string Key, GridRect Rect)[] MyTeamDefaults =
    [
        ("team-status", new GridRect(0, 0, 8, 6)),
        ("team-quick-actions", new GridRect(8, 0, 4, 4)),
        ("team-progress", new GridRect(8, 4, 4, 6)),
    ];

    private static IReadOnlyList<(string Key, GridRect Rect)> DesktopDefaults(string dashboardKey) => dashboardKey switch
    {
        DashboardKeys.MyWork => MyWorkDefaults,
        DashboardKeys.MyTeam => MyTeamDefaults,
        _ => [],
    };

    public static IReadOnlyList<(string Key, GridRect Rect)> DefaultLayout(string dashboardKey, string breakpoint) =>
        Derive(DesktopDefaults(dashboardKey), breakpoint);

    /// <summary>Default W x H for a newly added widget: its default-layout desktop rect, else its minimum size.</summary>
    public static (int W, int H) DefaultSize(string key)
    {
        var def = Get(key) ?? throw new ArgumentException($"Unknown widget '{key}'.", nameof(key));
        foreach (var (k, rect) in DesktopDefaults(def.DashboardKey))
        {
            if (k == key)
            {
                return (rect.W, rect.H);
            }
        }

        return (def.MinW, def.MinH);
    }

    /// <summary>Derives a tablet/mobile layout from a desktop layout. Desktop is returned unchanged.</summary>
    public static IReadOnlyList<(string Key, GridRect Rect)> Derive(
        IReadOnlyList<(string Key, GridRect Rect)> desktop, string breakpoint)
    {
        if (breakpoint == Breakpoints.Desktop)
        {
            return desktop;
        }

        var columns = Breakpoints.Columns(breakpoint);
        var ordered = desktop.OrderBy(i => i.Rect.Y).ThenBy(i => i.Rect.X).ToList();
        var result = new List<(string Key, GridRect Rect)>();

        if (breakpoint == Breakpoints.Mobile)
        {
            var y = 0;
            foreach (var (key, rect) in ordered)
            {
                result.Add((key, new GridRect(0, y, columns, rect.H)));
                y += rect.H;
            }

            return result;
        }

        foreach (var (key, rect) in ordered)
        {
            var minW = Get(key) is { } def ? Math.Min(def.MinW, columns) : 1;
            var w = Math.Max(rect.W >= 6 ? 6 : 3, minW);
            result.Add((key, DashboardGrid.AutoPlace(result.Select(r => r.Rect), w, rect.H, columns)));
        }

        return result;
    }
}
