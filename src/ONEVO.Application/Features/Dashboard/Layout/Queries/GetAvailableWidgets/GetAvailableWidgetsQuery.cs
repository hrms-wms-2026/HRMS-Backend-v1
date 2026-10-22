using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Dashboard.Layout.DTOs;

namespace ONEVO.Application.Features.Dashboard.Layout.Queries.GetAvailableWidgets;

/// <summary>Catalog entry the user may add to a dashboard. Sizes are clamped to the breakpoint column count.</summary>
public sealed record AvailableWidgetDto(
    string Key, string Title, string Description, WidgetSizeDto Size, int DefaultW, int DefaultH, bool Placed);

public sealed record GetAvailableWidgetsQuery(Guid DashboardId, string Breakpoint)
    : IRequest<Result<IReadOnlyList<AvailableWidgetDto>>>;
