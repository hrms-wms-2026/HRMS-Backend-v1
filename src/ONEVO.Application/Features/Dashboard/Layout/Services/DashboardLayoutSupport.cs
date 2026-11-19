using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Dashboard.Layout.Grid;
using ONEVO.Application.Features.Dashboard.Layout.Registry;
using ONEVO.Application.Features.Dashboard.Layout.RepositoryInterfaces;
using ONEVO.Domain.Features.Dashboard.Entities;

namespace ONEVO.Application.Features.Dashboard.Layout.Services;

/// <summary>Helpers shared by the dashboard read and mutation handlers.</summary>
public static class DashboardLayoutSupport
{
    public const string VersionConflictCode = "LAYOUT_VERSION_CONFLICT";
    public const string LayoutSetMismatch = "LAYOUT_SET_MISMATCH";
    public const string WidgetAlreadyPlaced = "WIDGET_ALREADY_PLACED";

    /// <summary>Allowed registry widgets, plus retired/unknown keys (rendered as an error placeholder).</summary>
    public static bool IsVisible(DashboardWidget widget, IReadOnlySet<string> allowed) =>
        allowed.Contains(widget.WidgetKey) || DashboardWidgetRegistry.Get(widget.WidgetKey) is null;

    public static List<DashboardWidget> VisibleWidgets(UserDashboard dashboard, IReadOnlySet<string> allowed) =>
        dashboard.Widgets.Where(w => IsVisible(w, allowed)).ToList();

    public static string ConflictMessage(long currentVersion) => $"{VersionConflictCode} currentVersion={currentVersion}";

    public static Result<T> Conflict<T>(long currentVersion) => Result<T>.Conflict(ConflictMessage(currentVersion));

    /// <summary>Conflict after losing a write race: reloads the stored version to surface it.</summary>
    public static async Task<Result<T>> ConflictAfterRaceAsync<T>(
        IUserDashboardRepository dashboards, Guid tenantId, Guid dashboardId, long expectedVersion, CancellationToken ct)
    {
        var current = await dashboards.GetByIdAsync(tenantId, dashboardId, ct);
        return Conflict<T>(current?.LayoutVersion ?? expectedVersion + 1);
    }

    public static DashboardWidgetLayout? LayoutRow(DashboardWidget widget, string breakpoint) =>
        widget.Layouts.FirstOrDefault(l => l.Breakpoint == breakpoint);

    public static GridRect? Rect(DashboardWidget widget, string breakpoint) =>
        LayoutRow(widget, breakpoint) is { } l ? new GridRect(l.X, l.Y, l.W, l.H) : null;

    /// <summary>Overwrites the breakpoint's layout row, adding the row when it is missing.</summary>
    public static void SetRect(DashboardWidget widget, string breakpoint, GridRect rect, Guid tenantId)
    {
        var row = LayoutRow(widget, breakpoint);
        if (row is null)
        {
            widget.Layouts.Add(new DashboardWidgetLayout
            {
                Id = Guid.NewGuid(), TenantId = tenantId, WidgetId = widget.Id,
                Breakpoint = breakpoint, X = rect.X, Y = rect.Y, W = rect.W, H = rect.H,
            });
            return;
        }

        row.X = rect.X; row.Y = rect.Y; row.W = rect.W; row.H = rect.H;
    }

    /// <summary>
    /// The permission-variant rects for the visible widgets when the allowed set selects a variant and every visible
    /// widget's stored rect for this breakpoint still equals the full default (the user never customized it); else null.
    /// </summary>
    public static Dictionary<Guid, GridRect>? VariantRects(
        string dashboardKey, List<DashboardWidget> visible, IReadOnlySet<string> allowed, string breakpoint)
    {
        if (visible.Count == 0 || !DashboardWidgetRegistry.HasVariant(dashboardKey, allowed))
            return null;

        var full = DashboardWidgetRegistry.DefaultLayout(dashboardKey, breakpoint).ToDictionary(i => i.Key, i => i.Rect);
        foreach (var widget in visible)
        {
            if (!full.TryGetValue(widget.WidgetKey, out var defaultRect) || Rect(widget, breakpoint) != defaultRect)
                return null;
        }

        var variant = DashboardWidgetRegistry.DefaultLayoutFor(dashboardKey, allowed, breakpoint)
            .ToDictionary(i => i.Key, i => i.Rect);
        if (visible.Any(w => !variant.ContainsKey(w.WidgetKey)))
            return null;

        // Compact in case the user removed a default widget without moving the others.
        var compacted = DashboardGrid.Compact(visible.Select(w => (w.Id.ToString(), variant[w.WidgetKey])).ToList());
        return compacted.ToDictionary(c => Guid.Parse(c.Key), c => c.Rect);
    }

    /// <summary>
    /// Persists the permission variant on every untouched breakpoint (visible widgets only) so a following Add/Remove
    /// builds on the no-gap layout instead of the stored full default. Breakpoints the user customized are left alone.
    /// </summary>
    public static void MaterializeVariants(UserDashboard dashboard, List<DashboardWidget> visible, IReadOnlySet<string> allowed)
    {
        foreach (var breakpoint in Breakpoints.All)
        {
            var variant = VariantRects(dashboard.DashboardKey, visible, allowed, breakpoint);
            if (variant is null)
                continue;

            foreach (var widget in visible)
                SetRect(widget, breakpoint, variant[widget.Id], dashboard.TenantId);
        }
    }

    /// <summary>Removes a widget and its layout rows explicitly (children are not cascade-soft-deleted).</summary>
    public static void RemoveWidget(UserDashboard dashboard, DashboardWidget widget)
    {
        widget.Layouts.Clear();
        dashboard.Widgets.Remove(widget);
    }

    /// <summary>Gravity-compacts the given visible widgets per breakpoint; only their rows are changed.</summary>
    public static void CompactVisible(IReadOnlyCollection<DashboardWidget> visible)
    {
        foreach (var breakpoint in Breakpoints.All)
        {
            var items = visible
                .Select(w => (Widget: w, Rect: Rect(w, breakpoint)))
                .Where(i => i.Rect is not null)
                .ToList();
            var compacted = DashboardGrid.Compact(items.Select(i => (i.Widget.Id.ToString(), i.Rect!)).ToList());
            var byId = items.ToDictionary(i => i.Widget.Id.ToString(), i => i.Widget);
            foreach (var (key, rect) in compacted)
            {
                var row = LayoutRow(byId[key], breakpoint)!;
                if (row.Y != rect.Y)
                {
                    row.Y = rect.Y;
                }
            }
        }
    }
}
