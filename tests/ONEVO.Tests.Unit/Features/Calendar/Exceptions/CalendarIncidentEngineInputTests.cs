using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Exceptions;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.Models;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.Services;
using ONEVO.Application.Features.Inbox.Escalation;
using ONEVO.Application.Features.Leave.Request.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Exceptions.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Meetings.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.Leave.Request.Entities;
using ONEVO.Tests.Unit.Fakes;
using Xunit;
using CoreEmployeeRepository = ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository;

namespace ONEVO.Tests.Unit.Features.Calendar.Exceptions;

/// <summary>Plan C2 security review: what the engine reads about an employee's meetings.</summary>
public sealed class CalendarIncidentEngineInputTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 18, 0, 0, TimeSpan.Zero);
    private readonly Employee _employee = new() { Id = Guid.NewGuid(), TenantId = TenantId, UserId = Guid.NewGuid(), Email = "pat@acme.com" };
    private readonly Mock<ICalendarEventRepository> _events = new();
    private readonly Mock<IMeetingComplianceReadRepository> _compliance = new();
    private readonly Mock<IMonitoringToggleResolver> _toggles = new();
    private readonly CalendarEvent _meeting;

    public CalendarIncidentEngineInputTests()
    {
        _meeting = new CalendarEvent
        {
            Id = Guid.NewGuid(), TenantId = TenantId, Title = "Doctor appointment", IsPrivate = true, CreatedById = Guid.NewGuid(),
            StartDate = Now.AddHours(-3), EndDate = Now.AddHours(-2),
        };
        _events.Setup(e => e.GetInDateRangeForEmployeeAsync(TenantId, _employee.Id, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([_meeting]);
        _events.Setup(e => e.GetRecurringMastersForEmployeeAsync(TenantId, _employee.Id, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _events.Setup(e => e.GetParticipantsForEventsAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<CalendarEventParticipant>>
            {
                [_meeting.Id] = [new CalendarEventParticipant { Id = Guid.NewGuid(), TenantId = TenantId, EventId = _meeting.Id, EmployeeId = _employee.Id, ResponseStatus = CalendarEventParticipantStatuses.Accepted }],
            });
        _events.Setup(e => e.ListRsvpHistoryAsync(TenantId, _employee.Id, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _events.Setup(e => e.ListHolidayDatesAsync(TenantId, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _compliance.Setup(c => c.ListAsync(TenantId, _employee.Id, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new MeetingApplicationComplianceItem(_meeting.Id, _meeting.Title, _meeting.StartDate, _meeting.EndDate, 1800, 0, 0m, false)]);
    }

    private CalendarIncidentEngine Engine()
    {
        var employees = new Mock<CoreEmployeeRepository>();
        employees.Setup(e => e.GetByIdAsync(TenantId, _employee.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_employee);
        var connections = new Mock<IExternalCalendarConnectionRepository>();
        connections.Setup(c => c.GetForUserAsync(TenantId, _employee.UserId, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var leaves = new Mock<ILeaveRequestReadRepository>();
        leaves.Setup(l => l.ListApprovedCoveringAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<LeaveRequest>());
        var cases = new CalendarCaseService(Mock.Of<ICalendarIncidentRepository>(), Mock.Of<IExceptionRepository>(), Mock.Of<IInboxEscalationPolicyRegistry>(),
            Mock.Of<INotificationDispatcher>(), employees.Object);
        return new CalendarIncidentEngine(_events.Object, Mock.Of<ICalendarEventMeetingRepository>(), Mock.Of<ICalendarEventMeetingAttendanceRepository>(),
            Mock.Of<ICalendarRecurrenceExpander>(), leaves.Object, Mock.Of<ILegalEntityRepository>(), employees.Object, connections.Object,
            _compliance.Object, Mock.Of<IEmployeeAuthorityResolverFactory>(), [], cases, new FakeDateTimeProvider { UtcNow = Now }, _toggles.Object);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task The_agent_estimate_is_only_used_when_activity_monitoring_is_on(bool monitoring, bool hasEstimate)
    {
        _toggles.Setup(t => t.IsEnabledForEmployeeAsync(TenantId, _employee.Id, MonitoringCapability.ActivityMonitoring, It.IsAny<CancellationToken>()))
            .ReturnsAsync(monitoring);

        var input = await Engine().BuildInputAsync(TenantId, _employee.Id, default);

        var view = input!.Meetings.Single();
        (view.AgentUsageRatio is not null).Should().Be(hasEstimate);
        if (!monitoring)
            _compliance.Verify(c => c.ListAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_private_meeting_is_evaluated_without_its_title()
    {
        var input = await Engine().BuildInputAsync(TenantId, _employee.Id, default);

        input!.Meetings.Single().Title.Should().Be("Private meeting");
    }
}
