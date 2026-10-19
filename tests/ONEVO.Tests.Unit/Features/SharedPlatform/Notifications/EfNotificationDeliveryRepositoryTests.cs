using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.SharedPlatform.Notifications.Entities;
using ONEVO.Infrastructure.ExternalServices.Messaging;
using ONEVO.Infrastructure.Identity.CurrentUser;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.SharedPlatform;
using ONEVO.Tests.Unit.Features.Auth;
using Xunit;

namespace ONEVO.Tests.Unit.Features.SharedPlatform.Notifications;

/// <summary>Real EF queries (SQLite) for the tray-facing delivery repository methods.</summary>
public sealed class EfNotificationDeliveryRepositoryTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 15, 0, 0, TimeSpan.Zero);
    private readonly string _connectionString;
    private readonly SqliteConnection _keepAlive;
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _user = Guid.NewGuid();

    public EfNotificationDeliveryRepositoryTests()
    {
        _connectionString = $"Data Source=delivery_repo_{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Foreign Keys=False";
        _keepAlive = new SqliteConnection(_connectionString);
        _keepAlive.Open();
        using var db = CreateContext();
        db.Database.EnsureCreated();
    }

    public void Dispose() => _keepAlive.Dispose();

    private ApplicationDbContext CreateContext()
    {
        var clock = new FixedClock();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connectionString).UseSnakeCaseNamingConvention().Options;
        return new SqliteTestApplicationDbContext(options,
            new AuditableEntityInterceptor(new AnonymousCurrentUser(), clock), new SoftDeleteInterceptor(clock),
            new DomainEventDispatchInterceptor(new NoOpPublisher()), new TenantContextAccessor());
    }

    private NotificationDelivery Delivery(DateTimeOffset expiresAt, string status = NotificationDeliveryStatus.Pending,
        NotificationChannels channel = NotificationChannels.Tray, Guid? user = null) => new()
    {
        Id = Guid.NewGuid(), TenantId = _tenant, EventId = Guid.NewGuid(), EventKey = "work_approval_requested",
        RecipientUserId = user ?? _user, Channel = channel, Title = "t", Body = "b", Status = status,
        CreatedAt = Now.AddMinutes(-10), ExpiresAt = expiresAt
    };

    [Fact]
    public async Task ExpirePendingTray_ExpiresOnlyUncollectedTrayDeliveries()
    {
        var expired = Delivery(Now.AddMinutes(-1));
        var live = Delivery(Now.AddHours(1));
        var accepted = Delivery(Now.AddMinutes(-1), NotificationDeliveryStatus.ClientAccepted);
        var email = Delivery(Now.AddMinutes(-1), channel: NotificationChannels.Email);
        await using (var arrange = CreateContext())
        {
            arrange.NotificationDeliveries.AddRange(expired, live, accepted, email);
            await arrange.SaveChangesAsync();
        }

        int count;
        await using (var act = CreateContext())
            count = await new EfNotificationDeliveryRepository(act).ExpirePendingTrayAsync(Now);

        await using var assert = CreateContext();
        var statuses = await assert.NotificationDeliveries.ToDictionaryAsync(d => d.Id, d => d.Status);
        count.Should().Be(1);
        statuses[expired.Id].Should().Be(NotificationDeliveryStatus.Expired);
        statuses[live.Id].Should().Be(NotificationDeliveryStatus.Pending);
        statuses[accepted.Id].Should().Be(NotificationDeliveryStatus.ClientAccepted);
        statuses[email.Id].Should().Be(NotificationDeliveryStatus.Pending);
    }

    [Fact]
    public async Task ListPendingTray_ReturnsUnexpiredPendingTrayRowsForTheUser_OldestFirst()
    {
        var older = Delivery(Now.AddHours(1));
        older.CreatedAt = Now.AddMinutes(-30);
        var newer = Delivery(Now.AddHours(1));
        await using (var arrange = CreateContext())
        {
            arrange.NotificationDeliveries.AddRange(newer, older,
                Delivery(Now.AddMinutes(-1)), Delivery(Now.AddHours(1), user: Guid.NewGuid()),
                Delivery(Now.AddHours(1), channel: NotificationChannels.InApp));
            await arrange.SaveChangesAsync();
        }

        await using var db = CreateContext();
        var pending = await new EfNotificationDeliveryRepository(db).ListPendingTrayAsync(_tenant, _user, Now, 50);

        pending.Select(d => d.Id).Should().Equal(older.Id, newer.Id);
    }

    private sealed class FixedClock : IDateTimeProvider
    {
        public DateTimeOffset UtcNow => Now;
        public DateOnly Today => DateOnly.FromDateTime(Now.UtcDateTime);
    }
}
