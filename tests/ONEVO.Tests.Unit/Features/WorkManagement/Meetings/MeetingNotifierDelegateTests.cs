using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Application.Features.WorkManagement.Meetings.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Meetings.Services;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Meetings;

/// <summary>Spec decision 6: notices addressed to the organizer also reach each active delegate, once per person.</summary>
public sealed class MeetingNotifierDelegateTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);

    private readonly Employee _ceo = Person("Chief", "Executive");
    private readonly Employee _secretary = Person("Sara", "Fernando");
    private readonly Employee _assistant = Person("Kavin", "Raj");
    private readonly Employee _attendee = Person("Nimal", "Perera");
    private readonly MeetingSeries _series;
    private readonly Mock<ICalendarNotificationSender> _sender = new();
    private readonly Mock<IEmployeeRepository> _employees = new();
    private readonly Mock<IMeetingDelegateRepository> _delegates = new();
    private readonly Mock<IDateTimeProvider> _clock = new();

    private static Employee Person(string first, string last) => new()
    {
        Id = Guid.NewGuid(), TenantId = Tenant, UserId = Guid.NewGuid(), FirstName = first, LastName = last,
    };

    public MeetingNotifierDelegateTests()
    {
        _series = new MeetingSeries { Id = Guid.NewGuid(), TenantId = Tenant, OrganizerEmployeeId = _ceo.Id, Title = "Board review" };
        var everyone = new[] { _ceo, _secretary, _assistant, _attendee }.ToDictionary(e => e.Id);
        _employees.Setup(e => e.ListByIdsAsync(Tenant, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, IReadOnlyCollection<Guid> ids, CancellationToken _) =>
                ids.Where(everyone.ContainsKey).ToDictionary(id => id, id => everyone[id]));
        _clock.SetupGet(c => c.UtcNow).Returns(Now);
    }

    private MeetingNotifier Sut() => new(_sender.Object, Mock.Of<IMeetingRepository>(), Mock.Of<IMeetingLinkRepository>(), _employees.Object,
        _clock.Object, _delegates.Object);

    private void ActiveDelegates(params Employee[] people)
        => _delegates.Setup(d => d.ListActiveDelegateIdsAsync(Tenant, _ceo.Id, Now, It.IsAny<CancellationToken>()))
            .ReturnsAsync(people.Select(p => p.Id).ToList());

    [Fact]
    public async Task ALinkProblemNotice_ReachesTheOrganizerAndEachActiveDelegate()
    {
        ActiveDelegates(_secretary, _assistant);

        await Sut().NotifyOrganizerAsync(_series, CalendarMeetingNoticeCodes.LinkFailed, CalendarEventMeetingProviders.MicrosoftTeams,
            CancellationToken.None);

        foreach (var person in new[] { _ceo, _secretary, _assistant })
            _sender.Verify(s => s.NotifyOrganizerAboutMeetingAsync(Tenant, person.UserId, CalendarMeetingNoticeCodes.LinkFailed, "Board review",
                It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        _sender.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AnRsvpNotice_ReachesTheOrganizerAndEachActiveDelegate()
    {
        ActiveDelegates(_secretary);

        await Sut().RsvpAnsweredAsync(_series, "Board review", _attendee.Id, accepted: true, CancellationToken.None);

        foreach (var person in new[] { _ceo, _secretary })
            _sender.Verify(s => s.NotifyRsvpAnsweredAsync(Tenant, person.UserId, "Board review", "Nimal Perera", true,
                It.IsAny<CancellationToken>()), Times.Once);
        _sender.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ADelegateAnsweringTheInvite_IsNotToldAboutTheirOwnAnswer()
    {
        ActiveDelegates(_secretary);

        await Sut().RsvpAnsweredAsync(_series, "Board review", _secretary.Id, accepted: false, CancellationToken.None);

        _sender.Verify(s => s.NotifyRsvpAnsweredAsync(Tenant, _ceo.UserId, "Board review", "Sara Fernando", false, It.IsAny<CancellationToken>()),
            Times.Once);
        _sender.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ARevokedOrExpiredDelegate_GetsNoNotice()
    {
        // The repository returns only active delegates; a revoked or expired one is simply not in the list.
        ActiveDelegates();

        await Sut().NotifyOrganizerAsync(_series, CalendarMeetingNoticeCodes.LinkUnknown, CalendarEventMeetingProviders.Zoom, CancellationToken.None);

        _sender.Verify(s => s.NotifyOrganizerAboutMeetingAsync(Tenant, _ceo.UserId, CalendarMeetingNoticeCodes.LinkUnknown, "Board review",
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        _sender.VerifyNoOtherCalls();
    }
}
