using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Dashboard.Layout.Access;
using ONEVO.Application.Features.Dashboard.Layout.Data;
using ONEVO.Application.Features.Dashboard.Layout.DTOs;
using ONEVO.Application.Features.Dashboard.Layout.Registry;
using ONEVO.Application.Features.Dashboard.Layout.RepositoryInterfaces;

namespace ONEVO.Application.Features.Dashboard.Layout.Queries.GetWidgetData;

/// <summary>
/// instance -> dashboard -> owner (404) -> widget allowed (403) -> provider (404 NO_PROVIDER). A provider
/// failure keeps the status the mirrored endpoint would have returned (504 on timeout, 500 on exception).
/// </summary>
public sealed class GetWidgetDataQueryHandler(
    IUserDashboardRepository dashboards,
    IDashboardWidgetAccess access,
    IWidgetDataOrchestrator orchestrator,
    ICurrentUser currentUser)
    : IRequestHandler<GetWidgetDataQuery, Result<WidgetDataResponseDto>>
{
    public async Task<Result<WidgetDataResponseDto>> Handle(GetWidgetDataQuery query, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result<WidgetDataResponseDto>.Forbidden();

        var dashboard = await dashboards.GetByWidgetIdAsync(currentUser.TenantId, query.InstanceId, ct);
        var widget = dashboard?.Widgets.FirstOrDefault(w => w.Id == query.InstanceId);
        if (dashboard is null || widget is null || dashboard.UserId != currentUser.UserId)
            return Result<WidgetDataResponseDto>.NotFound("Widget not found.");

        // A retired/unknown key has no definition, so no provider.
        if (DashboardWidgetRegistry.Get(widget.WidgetKey) is null)
            return Result<WidgetDataResponseDto>.NotFound(WidgetDataErrors.NoProvider);

        var allowed = await access.GetAllowedKeysAsync(dashboard.DashboardKey, ct);
        if (!allowed.Contains(widget.WidgetKey))
            return Result<WidgetDataResponseDto>.Forbidden();

        var outcome = await orchestrator.LoadOneAsync(widget.WidgetKey, ct);
        return outcome.State.Status switch
        {
            WidgetDataStatus.Success => Result<WidgetDataResponseDto>.Success(new WidgetDataResponseDto(
                widget.Id, outcome.State.Status, outcome.State.FetchedAt!.Value, outcome.Data)),
            WidgetDataStatus.Client => Result<WidgetDataResponseDto>.NotFound(WidgetDataErrors.NoProvider),
            _ => Result<WidgetDataResponseDto>.Failure(
                outcome.State.Error ?? WidgetDataErrors.Failed, outcome.FailureStatusCode ?? 500),
        };
    }
}
