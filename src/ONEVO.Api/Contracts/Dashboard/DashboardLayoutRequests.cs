using ONEVO.Application.Features.Dashboard.Layout.Commands.SaveDashboardLayout;
using ONEVO.Application.Features.Dashboard.Layout.Registry;

namespace ONEVO.Api.Contracts.Dashboard;

public sealed record AddDashboardWidgetRequest(string WidgetKey, long ExpectedVersion, GridRect? DesktopRect);

public sealed record SaveDashboardLayoutRequest(long ExpectedVersion, IReadOnlyList<LayoutItemDto> Widgets);

public sealed record ResetDashboardRequest(long ExpectedVersion);
