using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.ServiceInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Infrastructure.Services.Calendar;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class CalendarSyncServiceTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ConnectionId = Guid.NewGuid();

    private readonly Mock<IExternalCalendarConnectionRepository> _connections = new();
    private readonly Mock<IExternalCalendarEventLinkRepository> _links = new();
    private readonly Mock<ICalendarEventRepository> _events = new();
    private readonly Mock<ICalendarOAuthTokenExchangeClient> _tokenClient = new();
    private readonly Mock<IGoogleCalendarClient> _googleClient = new();
    private readonly Mock<IMicrosoftGraphCalendarClient> _msClient = new();
    private readonly Mock<IPlatformOAuthAppResolver> _appResolver = new();
    private readonly Mock<IEncryptionService> _encryption = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();

    // Note: CalendarSyncService.SyncConnectionAsync deliberately does NOT wrap its writes in
    // unitOfWork.ExecuteInTransactionAsync - it runs one connection's whole pull+push cycle as a
    // single unit of work bounded by the one SaveChangesAsync call at the end of the outer method,
    // which is the correct granularity for a background job syncing one connection (see the
    // service's own comment). So no ExecuteInTransactionAsync mock setup is needed here - a
    // placeholder setup that's never invoked would just be dead code.
    private CalendarSyncService BuildSut()
    {
        _encryption.Setup(x => x.DecryptBytes(It.IsAny<byte[]>())).Returns("decrypted-token");
        _encryption.Setup(x => x.EncryptBytes(It.IsAny<string>())).Returns<string>(s => System.Text.Encoding.UTF8.GetBytes(s));
        return new CalendarSyncService(
            _connections.Object, _links.Object, _events.Object, _tokenClient.Object,
            _googleClient.Object, _msClient.Object, _appResolver.Object, _encryption.Object,
            _unitOfWork.Object, NullLogger<CalendarSyncService>.Instance);
    }

    private static ExternalCalendarConnection MakeConnection(string syncDirection, DateTimeOffset? expiresAt = null) => new()
    {
        Id = ConnectionId, TenantId = TenantId, UserId = Guid.NewGuid(),
        Provider = CalendarExternalSources.GoogleCalendar, ExternalAccountEmail = "me@acme.com",
        ExternalCalendarId = "me@acme.com", AccessTokenEncrypted = [1], RefreshTokenEncrypted = [2],
        SyncDirection = syncDirection, Status = ExternalCalendarConnectionStatuses.Active,
        ExpiresAt = expiresAt ?? DateTimeOffset.UtcNow.AddHours(1)
    };

    [Fact]
    public async Task SyncConnectionAsync_ConnectionNotFound_DoesNothing()
    {
        var sut = BuildSut();
        _connections.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, ConnectionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ExternalCalendarConnection?)null);

        await sut.SyncConnectionAsync(TenantId, ConnectionId, CancellationToken.None);

        _googleClient.Verify(x => x.ListEventsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SyncConnectionAsync_DisabledDirection_SkipsSync()
    {
        var sut = BuildSut();
        var connection = MakeConnection(CalendarSyncDirections.Disabled);
        _connections.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, ConnectionId, It.IsAny<CancellationToken>())).ReturnsAsync(connection);

        await sut.SyncConnectionAsync(TenantId, ConnectionId, CancellationToken.None);

        _googleClient.Verify(x => x.ListEventsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SyncConnectionAsync_PullOnly_UpsertsNewEventAsCalendarEvent()
    {
        var sut = BuildSut();
        var connection = MakeConnection(CalendarSyncDirections.PullOnly);
        _connections.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, ConnectionId, It.IsAny<CancellationToken>())).ReturnsAsync(connection);
        _googleClient.Setup(x => x.ListEventsAsync("decrypted-token", "me@acme.com", null, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GoogleCalendarPage(
                [new GoogleCalendarEventDto("ext-1", "etag-1", "Standup", null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), false, "UTC", null, false, false)],
                "sync-token-1"));
        _links.Setup(x => x.GetTrackedByConnectionAndExternalEventAsync(TenantId, ConnectionId, "ext-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ExternalCalendarEventLink?)null);

        await sut.SyncConnectionAsync(TenantId, ConnectionId, CancellationToken.None);

        _events.Verify(x => x.AddAsync(It.Is<CalendarEvent>(e => e.Title == "Standup" && e.SourceType == CalendarEventSourceTypes.ExternalSync), It.IsAny<CancellationToken>()), Times.Once);
        _links.Verify(x => x.AddAsync(It.Is<ExternalCalendarEventLink>(l => l.ExternalEventId == "ext-1" && l.SyncStatus == ExternalCalendarSyncStatuses.Synced), It.IsAny<CancellationToken>()), Times.Once);
        _connections.Verify(x => x.Update(It.Is<ExternalCalendarConnection>(c => c.FailureCount == 0 && c.LastSyncedAt != null)), Times.Once);
    }

    [Fact]
    public async Task SyncConnectionAsync_PullOnly_PrivateEvent_RedactsTitleAndDescription()
    {
        var sut = BuildSut();
        var connection = MakeConnection(CalendarSyncDirections.PullOnly);
        _connections.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, ConnectionId, It.IsAny<CancellationToken>())).ReturnsAsync(connection);
        _googleClient.Setup(x => x.ListEventsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GoogleCalendarPage(
                [new GoogleCalendarEventDto("ext-2", "etag-2", "Doctor appointment", "sensitive details", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), false, "UTC", "Clinic", false, IsPrivate: true)],
                null));
        _links.Setup(x => x.GetTrackedByConnectionAndExternalEventAsync(TenantId, ConnectionId, "ext-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ExternalCalendarEventLink?)null);

        await sut.SyncConnectionAsync(TenantId, ConnectionId, CancellationToken.None);

        _events.Verify(x => x.AddAsync(It.Is<CalendarEvent>(e =>
            e.Title == "Busy" && e.Description == null && e.Location == null && e.IsPrivate), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SyncConnectionAsync_TokenRefreshFails_SetsReauthRequiredAndSkipsSync()
    {
        var sut = BuildSut();
        var connection = MakeConnection(CalendarSyncDirections.PullOnly, expiresAt: DateTimeOffset.UtcNow.AddMinutes(2));
        _connections.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, ConnectionId, It.IsAny<CancellationToken>())).ReturnsAsync(connection);
        _appResolver.Setup(x => x.GetActiveCredentialForProviderAsync("google", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.ServiceInterfaces.ResolvedPlatformOAuthAppCredential("google", "client", "secret", null, 1));
        _appResolver.Setup(x => x.GetActiveAppForProviderAsync("google", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.ServiceInterfaces.ResolvedPlatformOAuthApp("google", "client", "https://accounts.google.com/o/oauth2/v2/auth", "https://oauth2.googleapis.com/token", ["scope"]));
        _tokenClient.Setup(x => x.RefreshTokenAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("invalid_grant"));

        await sut.SyncConnectionAsync(TenantId, ConnectionId, CancellationToken.None);

        _connections.Verify(x => x.Update(It.Is<ExternalCalendarConnection>(c => c.Status == ExternalCalendarConnectionStatuses.ReauthRequired)), Times.Once);
        _googleClient.Verify(x => x.ListEventsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SyncConnectionAsync_ListEventsThrows_IncrementsFailureCount()
    {
        var sut = BuildSut();
        var connection = MakeConnection(CalendarSyncDirections.PullOnly);
        connection.FailureCount = 2;
        _connections.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, ConnectionId, It.IsAny<CancellationToken>())).ReturnsAsync(connection);
        _googleClient.Setup(x => x.ListEventsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("rate limited"));

        await sut.SyncConnectionAsync(TenantId, ConnectionId, CancellationToken.None);

        _connections.Verify(x => x.Update(It.Is<ExternalCalendarConnection>(c => c.FailureCount == 3 && c.Status == ExternalCalendarConnectionStatuses.Failed)), Times.Once);
    }

    [Theory]
    [InlineData(ExternalCalendarConnectionStatuses.Failed)]
    [InlineData(ExternalCalendarConnectionStatuses.ReauthRequired)]
    public async Task SyncConnectionAsync_SuccessfulRun_RecoversPreviouslyFailedOrReauthRequiredConnectionToActive(string priorStatus)
    {
        var sut = BuildSut();
        var connection = MakeConnection(CalendarSyncDirections.PullOnly);
        connection.Status = priorStatus;
        _connections.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, ConnectionId, It.IsAny<CancellationToken>())).ReturnsAsync(connection);
        _googleClient.Setup(x => x.ListEventsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GoogleCalendarPage([], null));

        await sut.SyncConnectionAsync(TenantId, ConnectionId, CancellationToken.None);

        Assert.Equal(ExternalCalendarConnectionStatuses.Active, connection.Status);
        _connections.Verify(x => x.Update(It.Is<ExternalCalendarConnection>(c => c.Status == ExternalCalendarConnectionStatuses.Active)), Times.Once);
    }

    [Fact]
    public async Task SyncConnectionAsync_PushOnly_UsesLastSuccessfulSyncAt_NotLaterLastSyncedAt_AsWatermark()
    {
        // Simulates "last attempt (LastSyncedAt) more recent than last success (LastSuccessfulSyncAt),
        // because the last attempt failed" - the push watermark must be the last SUCCESS time, not
        // the last attempt time, otherwise a failed run's advanced LastSyncedAt would silently skip
        // local edits made in between.
        var sut = BuildSut();
        var connection = MakeConnection(CalendarSyncDirections.PushOnly);
        var lastSuccess = DateTimeOffset.UtcNow.AddHours(-2);
        var lastAttempt = DateTimeOffset.UtcNow.AddMinutes(-5);
        connection.LastSuccessfulSyncAt = lastSuccess;
        connection.LastSyncedAt = lastAttempt;
        _connections.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, ConnectionId, It.IsAny<CancellationToken>())).ReturnsAsync(connection);
        _events.Setup(x => x.GetManualEventsUpdatedSinceForUserAsync(TenantId, connection.UserId, lastSuccess, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarEvent>());

        await sut.SyncConnectionAsync(TenantId, ConnectionId, CancellationToken.None);

        _events.Verify(x => x.GetManualEventsUpdatedSinceForUserAsync(TenantId, connection.UserId, lastSuccess, It.IsAny<CancellationToken>()), Times.Once);
        _events.Verify(x => x.GetManualEventsUpdatedSinceForUserAsync(TenantId, connection.UserId, lastAttempt, It.IsAny<CancellationToken>()), Times.Never);
    }

    // BatchLimitPerConnection is a private const inside CalendarSyncService, still used by
    // PushAsync (out of scope for this round). PullAsync no longer references it at all - ingestion
    // there is unbounded except by whatever the provider client itself returned (which is bounded
    // by the client's own internal MaxPages cap), so these tests exercise event counts well above
    // this value to prove PullAsync no longer truncates.
    private const int BatchLimitPerConnection = 200;

    private static List<CalendarEvent> MakeManualEvents(int count, bool isPrivate = false)
    {
        var baseTime = DateTimeOffset.UtcNow.AddDays(-1);
        return Enumerable.Range(0, count)
            .Select(i => new CalendarEvent
            {
                Id = Guid.NewGuid(), TenantId = TenantId, Title = $"Manual {i}",
                StartDate = DateTimeOffset.UtcNow, EndDate = DateTimeOffset.UtcNow.AddHours(1),
                SourceType = CalendarEventSourceTypes.Manual, IsPrivate = isPrivate,
                CreatedAt = baseTime.AddMinutes(i), UpdatedAt = baseTime.AddMinutes(i)
            })
            .ToList();
    }

    [Fact]
    public async Task SyncConnectionAsync_PushOnly_MoreThanBatchLimitPending_PushesOnlyBatchAndAdvancesWatermarkToLastPushedEvent()
    {
        // 250 pending manual events, ordered oldest-first by the repository (as it now is).
        // PushAsync must push only the first 200 (the batch) and advance LastSuccessfulSyncAt to
        // that batch's LAST event's own UpdatedAt - not DateTimeOffset.UtcNow - so the next run's
        // "since" query picks up exactly the remaining 50 instead of skipping them forever.
        const int pendingCount = BatchLimitPerConnection + 50; // 250
        var sut = BuildSut();
        var connection = MakeConnection(CalendarSyncDirections.PushOnly);
        var lastSuccess = DateTimeOffset.UtcNow.AddDays(-2);
        connection.LastSuccessfulSyncAt = lastSuccess;
        var pending = MakeManualEvents(pendingCount);
        var expectedWatermark = pending[BatchLimitPerConnection - 1].UpdatedAt;

        _connections.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, ConnectionId, It.IsAny<CancellationToken>())).ReturnsAsync(connection);
        _events.Setup(x => x.GetManualEventsUpdatedSinceForUserAsync(TenantId, connection.UserId, lastSuccess, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pending);
        _links.Setup(x => x.GetTrackedByCalendarEventAndConnectionAsync(TenantId, It.IsAny<Guid>(), ConnectionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ExternalCalendarEventLink?)null);
        _googleClient.Setup(x => x.InsertEventAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<GoogleCalendarEventDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string token, string calId, GoogleCalendarEventDto dto, CancellationToken _) => dto with { Id = "pushed-" + Guid.NewGuid(), Etag = "etag" });

        await sut.SyncConnectionAsync(TenantId, ConnectionId, CancellationToken.None);

        _googleClient.Verify(x => x.InsertEventAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<GoogleCalendarEventDto>(), It.IsAny<CancellationToken>()), Times.Exactly(BatchLimitPerConnection));
        Assert.Equal(expectedWatermark, connection.LastSuccessfulSyncAt);
    }

    [Fact]
    public async Task SyncConnectionAsync_PushOnly_FewerThanBatchLimitPending_AdvancesWatermarkToUtcNow()
    {
        var sut = BuildSut();
        var connection = MakeConnection(CalendarSyncDirections.PushOnly);
        var lastSuccess = DateTimeOffset.UtcNow.AddDays(-2);
        connection.LastSuccessfulSyncAt = lastSuccess;
        var pending = MakeManualEvents(5);
        var before = DateTimeOffset.UtcNow;

        _connections.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, ConnectionId, It.IsAny<CancellationToken>())).ReturnsAsync(connection);
        _events.Setup(x => x.GetManualEventsUpdatedSinceForUserAsync(TenantId, connection.UserId, lastSuccess, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pending);
        _links.Setup(x => x.GetTrackedByCalendarEventAndConnectionAsync(TenantId, It.IsAny<Guid>(), ConnectionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ExternalCalendarEventLink?)null);
        _googleClient.Setup(x => x.InsertEventAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<GoogleCalendarEventDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string token, string calId, GoogleCalendarEventDto dto, CancellationToken _) => dto with { Id = "pushed-" + Guid.NewGuid(), Etag = "etag" });

        await sut.SyncConnectionAsync(TenantId, ConnectionId, CancellationToken.None);

        Assert.True(connection.LastSuccessfulSyncAt >= before);
    }

    [Fact]
    public async Task SyncConnectionAsync_PushOnly_PrivateLocalEvent_PushesIsPrivateTrue()
    {
        var sut = BuildSut();
        var connection = MakeConnection(CalendarSyncDirections.PushOnly);
        var lastSuccess = DateTimeOffset.UtcNow.AddDays(-2);
        connection.LastSuccessfulSyncAt = lastSuccess;
        var pending = MakeManualEvents(1, isPrivate: true);

        _connections.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, ConnectionId, It.IsAny<CancellationToken>())).ReturnsAsync(connection);
        _events.Setup(x => x.GetManualEventsUpdatedSinceForUserAsync(TenantId, connection.UserId, lastSuccess, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pending);
        _links.Setup(x => x.GetTrackedByCalendarEventAndConnectionAsync(TenantId, It.IsAny<Guid>(), ConnectionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ExternalCalendarEventLink?)null);
        _googleClient.Setup(x => x.InsertEventAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<GoogleCalendarEventDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string token, string calId, GoogleCalendarEventDto dto, CancellationToken _) => dto with { Id = "pushed-1", Etag = "etag" });

        await sut.SyncConnectionAsync(TenantId, ConnectionId, CancellationToken.None);

        _googleClient.Verify(x => x.InsertEventAsync(It.IsAny<string>(), It.IsAny<string>(), It.Is<GoogleCalendarEventDto>(d => d.IsPrivate), It.IsAny<CancellationToken>()), Times.Once);
    }

    private static List<GoogleCalendarEventDto> MakeGoogleEvents(int count) =>
        Enumerable.Range(0, count)
            .Select(i => new GoogleCalendarEventDto($"ext-{i}", $"etag-{i}", $"Event {i}", null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), false, "UTC", null, false, false))
            .ToList();

    private static List<GraphEventDto> MakeGraphEvents(int count) =>
        Enumerable.Range(0, count)
            .Select(i => new GraphEventDto($"ext-{i}", $"etag-{i}", $"Event {i}", null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), false, "UTC", null, false, false))
            .ToList();

    [Fact]
    public async Task SyncConnectionAsync_GooglePullReturnsMoreThanBatchLimit_UpsertsAllEventsAndAdvancesSyncToken()
    {
        // The client accumulates all pages up to its own MaxPages cap and, here, reached the
        // provider's real final page within that cap - so it legitimately returns a non-null token
        // alongside a large event count (well above the old, now-removed, 200-event
        // BatchLimitPerConnection truncation). PullAsync must ingest every one of them and persist
        // the token: there is no longer any event-count-based boundary inside PullAsync itself.
        const int eventCount = BatchLimitPerConnection + 250; // 450
        var sut = BuildSut();
        var connection = MakeConnection(CalendarSyncDirections.PullOnly);
        Assert.Null(connection.SyncTokenEncrypted);
        _connections.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, ConnectionId, It.IsAny<CancellationToken>())).ReturnsAsync(connection);
        _googleClient.Setup(x => x.ListEventsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GoogleCalendarPage(MakeGoogleEvents(eventCount), "new-sync-token"));
        _links.Setup(x => x.GetTrackedByConnectionAndExternalEventAsync(TenantId, ConnectionId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ExternalCalendarEventLink?)null);

        await sut.SyncConnectionAsync(TenantId, ConnectionId, CancellationToken.None);

        _events.Verify(x => x.AddAsync(It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>()), Times.Exactly(eventCount));
        Assert.NotNull(connection.SyncTokenEncrypted);
        Assert.Equal("new-sync-token", System.Text.Encoding.UTF8.GetString(connection.SyncTokenEncrypted!));
    }

    [Fact]
    public async Task SyncConnectionAsync_GooglePullReturnsNullToken_DoesNotAdvanceSyncToken()
    {
        // The client itself returns a null token when its own internal MaxPages cap was hit before
        // reaching the provider's real final page (i.e. the client could not fetch everything in the
        // window). That null - not any event count comparison in PullAsync - is the sole signal that
        // the stored sync token must be withheld, so the next run retries from the same starting
        // point instead of silently skipping whatever lay past the cap.
        var sut = BuildSut();
        var connection = MakeConnection(CalendarSyncDirections.PullOnly);
        Assert.Null(connection.SyncTokenEncrypted);
        _connections.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, ConnectionId, It.IsAny<CancellationToken>())).ReturnsAsync(connection);
        _googleClient.Setup(x => x.ListEventsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GoogleCalendarPage(MakeGoogleEvents(BatchLimitPerConnection + 250), null));
        _links.Setup(x => x.GetTrackedByConnectionAndExternalEventAsync(TenantId, ConnectionId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ExternalCalendarEventLink?)null);

        await sut.SyncConnectionAsync(TenantId, ConnectionId, CancellationToken.None);

        Assert.Null(connection.SyncTokenEncrypted);
    }

    [Fact]
    public async Task SyncConnectionAsync_MicrosoftPullReturnsMoreThanBatchLimit_UpsertsAllEventsAndAdvancesDeltaLink()
    {
        const int eventCount = BatchLimitPerConnection + 250; // 450
        var sut = BuildSut();
        var connection = MakeConnection(CalendarSyncDirections.PullOnly);
        connection.Provider = CalendarExternalSources.OutlookCalendar;
        Assert.Null(connection.DeltaLinkEncrypted);
        _connections.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, ConnectionId, It.IsAny<CancellationToken>())).ReturnsAsync(connection);
        _msClient.Setup(x => x.ListEventsAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GraphCalendarPage(MakeGraphEvents(eventCount), "new-delta-link"));
        _links.Setup(x => x.GetTrackedByConnectionAndExternalEventAsync(TenantId, ConnectionId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ExternalCalendarEventLink?)null);

        await sut.SyncConnectionAsync(TenantId, ConnectionId, CancellationToken.None);

        _events.Verify(x => x.AddAsync(It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>()), Times.Exactly(eventCount));
        Assert.NotNull(connection.DeltaLinkEncrypted);
        Assert.Equal("new-delta-link", System.Text.Encoding.UTF8.GetString(connection.DeltaLinkEncrypted!));
    }

    [Fact]
    public async Task SyncConnectionAsync_MicrosoftPullReturnsNullToken_DoesNotAdvanceDeltaLink()
    {
        var sut = BuildSut();
        var connection = MakeConnection(CalendarSyncDirections.PullOnly);
        connection.Provider = CalendarExternalSources.OutlookCalendar;
        Assert.Null(connection.DeltaLinkEncrypted);
        _connections.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, ConnectionId, It.IsAny<CancellationToken>())).ReturnsAsync(connection);
        _msClient.Setup(x => x.ListEventsAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GraphCalendarPage(MakeGraphEvents(BatchLimitPerConnection + 250), null));
        _links.Setup(x => x.GetTrackedByConnectionAndExternalEventAsync(TenantId, ConnectionId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ExternalCalendarEventLink?)null);

        await sut.SyncConnectionAsync(TenantId, ConnectionId, CancellationToken.None);

        Assert.Null(connection.DeltaLinkEncrypted);
    }
}
