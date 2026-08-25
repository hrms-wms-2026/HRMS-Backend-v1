using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.SharedPlatform.Notifications.Entities;
using ONEVO.Infrastructure.ExternalServices.Messaging;
using ONEVO.Infrastructure.Identity.CurrentUser;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.SharedPlatform;
using ONEVO.Infrastructure.Persistence.Seeders;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Auth;

public sealed class NotificationTemplateSeederTests : IDisposable
{
    private readonly string _connectionString;
    private readonly SqliteConnection _masterConnection;
    private readonly TestClock _clock = new();

    public NotificationTemplateSeederTests()
    {
        var databaseName = $"notification_template_seeder_tests_{Guid.NewGuid():N}";
        _connectionString = $"Data Source={databaseName};Mode=Memory;Cache=Shared;Foreign Keys=False";
        _masterConnection = new SqliteConnection(_connectionString);
        _masterConnection.Open();

        using var schemaContext = CreateContext();
        schemaContext.Database.EnsureCreated();
    }

    public void Dispose() => _masterConnection.Dispose();

    [Fact]
    public async Task SeedAsync_SomeTemplatesAlreadyExist_StillAddsTheMissingOnes()
    {
        await using (var arrange = CreateContext())
        {
            arrange.NotificationTemplates.Add(new NotificationTemplate
            {
                Id = Guid.NewGuid(),
                Code = "work_task_creation_request_created",
                InAppTitleTemplate = "New task request",
                InAppBodyTemplate = "existing"
            });
            await arrange.SaveChangesAsync();
        }

        await using (var seedDb = CreateContext())
        {
            var services = new ServiceCollection();
            services.AddScoped(_ => seedDb);
            services.AddScoped<INotificationRepository>(_ => new EfNotificationRepository(seedDb));
            var sp = services.BuildServiceProvider();
            var seeder = new NotificationTemplateSeeder(sp, NullLogger<NotificationTemplateSeeder>.Instance);
            await seeder.StartAsync(CancellationToken.None);
        }

        await using var assert = CreateContext();
        var codes = await assert.NotificationTemplates.Select(t => t.Code).ToListAsync();

        Assert.Contains("work_task_creation_request_created", codes);
        Assert.Contains("work_task_edit_request_decided", codes);
        Assert.Contains("work_sprint_completed", codes);
        Assert.Contains("work_sprint_incomplete", codes);
                Assert.Contains("work_sprint_achieved", codes);
        Assert.Contains("attendance_correction_request_created", codes);
        Assert.Contains("attendance_correction_request_decided", codes);
        Assert.Contains("attendance_correction_request_cancelled", codes);
        Assert.Equal(11, codes.Count);

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
        public DateTimeOffset UtcNow { get; } = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);
        public DateOnly Today => DateOnly.FromDateTime(UtcNow.UtcDateTime);
    }
}
