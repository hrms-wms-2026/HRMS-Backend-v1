using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.ServiceInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Infrastructure.Services.Calendar;

public sealed class CalendarSyncService(
    IExternalCalendarConnectionRepository connections,
    IExternalCalendarEventLinkRepository links,
    ICalendarEventRepository events,
    ICalendarOAuthTokenExchangeClient tokenExchangeClient,
    IGoogleCalendarClient googleClient,
    IMicrosoftGraphCalendarClient msClient,
    IPlatformOAuthAppResolver appResolver,
    IEncryptionService encryption,
    IUnitOfWork unitOfWork,
    ILogger<CalendarSyncService> logger)
    : ICalendarSyncService
{
    private static readonly TimeSpan SyncWindowPast = TimeSpan.FromDays(30);
    private static readonly TimeSpan SyncWindowFuture = TimeSpan.FromDays(180);
    private const int BatchLimitPerConnection = 200;
    private const int MaxConsecutiveFailures = 3;

    // This method deliberately does NOT wrap its writes in unitOfWork.ExecuteInTransactionAsync
    // the way every CQRS command handler does - it runs one connection's whole pull+push cycle as
    // a single unit of work bounded by the single SaveChangesAsync call at the end of this method,
    // which is the correct granularity here (a background job syncing one connection, not a
    // user-facing request/response). This is a deliberate deviation from the Global Constraint for
    // CQRS command handlers, not an oversight.
    public async Task SyncConnectionAsync(Guid tenantId, Guid connectionId, CancellationToken ct)
    {
        var connection = await connections.GetTrackedByIdForTenantAsync(tenantId, connectionId, ct);
        if (connection is null || connection.SyncDirection == CalendarSyncDirections.Disabled)
            return;

        var oauthProvider = connection.Provider == CalendarExternalSources.GoogleCalendar ? "google" : "microsoft";

        var accessToken = await EnsureFreshAccessTokenAsync(connection, oauthProvider, ct);
        if (accessToken is null)
            return; // refresh failed - EnsureFreshAccessTokenAsync already marked ReauthRequired and saved.

        try
        {
            if (connection.SyncDirection is CalendarSyncDirections.PullOnly or CalendarSyncDirections.TwoWay)
                await PullAsync(connection, accessToken, ct);

            // PushAsync returns the watermark to advance LastSuccessfulSyncAt to: DateTimeOffset.UtcNow
            // when every pending manual event was pushed this run, or the timestamp of the last event
            // actually pushed when the batch was truncated by BatchLimitPerConnection - advancing to
            // UtcNow unconditionally in that case would skip the untransmitted remainder forever, since
            // this same field is the "since" cursor PushAsync reads on the next run.
            DateTimeOffset? pushWatermark = null;
            if (connection.SyncDirection is CalendarSyncDirections.PushOnly or CalendarSyncDirections.TwoWay)
                pushWatermark = await PushAsync(connection, accessToken, ct);

            connection.LastSyncedAt = DateTimeOffset.UtcNow;
            connection.LastSuccessfulSyncAt = pushWatermark ?? DateTimeOffset.UtcNow;
            connection.FailureCount = 0;
            connection.LastError = null;
            // Recover a connection that was previously Failed (3 consecutive failures) or
            // ReauthRequired (a token refresh that has since started succeeding again) - without
            // this, EfExternalCalendarConnectionRepository.GetActiveAsync's exclusion of Failed
            // connections would permanently strand it outside all future automatic syncs even after
            // the underlying issue (transient provider outage, expired-then-renewed grant) resolves.
            // A no-op when already Active.
            connection.Status = ExternalCalendarConnectionStatuses.Active;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Calendar sync failed for connection {ConnectionId}.", connection.Id);
            connection.FailureCount++;
            connection.LastError = ex.Message;
            connection.LastSyncedAt = DateTimeOffset.UtcNow;
            if (connection.FailureCount >= MaxConsecutiveFailures)
                connection.Status = ExternalCalendarConnectionStatuses.Failed;
        }

        connections.Update(connection);
        await unitOfWork.SaveChangesAsync(ct);
    }

    private async Task<string?> EnsureFreshAccessTokenAsync(ExternalCalendarConnection connection, string oauthProvider, CancellationToken ct)
    {
        var needsRefresh = connection.ExpiresAt is null || connection.ExpiresAt <= DateTimeOffset.UtcNow.AddMinutes(5);
        if (!needsRefresh && connection.AccessTokenEncrypted is not null)
            return encryption.DecryptBytes(connection.AccessTokenEncrypted);

        try
        {
            var app = await appResolver.GetActiveAppForProviderAsync(oauthProvider, ct);
            var credential = await appResolver.GetActiveCredentialForProviderAsync(oauthProvider, ct);
            if (app is null || credential is null)
                throw new InvalidOperationException($"No active OAuth app configured for provider '{oauthProvider}'.");

            var refreshToken = encryption.DecryptBytes(connection.RefreshTokenEncrypted);
            var tokens = await tokenExchangeClient.RefreshTokenAsync(app.TokenUrl, credential.ClientId, credential.ClientSecret, refreshToken, ct);

            connection.AccessTokenEncrypted = encryption.EncryptBytes(tokens.AccessToken);
            connection.RefreshTokenEncrypted = encryption.EncryptBytes(tokens.RefreshToken ?? refreshToken);
            connection.ExpiresAt = tokens.ExpiresAt;
            return tokens.AccessToken;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Token refresh failed for connection {ConnectionId}; marking reauth_required.", connection.Id);
            connection.Status = ExternalCalendarConnectionStatuses.ReauthRequired;
            connection.LastError = ex.Message;
            connections.Update(connection);
            await unitOfWork.SaveChangesAsync(ct);
            return null;
        }
    }

    private async Task PullAsync(ExternalCalendarConnection connection, string accessToken, CancellationToken ct)
    {
        var calendarId = connection.ExternalCalendarId ?? connection.ExternalAccountEmail;
        var windowStart = DateTimeOffset.UtcNow.Subtract(SyncWindowPast);
        var windowEnd = DateTimeOffset.UtcNow.Add(SyncWindowFuture);
        var syncToken = connection.SyncTokenEncrypted is not null ? encryption.DecryptBytes(connection.SyncTokenEncrypted) : null;

        if (connection.Provider == CalendarExternalSources.GoogleCalendar)
        {
            var page = await googleClient.ListEventsAsync(accessToken, calendarId, syncToken, windowStart, windowEnd, ct);
            // Ingest every event the client returned. The client itself already bounds this to its
            // own MaxPages cap (see GoogleCalendarClient.ListEventsAsync), so there is still an
            // upper bound on work-per-run - just one described by the client's paging cap, not by
            // BatchLimitPerConnection (which is a separate, smaller, push-side batch size and has no
            // business truncating what gets ingested here).
            foreach (var item in page.Events)
                await UpsertPulledEventAsync(connection, item.Id, item.Etag, item.Title, item.Description, item.Start, item.End, item.IsAllDay, item.Timezone, item.Location, item.IsCancelled, item.IsPrivate, ct);
            // Advance the stored sync token exactly when the client says it reached the provider's
            // true final page. The client returns a null token when its own MaxPages cap was hit
            // before the real final page was found (see GoogleCalendarClient.ListEventsAsync) - that
            // is the single source of truth for "was every event in this window actually fetched",
            // now that every event returned above was also applied above. Withholding the token in
            // that case means the NEXT run re-queries from the same starting point and makes
            // progress across multiple runs instead of losing data.
            if (page.NextSyncToken is not null)
                connection.SyncTokenEncrypted = encryption.EncryptBytes(page.NextSyncToken);
        }
        else
        {
            var deltaLink = connection.DeltaLinkEncrypted is not null ? encryption.DecryptBytes(connection.DeltaLinkEncrypted) : null;
            var page = await msClient.ListEventsAsync(accessToken, deltaLink, windowStart, windowEnd, ct);
            // Same reasoning as the Google branch above: ingest everything the client returned
            // (already bounded by the client's own MaxPages cap) and trust the client's own
            // null-when-capped signal for whether the delta link is safe to advance.
            foreach (var item in page.Events)
                await UpsertPulledEventAsync(connection, item.Id, item.Etag, item.Title, item.Description, item.Start, item.End, item.IsAllDay, item.Timezone, item.Location, item.IsCancelled, item.IsPrivate, ct);
            if (page.NextDeltaLink is not null)
                connection.DeltaLinkEncrypted = encryption.EncryptBytes(page.NextDeltaLink);
        }
    }

    private async Task UpsertPulledEventAsync(
        ExternalCalendarConnection connection, string externalEventId, string? etag, string title, string? description,
        DateTimeOffset start, DateTimeOffset end, bool isAllDay, string? timezone, string? location, bool isCancelled, bool isPrivate, CancellationToken ct)
    {
        var link = await links.GetTrackedByConnectionAndExternalEventAsync(connection.TenantId, connection.Id, externalEventId, ct);

        if (isCancelled)
        {
            if (link is not null)
            {
                var existingEvent = await events.GetTrackedByIdForTenantAsync(connection.TenantId, link.CalendarEventId, ct);
                if (existingEvent is not null)
                    events.Remove(existingEvent);
                links.Remove(link);
            }
            return;
        }

        var displayTitle = isPrivate ? "Busy" : title;
        var displayDescription = isPrivate ? null : description;
        var displayLocation = isPrivate ? null : location;

        if (link is null)
        {
            var calendarEvent = new CalendarEvent
            {
                Id = Guid.NewGuid(), TenantId = connection.TenantId, Title = displayTitle, Description = displayDescription,
                StartDate = start, EndDate = end, SourceType = CalendarEventSourceTypes.ExternalSync,
                ExternalId = externalEventId, ExternalSource = connection.Provider, IsAllDay = isAllDay,
                Timezone = timezone, IsPrivate = isPrivate, Location = displayLocation,
                CreatedById = connection.UserId, CreatedAt = DateTimeOffset.UtcNow, ExternalUpdatedAt = DateTimeOffset.UtcNow
            };
            await events.AddAsync(calendarEvent, ct);
            await links.AddAsync(new ExternalCalendarEventLink
            {
                Id = Guid.NewGuid(), TenantId = connection.TenantId, CalendarEventId = calendarEvent.Id,
                ExternalCalendarConnectionId = connection.Id, Provider = connection.Provider,
                ExternalCalendarId = connection.ExternalCalendarId ?? connection.ExternalAccountEmail, ExternalEventId = externalEventId,
                ExternalEtag = etag, SyncDirection = ExternalCalendarLinkDirections.Inbound,
                SyncStatus = ExternalCalendarSyncStatuses.Synced, LastSyncedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow
            }, ct);
            return;
        }

        var localEvent = await events.GetTrackedByIdForTenantAsync(connection.TenantId, link.CalendarEventId, ct);
        if (localEvent is null)
            return;

        // Two-way conflict check: both sides changed since the last successful sync -> pull wins,
        // flag the link as conflict for later admin visibility (no resolution UI in this pass).
        // Deliberately compares against LastSuccessfulSyncAt, not LastSyncedAt: LastSyncedAt
        // advances on every attempt (success or failure), so after a failed run it could sit later
        // than a local edit that hasn't actually been accounted for by any successful sync yet,
        // letting that edit silently escape conflict detection.
        var bothSidesChanged = connection.SyncDirection == CalendarSyncDirections.TwoWay
            && link.ExternalEtag != etag
            && connection.LastSuccessfulSyncAt is not null
            && localEvent.UpdatedAt is not null && localEvent.UpdatedAt > connection.LastSuccessfulSyncAt;

        localEvent.Title = displayTitle;
        localEvent.Description = displayDescription;
        localEvent.StartDate = start;
        localEvent.EndDate = end;
        localEvent.IsAllDay = isAllDay;
        localEvent.Timezone = timezone;
        localEvent.Location = displayLocation;
        localEvent.IsPrivate = isPrivate;
        localEvent.ExternalUpdatedAt = DateTimeOffset.UtcNow;
        localEvent.UpdatedAt = DateTimeOffset.UtcNow;
        events.Update(localEvent);

        link.ExternalEtag = etag;
        link.LastSyncedAt = DateTimeOffset.UtcNow;
        link.SyncStatus = bothSidesChanged ? ExternalCalendarSyncStatuses.Conflict : ExternalCalendarSyncStatuses.Synced;
        if (bothSidesChanged)
            link.LastError = "Both the local event and the external event changed since the last sync; the external version was kept.";
        links.Update(link);
    }

    private async Task<DateTimeOffset> PushAsync(ExternalCalendarConnection connection, string accessToken, CancellationToken ct)
    {
        // Watermark deliberately reads LastSuccessfulSyncAt, not LastSyncedAt: LastSyncedAt
        // advances even on a failed run (see the catch block above), which would silently move the
        // "since" cutoff past local edits made during that failed run's window, permanently
        // skipping them on every future push. LastSyncedAt itself is left alone elsewhere - it
        // still means "last time we attempted a sync" for observability - only its use as a
        // watermark here (and in the conflict check above) is replaced.
        var since = connection.LastSuccessfulSyncAt ?? DateTimeOffset.UtcNow.Subtract(SyncWindowPast);
        // Ordered ascending by the repository (oldest-changed-first), so if more than
        // BatchLimitPerConnection events are pending, the batch we actually push is a contiguous
        // prefix and the last item's own UpdatedAt/CreatedAt is a safe cursor for the next run.
        var candidates = await events.GetManualEventsUpdatedSinceForUserAsync(connection.TenantId, connection.UserId, since, ct);
        var calendarId = connection.ExternalCalendarId ?? connection.ExternalAccountEmail;

        var batch = candidates.Take(BatchLimitPerConnection).ToList();
        var wasTruncated = candidates.Count > BatchLimitPerConnection;

        foreach (var localEvent in batch)
        {
            var existingLink = await links.GetTrackedByCalendarEventAndConnectionAsync(connection.TenantId, localEvent.Id, connection.Id, ct);

            if (connection.Provider == CalendarExternalSources.GoogleCalendar)
            {
                var dto = new GoogleCalendarEventDto(existingLink?.ExternalEventId ?? string.Empty, existingLink?.ExternalEtag, localEvent.Title,
                    localEvent.Description, localEvent.StartDate, localEvent.EndDate, localEvent.IsAllDay, localEvent.Timezone, localEvent.Location, false, localEvent.IsPrivate);
                var pushed = existingLink is null
                    ? await googleClient.InsertEventAsync(accessToken, calendarId, dto, ct)
                    : await googleClient.PatchEventAsync(accessToken, calendarId, existingLink.ExternalEventId, dto, ct);
                await UpsertOutboundLinkAsync(connection, localEvent.Id, existingLink, pushed.Id, pushed.Etag, calendarId, ct);
            }
            else
            {
                var dto = new GraphEventDto(existingLink?.ExternalEventId ?? string.Empty, existingLink?.ExternalEtag, localEvent.Title,
                    localEvent.Description, localEvent.StartDate, localEvent.EndDate, localEvent.IsAllDay, localEvent.Timezone, localEvent.Location, false, localEvent.IsPrivate);
                var pushed = existingLink is null
                    ? await msClient.CreateEventAsync(accessToken, dto, ct)
                    : await msClient.UpdateEventAsync(accessToken, existingLink.ExternalEventId, dto, ct);
                await UpsertOutboundLinkAsync(connection, localEvent.Id, existingLink, pushed.Id, pushed.Etag, calendarId, ct);
            }
        }

        // Only advance past what we actually pushed. If nothing was truncated, every pending event
        // (including zero pending) got pushed, so UtcNow is safe. If truncated, advancing only to
        // the last pushed event's own timestamp means the next run's "since" query picks up exactly
        // the remainder, instead of skipping it forever.
        if (!wasTruncated || batch.Count == 0)
            return DateTimeOffset.UtcNow;

        var lastPushed = batch[^1];
        // .ToUniversalTime() is required here, not just defensive: this value gets written to
        // LastSuccessfulSyncAt, a `timestamp with time zone` column - Npgsql refuses to write any
        // DateTimeOffset whose Offset isn't exactly zero. CalendarEvent.UpdatedAt/CreatedAt are not
        // guaranteed UTC at the source (confirmed: some existing rows carry a +05:30 offset), so this
        // watermark must be normalized before the caller persists it, even though it represents the
        // same instant either way.
        return (lastPushed.UpdatedAt ?? lastPushed.CreatedAt).ToUniversalTime();
    }

    private async Task UpsertOutboundLinkAsync(ExternalCalendarConnection connection, Guid calendarEventId, ExternalCalendarEventLink? existingLink, string externalEventId, string? etag, string calendarId, CancellationToken ct)
    {
        if (existingLink is not null)
        {
            existingLink.ExternalEventId = externalEventId;
            existingLink.ExternalEtag = etag;
            existingLink.LastSyncedAt = DateTimeOffset.UtcNow;
            existingLink.SyncStatus = ExternalCalendarSyncStatuses.Synced;
            links.Update(existingLink);
            return;
        }

        await links.AddAsync(new ExternalCalendarEventLink
        {
            Id = Guid.NewGuid(), TenantId = connection.TenantId, CalendarEventId = calendarEventId,
            ExternalCalendarConnectionId = connection.Id, Provider = connection.Provider, ExternalCalendarId = calendarId,
            ExternalEventId = externalEventId, ExternalEtag = etag, SyncDirection = ExternalCalendarLinkDirections.Outbound,
            SyncStatus = ExternalCalendarSyncStatuses.Synced, LastSyncedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow
        }, ct);
    }
}
