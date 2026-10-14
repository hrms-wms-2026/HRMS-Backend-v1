using Microsoft.Extensions.Logging;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Application.Features.Calendar.Services;

public enum MeetingProvisioningOutcomeKind { Created, Unauthorized, RateLimited, Permanent, Ambiguous, NotConnected }

public sealed record MeetingProvisioningRequest(
    ExternalCalendarConnection Connection, string Provider, Guid EventId, int Generation,
    string Subject, DateTimeOffset Start, DateTimeOffset End, bool Recovery);

public sealed record ProvisionedMeeting(string ExternalMeetingId, string JoinUrl, string? OrganizerJoinUrl, string? PasscodeOrPin);

public sealed record MeetingProvisioningOutcome(MeetingProvisioningOutcomeKind Kind, ProvisionedMeeting? Meeting, string? Reason);

public interface IMeetingProvisioner
{
    /// <summary>Creates (or, in recovery, adopts) the meeting for one link generation and classifies the result.</summary>
    Task<MeetingProvisioningOutcome> ProvisionAsync(MeetingProvisioningRequest request, CancellationToken ct);

    /// <summary>For Remove on provisioning_unknown: deletes the generation's remote meeting if it exists.
    /// True = nothing of it remains remotely; false = can't tell (keep the correlation data, warn the organizer).</summary>
    Task<bool> ResolveForRemovalAsync(MeetingProvisioningRequest request, CancellationToken ct);
}

/// <summary>Spec §3.2.2 provider-call policy: 90 s total budget; 401 → refresh once; 429 → honour
/// Retry-After, 3 attempts; timeout/5xx → ambiguous. Teams is idempotent through createOrGet; Zoom
/// recovery adopts only an exact correlation-token match.</summary>
public sealed class MeetingProvisioner(
    ICalendarConnectionTokenProvider tokenProvider,
    ITeamsMeetingClient teamsClient,
    IZoomMeetingClient zoomClient,
    ILogger<MeetingProvisioner> logger,
    Func<TimeSpan, CancellationToken, Task>? delay = null) : IMeetingProvisioner
{
    public static readonly TimeSpan TotalBudget = TimeSpan.FromSeconds(90);
    public static readonly TimeSpan MaxSingleWait = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan DefaultRateLimitWait = TimeSpan.FromSeconds(5);
    private const int MaxRateLimitedAttempts = 3;

    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? Task.Delay;

    public async Task<MeetingProvisioningOutcome> ProvisionAsync(MeetingProvisioningRequest request, CancellationToken ct)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TotalBudget);
        var isZoom = request.Provider == CalendarEventMeetingProviders.Zoom;
        var oauthProvider = isZoom ? "zoom" : "microsoft";
        try
        {
            var accessToken = await tokenProvider.GetFreshAccessTokenAsync(request.Connection, oauthProvider, budget.Token);
            if (accessToken is null)
                return Failed(MeetingProvisioningOutcomeKind.NotConnected, "not_connected");

            if (isZoom && request.Recovery)
            {
                try
                {
                    var match = await zoomClient.FindMeetingByCorrelationTokenAsync(
                        accessToken, CalendarMeetingCorrelation.ZoomToken(request.EventId, request.Generation), request.Start, budget.Token);
                    if (match is not null)
                        return Created(match.ExternalMeetingId, match.JoinUrl, match.OrganizerJoinUrl, match.PasscodeOrPin);
                }
                catch (MeetingProviderException ex)
                {
                    // Can't tell whether the earlier attempt created one: creating now could duplicate it.
                    logger.LogWarning(ex, "Zoom correlation failed for event {EventId} generation {Generation}.", request.EventId, request.Generation);
                    return Failed(MeetingProvisioningOutcomeKind.Ambiguous, "correlation_unavailable");
                }
            }

            var refreshed = false;
            var rateLimited = 0;
            while (true)
            {
                try
                {
                    if (isZoom)
                    {
                        var zoom = await zoomClient.CreateMeetingAsync(
                            accessToken, request.Subject, request.Start, request.End,
                            CalendarMeetingCorrelation.ZoomToken(request.EventId, request.Generation), budget.Token);
                        return Created(zoom.ExternalMeetingId, zoom.JoinUrl, zoom.OrganizerJoinUrl, zoom.PasscodeOrPin);
                    }

                    var teams = await teamsClient.CreateOrGetMeetingAsync(
                        accessToken, CalendarMeetingCorrelation.TeamsExternalId(request.EventId, request.Generation),
                        request.Subject, request.Start, request.End, budget.Token);
                    return Created(teams.ExternalMeetingId, teams.JoinUrl, teams.OrganizerJoinUrl, teams.PasscodeOrPin);
                }
                catch (MeetingProviderException ex) when (ex.Kind == MeetingProviderFailureKind.Unauthorized && !refreshed)
                {
                    refreshed = true;
                    accessToken = await tokenProvider.ForceRefreshAccessTokenAsync(request.Connection, oauthProvider, budget.Token);
                    if (accessToken is null)
                        return Failed(MeetingProvisioningOutcomeKind.Unauthorized, "auth_failed");
                }
                catch (MeetingProviderException ex) when (ex.Kind == MeetingProviderFailureKind.RateLimited)
                {
                    if (++rateLimited >= MaxRateLimitedAttempts)
                        return Failed(MeetingProvisioningOutcomeKind.RateLimited, "rate_limited");
                    var wait = ex.RetryAfter is { } retryAfter && retryAfter > TimeSpan.Zero
                        ? (retryAfter > MaxSingleWait ? MaxSingleWait : retryAfter)
                        : DefaultRateLimitWait;
                    await _delay(wait, budget.Token);
                }
                catch (MeetingProviderException ex)
                {
                    logger.LogWarning(ex, "{Provider} meeting creation failed ({Kind}) for event {EventId}.", request.Provider, ex.Kind, request.EventId);
                    return ex.Kind switch
                    {
                        MeetingProviderFailureKind.Unauthorized => Failed(MeetingProvisioningOutcomeKind.Unauthorized, "auth_failed"),
                        MeetingProviderFailureKind.Permanent => Failed(MeetingProvisioningOutcomeKind.Permanent, "rejected"),
                        _ => Failed(MeetingProvisioningOutcomeKind.Ambiguous, "timeout_or_server_error")
                    };
                }
            }
        }
        catch (OperationCanceledException) when (budget.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            return Failed(MeetingProvisioningOutcomeKind.Ambiguous, "timeout_or_server_error");
        }
    }

    public async Task<bool> ResolveForRemovalAsync(MeetingProvisioningRequest request, CancellationToken ct)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TotalBudget);
        var isZoom = request.Provider == CalendarEventMeetingProviders.Zoom;
        try
        {
            var accessToken = await tokenProvider.GetFreshAccessTokenAsync(request.Connection, isZoom ? "zoom" : "microsoft", budget.Token);
            if (accessToken is null)
                return false;

            if (isZoom)
            {
                var match = await zoomClient.FindMeetingByCorrelationTokenAsync(
                    accessToken, CalendarMeetingCorrelation.ZoomToken(request.EventId, request.Generation), request.Start, budget.Token);
                if (match is not null)
                    await zoomClient.CancelMeetingAsync(accessToken, match.ExternalMeetingId, budget.Token);
                return true; // deleted, or a complete list read found nothing to delete
            }

            // createOrGet returns the meeting if it exists (or creates it): either way we get its ID to delete.
            // No link was ever sent for this generation, so a create-then-delete is invisible to users.
            var teams = await teamsClient.CreateOrGetMeetingAsync(
                accessToken, CalendarMeetingCorrelation.TeamsExternalId(request.EventId, request.Generation),
                request.Subject, request.Start, request.End, budget.Token);
            await teamsClient.CancelMeetingAsync(accessToken, teams.ExternalMeetingId, budget.Token);
            return true;
        }
        catch (Exception ex) when (ex is MeetingProviderException or ZoomMeetingInProgressException
            || (ex is OperationCanceledException && budget.IsCancellationRequested && !ct.IsCancellationRequested))
        {
            logger.LogWarning(ex, "Couldn't resolve the {Provider} meeting for event {EventId}; leaving it unresolved.", request.Provider, request.EventId);
            return false;
        }
    }

    private static MeetingProvisioningOutcome Created(string externalMeetingId, string joinUrl, string? organizerJoinUrl, string? passcode)
        => new(MeetingProvisioningOutcomeKind.Created, new ProvisionedMeeting(externalMeetingId, joinUrl, organizerJoinUrl, passcode), null);

    private static MeetingProvisioningOutcome Failed(MeetingProvisioningOutcomeKind kind, string reason) => new(kind, null, reason);
}
