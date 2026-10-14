using Microsoft.EntityFrameworkCore;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Commands.CancelRecurringOccurrence;
using ONEVO.Application.Features.Calendar.Commands.EditRecurringOccurrence;
using ONEVO.Application.Features.Calendar.Commands.UpdateCalendarEvent;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.SharedPlatform.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Services.SharedPlatform.Outbox;
using CoreEmployeeRepository = ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository;

namespace ONEVO.Tests.Unit.Features.Calendar;

/// <summary>
/// Calendar P0-1 (plan C0): the participants' "updated" / "cancelled" notifications are outbox rows; they must be
/// staged before the command's last SaveChanges or they are never written. Real OutboxWriter + CalendarNotificationSender
/// over a DbContext, and the rows are read back from a fresh context (only what was saved counts).
/// </summary>
public sealed class CalendarCommandOutboxPersistenceTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 10, 9, 0, 0, TimeSpan.Zero);
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly string _database = Guid.NewGuid().ToString();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<ICalendarEventRepository> _events = new();
    private readonly Mock<CoreEmployeeRepository> _employees = new();
    private readonly Employee _participant;
    private readonly CalendarEvent _master;

    public CalendarCommandOutboxPersistenceTests()
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(x => x.TenantId).Returns(_tenantId);
        _currentUser.SetupGet(x => x.UserId).Returns(_userId);
        _participant = new Employee { Id = Guid.NewGuid(), TenantId = _tenantId, UserId = Guid.NewGuid(), FirstName = "Pat", LastName = "Doe" };
        _employees.Setup(x => x.GetByIdAsync(_tenantId, _participant.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_participant);
        _employees.Setup(x => x.GetDefaultForUserAsync(_tenantId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = Guid.NewGuid(), TenantId = _tenantId, UserId = _userId, FirstName = "Ada", LastName = "Owner" });
        _master = new CalendarEvent
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, CreatedById = _userId, Title = "Standup", StartDate = Start, EndDate = Start.AddMinutes(30),
            Recurrence = "weekly", RecurrenceRule = "FREQ=WEEKLY",
        };
        _events.Setup(x => x.GetTrackedByIdForTenantAsync(_tenantId, _master.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_master);
        _events.Setup(x => x.GetParticipantsForEventsAsync(_tenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<CalendarEventParticipant>>
            {
                [_master.Id] = [new CalendarEventParticipant { Id = Guid.NewGuid(), TenantId = _tenantId, EventId = _master.Id, EmployeeId = _participant.Id }],
            });
    }

    private ApplicationDbContext Db()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(_database).Options;
        var clock = new Mock<IDateTimeProvider>();
        return new ApplicationDbContext(options,
            new AuditableEntityInterceptor(new Mock<ICurrentUser>().Object, clock.Object),
            new SoftDeleteInterceptor(clock.Object),
            new DomainEventDispatchInterceptor(new Mock<MediatR.IPublisher>().Object),
            new Mock<ITenantContext>().Object);
    }

    /// <summary>A unit of work over the test DbContext: saves it, runs transactions inline.</summary>
    private sealed class DbUnitOfWork(ApplicationDbContext db) : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => db.SaveChangesAsync(cancellationToken);
        public Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default) => operation(cancellationToken);
        public void ClearTracking() => db.ChangeTracker.Clear();
    }

    private (CalendarNotificationSender Sender, IUnitOfWork UnitOfWork) Wiring(ApplicationDbContext db)
    {
        var encryption = new Mock<IEncryptionService>();
        encryption.Setup(e => e.Encrypt(It.IsAny<string>())).Returns<string>(s => s);
        var outbox = new OutboxWriter(db, encryption.Object, new Mock<IDateTimeProvider>().Object);
        return (new CalendarNotificationSender(outbox, _employees.Object), new DbUnitOfWork(db));
    }

    private int SavedOutboxRows()
    {
        using var fresh = Db();
        return fresh.Set<OutboxMessage>().Count();
    }

    [Fact]
    public async Task Update_persists_the_participants_updated_notification()
    {
        await using var db = Db();
        var (sender, uow) = Wiring(db);

        var result = await new UpdateCalendarEventCommandHandler(_currentUser.Object, _events.Object, _employees.Object, sender, uow).Handle(
            new UpdateCalendarEventCommand(_master.Id, "Standup v2", null, Start, Start.AddHours(1), false, null, null, null, "weekly"), default);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(1, SavedOutboxRows());
    }

    [Fact]
    public async Task Edit_all_occurrences_persists_the_participants_updated_notification()
    {
        await using var db = Db();
        var (sender, uow) = Wiring(db);

        var result = await new EditRecurringOccurrenceCommandHandler(_currentUser.Object, _events.Object, _employees.Object, sender, uow).Handle(
            new EditRecurringOccurrenceCommand(_master.Id, Start, RecurrenceEditScope.AllEvents, "Standup v2", null, Start, Start.AddHours(1), false, null, null, null),
            default);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(1, SavedOutboxRows());
    }

    [Fact]
    public async Task Cancel_occurrence_persists_the_participants_cancelled_notification()
    {
        await using var db = Db();
        var (sender, uow) = Wiring(db);

        var result = await new CancelRecurringOccurrenceCommandHandler(_currentUser.Object, _events.Object, _employees.Object, sender, uow).Handle(
            new CancelRecurringOccurrenceCommand(_master.Id, Start.AddDays(7)), default);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(1, SavedOutboxRows());
    }
}
