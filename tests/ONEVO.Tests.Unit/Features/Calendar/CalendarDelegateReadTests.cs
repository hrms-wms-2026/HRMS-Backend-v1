using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Queries.GetCalendarEvents;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Meetings.Contracts;
using ONEVO.Application.Features.WorkManagement.Meetings.RepositoryInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

/// <summary>Spec "Seeing the principal's calendar": GET /calendar?forEmployeeId= gives an active delegate exactly what the
/// principal sees, private items included; anyone else gets 403 and nothing.</summary>
public sealed class CalendarDelegateReadTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset From = Now.AddDays(-1);
    private static readonly DateTimeOffset To = Now.AddDays(7);

    private readonly Employee _secretary = new() { Id = Guid.NewGuid(), TenantId = Tenant, UserId = Guid.NewGuid(), FirstName = "Sara", LastName = "Fernando" };
    private readonly Employee _ceo = new() { Id = Guid.NewGuid(), TenantId = Tenant, UserId = Guid.NewGuid(), FirstName = "Chief", LastName = "Executive" };
    private readonly Mock<ICurrentUser> _user = new();
    private readonly Mock<IEmployeeRepository> _employees = new();
    private readonly Mock<ICalendarEventRepository> _events = new();
    private readonly Mock<IMeetingDelegateRepository> _delegates = new();
    private readonly Mock<IDateTimeProvider> _clock = new();
    private readonly CalendarEvent _privateEvent;

    public CalendarDelegateReadTests()
    {
        _user.SetupGet(u => u.IsAuthenticated).Returns(true);
        _user.SetupGet(u => u.TenantId).Returns(Tenant);
        _user.SetupGet(u => u.UserId).Returns(_secretary.UserId);
        _employees.Setup(e => e.GetDefaultForUserAsync(Tenant, _secretary.UserId, It.IsAny<CancellationToken>())).ReturnsAsync(_secretary);
        _employees.Setup(e => e.GetDefaultForUserAsync(Tenant, _ceo.UserId, It.IsAny<CancellationToken>())).ReturnsAsync(_ceo);
        _employees.Setup(e => e.GetByIdAsync(Tenant, _ceo.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_ceo);
        _privateEvent = new CalendarEvent
        {
            Id = Guid.NewGuid(), TenantId = Tenant, Title = "Salary review", StartDate = Now.AddHours(2), EndDate = Now.AddHours(3),
            IsPrivate = true, CreatedById = _ceo.UserId,
        };
        _events.Setup(e => e.GetInDateRangeForCallerAsync(Tenant, _ceo.UserId, _ceo.Id, From, To, It.IsAny<CancellationToken>()))
            .ReturnsAsync([_privateEvent]);
        _events.Setup(e => e.GetInDateRangeForCallerAsync(Tenant, _secretary.UserId, _secretary.Id, From, To, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _events.Setup(e => e.GetRecurringMastersForCallerAsync(Tenant, It.IsAny<Guid>(), It.IsAny<Guid?>(), To, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _events.Setup(e => e.GetParticipantsForEventsAsync(Tenant, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<CalendarEventParticipant>>());
        _events.Setup(e => e.GetGuestsForEventsAsync(Tenant, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<CalendarEventGuest>>());
        _clock.SetupGet(c => c.UtcNow).Returns(Now);
    }

    private GetCalendarEventsQueryHandler Sut() => new(_user.Object, _employees.Object, _events.Object, Mock.Of<ICalendarRecurrenceExpander>(),
        meetings: null, delegates: _delegates.Object, clock: _clock.Object);

    private void SecretaryIsDelegate(bool active)
        => _delegates.Setup(d => d.IsActiveDelegateAsync(Tenant, _ceo.Id, _secretary.Id, Now, It.IsAny<CancellationToken>())).ReturnsAsync(active);

    [Fact]
    public async Task ADelegate_SeesThePrincipalsCalendar_PrivateMeetingsIncluded()
    {
        SecretaryIsDelegate(active: true);

        var result = await Sut().Handle(new GetCalendarEventsQuery(From, To, _ceo.Id), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        var item = Assert.Single(result.Value!.Events);
        Assert.Equal((_privateEvent.Id, true, true), (item.Id, item.IsPrivate, item.IsOrganizer));
    }

    [Fact]
    public async Task ANonDelegate_Is403_AndSeesNothing()
    {
        SecretaryIsDelegate(active: false);

        var result = await Sut().Handle(new GetCalendarEventsQuery(From, To, _ceo.Id), CancellationToken.None);

        Assert.Equal((403, MeetingErrorCodes.DelegateRequired), (result.StatusCode, result.ErrorCode));
        _events.Verify(e => e.GetInDateRangeForCallerAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<DateTimeOffset>(),
            It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AnExpiredDelegate_Is403()
    {
        // An expired grant is not active: the repository answers false, exactly as for a revoked one.
        SecretaryIsDelegate(active: false);

        var result = await Sut().Handle(new GetCalendarEventsQuery(From, To, _ceo.Id), CancellationToken.None);

        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task NoForEmployeeId_BehavesLikeToday()
    {
        var mine = await Sut().Handle(new GetCalendarEventsQuery(From, To), CancellationToken.None);
        var myself = await Sut().Handle(new GetCalendarEventsQuery(From, To, _secretary.Id), CancellationToken.None);

        Assert.True(mine.IsSuccess, mine.Error);
        Assert.True(myself.IsSuccess, myself.Error);
        Assert.Empty(mine.Value!.Events);
        _delegates.VerifyNoOtherCalls();
    }
}
