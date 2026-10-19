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
        Assert.Contains("leave_request_approved", codes);
        Assert.Contains("leave_request_rejected", codes);
        Assert.Contains("leave_request_next_approval_required", codes);
        Assert.Contains("leave_request_cancelled_by_employee", codes);
        Assert.Contains("leave_request_cancelled_by_hr", codes);
        Assert.Contains("leave_request_partially_cancelled", codes);
        Assert.Contains("leave_request_decision_undone", codes);
        Assert.Contains("face_verification_override", codes);
        Assert.Contains("work_task_status_change_request_created", codes);
        Assert.Contains("work_task_status_change_request_decided", codes);
        Assert.Contains("work_activity_recorded", codes);
        Assert.Contains("work_approval_requested", codes);
        Assert.Contains("work_approval_decided", codes);
        Assert.Contains("work_monitor_alert", codes);
        Assert.Contains("exception_alert_detected", codes);
        Assert.Contains("exception_alert_escalated", codes);
        Assert.Contains("conversation_member_added", codes);
        Assert.Contains("conversation_mentioned", codes);
        Assert.Equal(56, codes.Count);
        var expectedCodes = new[]
        {
            "work_task_creation_request_created",
            "work_task_creation_request_decided",
            "work_task_edit_request_decided",
            "work_task_status_change_request_created",
            "work_task_status_change_request_decided",
            "work_allocation_extend_request_created",
            "work_allocation_extend_request_decided",
            "work_objective_edit_request_created",
            "work_objective_edit_request_decided",
            "work_sprint_completed",
            "work_sprint_incomplete",
            "work_sprint_achieved",
            "work_activity_recorded",
            "work_approval_requested",
            "work_approval_decided",
            "work_monitor_alert",
            "conversation_member_added",
            "conversation_mentioned",
            "leave_request_approved",
            "leave_request_rejected",
            "leave_request_next_approval_required",
            "leave_request_cancelled_by_employee",
            "leave_request_cancelled_by_hr",
            "leave_request_partially_cancelled",
            "face_verification_override",
            "exception_alert_detected",
            "exception_alert_escalated",
            "attendance_correction_request_created",
            "attendance_correction_request_decided",
            "attendance_correction_request_cancelled",
            "work_area_change_request_created",
            "work_area_change_request_decided",
            "work_area_change_request_cancelled",
            "work_project_member_invited",
            "work_objective_invitation_created",
            "work_project_member_accepted",
            "calendar_event_participant_added",
            "calendar_event_updated",
            "calendar_event_cancelled",
            "calendar_event_resolution_requested",
            "calendar_event_replacement_nominated",
            "calendar_event_rsvp_accepted",
            "calendar_event_rsvp_rejected",
            "calendar_event_replacement_nominee",
            "calendar_event_participant_removed",
            "calendar_event_meeting_link_removed",
            "calendar_event_meeting_link_failed",
            "calendar_connection_reauth_required",
            "calendar_connection_failed",
            "calendar_connection_sync_conflict",
            "calendar_event_reminder",
            "calendar_exception_detected",
            "calendar_exception_raised_for_you",
            "calendar_exception_escalated",
            "work_monitor_alert_escalated",
            "leave_request_decision_undone"
        };

        Assert.Equal(
            expectedCodes.OrderBy(x => x),
            codes.OrderBy(x => x));

        Assert.Equal(codes.Count, codes.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task SeedAsync_NewTemplates_GetAPublishedActiveFirstVersion()
    {
        await RunSeederAsync();

        await using var assert = CreateContext();
        var template = await assert.NotificationTemplates.SingleAsync(t => t.Code == "work_approval_requested");
        var versions = await assert.NotificationTemplateVersions.Where(v => v.TemplateId == template.Id).ToListAsync();

        var version = Assert.Single(versions);
        Assert.Equal(NotificationTemplateVersionStatus.Published, version.Status);
        Assert.Equal(version.Id, template.ActiveVersionId);
        Assert.Equal("work_approval_requested", template.EventKey);
        Assert.Equal(template.InAppBodyTemplate, version.InAppBody);
    }

    [Fact]
    public async Task SeedAsync_Rerun_LeavesEditedDraftsAndPublishedVersionsUnchanged()
    {
        await RunSeederAsync();

        Guid templateId;
        await using (var edit = CreateContext())
        {
            var template = await edit.NotificationTemplates.SingleAsync(t => t.Code == "work_approval_requested");
            templateId = template.Id;
            var published = NotificationTemplateVersion.CreateDraft(template.Id, 2, Content("Admin wording"), null, _clock.UtcNow);
            published.Publish(null, _clock.UtcNow);
            edit.NotificationTemplateVersions.Add(published);
            template.Activate(published, _clock.UtcNow);
            edit.NotificationTemplateVersions.Add(
                NotificationTemplateVersion.CreateDraft(template.Id, 3, Content("Draft in progress"), null, _clock.UtcNow));
            await edit.SaveChangesAsync();
        }

        var before = await SnapshotAsync(templateId);
        await RunSeederAsync();
        var after = await SnapshotAsync(templateId);

        Assert.Equal(before, after);
        await using var assert = CreateContext();
        Assert.Equal("Admin wording", (await assert.NotificationTemplates.SingleAsync(t => t.Id == templateId)).InAppTitleTemplate);
    }

    [Fact]
    public async Task SeedAsync_PreVersioningTemplate_IsFrozenAsV1WithoutChangingItsCopy()
    {
        await using (var arrange = CreateContext())
        {
            arrange.NotificationTemplates.Add(new NotificationTemplate
            {
                Id = Guid.NewGuid(), Code = "work_sprint_completed",
                InAppTitleTemplate = "Edited title", InAppBodyTemplate = "Edited body"
            });
            await arrange.SaveChangesAsync();
        }

        await RunSeederAsync();

        await using var assert = CreateContext();
        var template = await assert.NotificationTemplates.SingleAsync(t => t.Code == "work_sprint_completed");
        var version = await assert.NotificationTemplateVersions.SingleAsync(v => v.TemplateId == template.Id);
        Assert.Equal("Edited body", template.InAppBodyTemplate);
        Assert.Equal("Edited body", version.InAppBody);
        Assert.Equal(version.Id, template.ActiveVersionId);
    }

    private static NotificationTemplateContent Content(string title) =>
        new(title, "{{actorName}} body", null, null, null, null, null, null, "open_related");

    private async Task RunSeederAsync()
    {
        await using var seedDb = CreateContext();
        var services = new ServiceCollection();
        services.AddScoped(_ => seedDb);
        services.AddScoped<INotificationRepository>(_ => new EfNotificationRepository(seedDb));
        var sp = services.BuildServiceProvider();
        await new NotificationTemplateSeeder(sp, NullLogger<NotificationTemplateSeeder>.Instance).StartAsync(CancellationToken.None);
    }

    private async Task<string> SnapshotAsync(Guid templateId)
    {
        await using var db = CreateContext();
        var t = await db.NotificationTemplates.AsNoTracking().SingleAsync(x => x.Id == templateId);
        var versions = await db.NotificationTemplateVersions.AsNoTracking()
            .Where(v => v.TemplateId == templateId).OrderBy(v => v.Version).ToListAsync();
        return System.Text.Json.JsonSerializer.Serialize(new { t.InAppTitleTemplate, t.InAppBodyTemplate, t.ActiveVersionId, t.UpdatedAt, versions });
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
