using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Domain.Features.Calendar.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class MeetingProvisionerTests
{
    private static readonly Guid EventId = Guid.NewGuid();
    private static readonly DateTimeOffset Start = new(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);
    private readonly ExternalCalendarConnection _connection = new() { Id = Guid.NewGuid() };
    private readonly Mock<ICalendarConnectionTokenProvider> _tokens = new();
    private readonly Mock<ITeamsMeetingClient> _teams = new();
    private readonly Mock<IZoomMeetingClient> _zoom = new();
    private readonly List<TimeSpan> _waits = [];

    private MeetingProvisioner BuildSut()
    {
        _tokens.Setup(t => t.GetFreshAccessTokenAsync(_connection, It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync("t1");
        return new MeetingProvisioner(_tokens.Object, _teams.Object, _zoom.Object, NullLogger<MeetingProvisioner>.Instance,
            (wait, _) => { _waits.Add(wait); return Task.CompletedTask; });
    }

    private MeetingProvisioningRequest Request(string provider, int generation = 1, bool recovery = false) =>
        new(_connection, provider, EventId, generation, "Review", Start, Start.AddHours(1), recovery);

    private static readonly TeamsMeetingDto Teams = new("graph-1", "https://teams/j/1", null, null);
    private static readonly ZoomMeetingDto Zoom = new("999", "https://zoom.us/j/999", null, "pw");

    private void TeamsSequence(params object[] steps)
    {
        var setup = _teams.SetupSequence(t => t.CreateOrGetMeetingAsync(
            It.IsAny<string>(), $"{EventId}:1", "Review", Start, Start.AddHours(1), It.IsAny<CancellationToken>()));
        foreach (var step in steps)
            setup = step is Exception ex ? setup.ThrowsAsync(ex) : setup.ReturnsAsync((TeamsMeetingDto)step);
    }

    [Fact]
    public async Task Teams2xx_Created_UsingTheGenerationsExternalId()
    {
        TeamsSequence(Teams);

        var outcome = await BuildSut().ProvisionAsync(Request(CalendarEventMeetingProviders.MicrosoftTeams), CancellationToken.None);

        Assert.Equal(MeetingProvisioningOutcomeKind.Created, outcome.Kind);
        Assert.Equal("graph-1", outcome.Meeting!.ExternalMeetingId);
    }

    [Fact]
    public async Task Unauthorized_RefreshesOnce_ThenSucceeds()
    {
        _tokens.Setup(t => t.ForceRefreshAccessTokenAsync(_connection, "microsoft", It.IsAny<CancellationToken>())).ReturnsAsync("t2");
        TeamsSequence(new MeetingProviderException(MeetingProviderFailureKind.Unauthorized, "401"), Teams);

        var outcome = await BuildSut().ProvisionAsync(Request(CalendarEventMeetingProviders.MicrosoftTeams), CancellationToken.None);

        Assert.Equal(MeetingProvisioningOutcomeKind.Created, outcome.Kind);
        _tokens.Verify(t => t.ForceRefreshAccessTokenAsync(_connection, "microsoft", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UnauthorizedTwice_IsAuthFailed()
    {
        _tokens.Setup(t => t.ForceRefreshAccessTokenAsync(_connection, "microsoft", It.IsAny<CancellationToken>())).ReturnsAsync("t2");
        TeamsSequence(
            new MeetingProviderException(MeetingProviderFailureKind.Unauthorized, "401"),
            new MeetingProviderException(MeetingProviderFailureKind.Unauthorized, "401"));

        var outcome = await BuildSut().ProvisionAsync(Request(CalendarEventMeetingProviders.MicrosoftTeams), CancellationToken.None);

        Assert.Equal(MeetingProvisioningOutcomeKind.Unauthorized, outcome.Kind);
        Assert.Equal("auth_failed", outcome.Reason);
    }

    [Fact]
    public async Task RateLimitedTwice_ThenCreated_HonouringRetryAfter()
    {
        TeamsSequence(
            new MeetingProviderException(MeetingProviderFailureKind.RateLimited, "429", TimeSpan.FromSeconds(3)),
            new MeetingProviderException(MeetingProviderFailureKind.RateLimited, "429", TimeSpan.FromSeconds(4)),
            Teams);

        var outcome = await BuildSut().ProvisionAsync(Request(CalendarEventMeetingProviders.MicrosoftTeams), CancellationToken.None);

        Assert.Equal(MeetingProvisioningOutcomeKind.Created, outcome.Kind);
        Assert.Equal(new[] { TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(4) }, _waits);
    }

    [Fact]
    public async Task RateLimitedThreeTimes_IsRateLimited_AfterThreeAttempts()
    {
        var limited = new MeetingProviderException(MeetingProviderFailureKind.RateLimited, "429", TimeSpan.FromSeconds(1));
        TeamsSequence(limited, limited, limited, Teams);

        var outcome = await BuildSut().ProvisionAsync(Request(CalendarEventMeetingProviders.MicrosoftTeams), CancellationToken.None);

        Assert.Equal(MeetingProvisioningOutcomeKind.RateLimited, outcome.Kind);
        _teams.Verify(t => t.CreateOrGetMeetingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task RetryAfterLongerThanTheCap_WaitsOnlyTheCap()
    {
        TeamsSequence(new MeetingProviderException(MeetingProviderFailureKind.RateLimited, "429", TimeSpan.FromMinutes(5)), Teams);

        await BuildSut().ProvisionAsync(Request(CalendarEventMeetingProviders.MicrosoftTeams), CancellationToken.None);

        Assert.Equal(MeetingProvisioner.MaxSingleWait, Assert.Single(_waits));
    }

    [Theory]
    [InlineData(MeetingProviderFailureKind.Permanent, MeetingProvisioningOutcomeKind.Permanent)]
    [InlineData(MeetingProviderFailureKind.Ambiguous, MeetingProvisioningOutcomeKind.Ambiguous)]
    public async Task OtherFailures_AreClassified_WithoutRetrying(MeetingProviderFailureKind kind, MeetingProvisioningOutcomeKind expected)
    {
        TeamsSequence(new MeetingProviderException(kind, "x"), Teams);

        var outcome = await BuildSut().ProvisionAsync(Request(CalendarEventMeetingProviders.MicrosoftTeams), CancellationToken.None);

        Assert.Equal(expected, outcome.Kind);
    }

    [Fact]
    public async Task NoAccessToken_IsNotConnected()
    {
        var sut = BuildSut();
        _tokens.Setup(t => t.GetFreshAccessTokenAsync(_connection, It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);

        var outcome = await sut.ProvisionAsync(Request(CalendarEventMeetingProviders.Zoom), CancellationToken.None);

        Assert.Equal(MeetingProvisioningOutcomeKind.NotConnected, outcome.Kind);
    }

    [Fact]
    public async Task ZoomCreate_CarriesTheGenerationsToken()
    {
        _zoom.Setup(z => z.CreateMeetingAsync("t1", "Review", Start, Start.AddHours(1), $"onexso:{EventId}:3", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Zoom);

        var outcome = await BuildSut().ProvisionAsync(Request(CalendarEventMeetingProviders.Zoom, generation: 3), CancellationToken.None);

        Assert.Equal("999", outcome.Meeting!.ExternalMeetingId);
    }

    [Fact]
    public async Task ZoomRecovery_TokenMatch_AdoptsWithoutCreating()
    {
        _zoom.Setup(z => z.FindMeetingByCorrelationTokenAsync("t1", $"onexso:{EventId}:1", Start, It.IsAny<CancellationToken>())).ReturnsAsync(Zoom);

        var outcome = await BuildSut().ProvisionAsync(Request(CalendarEventMeetingProviders.Zoom, recovery: true), CancellationToken.None);

        Assert.Equal(MeetingProvisioningOutcomeKind.Created, outcome.Kind);
        _zoom.Verify(z => z.CreateMeetingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ZoomRecovery_NoMatch_CreatesWithTheSameToken()
    {
        _zoom.Setup(z => z.FindMeetingByCorrelationTokenAsync("t1", $"onexso:{EventId}:1", Start, It.IsAny<CancellationToken>())).ReturnsAsync((ZoomMeetingDto?)null);
        _zoom.Setup(z => z.CreateMeetingAsync("t1", "Review", Start, Start.AddHours(1), $"onexso:{EventId}:1", It.IsAny<CancellationToken>())).ReturnsAsync(Zoom);

        var outcome = await BuildSut().ProvisionAsync(Request(CalendarEventMeetingProviders.Zoom, recovery: true), CancellationToken.None);

        Assert.Equal(MeetingProvisioningOutcomeKind.Created, outcome.Kind);
    }

    [Fact]
    public async Task ZoomRecovery_ListUnavailable_StaysAmbiguous_AndNeverCreates()
    {
        _zoom.Setup(z => z.FindMeetingByCorrelationTokenAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new MeetingProviderException(MeetingProviderFailureKind.Permanent, "403 missing scope"));

        var outcome = await BuildSut().ProvisionAsync(Request(CalendarEventMeetingProviders.Zoom, recovery: true), CancellationToken.None);

        Assert.Equal(MeetingProvisioningOutcomeKind.Ambiguous, outcome.Kind);
        Assert.Equal("correlation_unavailable", outcome.Reason);
        _zoom.Verify(z => z.CreateMeetingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ResolveForRemoval_Teams_CreateOrGetThenDelete()
    {
        TeamsSequence(Teams);

        Assert.True(await BuildSut().ResolveForRemovalAsync(Request(CalendarEventMeetingProviders.MicrosoftTeams), CancellationToken.None));
        _teams.Verify(t => t.CancelMeetingAsync("t1", "graph-1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ResolveForRemoval_Zoom_NoMatchAfterAFullRead_IsResolvedWithoutADelete()
    {
        _zoom.Setup(z => z.FindMeetingByCorrelationTokenAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ZoomMeetingDto?)null);

        Assert.True(await BuildSut().ResolveForRemovalAsync(Request(CalendarEventMeetingProviders.Zoom), CancellationToken.None));
        _zoom.Verify(z => z.CancelMeetingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ResolveForRemoval_Zoom_ListFails_IsUnresolved()
    {
        _zoom.Setup(z => z.FindMeetingByCorrelationTokenAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new MeetingProviderException(MeetingProviderFailureKind.Ambiguous, "500"));

        Assert.False(await BuildSut().ResolveForRemovalAsync(Request(CalendarEventMeetingProviders.Zoom), CancellationToken.None));
    }
}
