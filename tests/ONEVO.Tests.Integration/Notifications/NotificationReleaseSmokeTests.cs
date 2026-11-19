using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ONEVO.Application;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.SharedPlatform.Notifications.Catalog;
using ONEVO.Application.Features.SharedPlatform.Notifications.RepositoryInterfaces;
using ONEVO.Domain.Features.SharedPlatform.Entities;
using ONEVO.Domain.Features.SharedPlatform.Notifications.Entities;
using ONEVO.Infrastructure;
using ONEVO.Infrastructure.Persistence;
using Xunit;

namespace ONEVO.Tests.Integration.Notifications;

/// <summary>Runs only when NOTIF_SMOKE=1 and a migrated database is supplied (see docs/notifications/rollout.md).</summary>
public sealed class NotificationSmokeFactAttribute : FactAttribute
{
    public NotificationSmokeFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("NOTIF_SMOKE") != "1")
            Skip = "Release smoke test: set NOTIF_SMOKE=1, ConnectionStrings__DefaultConnection (restricted app role), NOTIF_SMOKE_TENANT_ID and NOTIF_SMOKE_USER_ID against a migrated COPY of a database.";
    }
}

/// <summary>
/// Release verification against a real, migrated PostgreSQL copy, as the restricted runtime role (RLS enforced):
/// routing, in-transaction in-app rows, email/tray reservation, de-duplication, tenant settings and publishing.
/// Writes test notifications - never point it at a shared or live database.
/// </summary>
public sealed class NotificationReleaseSmokeTests
{
    private const string Event = NotificationEventCatalog.FirstSliceEventKey;

    private static IServiceProvider Build()
    {
        var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();
        services.AddInfrastructure(configuration);
        return services.BuildServiceProvider();
    }

    private static readonly Guid TenantId = Guid.TryParse(Environment.GetEnvironmentVariable("NOTIF_SMOKE_TENANT_ID"), out var t) ? t : Guid.Empty;
    private static readonly Guid UserId = Guid.TryParse(Environment.GetEnvironmentVariable("NOTIF_SMOKE_USER_ID"), out var u) ? u : Guid.Empty;

    /// <summary>A fresh scope switched into the tenant, the way request middleware and background jobs do.</summary>
    private static async Task<AsyncServiceScope> TenantScopeAsync(IServiceProvider root)
    {
        var scope = root.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        sp.GetRequiredService<IWritableTenantContext>().SetAdminMode();
        var tenant = await sp.GetRequiredService<ITenantRepository>().GetByIdAsync(TenantId);
        tenant.Should().NotBeNull("NOTIF_SMOKE_TENANT_ID must exist in the database");
        await sp.GetRequiredService<ITenantContextSwitcher>().SwitchToTenantAsync(
            new TenantRegistryEntry(tenant!.Id, tenant.Slug, tenant.Status, PlanCode: null));
        return scope;
    }

    private static async Task SendAsync(IServiceProvider root, Guid eventId, Guid relatedId)
    {
        await using var scope = await TenantScopeAsync(root);
        var sp = scope.ServiceProvider;
        await sp.GetRequiredService<INotificationDispatcher>().SendTemplatedEventAsync(
            eventId, TenantId, UserId, Event,
            new Dictionary<string, string> { ["actorName"] = "Smoke", ["actionLabel"] = "edited the task", ["targetTitle"] = "Release check", ["decision"] = "" },
            "work_approval_request", relatedId);
        await sp.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
    }

    private static async Task<List<NotificationDelivery>> DeliveriesAsync(IServiceProvider root, Guid eventId)
    {
        await using var scope = await TenantScopeAsync(root);
        return await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().NotificationDeliveries.AsNoTracking()
            .Where(d => d.EventId == eventId).ToListAsync();
    }

    private static async Task SetChannelsAsync(IServiceProvider root, NotificationChannels? channels)
    {
        await using var scope = await TenantScopeAsync(root);
        var sp = scope.ServiceProvider;
        var rules = sp.GetRequiredService<ITenantNotificationRuleRepository>();
        var existing = await rules.GetTrackedAsync(TenantId, Event);
        if (existing is not null)
            rules.Remove(existing);
        if (channels is { } c)
            await rules.AddAsync(new TenantNotificationRule
            {
                Id = Guid.NewGuid(), TenantId = TenantId, EventKey = Event, EnabledChannels = c, CreatedAt = DateTimeOffset.UtcNow
            });
        await sp.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
    }

    [NotificationSmokeFact]
    public async Task FirstSliceEvent_EndToEnd_AgainstAMigratedDatabase()
    {
        var root = Build();

        // 1. Tenant routes the event to all three channels.
        await SetChannelsAsync(root, NotificationChannels.InApp | NotificationChannels.Email | NotificationChannels.Tray);
        var eventId = Guid.NewGuid();
        var relatedId = Guid.NewGuid();
        await SendAsync(root, eventId, relatedId);

        var deliveries = await DeliveriesAsync(root, eventId);
        deliveries.Select(d => d.Channel).Should().BeEquivalentTo([NotificationChannels.InApp, NotificationChannels.Email, NotificationChannels.Tray]);
        var inApp = deliveries.Single(d => d.Channel == NotificationChannels.InApp);
        inApp.Status.Should().Be(NotificationDeliveryStatus.AvailableInInbox);
        deliveries.Single(d => d.Channel == NotificationChannels.Email).Status.Should().Be(NotificationDeliveryStatus.Pending);
        deliveries.Single(d => d.Channel == NotificationChannels.Tray).ExpiresAt.Should().NotBeNull();

        await using (var scope = await TenantScopeAsync(root))
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var row = await db.Notifications.AsNoTracking().SingleAsync(n => n.Id == inApp.NotificationId);
            row.RecipientUserId.Should().Be(UserId);
            row.TemplateCode.Should().Be(Event);
            row.RelatedEntityType.Should().Be("work_approval_request");
            row.RelatedEntityId.Should().Be(relatedId);
            row.Body.Should().Contain("Release check");

            // Tray: the device user's pending query sees it.
            var tray = await scope.ServiceProvider.GetRequiredService<INotificationDeliveryRepository>()
                .ListPendingTrayAsync(TenantId, UserId, DateTimeOffset.UtcNow, 50);
            tray.Should().Contain(d => d.EventId == eventId);
        }

        // 2. Same occurrence again: the unique delivery identity keeps one record per channel.
        await SendAsync(root, eventId, relatedId);
        (await DeliveriesAsync(root, eventId)).Should().HaveCount(3);

        // 3. Email/tray work was reserved in the outbox.
        await using (var scope = await TenantScopeAsync(root))
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var since = DateTimeOffset.UtcNow.AddMinutes(-5);
            var types = await db.Set<OutboxMessage>().AsNoTracking()
                .Where(m => m.TenantId == TenantId && m.CreatedAt >= since).Select(m => m.Type).ToListAsync();
            types.Should().Contain(OutboxMessageTypes.NotificationDelivery).And.Contain(OutboxMessageTypes.NotificationTrayPush);
        }

        // 4. Reset to defaults: the next occurrence is in-app only.
        await SetChannelsAsync(root, null);
        var defaultEvent = Guid.NewGuid();
        await SendAsync(root, defaultEvent, relatedId);
        (await DeliveriesAsync(root, defaultEvent)).Select(d => d.Channel).Should().Equal(NotificationChannels.InApp);

        // 5. Publishing new wording changes the next send, not earlier ones.
        await using (var scope = await TenantScopeAsync(root))
        {
            var sp = scope.ServiceProvider;
            var store = sp.GetRequiredService<INotificationTemplateStore>();
            var template = await store.GetDefaultForEventAsync(Event);
            var tracked = await store.GetTrackedAsync(template!.Id);
            var next = await store.GetLatestVersionNumberAsync(tracked!.Id) + 1;
            var draft = NotificationTemplateVersion.CreateDraft(tracked.Id, next,
                new NotificationTemplateContent("Smoke wording v" + next, "{{actorName}} smoke {{targetTitle}}", null, null, null, null, null, null, "open_related"),
                null, DateTimeOffset.UtcNow);
            draft.Publish(null, DateTimeOffset.UtcNow);
            await store.AddVersionAsync(draft);
            tracked.Activate(draft, DateTimeOffset.UtcNow);
            await sp.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }
        var afterPublish = Guid.NewGuid();
        await SendAsync(root, afterPublish, relatedId);
        (await DeliveriesAsync(root, afterPublish)).Single().Title.Should().StartWith("Smoke wording v");
        inApp.Title.Should().NotStartWith("Smoke wording v");
    }
}
