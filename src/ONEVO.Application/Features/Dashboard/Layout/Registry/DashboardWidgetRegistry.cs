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
}
