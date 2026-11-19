using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Services.Calendar;
using ONEVO.Tests.Unit.Fakes;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class CalendarEventReminderJobTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Every_active_tenant_gets_its_due_reminders_and_one_failing_tenant_does_not_stop_the_rest()
    {
        var failing = Guid.NewGuid();
        var healthy = Guid.NewGuid();
        var tenants = new Mock<ITenantRepository>();
        tenants.Setup(t => t.ListAsync(TenantStatus.Active, null, 0, int.MaxValue, It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            new Tenant { Id = failing, Name = "A", Slug = "a", Status = TenantStatus.Active },
            new Tenant { Id = healthy, Name = "B", Slug = "b", Status = TenantStatus.Active },
        ]);
        var events = new Mock<ICalendarEventRepository>();
        events.Setup(e => e.ListWithParticipantsStartingBetweenAsync(failing, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("bad row"));
        var evt = new CalendarEvent { Id = Guid.NewGuid(), TenantId = healthy, Title = "Standup", StartDate = Now.AddMinutes(10) };
        events.Setup(e => e.ListWithParticipantsStartingBetweenAsync(healthy, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([evt]);
        events.Setup(e => e.ListRecurringMastersWithParticipantsAsync(healthy, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var person = new Employee { Id = Guid.NewGuid(), TenantId = healthy, UserId = Guid.NewGuid() };
        events.Setup(e => e.GetParticipantsForEventsAsync(healthy, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<CalendarEventParticipant>>
            {
                [evt.Id] = [new CalendarEventParticipant { Id = Guid.NewGuid(), TenantId = healthy, EventId = evt.Id, EmployeeId = person.Id }],
            });
        events.Setup(e => e.ListSentRemindersAsync(healthy, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<(Guid, DateTimeOffset, Guid)>());
        var employees = new Mock<IEmployeeRepository>();
        employees.Setup(e => e.ListByIdsAsync(healthy, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, Employee> { [person.Id] = person });
        var notifications = new Mock<ICalendarNotificationSender>();
        var uow = new Mock<IUnitOfWork>();

        var services = new ServiceCollection();
        services.AddSingleton(tenants.Object);
        services.AddSingleton(Mock.Of<ITenantContextSwitcher>());
        services.AddSingleton<IWritableTenantContext>(new TenantContextAccessor());
        services.AddSingleton(new CalendarEventReminderService(events.Object, Mock.Of<ICalendarRecurrenceExpander>(), employees.Object, notifications.Object, uow.Object));
        services.AddSingleton(uow.Object);
        services.AddSingleton<IDateTimeProvider>(new FakeDateTimeProvider { UtcNow = Now });

        var sent = await new CalendarEventReminderJob(services.BuildServiceProvider(), NullLogger<CalendarEventReminderJob>.Instance).RunOnceAsync(default);

        sent.Should().Be(1);
        notifications.Verify(n => n.NotifyEventReminderAsync(healthy, person.UserId, "Standup", evt.StartDate, It.IsAny<CancellationToken>()), Times.Once);
        uow.Verify(u => u.ClearTracking(), Times.Once);
    }
}
