using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Inbox.Contracts;
using ONEVO.Application.Features.Inbox.Services;
using ONEVO.Infrastructure.ExternalServices.Messaging;
using ONEVO.Infrastructure.Identity.CurrentUser;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.SharedPlatform;
using ONEVO.Infrastructure.Persistence.Seeders;
using ONEVO.Tests.Unit.Features.Auth;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Inbox;

/// <summary>The bell-navigation contract: every inbox template is a real seeded template and maps to an inbox ref.</summary>
public sealed class InboxNotificationNavigationTests : IDisposable
{
    private readonly string _connectionString;
    private readonly SqliteConnection _masterConnection;
    private readonly TestClock _clock = new();

    public InboxNotificationNavigationTests()
    {
        _connectionString = $"Data Source=inbox_nav_{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Foreign Keys=False";
        _masterConnection = new SqliteConnection(_connectionString);
        _masterConnection.Open();
        using var schemaContext = CreateContext();
        schemaContext.Database.EnsureCreated();
    }

    public void Dispose() => _masterConnection.Dispose();

    [Fact]
    public async Task Every_inbox_template_is_seeded()
    {
        await using (var seedDb = CreateContext())
        {
            var services = new ServiceCollection();
            services.AddScoped(_ => seedDb);
            services.AddScoped<INotificationRepository>(_ => new EfNotificationRepository(seedDb));
            await new NotificationTemplateSeeder(services.BuildServiceProvider(), NullLogger<NotificationTemplateSeeder>.Instance)
                .StartAsync(CancellationToken.None);
        }

        await using var read = CreateContext();
        var codes = await read.NotificationTemplates.Select(t => t.Code).ToListAsync();

        codes.Should().Contain(InboxNotificationNavigation.ByTemplate.Keys);
    }

    [Fact]
    public void Templates_map_to_the_inbox_refs_the_modules_use()
    {
        var known = new[]
        {
            InboxRefs.LeaveRequest(Guid.Empty), InboxRefs.AttendanceCorrection(Guid.Empty), InboxRefs.AttendanceWorkModeChange(Guid.Empty),
            InboxRefs.AttendanceLocationChange(Guid.Empty), InboxRefs.AttendanceDeviceChange(Guid.Empty), InboxRefs.AttendanceAlert(Guid.Empty),
            InboxRefs.ExceptionCase(Guid.Empty), InboxRefs.CalendarMeetingException(Guid.Empty),
        }.Select(r => (r.Source, r.Kind)).ToHashSet();

        InboxNotificationNavigation.ByTemplate.Values.Should().OnlyContain(target => known.Contains(target));
        InboxNotificationNavigation.RelatedEntityTypeFor("leave_request_approved").Should().Be("inbox:leave:request");
        InboxNotificationNavigation.RelatedEntityTypeFor("exception_alert_escalated").Should().Be("inbox:monitoring:exception_case");
        InboxNotificationNavigation.RelatedEntityTypeFor("work_sprint_completed").Should().BeNull();
    }

    private ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        return new SqliteTestApplicationDbContext(
            options,
            new AuditableEntityInterceptor(new AnonymousCurrentUser(), _clock),
            new SoftDeleteInterceptor(_clock),
            new DomainEventDispatchInterceptor(new NoOpPublisher()),
            new TenantContextAccessor());
    }

    private sealed class TestClock : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 10, 14, 0, 0, 0, TimeSpan.Zero);
        public DateOnly Today => DateOnly.FromDateTime(UtcNow.UtcDateTime);
    }
}
