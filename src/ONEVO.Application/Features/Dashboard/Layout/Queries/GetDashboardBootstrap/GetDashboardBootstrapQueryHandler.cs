using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Dashboard.Layout.Access;
using ONEVO.Application.Features.Dashboard.Layout.Data;
using ONEVO.Application.Features.Dashboard.Layout.DTOs;
using ONEVO.Application.Features.Dashboard.Layout.Grid;
using ONEVO.Application.Features.Dashboard.Layout.Registry;
using ONEVO.Application.Features.Dashboard.Layout.RepositoryInterfaces;
using ONEVO.Application.Features.Dashboard.Layout.Services;
using ONEVO.Domain.Features.Dashboard.Entities;

namespace ONEVO.Application.Features.Dashboard.Layout.Queries.GetDashboardBootstrap;

public sealed class GetDashboardBootstrapQueryHandler(
    IUserDashboardRepository dashboards,
    IDashboardWidgetAccess access,
    ICurrentUser currentUser,
    IWidgetDataOrchestrator widgetData)
    : IRequestHandler<GetDashboardBootstrapQuery, Result<DashboardBootstrapDto>>
{
    public const int RowHeight = 96;
    public const int Gap = 12;
    public const string UnsupportedWidget = "UNSUPPORTED_WIDGET";

    public async Task<Result<DashboardBootstrapDto>> Handle(GetDashboardBootstrapQuery query, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result<DashboardBootstrapDto>.Forbidden();

        if (!Breakpoints.All.Contains(query.Breakpoint))
            return Result<DashboardBootstrapDto>.UnprocessableEntity($"Unknown breakpoint '{query.Breakpoint}'.");

        var dashboard = await dashboards.GetByIdAsync(currentUser.TenantId, query.DashboardId, ct);
        if (dashboard is null || dashboard.UserId != currentUser.UserId)
            return Result<DashboardBootstrapDto>.NotFound("Dashboard not found.");

        var breakpoint = query.Breakpoint;
        var columns = Breakpoints.Columns(breakpoint);
        var allowed = await access.GetAllowedKeysAsync(dashboard.DashboardKey, ct);
        var capabilitiesUnavailable = await access.CapabilitiesUnavailableAsync(ct);

        // Allowed registry widgets, plus retired/unknown keys which render as an error placeholder.
        var visible = DashboardLayoutSupport.VisibleWidgets(dashboard, allowed);

        // An untouched default layout is shown as the viewer's permission variant (spec 6.2) so a hidden gated widget
        // leaves no hole. Display only: the stored full default stays intact so a later-allowed widget gets its slot.
        var variant = VariantRects(dashboard.DashboardKey, visible, allowed, breakpoint);
        var final = variant ?? ResolveRects(visible, breakpoint, columns);

        var changed = false;
        foreach (var widget in variant is null ? visible : [])
        {
            var rect = final[widget.Id];
            var row = widget.Layouts.FirstOrDefault(l => l.Breakpoint == breakpoint);
            if (row is null)
            {
                widget.Layouts.Add(new DashboardWidgetLayout
                {
                    Id = Guid.NewGuid(), TenantId = dashboard.TenantId, WidgetId = widget.Id,
                    Breakpoint = breakpoint, X = rect.X, Y = rect.Y, W = rect.W, H = rect.H,
                });
                changed = true;
            }
            else if (row.X != rect.X || row.Y != rect.Y || row.W != rect.W || row.H != rect.H)
            {
                row.X = rect.X; row.Y = rect.Y; row.W = rect.W; row.H = rect.H;
                changed = true;
            }
        }

        var version = dashboard.LayoutVersion;
        if (changed)
        {
            var expected = dashboard.LayoutVersion;
            // On a lost race the next load retries; keep the computed layout with the old version.
            if (await dashboards.SaveWithVersionAsync(dashboard, expected, ct))
                version = dashboard.LayoutVersion;
        }

        // Bounded, concurrent provider loads; a failing provider becomes that widget's error state.
        var data = await widgetData.LoadAsync(
            visible.Where(w => DashboardWidgetRegistry.Get(w.WidgetKey) is not null).Select(w => w.WidgetKey).ToList(), ct);

        var widgets = visible
            .Select(w => ToDto(w, final[w.Id], columns, data))
            .OrderBy(w => w.Layout.Y).ThenBy(w => w.Layout.X)
            .ToList();

        var hasGated = dashboard.DashboardKey != DashboardKeys.MyTeam
            || allowed.Contains(DashboardWidgetAccess.TeamStatus)
            || allowed.Contains(DashboardWidgetAccess.TeamProgress);

        var title = DashboardKeys.Title(dashboard.DashboardKey);
        return Result<DashboardBootstrapDto>.Success(new DashboardBootstrapDto(
            dashboard.Id, dashboard.DashboardKey, title, version,
            new GridSpecDto(breakpoint, columns, RowHeight, Gap),
            widgets, hasGated, capabilitiesUnavailable));
    }

    /// <summary>
    /// The permission-variant rects for the visible widgets when the allowed set selects a variant and every visible
    /// widget's stored rect for this breakpoint still equals the full default (the user never customized it); else null.
    /// </summary>
    private static Dictionary<Guid, GridRect>? VariantRects(
        string dashboardKey, List<DashboardWidget> visible, IReadOnlySet<string> allowed, string breakpoint)
    {
        if (visible.Count == 0 || !DashboardWidgetRegistry.HasVariant(dashboardKey, allowed))
            return null;

        var full = DashboardWidgetRegistry.DefaultLayout(dashboardKey, breakpoint).ToDictionary(i => i.Key, i => i.Rect);
        foreach (var widget in visible)
        {
            if (!full.TryGetValue(widget.WidgetKey, out var defaultRect)
                || DashboardLayoutSupport.Rect(widget, breakpoint) != defaultRect)
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

    /// <summary>Keeps valid non-overlapping stored rects, auto-places the rest, then gravity-compacts.</summary>
    private static Dictionary<Guid, GridRect> ResolveRects(List<DashboardWidget> visible, string breakpoint, int columns)
    {
        var stored = visible.ToDictionary(
            w => w.Id, w => w.Layouts.FirstOrDefault(l => l.Breakpoint == breakpoint));

        var accepted = new List<(Guid Id, GridRect Rect)>();
        var pending = new List<DashboardWidget>();

        var candidates = visible
            .Where(w => stored[w.Id] is { } l && InBounds(l, columns))
            .OrderBy(w => stored[w.Id]!.Y).ThenBy(w => stored[w.Id]!.X);
        foreach (var w in candidates)
        {
            var l = stored[w.Id]!;
            var rect = new GridRect(l.X, l.Y, l.W, l.H);
            if (accepted.Any(a => DashboardGrid.Overlaps(a.Rect, rect)))
                pending.Add(w);
            else
                accepted.Add((w.Id, rect));
        }

        // Widgets with no (or unusable) layout row for this breakpoint are placed after the stored ones.
        pending.AddRange(visible.Where(w => stored[w.Id] is not { } l || !InBounds(l, columns)));

        foreach (var w in pending)
        {
            var (pw, ph) = PlacementSize(w, stored[w.Id], breakpoint, columns);
            accepted.Add((w.Id, DashboardGrid.AutoPlace(accepted.Select(a => a.Rect), pw, ph, columns)));
        }

        var compacted = DashboardGrid.Compact(accepted.Select(a => (a.Id.ToString(), a.Rect)).ToList());
        return compacted.ToDictionary(c => Guid.Parse(c.Key), c => c.Rect);
    }

    private static bool InBounds(DashboardWidgetLayout l, int columns) =>
        l.X >= 0 && l.Y >= 0 && l.W >= 1 && l.H >= 1 && l.X < columns && l.W <= columns - l.X && l.H <= DashboardGrid.MaxRows - l.Y;

    private static (int W, int H) PlacementSize(DashboardWidget widget, DashboardWidgetLayout? row, string breakpoint, int columns)
    {
        if (row is { W: >= 1, H: >= 1 } && row.H <= DashboardGrid.MaxRows)
            return (Math.Min(row.W, columns), row.H);

        var def = DashboardWidgetRegistry.Get(widget.WidgetKey);
        if (def is null)
            return (Math.Min(3, columns), 3);

        var (dw, dh) = DashboardWidgetRegistry.DefaultSize(widget.WidgetKey);
        var w = breakpoint switch
        {
            Breakpoints.Mobile => columns,
            Breakpoints.Tablet => DashboardWidgetRegistry.TabletWidth(def, dw),
            _ => dw,
        };
        return (Math.Min(w, columns), dh);
    }

    private static BootstrapWidgetDto ToDto(
        DashboardWidget widget, GridRect rect, int columns,
        IReadOnlyDictionary<string, (WidgetDataStateDto State, object? Data)> data)
    {
        var def = DashboardWidgetRegistry.Get(widget.WidgetKey);
        if (def is null)
        {
            return new BootstrapWidgetDto(
                widget.Id, widget.WidgetKey, widget.WidgetKey, rect,
                new WidgetSizeDto(1, 1, columns, DashboardGrid.MaxRows),
                new WidgetDataStateDto(WidgetDataStatus.Error, null, UnsupportedWidget), null);
        }

        var loaded = data.TryGetValue(widget.WidgetKey, out var value)
            ? value
            : (State: new WidgetDataStateDto(WidgetDataStatus.NotLoaded, null, null), Data: (object?)null);
        return new BootstrapWidgetDto(
            widget.Id, widget.WidgetKey, def.Title, rect,
            new WidgetSizeDto(Math.Min(def.MinW, columns), def.MinH, Math.Min(def.MaxW, columns), def.MaxH),
            loaded.State, loaded.Data);
    }
}
