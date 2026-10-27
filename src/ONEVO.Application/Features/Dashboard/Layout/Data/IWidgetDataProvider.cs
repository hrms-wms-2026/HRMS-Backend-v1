using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Dashboard.Layout.Data;

/// <summary>
/// Loads one widget's data by mirroring the existing endpoint its card calls today: same gates, same
/// MediatR query and parameters, same view-model mapping. Registered as a keyed scoped service whose
/// key equals <see cref="WidgetKey"/>; each call runs in its own DI scope (see WidgetDataOrchestrator).
/// </summary>
public interface IWidgetDataProvider
{
    string WidgetKey { get; }

    Task<Result<object>> GetAsync(CancellationToken ct);
}
