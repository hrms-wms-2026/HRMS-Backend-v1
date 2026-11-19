using ONEVO.Application.Features.Dashboard.Layout.Registry;

namespace ONEVO.Application.Features.Dashboard.Layout.DTOs;

public sealed record GridSpecDto(string Breakpoint, int Columns, int RowHeight, int Gap);

/// <summary>Widget size limits, already clamped to the breakpoint column count.</summary>
public sealed record WidgetSizeDto(int MinW, int MinH, int MaxW, int MaxH);

/// <summary>Status is one of success | error | not_loaded | client.</summary>
public sealed record WidgetDataStateDto(string Status, DateTimeOffset? FetchedAt, string? Error);

public sealed record BootstrapWidgetDto(
    Guid InstanceId,
    string WidgetKey,
    string Title,
    GridRect Layout,
    WidgetSizeDto Size,
    WidgetDataStateDto DataState,
    object? Data);

/// <summary>Refresh endpoint body; Payload is the mirrored endpoint's response body (same view model).</summary>
public sealed record WidgetDataResponseDto(Guid InstanceId, string Status, DateTimeOffset FetchedAt, object? Payload);

public sealed record DashboardBootstrapDto(
    Guid Id,
    string Key,
    string Title,
    long LayoutVersion,
    GridSpecDto Grid,
    IReadOnlyList<BootstrapWidgetDto> Widgets,
    bool HasAllowedGatedWidgets,
    bool CapabilitiesUnavailable);
