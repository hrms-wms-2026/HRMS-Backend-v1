using MediatR;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Features.Dashboard.Layout.Registry;
using ONEVO.Application.Features.Dashboard.Team.DTOs;
using ONEVO.Application.Features.Dashboard.Team.Queries;

namespace ONEVO.Application.Features.Dashboard.Layout.Access;

/// <summary>Mirrors the FE my-team-widget-visibility util. Permission truth stays in GetMyTeamCapabilitiesQuery.</summary>
public sealed class DashboardWidgetAccess(
    IMediator mediator,
    ILogger<DashboardWidgetAccess> logger) : IDashboardWidgetAccess
{
    public const string ActionCenter = "action-center";
    public const string TeamQuickActions = "team-quick-actions";
    public const string TeamStatus = "team-status";
    public const string TeamProgress = "team-progress";
    public const string TeamAttendance = "team-attendance";

    private Task<MyTeamCapabilitiesResponse?>? _capabilities;

    public async Task<IReadOnlySet<string>> GetAllowedKeysAsync(string dashboardKey, CancellationToken ct)
    {
        var caps = await GetCapabilitiesAsync(ct);
        var keys = new HashSet<string>(StringComparer.Ordinal);

        switch (dashboardKey)
        {
            case DashboardKeys.MyWork:
                foreach (var def in DashboardWidgetRegistry.ForDashboard(DashboardKeys.MyWork))
                {
                    if (def.Key != ActionCenter) keys.Add(def.Key);
                }
                if (caps is not null
                    && (caps.CanReviewPeopleApprovals || caps.CanReviewExceptions
                        || caps.HasWorkApprovals || caps.LeadsWork))
                {
                    keys.Add(ActionCenter);
                }
                break;
            case DashboardKeys.MyTeam:
                keys.Add(TeamQuickActions);
                if (caps is { IsAvailable: true, CanViewPeopleStatus: true })
                {
                    keys.Add(TeamStatus);
                    keys.Add(TeamAttendance);
                }
                if (caps is { IsAvailable: true, LeadsWork: true }) keys.Add(TeamProgress);
                break;
        }

        return keys;
    }

    public async Task<bool> CapabilitiesUnavailableAsync(CancellationToken ct) =>
        await GetCapabilitiesAsync(ct) is null;

    private Task<MyTeamCapabilitiesResponse?> GetCapabilitiesAsync(CancellationToken ct) =>
        _capabilities ??= LoadAsync(ct);

    private async Task<MyTeamCapabilitiesResponse?> LoadAsync(CancellationToken ct)
    {
        try
        {
            var result = await mediator.Send(new GetMyTeamCapabilitiesQuery(), ct);
            if (result.IsSuccess && result.Value is not null) return result.Value;
            logger.LogWarning("My Team capabilities query failed: {Error}", result.Error);
            return null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "My Team capabilities query threw; gated dashboard widgets hidden.");
            return null;
        }
    }
}
