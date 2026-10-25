namespace ONEVO.Application.Features.Dashboard.Layout.Access;

/// <summary>
/// Resolves which dashboard widgets the current user may see, from My Team capabilities.
/// Scoped: the capabilities query runs at most once per request.
/// </summary>
public interface IDashboardWidgetAccess
{
    Task<IReadOnlySet<string>> GetAllowedKeysAsync(string dashboardKey, CancellationToken ct);

    /// <summary>True when the capabilities query failed (failed Result or exception); gated widgets are then hidden.</summary>
    Task<bool> CapabilitiesUnavailableAsync(CancellationToken ct);
}
