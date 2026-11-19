using Microsoft.Extensions.Options;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.OutboxHandlers;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Application.Features.WorkManagement.Common.OutboxHandlers;
using ONEVO.Application.Features.WorkManagement.Meetings;
using ONEVO.Application.Features.WorkManagement.Meetings.Contracts;
using ONEVO.Application.Features.WorkManagement.Meetings.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Meetings.Services;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;
using Xunit;
using SenderEmployees = ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Meetings;

/// <summary>Who hears what when a series link settles, counted at the outbox with the real notifier and sender behind the
/// service: invites held back at create are released exactly once - in-app and by email - and a link that held nothing back
/// (a regenerate, a retry after the fallback invites) adds no second "added to an event" notice.</summary>
public sealed class SeriesMeetingLinkServiceTests
{
    private const string AddedNotice = "calendar_event_participant_added";
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 6, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Start = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);

    private readonly OutboxSpy _outbox = new();
    private readonly Mock<IMeetingProvisioner> _provisioner = new();
    private readonly Mock<IMeetingLinkRepository> _links = new();
    private readonly Mock<IMeetingRepository> _meetings = new();

    private readonly Guid _attendeeUser = Guid.NewGuid();
    private readonly MeetingSeries _series;
    private readonly CalendarEventMeeting _link;
    private readonly CalendarEventParticipant _attendeeRow;
    private readonly CalendarEventGuest _guestRow;
    private readonly SeriesMeetingLinkService _sut;

    public SeriesMeetingLinkServiceTests()
    {
        var organizer = new Employee { Id = Guid.NewGuid(), TenantId = Tenant, UserId = Guid.NewGuid(), FirstName = "Ada", LastName = "Lead", Email = "ada@example.com" };
        var attendee = new Employee { Id = Guid.NewGuid(), TenantId = Tenant, UserId = _attendeeUser, FirstName = "Sam", LastName = "Ray", Email = "sam@example.com" };
        var removed = new Employee { Id = Guid.NewGuid(), TenantId = Tenant, UserId = Guid.NewGuid(), FirstName = "Lee", LastName = "Fox", Email = "lee@example.com" };
        var connection = new ExternalCalendarConnection
        {
            Id = Guid.NewGuid(), TenantId = Tenant, UserId = organizer.UserId, Provider = CalendarExternalSources.OutlookCalendar,
            ExternalAccountEmail = "ada@example.com", ScopesJson = "[\"OnlineMeetings.ReadWrite\"]", Status = ExternalCalendarConnectionStatuses.Active,
        };
        _series = new MeetingSeries
        {
            Id = Guid.NewGuid(), TenantId = Tenant, OrganizerEmployeeId = organizer.Id, Title = "Weekly sync", DtStart = Start,
            DurationMinutes = 30, Timezone = "UTC", RecurrenceRule = "FREQ=WEEKLY",
        };
        _link = new CalendarEventMeeting
        {
            Id = Guid.NewGuid(), TenantId = Tenant, ExternalCalendarConnectionId = connection.Id, Provider = CalendarEventMeetingProviders.MicrosoftTeams,
            Status = CalendarEventMeetingStatuses.Provisioning, LinkGeneration = 1, ProvisioningStartedAt = Now,
        };
        var organizerRow = new CalendarEventParticipant { Id = Guid.NewGuid(), TenantId = Tenant, MeetingSeriesId = _series.Id, EmployeeId = organizer.Id };
        var removedRow = new CalendarEventParticipant { Id = Guid.NewGuid(), TenantId = Tenant, MeetingSeriesId = _series.Id, EmployeeId = removed.Id, IsDeleted = true };
        _attendeeRow = new CalendarEventParticipant { Id = Guid.NewGuid(), TenantId = Tenant, MeetingSeriesId = _series.Id, EmployeeId = attendee.Id };
        _guestRow = new CalendarEventGuest { Id = Guid.NewGuid(), TenantId = Tenant, MeetingSeriesId = _series.Id, Email = "client@example.com" };

        _links.Setup(l => l.GetAsync(Tenant, _link.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_link);
        _links.Setup(l => l.GetTrackedAsync(Tenant, _link.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_link);
        _links.Setup(l => l.GetActiveForSeriesAsync(Tenant, _series.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_link);
        _links.Setup(l => l.ListActiveReferencingSeriesAsync(Tenant, _link.Id, It.IsAny<CancellationToken>())).ReturnsAsync([_series.Id]);
        _meetings.Setup(m => m.GetSeriesAsync(Tenant, _series.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_series);
        _meetings.Setup(m => m.ListSeriesAsync(Tenant, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync([_series]);
        _meetings.Setup(m => m.ListSeriesParticipantsAsync(Tenant, _series.Id, It.IsAny<CancellationToken>())).ReturnsAsync([organizerRow, removedRow, _attendeeRow]);
        _meetings.Setup(m => m.ListSeriesGuestsAsync(Tenant, _series.Id, It.IsAny<CancellationToken>())).ReturnsAsync([_guestRow]);

        var connections = new Mock<IExternalCalendarConnectionRepository>();
        connections.Setup(c => c.GetByIdForTenantAsync(Tenant, connection.Id, It.IsAny<CancellationToken>())).ReturnsAsync(connection);

        var people = new Dictionary<Guid, Employee> { [organizer.Id] = organizer, [attendee.Id] = attendee, [removed.Id] = removed };
        var employees = new Mock<IEmployeeRepository>();
        employees.Setup(e => e.ListByIdsAsync(Tenant, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync(people);
        var senderEmployees = new Mock<SenderEmployees>();
        senderEmployees.Setup(e => e.GetByIdAsync(Tenant, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, Guid id, CancellationToken _) => people.GetValueOrDefault(id));

        var recurrence = new Mock<IMeetingRecurrence>();
        recurrence.Setup(r => r.Occurrences(It.IsAny<string?>(), It.IsAny<DateTimeOffset>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>()))
            .Returns([Start]);

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(u => u.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result<MeetingLinkResponse>>>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<Result<MeetingLinkResponse>>>, CancellationToken>((op, ct) => op(ct));

        var clock = new FixedClock(Now);
        var notifier = new MeetingNotifier(new CalendarNotificationSender(_outbox, senderEmployees.Object), _meetings.Object, _links.Object, employees.Object, clock);
        _sut = new SeriesMeetingLinkService(_links.Object, _meetings.Object, Mock.Of<IMeetingOccurrenceWriter>(), connections.Object, _provisioner.Object,
            notifier, recurrence.Object, employees.Object, _outbox, unitOfWork.Object, clock, Mock.Of<IMeetingAccessResolver>(), Options.Create(new MeetingsOptions()));
    }

    [Fact]
    public async Task ProvisionCreated_ReleasingHeldBackInvites_SendsEachInviteeOneInAppNoticeAndOneLinkEmail()
    {
        _link.InvitesPending = true;

        var result = await ProvisionAsync(Created());

        Assert.Equal(CalendarEventMeetingStatuses.Active, result.Value!.Status);
        Assert.False(_link.InvitesPending);
        Assert.Equal([_attendeeUser], AddedNoticeRecipients());
        Assert.Equal(LinkEmailKeys(), _outbox.EmailKeys.Order());
    }

    [Fact]
    public async Task ProvisionCreated_ForARegeneratedLink_EmailsTheLinkWithoutASecondAddedNotice()
    {
        _link.InvitesPending = false;   // RegenerateAsync claims with invitesPending: false

        var result = await ProvisionAsync(Created());

        Assert.Equal(CalendarEventMeetingStatuses.Active, result.Value!.Status);
        Assert.Empty(AddedNoticeRecipients());
        Assert.Equal(LinkEmailKeys(), _outbox.EmailKeys.Order());
    }

    [Theory]
    [InlineData(MeetingProvisioningOutcomeKind.Permanent)]
    [InlineData(MeetingProvisioningOutcomeKind.Ambiguous)]
    public async Task ProvisionWithoutALink_ReleasingHeldBackInvites_SendsEachInviteeOneInAppNoticeAndOneInviteEmail(MeetingProvisioningOutcomeKind kind)
    {
        _link.InvitesPending = true;

        await ProvisionAsync(new MeetingProvisioningOutcome(kind, null, "rejected"));

        Assert.False(_link.InvitesPending);
        Assert.Equal([_attendeeUser], AddedNoticeRecipients());
        Assert.Equal(InviteEmailKeys(), _outbox.EmailKeys.Order());
    }

    [Fact]
    public async Task ARetrySucceedingAfterTheFallbackInvites_EmailsTheLinkWithoutASecondAddedNotice()
    {
        _link.InvitesPending = true;
        await ProvisionAsync(new MeetingProvisioningOutcome(MeetingProvisioningOutcomeKind.Permanent, null, "rejected"));
        _link.Status = CalendarEventMeetingStatuses.Provisioning;   // what TryClaimRetryAsync does

        await ProvisionAsync(Created());

        Assert.Equal([_attendeeUser], AddedNoticeRecipients());
        Assert.Equal(InviteEmailKeys().Concat(LinkEmailKeys()).Order(), _outbox.EmailKeys.Order());
    }

    [Fact]
    public async Task ASeriesCancelledDuringTheCall_TellsNobody()
    {
        _link.InvitesPending = true;
        _series.CancelledAt = Now;

        var result = await ProvisionAsync(Created());

        Assert.Equal(CalendarEventMeetingStatuses.Cancelled, result.Value!.Status);
        Assert.Empty(AddedNoticeRecipients());
        Assert.Empty(_outbox.EmailKeys);
    }

    private Task<Result<MeetingLinkResponse>> ProvisionAsync(MeetingProvisioningOutcome outcome)
    {
        _provisioner.Setup(p => p.ProvisionAsync(It.IsAny<MeetingProvisioningRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(outcome);
        return _sut.ProvisionAsync(Tenant, _link.Id, _series.Id, recovery: false, CancellationToken.None);
    }

    private static MeetingProvisioningOutcome Created() => new(MeetingProvisioningOutcomeKind.Created,
        new ProvisionedMeeting("ext-1", "https://teams.example/join/1", "https://teams.example/org/1", null), null);

    private IEnumerable<Guid> AddedNoticeRecipients()
        => _outbox.Notices.Where(n => n.TemplateCode == AddedNotice).Select(n => n.RecipientUserId);

    private IEnumerable<string> InviteEmailKeys()
        => new[] { CalendarEmailKeys.Invite(_attendeeRow.Id), CalendarEmailKeys.Invite(_guestRow.Id) }.Order();

    private IEnumerable<string> LinkEmailKeys()
        => new[] { CalendarEmailKeys.Link(_link.Id, 1, _attendeeRow.Id), CalendarEmailKeys.Link(_link.Id, 1, _guestRow.Id) }.Order();

    /// <summary>The outbox's contract for what these tests count: a keyed message is written once per key.</summary>
    private sealed class OutboxSpy : IOutboxWriter
    {
        private readonly HashSet<string> _keys = [];

        public List<WorkNotificationPayload> Notices { get; } = [];
        public List<string> EmailKeys { get; } = [];

        public Task EnqueueAsync<TPayload>(string type, TPayload payload, Guid? tenantId = null, CancellationToken ct = default)
        {
            if (payload is WorkNotificationPayload notice)
                Notices.Add(notice);
            return Task.CompletedTask;
        }

        public Task<bool> EnqueueKeyedAsync<TPayload>(string type, TPayload payload, string deduplicationKey, Guid? tenantId = null, CancellationToken ct = default)
        {
            var written = _keys.Add(deduplicationKey);
            if (written && payload is CalendarEventInviteEmailPayload)
                EmailKeys.Add(deduplicationKey);
            return Task.FromResult(written);
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow => now;
        public DateOnly Today => DateOnly.FromDateTime(now.UtcDateTime);
    }
}
