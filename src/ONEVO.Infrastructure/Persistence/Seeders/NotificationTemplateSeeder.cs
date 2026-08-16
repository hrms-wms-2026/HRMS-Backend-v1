using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Domain.Features.SharedPlatform.Notifications.Entities;
using ONEVO.Infrastructure.Persistence;

namespace ONEVO.Infrastructure.Persistence.Seeders;

public class NotificationTemplateSeeder : IHostedService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<NotificationTemplateSeeder> _logger;

    public NotificationTemplateSeeder(IServiceProvider services, ILogger<NotificationTemplateSeeder> logger)
    {
        _services = services;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = _services.CreateAsyncScope();
            var notifications = scope.ServiceProvider.GetRequiredService<INotificationRepository>();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await SeedAsync(notifications, db, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Notification template seeder could not run because the database or migrated schema is unavailable.");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task SeedAsync(INotificationRepository notifications, ApplicationDbContext db, CancellationToken ct)
    {
        if (await notifications.AnyTemplatesExistAsync(ct))
        {
            _logger.LogInformation("Notification templates already seeded — skipping.");
            return;
        }

        var templates = new List<NotificationTemplate>
        {
            new()
            {
                Id = Guid.NewGuid(), Code = "work_task_creation_request_created",
                InAppTitleTemplate = "New task request",
                InAppBodyTemplate = "{{requesterName}} requested a new task \"{{taskTitle}}\" on {{objectiveName}}."
            },
            new()
            {
                Id = Guid.NewGuid(), Code = "work_task_creation_request_decided",
                InAppTitleTemplate = "Task request {{decision}}",
                InAppBodyTemplate = "Your task request \"{{taskTitle}}\" on {{objectiveName}} was {{decision}}."
            },
            new()
            {
                Id = Guid.NewGuid(), Code = "work_allocation_extend_request_created",
                InAppTitleTemplate = "Allocation extension requested",
                InAppBodyTemplate = "{{requesterName}} requested {{requestedHours}} more hours for {{objectiveName}}."
            },
            new()
            {
                Id = Guid.NewGuid(), Code = "work_allocation_extend_request_decided",
                InAppTitleTemplate = "Allocation request {{decision}}",
                InAppBodyTemplate = "Your allocation extension request for {{objectiveName}} was {{decision}}."
            }
        };

        await notifications.AddTemplateRangeAsync(templates, ct);
        await db.SaveChangesAsync(ct);
        _logger.LogInformation("Seeded {Count} notification templates.", templates.Count);
    }
}
