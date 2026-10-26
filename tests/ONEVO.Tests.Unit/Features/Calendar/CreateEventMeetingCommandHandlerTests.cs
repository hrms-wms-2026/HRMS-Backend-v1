using ONEVO.Application.Features.WorkManagement.Meetings;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Commands.CreateEventMeeting;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class CreateEventMeetingCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid EventId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);

    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<ICalendarEventRepository> _events = new();
    private readonly Mock<IExternalCalendarConnectionRepository> _connections = new();
    private readonly Mock<ICalendarEventMeetingRepository> _meetings = new();
    private readonly Mock<IMeetingProvisioner> _provisioner = new();
    private readonly Mock<ICalendarNotificationSender> _notifications = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository> _employees = new();
    private readonly Mock<IDateTimeProvider> _clock = new();

    private readonly CalendarEvent _evt = new()
    {
        Id = EventId, TenantId = TenantId, CreatedById = UserId, Title = "Review",
        StartDate = Now.AddDays(1), EndDate = Now.AddDays(1).AddHours(1)
    };

    private readonly ExternalCalendarConnection _connection = new()
    {
        Id = Guid.NewGuid(), TenantId = TenantId, UserId = UserId, Provider = CalendarExternalSources.OutlookCalendar,
        Status = ExternalCalendarConnectionStatuses.Active, ScopesJson = """["OnlineMeetings.ReadWrite"]"""
    };

    private CreateEventMeetingCommandHandler BuildSut()
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        _currentUser.SetupGet(x => x.UserId).Returns(UserId);
        _clock.SetupGet(c => c.UtcNow).Returns(Now);
        _events.Setup(e => e.GetTrackedByIdForTenantAsync(TenantId, EventId, It.IsAny<CancellationToken>())).ReturnsAsync(_evt);
        _events.Setup(e => e.GetByIdForTenantAsync(TenantId, EventId, It.IsAny<CancellationToken>())).ReturnsAsync(_evt);
        _events.Setup(e => e.GetParticipantsForEventsAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<CalendarEventParticipant>>());
        _events.Setup(e => e.GetGuestsForEventsAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<CalendarEventGuest>>());
        _connections.Setup(c => c.GetByTenantUserProviderAsync(TenantId, UserId, CalendarExternalSources.OutlookCalendar, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_connection);
        _employees.Setup(e => e.GetDefaultForUserAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = Guid.NewGuid(), TenantId = TenantId, UserId = UserId, FirstName = "Ada", LastName = "Owner" });
        _unitOfWork.Setup(u => u.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result<CreateEventMeetingResult>>>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<Result<CreateEventMeetingResult>>>, CancellationToken>((op, ct) => op(ct));
        _unitOfWork.Setup(u => u.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<bool>>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<bool>>, CancellationToken>((op, ct) => op(ct));
        var links = new EventMeetingLinkService(_events.Object, _meetings.Object, _connections.Object, _provisioner.Object,
            _notifications.Object, _unitOfWork.Object, _clock.Object, Mock.Of<IOutboxWriter>(), _employees.Object);
        return new CreateEventMeetingCommandHandler(
            _currentUser.Object, _events.Object, _connections.Object, _meetings.Object, links, _employees.Object, _clock.Object, _unitOfWork.Object);
    }

    [Fact]
    public async Task With_meetings_on_a_personal_event_cannot_gain_a_meeting_link()
    {
        var sut = BuildSut();
        var evt = new CalendarEvent { Id = Guid.NewGuid(), TenantId = TenantId, Title = "Focus", CreatedById = UserId,
            StartDate = DateTimeOffset.UtcNow.AddHours(1), EndDate = DateTimeOffset.UtcNow.AddHours(2) };
        _events.Setup(e => e.GetTrackedByIdForTenantAsync(TenantId, evt.Id, It.IsAny<CancellationToken>())).ReturnsAsync(evt);
        var guarded = new CreateEventMeetingCommandHandler(_currentUser.Object, _events.Object, _connections.Object, _meetings.Object,
            null!, _employees.Object, _clock.Object, _unitOfWork.Object, new CalendarMeetingGuard(Options.Create(new MeetingsOptions { Create = true }), _events.Object));

        var result = await guarded.Handle(new CreateEventMeetingCommand(evt.Id, "zoom"), CancellationToken.None);

        Assert.Equal((400, CalendarMeetingGuard.ErrorCode), (result.StatusCode, result.ErrorCode));
        _ = sut;
    }

    private void FirstClaimSucceeds(int generation = 1, bool invitesPending = true)
    {
        _meetings.Setup(m => m.GetByCalendarEventAsync(TenantId, EventId, It.IsAny<CancellationToken>())).ReturnsAsync((CalendarEventMeeting?)null);
        _meetings.Setup(m => m.TryInsertProvisioningAsync(It.IsAny<CalendarEventMeeting>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _meetings.Setup(m => m.GetTrackedByCalendarEventAsync(TenantId, EventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CalendarEventMeeting
            {
                Id = Guid.NewGuid(), TenantId = TenantId, CalendarEventId = EventId, Provider = CalendarEventMeetingProviders.MicrosoftTeams,
                Status = CalendarEventMeetingStatuses.Provisioning, LinkGeneration = generation, ExternalCalendarConnectionId = _connection.Id,
                InvitesPending = invitesPending
            });
    }

    private void ProvisionerReturns(MeetingProvisioningOutcome outcome) =>
        _provisioner.Setup(p => p.ProvisionAsync(It.IsAny<MeetingProvisioningRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(outcome);

    private static CreateEventMeetingCommand Teams(bool invitesHeldBack = true) => new(EventId, CalendarEventMeetingProviders.MicrosoftTeams, invitesHeldBack);

    [Fact]
    public async Task Created_SetsTheLink_AndKeysLinkEmailsOnTheGeneration()
    {
        var sut = BuildSut();
        FirstClaimSucceeds();
        ProvisionerReturns(new MeetingProvisioningOutcome(MeetingProvisioningOutcomeKind.Created, new ProvisionedMeeting("graph-1", "https://teams/j/1", null, null), null));
        var participant = new CalendarEventParticipant { Id = Guid.NewGuid(), TenantId = TenantId, EventId = EventId, EmployeeId = Guid.NewGuid() };
        _events.Setup(e => e.GetParticipantsForEventsAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<CalendarEventParticipant>> { [EventId] = [participant] });

        var result = await sut.Handle(Teams(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("https://teams/j/1", _evt.MeetingLink);
        _notifications.Verify(n => n.SendMeetingLinkAsync(
            TenantId, "Review", _evt.StartDate, null,
            It.Is<IReadOnlyList<CalendarParticipantRecipient>>(r => r.Single().EmailKey!.StartsWith("link:") && r.Single().EmailKey!.Contains($":1:{participant.Id}")),
            It.IsAny<IReadOnlyList<CalendarGuestRecipient>>(),
            "Ada Owner", "https://teams/j/1", It.Is<CalendarEventIcs?>(ics => ics != null && ics.EventId == EventId), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ActiveMeetingAlready_ReturnsItsLink_WithoutAProviderCall()
    {
        var sut = BuildSut();
        _meetings.Setup(m => m.GetByCalendarEventAsync(TenantId, EventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CalendarEventMeeting { Id = Guid.NewGuid(), Status = CalendarEventMeetingStatuses.Active, JoinUrl = "https://teams/j/old" });

        var result = await sut.Handle(Teams(), CancellationToken.None);

        Assert.Equal("https://teams/j/old", result.Value!.JoinUrl);
        _provisioner.Verify(p => p.ProvisionAsync(It.IsAny<MeetingProvisioningRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LostTheFirstClaim_Is409InProgress_WithoutAProviderCall()
    {
        var sut = BuildSut();
        _meetings.Setup(m => m.GetByCalendarEventAsync(TenantId, EventId, It.IsAny<CancellationToken>())).ReturnsAsync((CalendarEventMeeting?)null);
        _meetings.Setup(m => m.TryInsertProvisioningAsync(It.IsAny<CalendarEventMeeting>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await sut.Handle(Teams(), CancellationToken.None);

        Assert.Equal(409, result.StatusCode);
        Assert.Equal("meeting_link_in_progress", result.Error);
        _provisioner.Verify(p => p.ProvisionAsync(It.IsAny<MeetingProvisioningRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task FreshProvisioning_Is409InProgress()
    {
        var sut = BuildSut();
        _meetings.Setup(m => m.GetByCalendarEventAsync(TenantId, EventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CalendarEventMeeting { Id = Guid.NewGuid(), Status = CalendarEventMeetingStatuses.Provisioning, ProvisioningStartedAt = Now.AddMinutes(-1) });

        var result = await sut.Handle(Teams(), CancellationToken.None);

        Assert.Equal("meeting_link_in_progress", result.Error);
    }

    [Fact]
    public async Task StaleProvisioning_BecomesUnknown_FallbackInvitesGoOut_AndIs409Unknown()
    {
        var sut = BuildSut();
        var stale = new CalendarEventMeeting { Id = Guid.NewGuid(), Status = CalendarEventMeetingStatuses.Provisioning, ProvisioningStartedAt = Now.AddMinutes(-6), Provider = CalendarEventMeetingProviders.MicrosoftTeams };
        _meetings.Setup(m => m.GetByCalendarEventAsync(TenantId, EventId, It.IsAny<CancellationToken>())).ReturnsAsync(stale);
        _meetings.Setup(m => m.TryMarkStaleProvisioningUnknownAsync(TenantId, stale.Id, Now.AddMinutes(-5), Now, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _meetings.Setup(m => m.GetTrackedByCalendarEventAsync(TenantId, EventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CalendarEventMeeting { Id = stale.Id, Status = CalendarEventMeetingStatuses.ProvisioningUnknown, InvitesPending = true });
        var participant = new CalendarEventParticipant { Id = Guid.NewGuid(), TenantId = TenantId, EventId = EventId, EmployeeId = Guid.NewGuid() };
        _events.Setup(e => e.GetParticipantsForEventsAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<CalendarEventParticipant>> { [EventId] = [participant] });

        var result = await sut.Handle(Teams(), CancellationToken.None);

        Assert.Equal("meeting_link_status_unknown", result.Error);
        _notifications.Verify(n => n.SendMeetingLinkAsync(
            TenantId, "Review", _evt.StartDate, null,
            It.Is<IReadOnlyList<CalendarParticipantRecipient>>(r => r.Single().EmailKey == $"invite:{participant.Id}"),
            It.IsAny<IReadOnlyList<CalendarGuestRecipient>>(),
            "Ada Owner", null, It.IsAny<CalendarEventIcs?>(), It.IsAny<CancellationToken>()), Times.Once);
        _notifications.Verify(n => n.NotifyOrganizerAboutMeetingAsync(
            TenantId, UserId, CalendarMeetingNoticeCodes.LinkUnknown, "Review", "Microsoft Teams", It.IsAny<CancellationToken>()), Times.Once);
        _provisioner.Verify(p => p.ProvisionAsync(It.IsAny<MeetingProvisioningRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(CalendarEventMeetingStatuses.ProvisioningUnknown, "meeting_link_status_unknown")]
    [InlineData(CalendarEventMeetingStatuses.Unresolved, "meeting_link_unresolved")]
    public async Task UnknownOrUnresolved_RefusesWithoutAProviderCall(string status, string error)
    {
        var sut = BuildSut();
        _meetings.Setup(m => m.GetByCalendarEventAsync(TenantId, EventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CalendarEventMeeting { Id = Guid.NewGuid(), Status = status });

        var result = await sut.Handle(Teams(), CancellationToken.None);

        Assert.Equal(error, result.Error);
        _provisioner.Verify(p => p.ProvisionAsync(It.IsAny<MeetingProvisioningRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AfterFailedOrCancelled_ClaimsTheNextGeneration()
    {
        var sut = BuildSut();
        var failed = new CalendarEventMeeting { Id = Guid.NewGuid(), Status = CalendarEventMeetingStatuses.Failed, LinkGeneration = 2 };
        _meetings.Setup(m => m.GetByCalendarEventAsync(TenantId, EventId, It.IsAny<CancellationToken>())).ReturnsAsync(failed);
        _meetings.Setup(m => m.TryClaimNewGenerationAsync(TenantId, failed.Id, _connection.Id, CalendarEventMeetingProviders.MicrosoftTeams, Now, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(3);
        _meetings.Setup(m => m.GetTrackedByCalendarEventAsync(TenantId, EventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CalendarEventMeeting { Id = failed.Id, Status = CalendarEventMeetingStatuses.Provisioning, LinkGeneration = 3, Provider = CalendarEventMeetingProviders.MicrosoftTeams });
        ProvisionerReturns(new MeetingProvisioningOutcome(MeetingProvisioningOutcomeKind.Created, new ProvisionedMeeting("g", "https://teams/j/3", null, null), null));

        var result = await sut.Handle(Teams(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _provisioner.Verify(p => p.ProvisionAsync(It.Is<MeetingProvisioningRequest>(r => r.Generation == 3 && !r.Recovery), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProviderRejects_IsFailed502_InvitesWithoutLink_OrganizerNoticed()
    {
        var sut = BuildSut();
        FirstClaimSucceeds();
        ProvisionerReturns(new MeetingProvisioningOutcome(MeetingProvisioningOutcomeKind.Permanent, null, "rejected"));
        var guest = new CalendarEventGuest { Id = Guid.NewGuid(), TenantId = TenantId, EventId = EventId, Email = "vendor@example.com" };
        _events.Setup(e => e.GetGuestsForEventsAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<CalendarEventGuest>> { [EventId] = [guest] });

        var result = await sut.Handle(Teams(), CancellationToken.None);

        Assert.Equal(502, result.StatusCode);
        Assert.Equal("meeting_link_failed", result.Error);
        _notifications.Verify(n => n.SendMeetingLinkAsync(
            TenantId, "Review", _evt.StartDate, null, It.IsAny<IReadOnlyList<CalendarParticipantRecipient>>(),
            It.Is<IReadOnlyList<CalendarGuestRecipient>>(g => g.Single().EmailKey == $"invite:{guest.Id}"), "Ada Owner", null,
            It.IsAny<CalendarEventIcs?>(), It.IsAny<CancellationToken>()), Times.Once);
        _notifications.Verify(n => n.NotifyOrganizerAboutMeetingAsync(
            TenantId, UserId, CalendarMeetingNoticeCodes.LinkFailed, "Review", "Microsoft Teams", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Ambiguous_IsProvisioningUnknown409_WithFallbackAndNotice()
    {
        var sut = BuildSut();
        FirstClaimSucceeds();
        ProvisionerReturns(new MeetingProvisioningOutcome(MeetingProvisioningOutcomeKind.Ambiguous, null, "timeout_or_server_error"));

        var result = await sut.Handle(Teams(), CancellationToken.None);

        Assert.Equal("meeting_link_status_unknown", result.Error);
        _notifications.Verify(n => n.NotifyOrganizerAboutMeetingAsync(
            TenantId, UserId, CalendarMeetingNoticeCodes.LinkUnknown, "Review", "Microsoft Teams", It.IsAny<CancellationToken>()), Times.Once);
        _meetings.Verify(m => m.Update(It.Is<CalendarEventMeeting>(x => x.Status == CalendarEventMeetingStatuses.ProvisioningUnknown)), Times.Once);
    }

    [Fact]
    public async Task NotConnected_SendsTheHeldBackInvites_And409()
    {
        var sut = BuildSut();
        _meetings.Setup(m => m.GetByCalendarEventAsync(TenantId, EventId, It.IsAny<CancellationToken>())).ReturnsAsync((CalendarEventMeeting?)null);
        _connections.Setup(c => c.GetByTenantUserProviderAsync(TenantId, UserId, CalendarExternalSources.OutlookCalendar, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ExternalCalendarConnection?)null);
        var participant = new CalendarEventParticipant { Id = Guid.NewGuid(), TenantId = TenantId, EventId = EventId, EmployeeId = Guid.NewGuid() };
        _events.Setup(e => e.GetParticipantsForEventsAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<CalendarEventParticipant>> { [EventId] = [participant] });

        var result = await sut.Handle(Teams(), CancellationToken.None);

        Assert.Equal("meeting_provider_not_connected", result.Error);
        _notifications.Verify(n => n.SendMeetingLinkAsync(
            TenantId, "Review", _evt.StartDate, null, It.IsAny<IReadOnlyList<CalendarParticipantRecipient>>(), It.IsAny<IReadOnlyList<CalendarGuestRecipient>>(), "Ada Owner", null, It.IsAny<CalendarEventIcs?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NotOrganizer_Forbidden()
    {
        var sut = BuildSut();
        _evt.CreatedById = Guid.NewGuid();

        var result = await sut.Handle(Teams(), CancellationToken.None);

        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task RequestCancelledAfterTheClaim_ProvisioningAndItsSaveStillRunToTheEnd()
    {
        // A closed tab must not strand a claimed row in provisioning with the held-back invites unsent.
        var sut = BuildSut();
        FirstClaimSucceeds();
        ProvisionerReturns(new MeetingProvisioningOutcome(MeetingProvisioningOutcomeKind.Created, new ProvisionedMeeting("graph-1", "https://teams/j/1", null, null), null));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        var result = await sut.Handle(Teams(), cancelled.Token);

        Assert.True(result.IsSuccess);
        _provisioner.Verify(p => p.ProvisionAsync(It.IsAny<MeetingProvisioningRequest>(), It.Is<CancellationToken>(t => !t.IsCancellationRequested)), Times.Once);
        _unitOfWork.Verify(u => u.ExecuteInTransactionAsync(
            It.IsAny<Func<CancellationToken, Task<Result<CreateEventMeetingResult>>>>(), It.Is<CancellationToken>(t => !t.IsCancellationRequested)), Times.Once);
    }

    // ---- development's P1-5: the fallback is only for invites that were held back for the link ---------------

    [Fact]
    public async Task NotConnected_InvitesNotHeldBack_SendsNothing_And409()
    {
        var sut = BuildSut();
        _meetings.Setup(m => m.GetByCalendarEventAsync(TenantId, EventId, It.IsAny<CancellationToken>())).ReturnsAsync((CalendarEventMeeting?)null);
        _connections.Setup(c => c.GetByTenantUserProviderAsync(TenantId, UserId, CalendarExternalSources.OutlookCalendar, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ExternalCalendarConnection?)null);

        var result = await sut.Handle(Teams(invitesHeldBack: false), CancellationToken.None);

        Assert.Equal("meeting_provider_not_connected", result.Error);
        _notifications.Verify(n => n.SendMeetingLinkAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<string?>(),
            It.IsAny<IReadOnlyList<CalendarParticipantRecipient>>(), It.IsAny<IReadOnlyList<CalendarGuestRecipient>>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<CalendarEventIcs?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProviderRejects_InvitesAlreadySent_NoSecondInvite_ButTheOrganizerIsTold()
    {
        var sut = BuildSut();
        FirstClaimSucceeds(invitesPending: false);
        ProvisionerReturns(new MeetingProvisioningOutcome(MeetingProvisioningOutcomeKind.Permanent, null, "rejected"));

        var result = await sut.Handle(Teams(invitesHeldBack: false), CancellationToken.None);

        Assert.Equal(502, result.StatusCode);
        _notifications.Verify(n => n.SendMeetingLinkAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<string?>(),
            It.IsAny<IReadOnlyList<CalendarParticipantRecipient>>(), It.IsAny<IReadOnlyList<CalendarGuestRecipient>>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<CalendarEventIcs?>(), It.IsAny<CancellationToken>()), Times.Never);
        _notifications.Verify(n => n.NotifyOrganizerAboutMeetingAsync(
            TenantId, UserId, CalendarMeetingNoticeCodes.LinkFailed, "Review", "Microsoft Teams", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TheFirstClaimRecordsWhetherInvitesAreHeldBack()
    {
        var sut = BuildSut();
        FirstClaimSucceeds();
        CalendarEventMeeting? inserted = null;
        _meetings.Setup(m => m.TryInsertProvisioningAsync(It.IsAny<CalendarEventMeeting>(), It.IsAny<CancellationToken>()))
            .Callback((CalendarEventMeeting row, CancellationToken _) => inserted = row).ReturnsAsync(true);
        ProvisionerReturns(new MeetingProvisioningOutcome(MeetingProvisioningOutcomeKind.Created, new ProvisionedMeeting("g", "https://teams/j/1", null, null), null));

        await sut.Handle(Teams(invitesHeldBack: true), CancellationToken.None);

        Assert.True(inserted!.InvitesPending);
    }

    // ---- development's P2-3: the people invited to the event are added to the meeting ------------------------

    [Fact]
    public async Task TheProviderIsGivenTheParticipantsAndGuestsEmails()
    {
        var sut = BuildSut();
        FirstClaimSucceeds();
        var participant = new CalendarEventParticipant { Id = Guid.NewGuid(), TenantId = TenantId, EventId = EventId, EmployeeId = Guid.NewGuid() };
        _events.Setup(e => e.GetParticipantsForEventsAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<CalendarEventParticipant>> { [EventId] = [participant] });
        _events.Setup(e => e.GetGuestsForEventsAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<CalendarEventGuest>>
            {
                [EventId] = [new CalendarEventGuest { Id = Guid.NewGuid(), TenantId = TenantId, EventId = EventId, Email = "vendor@example.com" }]
            });
        _employees.Setup(e => e.GetByIdAsync(TenantId, participant.EmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = participant.EmployeeId, TenantId = TenantId, Email = "grace@acme.com", FirstName = "Grace", LastName = "Hopper" });
        MeetingProvisioningRequest? sent = null;
        _provisioner.Setup(p => p.ProvisionAsync(It.IsAny<MeetingProvisioningRequest>(), It.IsAny<CancellationToken>()))
            .Callback((MeetingProvisioningRequest r, CancellationToken _) => sent = r)
            .ReturnsAsync(new MeetingProvisioningOutcome(MeetingProvisioningOutcomeKind.Created, new ProvisionedMeeting("g", "https://teams/j/1", null, null), null));

        await sut.Handle(Teams(), CancellationToken.None);

        Assert.Equal(new[] { "vendor@example.com", "grace@acme.com" }, sent!.AttendeeEmails);
    }
}
