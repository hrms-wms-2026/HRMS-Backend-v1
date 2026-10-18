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

    /// <summary>Product-default notification copy. Seeding inserts only codes that are missing, so an
    /// administrator's edits are never overwritten on restart.</summary>
    public static IReadOnlyList<NotificationTemplate> DefaultTemplates()
    {
        return new List<NotificationTemplate>
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
                Id = Guid.NewGuid(), Code = "work_task_status_change_request_created",
                InAppTitleTemplate = "Task status change requested",
                InAppBodyTemplate = "{{requesterName}} requested changes to the task statuses of {{projectName}}."
            },
            new()
            {
                Id = Guid.NewGuid(), Code = "work_task_status_change_request_decided",
                InAppTitleTemplate = "Task status change {{decision}}",
                InAppBodyTemplate = "Your task status change request for {{projectName}} was {{decision}}."
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
                Id = Guid.NewGuid(), Code = "work_objective_edit_request_created",
                InAppTitleTemplate = "Milestone edit requested",
                InAppBodyTemplate = "{{requesterName}} requested edits to {{objectiveName}}."
            },
            new()
            {
                Id = Guid.NewGuid(), Code = "work_objective_edit_request_decided",
                InAppTitleTemplate = "Milestone edit {{decision}}",
                InAppBodyTemplate = "Your edit request for {{objectiveName}} was {{decision}}."
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
                Id = Guid.NewGuid(), Code = "work_activity_recorded",
                InAppTitleTemplate = "Work update",
                InAppBodyTemplate = "{{actorName}} {{actionLabel}} \"{{targetTitle}}\"."
            },
            new()
            {
                Id = Guid.NewGuid(), Code = "work_approval_requested",
                InAppTitleTemplate = "Approval needed",
                InAppBodyTemplate = "{{actorName}} is waiting for your approval: {{actionLabel}} \"{{targetTitle}}\"."
            },
            new()
            {
                Id = Guid.NewGuid(), Code = "work_approval_decided",
                InAppTitleTemplate = "Request {{decision}}",
                InAppBodyTemplate = "{{actorName}}'s request ({{actionLabel}} \"{{targetTitle}}\") was {{decision}}."
            },
            new()
            {
                Id = Guid.NewGuid(), Code = "work_monitor_alert",
                InAppTitleTemplate = "Project alert",
                InAppBodyTemplate = "{{actionLabel}}: \"{{targetTitle}}\"."
            },
            new()
            {
                Id = Guid.NewGuid(), Code = "conversation_member_added",
                InAppTitleTemplate = "Added to a conversation",
                InAppBodyTemplate = "{{actorName}} added you to \"{{itemTitle}}\"."
            },
            new()
            {
                Id = Guid.NewGuid(), Code = "conversation_mentioned",
                InAppTitleTemplate = "You were mentioned",
                InAppBodyTemplate = "{{actorName}} mentioned you in \"{{itemTitle}}\"."
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
                Id = Guid.NewGuid(), Code = "face_verification_override",
                InAppTitleTemplate = "Face verification needs review",
                InAppBodyTemplate = "{{employeeName}} could not pass face verification {{attempts}} times and was allowed to {{action}} at {{time}} UTC. Reasons: {{reasons}}. Please review the photo."
            },
            new()
            {
                Id = Guid.NewGuid(), Code = "exception_alert_detected",
                InAppTitleTemplate = "{{title}}: {{employeeName}}",
                InAppBodyTemplate = "{{description}} Please review and acknowledge, resolve or escalate to HR."
            },
            new()
            {
                Id = Guid.NewGuid(), Code = "exception_alert_escalated",
                InAppTitleTemplate = "Escalated to HR: {{title}} - {{employeeName}}",
                InAppBodyTemplate = "{{description}} This case was escalated to HR for review."
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
                Id = Guid.NewGuid(), Code = "work_area_change_request_created",
                InAppTitleTemplate = "Work-area change requested",
                InAppBodyTemplate = "{{employeeName}} requested {{requestedWorkArea}} work for {{date}}."
            },
            new()
            {
                Id = Guid.NewGuid(), Code = "work_area_change_request_decided",
                InAppTitleTemplate = "Work-area change {{decision}}",
                InAppBodyTemplate = "Your work-area change request for {{date}} was {{decision}}. {{reviewComment}}"
            },
            new()
            {
                Id = Guid.NewGuid(), Code = "work_area_change_request_cancelled",
                InAppTitleTemplate = "Work-area change cancelled",
                InAppBodyTemplate = "{{employeeName}} cancelled the work-area change request for {{date}}."
            },

            new()
            {
                Id = Guid.NewGuid(), Code = "work_project_member_invited",
                InAppTitleTemplate = "You've been added to a project",
                InAppBodyTemplate = "{{inviterName}} invited you to join {{projectName}}."
            },
            new()
            {
                Id = Guid.NewGuid(), Code = "work_objective_invitation_created",
                InAppTitleTemplate = "You've been invited to a milestone",
                InAppBodyTemplate = "{{inviterName}} invited you to join {{objectiveName}} as {{inviteType}}."
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
            },
            new()
            {
                Id = Guid.NewGuid(), Code = "calendar_event_resolution_requested",
                InAppTitleTemplate = "Conflict resolution requested",
                InAppBodyTemplate = "{{responderName}} requested help resolving a conflict for \"{{eventTitle}}\": {{reason}}"
            },
            new()
            {
                Id = Guid.NewGuid(), Code = "calendar_event_replacement_nominated",
                InAppTitleTemplate = "Replacement nominated",
                InAppBodyTemplate = "{{responderName}} nominated {{nomineeName}} as a replacement for \"{{eventTitle}}\"."
            },
            new()
            {
                Id = Guid.NewGuid(), Code = "calendar_event_rsvp_accepted",
                InAppTitleTemplate = "Invitation accepted",
                InAppBodyTemplate = "{{responderName}} accepted \"{{eventTitle}}\"."
            },
            new()
            {
                Id = Guid.NewGuid(), Code = "calendar_event_rsvp_rejected",
                InAppTitleTemplate = "Invitation declined",
                InAppBodyTemplate = "{{responderName}} declined \"{{eventTitle}}\"."
            },
            new()
            {
                Id = Guid.NewGuid(), Code = "calendar_event_replacement_nominee",
                InAppTitleTemplate = "You were nominated as a replacement",
                InAppBodyTemplate = "{{responderName}} nominated you to attend \"{{eventTitle}}\" in their place."
            },
            new()
            {
                Id = Guid.NewGuid(), Code = "calendar_event_participant_removed",
                InAppTitleTemplate = "Removed from an event",
                InAppBodyTemplate = "{{organizerName}} removed you from \"{{eventTitle}}\"."
            },
            new()
            {
                Id = Guid.NewGuid(), Code = "calendar_event_meeting_link_removed",
                InAppTitleTemplate = "Meeting link removed",
                InAppBodyTemplate = "{{organizerName}} removed the meeting link from \"{{eventTitle}}\"."
            },
            new()
            {
                Id = Guid.NewGuid(), Code = "calendar_event_meeting_link_failed",
                InAppTitleTemplate = "Meeting link could not be created",
                InAppBodyTemplate = "The {{provider}} link for \"{{eventTitle}}\" could not be created. Invitations were sent without it - add the link again from the event."
            },
            new()
            {
                Id = Guid.NewGuid(), Code = "calendar_connection_reauth_required",
                InAppTitleTemplate = "Reconnect your calendar",
                InAppBodyTemplate = "Your {{provider}} connection needs you to sign in again before it can sync."
            },
            new()
            {
                Id = Guid.NewGuid(), Code = "calendar_connection_failed",
                InAppTitleTemplate = "Calendar sync failed",
                InAppBodyTemplate = "Your {{provider}} calendar could not be synced. Check the connection in your calendar settings."
            },
            new()
            {
                Id = Guid.NewGuid(), Code = "calendar_connection_sync_conflict",
                InAppTitleTemplate = "Calendar sync conflict",
                InAppBodyTemplate = "Some events in your {{provider}} calendar conflicted with ONEVO and were not synced."
            },
            new()
            {
                Id = Guid.NewGuid(), Code = "calendar_event_reminder",
                InAppTitleTemplate = "Starting soon",
                InAppBodyTemplate = "\"{{eventTitle}}\" starts at {{eventTime}}."
            },
            new()
            {
                Id = Guid.NewGuid(), Code = "calendar_exception_detected",
                InAppTitleTemplate = "Meeting exception to review",
                InAppBodyTemplate = "{{employeeName}}: {{title}}. {{description}}"
            },
            new()
            {
                Id = Guid.NewGuid(), Code = "calendar_exception_raised_for_you",
                InAppTitleTemplate = "A meeting exception was raised for you",
                InAppBodyTemplate = "{{title}}. {{description}} You can add your explanation in the Inbox."
            },
            new()
            {
                Id = Guid.NewGuid(), Code = "calendar_exception_escalated",
                InAppTitleTemplate = "Meeting exception escalated",
                InAppBodyTemplate = "{{title}} was not reviewed in time and needs your attention. {{description}}"
            },
            new()
            {
                Id = Guid.NewGuid(), Code = "work_monitor_alert_escalated",
                InAppTitleTemplate = "Project alert escalated to you",
                InAppBodyTemplate = "{{title}}: {{message}}"
            },
            new()
            {
                Id = Guid.NewGuid(), Code = "leave_request_decision_undone",
                InAppTitleTemplate = "Leave decision undone",
                InAppBodyTemplate = "The {{decision}} of your {{leaveType}} from {{startDate}} was undone. Your request is pending again."
            }

        };
    }

    private async Task SeedAsync(INotificationRepository notifications, ApplicationDbContext db, CancellationToken ct)
    {
        var templates = DefaultTemplates();

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
