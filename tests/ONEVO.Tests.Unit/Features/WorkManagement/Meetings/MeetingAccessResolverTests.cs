using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Meetings.Contracts;
using ONEVO.Application.Features.WorkManagement.Meetings.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Meetings.Services;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Meetings;

/// <summary>Spec "Access": an active delegate of the organizer gets the Organizer role, acting for the organizer; nobody
/// else gains anything.</summary>
public sealed class MeetingAccessResolverTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Start = Now.AddDays(1);

    private readonly Guid _principal = Guid.NewGuid();
    private readonly Guid _secretary = Guid.NewGuid();
    private readonly MeetingSeries _series;
    private readonly MeetingDetails _details;
    private readonly Mock<IMeetingRepository> _meetings = new();
    private readonly Mock<IMeetingDelegateRepository> _delegates = new();
    private readonly Mock<IDateTimeProvider> _clock = new();

    public MeetingAccessResolverTests()
    {
        _series = new MeetingSeries { Id = Guid.NewGuid(), TenantId = Tenant, OrganizerEmployeeId = _principal, Title = "Board review" };
        _details = new MeetingDetails { TaskId = Guid.NewGuid(), TenantId = Tenant, SeriesId = _series.Id, OriginalStart = Start, StartAt = Start };
        _meetings.Setup(m => m.GetSeriesAsync(Tenant, _series.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_series);
        _meetings.Setup(m => m.GetDetailsAsync(Tenant, _series.Id, Start, It.IsAny<CancellationToken>())).ReturnsAsync(_details);
        _meetings.Setup(m => m.ListOverridesAsync(Tenant, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _meetings.Setup(m => m.ListSeriesParticipantsAsync(Tenant, _series.Id, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _clock.SetupGet(c => c.UtcNow).Returns(Now);
    }

    private MeetingAccessResolver Sut() => new(_meetings.Object, _delegates.Object, _clock.Object);

    private void Delegated(Guid principal, Guid delegateId)
        => _delegates.Setup(d => d.IsActiveDelegateAsync(Tenant, principal, delegateId, Now, It.IsAny<CancellationToken>())).ReturnsAsync(true);

    [Fact]
    public async Task AnActiveDelegate_GetsOrganizer_ActingForThePrincipal()
    {
        Delegated(_principal, _secretary);

        var series = await Sut().ResolveForSeriesAsync(Tenant, _secretary, _series.Id, CancellationToken.None);
        var occurrence = await Sut().ResolveForOccurrenceAsync(Tenant, _secretary, _series.Id, Start, CancellationToken.None);

        Assert.Equal((MeetingRoles.Organizer, (Guid?)_principal), (series!.Role, series.ActingForEmployeeId));
        Assert.Equal((MeetingRoles.Organizer, (Guid?)_principal), (occurrence!.Role, occurrence.ActingForEmployeeId));
        Assert.Same(_details, occurrence.Details);
    }

    [Fact]
    public async Task ADelegateWhoIsAlsoAnAttendee_GetsOrganizer()
    {
        Delegated(_principal, _secretary);
        _meetings.Setup(m => m.ListSeriesParticipantsAsync(Tenant, _series.Id, It.IsAny<CancellationToken>())).ReturnsAsync([
            new CalendarEventParticipant { Id = Guid.NewGuid(), TenantId = Tenant, MeetingSeriesId = _series.Id, EmployeeId = _secretary },
        ]);

        var access = await Sut().ResolveForOccurrenceAsync(Tenant, _secretary, _series.Id, Start, CancellationToken.None);

        Assert.Equal((MeetingRoles.Organizer, (Guid?)_principal), (access!.Role, access.ActingForEmployeeId));
    }

    [Fact]
    public async Task ARevokedExpiredOrInactiveDelegate_GetsNothing()
    {
        // The repository's "active" check covers revoked, expired and inactive grants; here it says no.
        _delegates.Setup(d => d.IsActiveDelegateAsync(Tenant, _principal, _secretary, Now, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        Assert.Null(await Sut().ResolveForSeriesAsync(Tenant, _secretary, _series.Id, CancellationToken.None));
        Assert.Null(await Sut().ResolveForOccurrenceAsync(Tenant, _secretary, _series.Id, Start, CancellationToken.None));
    }

    [Fact]
    public async Task TheDelegateOfSomeoneElse_GetsNothing()
    {
        Delegated(Guid.NewGuid(), _secretary);

        Assert.Null(await Sut().ResolveForSeriesAsync(Tenant, _secretary, _series.Id, CancellationToken.None));
        Assert.Null(await Sut().ResolveForOccurrenceAsync(Tenant, _secretary, _series.Id, Start, CancellationToken.None));
    }

    [Fact]
    public async Task TheOrganizer_HasNoActingFor()
    {
        var access = await Sut().ResolveForOccurrenceAsync(Tenant, _principal, _series.Id, Start, CancellationToken.None);

        Assert.Equal((MeetingRoles.Organizer, (Guid?)null), (access!.Role, access.ActingForEmployeeId));
        _delegates.VerifyNoOtherCalls();
    }
}
