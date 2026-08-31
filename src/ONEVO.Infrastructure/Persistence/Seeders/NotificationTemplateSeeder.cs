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
                Id = Guid.NewGuid(), Code = "work_task_edit_request_decided",
                InAppTitleTemplate = "Task edit request {{decision}}",
                InAppBodyTemplate = "Your edit request for \"{{taskTitle}}\" on {{objectiveName}} was {{decision}}."
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
            },
            new()
            {
                Id = Guid.NewGuid(), Code = "work_sprint_completed",
                InAppTitleTemplate = "Sprint completed",
                InAppBodyTemplate = "\"{{sprintName}}\" on {{objectiveName}} was marked Complete."
            },
            new()
            {
                Id = Guid.NewGuid(), Code = "work_sprint_incomplete",
                InAppTitleTemplate = "Sprint ended incomplete",
                InAppBodyTemplate = "\"{{sprintName}}\" on {{objectiveName}} ended with unfinished tasks and is now Incomplete."
            },
                        new()
            {
                Id = Guid.NewGuid(), Code = "work_sprint_achieved",
                InAppTitleTemplate = "Sprint achieved",
                InAppBodyTemplate = "\"{{sprintName}}\" on {{objectiveName}} was achieved and its tasks are now frozen."
            },
            new()
            {
                Id = Guid.NewGuid(), Code = "leave_request_approved",
                InAppTitleTemplate = "Leave approved",
                InAppBodyTemplate = "{{leaveTypeName}} from {{startDate}} to {{endDate}} was approved."
            },
            new()
            {
                Id = Guid.NewGuid(), Code = "leave_request_rejected",
                InAppTitleTemplate = "Leave rejected",
                InAppBodyTemplate = "{{leaveTypeName}} from {{startDate}} to {{endDate}} was rejected. {{reason}}"
            },
            new()
            {
                Id = Guid.NewGuid(), Code = "leave_request_information_requested",
                InAppTitleTemplate = "More information requested",
                InAppBodyTemplate = "{{approverName}} requested more information for {{leaveTypeName}} from {{startDate}} to {{endDate}}."
            },
            new()
            {
                Id = Guid.NewGuid(), Code = "leave_request_next_approval_required",
                InAppTitleTemplate = "Leave approval required",
                InAppBodyTemplate = "{{employeeName}} requested {{leaveTypeName}} from {{startDate}} to {{endDate}}."
            },
            new()
            {
                Id = Guid.NewGuid(), Code = "leave_request_cancelled_by_employee",
                InAppTitleTemplate = "Leave cancelled",
                InAppBodyTemplate = "{{employeeName}} cancelled {{leaveTypeName}} from {{startDate}} to {{endDate}}."
            },
            new()
            {
                Id = Guid.NewGuid(), Code = "leave_request_cancelled_by_hr",
                InAppTitleTemplate = "Leave cancelled by HR",
                InAppBodyTemplate = "{{cancelledByName}} cancelled your {{leaveTypeName}} from {{startDate}} to {{endDate}}. {{reason}}"
            },
            new()
            {
                Id = Guid.NewGuid(), Code = "leave_request_partially_cancelled",
                InAppTitleTemplate = "Leave partially cancelled",
                InAppBodyTemplate = "{{leaveTypeName}} from {{effectiveDate}} to {{endDate}} was cancelled. {{restoredDays}} days restored."
            },
            new()
            {
                Id = Guid.NewGuid(), Code = "attendance_correction_request_created",
                InAppTitleTemplate = "Attendance correction request",
                InAppBodyTemplate = "Attendance correction request from {{employeeName}}."
            },
            new()
            {
                Id = Guid.NewGuid(), Code = "attendance_correction_request_decided",
                InAppTitleTemplate = "Attendance correction {{decision}}",
                InAppBodyTemplate = "Your attendance correction request was {{decision}}."
            },
            new()
            {
                Id = Guid.NewGuid(), Code = "attendance_correction_request_cancelled",
                InAppTitleTemplate = "Attendance correction cancelled",
                InAppBodyTemplate = "Your attendance correction request was cancelled."
            },
            new()
            {
                Id = Guid.NewGuid(), Code = "work_project_member_invited",
                InAppTitleTemplate = "You've been added to a project",
                InAppBodyTemplate = "{{inviterName}} invited you to join {{projectName}}."
            },
            new()
            {
                Id = Guid.NewGuid(), Code = "work_project_member_accepted",
                InAppTitleTemplate = "Invitation accepted",
                InAppBodyTemplate = "{{accepterName}} accepted your invitation to join {{projectName}}."
            },
            new()
            {
                Id = Guid.NewGuid(), Code = "calendar_event_participant_added",
                InAppTitleTemplate = "Added to an event",
                InAppBodyTemplate = "{{organizerName}} added you to \"{{eventTitle}}\" on {{eventDate}}."
            },
            new()
            {
                Id = Guid.NewGuid(), Code = "calendar_event_updated",
                InAppTitleTemplate = "Event updated",
                InAppBodyTemplate = "{{organizerName}} updated \"{{eventTitle}}\"."
            },
            new()
            {
                Id = Guid.NewGuid(), Code = "calendar_event_cancelled",
                InAppTitleTemplate = "Event cancelled",
                InAppBodyTemplate = "{{organizerName}} cancelled \"{{eventTitle}}\"."
            }

        };

        var addedCount = 0;
        foreach (var template in templates)
        {
            var existing = await notifications.GetTemplateByCodeAsync(template.Code, ct);
            if (existing is not null)
                continue;

            await notifications.AddTemplateRangeAsync(new[] { template }, ct);
            addedCount++;
        }

        if (addedCount > 0)
        {
            await db.SaveChangesAsync(ct);
            _logger.LogInformation("Seeded {Count} new notification templates.", addedCount);
        }
        else
        {
            _logger.LogInformation("Notification templates already up to date — nothing to seed.");
        }
    }
}
