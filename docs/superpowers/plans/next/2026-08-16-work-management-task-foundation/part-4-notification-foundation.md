# Work Management — Task Foundation, Part 4: Notification Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the shared `notification_templates`/`notifications` tables, give `INotificationDispatcher` its first real implementation, seed Work Management's four templates, and expose the notifications API + call sites from Parts 2-3's request flows.

**Architecture:** New `ONEVO.Domain/Features/SharedPlatform/Notifications/` module (placed alongside the existing `Outbox` module, same `SharedPlatform` area) — generic, first consumer is Work Management. `INotificationDispatcher` already exists as an unimplemented interface (`src/ONEVO.Application/Common/ServiceInterfaces/INotificationDispatcher.cs`) — this plan gives it a body.

**Tech Stack:** Same as Parts 1-3, plus a startup seeder following the existing `LookupDataSeeder` convention.

**Spec:** `docs/superpowers/specs/next/2026-08-16-work-management-task-foundation-design.md` §6.

## Global Constraints

- Mail channel is schema-ready but **not wired** in this slice (`mail_enabled = false` seed default) — do not call `IOutboxWriter` from this plan's dispatcher implementation.
- `notifications.recipient_user_id` is **UserId**, not EmployeeId — spec §6.2 explicitly carries forward the Phase 2 migration's audit/notification-fields-stay-UserId boundary.
- `notification_templates` is **not tenant-scoped** — no `tenant_id` column, no RLS policy needed for that table (it's product copy, like other global lookup tables such as `version_statuses` — Part 1 Task 1's reference reads already showed that pattern).
- Read `src/ONEVO.Application/Common/ServiceInterfaces/INotificationDispatcher.cs` in full before Task 3 — its existing method signatures (`SendToUserAsync`/`SendToTenantAsync`/`SendToGroupAsync`) constrain what this plan can implement without changing the interface; if its parameters don't already accommodate `(templateCode, placeholderValues, relatedEntityType, relatedEntityId)`, this plan must extend the interface (additive only — do not remove or rename the existing three methods, since they may be referenced elsewhere even though currently unimplemented).

---

### Task 1: `NotificationTemplate` and `Notification` entities, configurations, repository, migration

**Files:**
- Create: `src/ONEVO.Domain/Features/SharedPlatform/Notifications/Entities/NotificationTemplate.cs`
- Create: `src/ONEVO.Domain/Features/SharedPlatform/Notifications/Entities/Notification.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Configurations/SharedPlatform/Notifications/NotificationTemplateConfiguration.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Configurations/SharedPlatform/Notifications/NotificationConfiguration.cs`
- Create: `src/ONEVO.Application/Common/RepositoryInterfaces/INotificationRepository.cs` (placed in `Common`, not a Work Management folder, since this is shared infra — mirrors where `IOutboxWriter` lives)
- Create: `src/ONEVO.Infrastructure/Repositories/SharedPlatform/NotificationRepository.cs`
- Create: `src/ONEVO.Infrastructure/Migrations/<timestamp>_AddNotificationFoundation.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/SharedPlatform/Notifications/NotificationConfigurationTests.cs`

**Interfaces:**
- Produces: `NotificationTemplate` (Id, Code, InAppTitleTemplate, InAppBodyTemplate, MailSubjectTemplate?, MailBodyTemplate?, InAppEnabled, MailEnabled), `Notification` (Id, TenantId, RecipientUserId, TemplateCode, Title, Body, RelatedEntityType?, RelatedEntityId?, IsRead, ReadAt?, CreatedAt), `INotificationRepository.{AddAsync, GetByRecipientAsync, GetUnreadCountAsync, GetByIdForRecipientAsync, MarkReadAsync, MarkAllReadAsync}`.

- [ ] **Step 1: Write the failing test**

```csharp
using ONEVO.Domain.Features.SharedPlatform.Notifications.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.SharedPlatform.Notifications;

public class NotificationConfigurationTests
{
    [Fact]
    public void Notification_DefaultsToUnread()
    {
        var notification = new Notification
        {
            Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), RecipientUserId = Guid.NewGuid(),
            TemplateCode = "work_task_creation_request_created", Title = "t", Body = "b", CreatedAt = DateTimeOffset.UtcNow
        };

        Assert.False(notification.IsRead);
        Assert.Null(notification.ReadAt);
    }

    [Fact]
    public void NotificationTemplate_DefaultsToInAppEnabledMailDisabled()
    {
        var template = new NotificationTemplate
        {
            Id = Guid.NewGuid(), Code = "work_task_creation_request_created",
            InAppTitleTemplate = "New task request", InAppBodyTemplate = "{{requesterName}} requested a task."
        };

        Assert.True(template.InAppEnabled);
        Assert.False(template.MailEnabled);
    }
}
```

- [ ] **Step 2: Run test, verify FAIL.**

- [ ] **Step 3: Write the entities**

```csharp
// src/ONEVO.Domain/Features/SharedPlatform/Notifications/Entities/NotificationTemplate.cs
namespace ONEVO.Domain.Features.SharedPlatform.Notifications.Entities;

/// <summary>Global (not tenant-scoped) template - product copy, not tenant configuration. See
/// docs/superpowers/specs/next/2026-08-16-work-management-task-foundation-design.md §6.1.</summary>
public class NotificationTemplate
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string InAppTitleTemplate { get; set; } = string.Empty;
    public string InAppBodyTemplate { get; set; } = string.Empty;
    public string? MailSubjectTemplate { get; set; }
    public string? MailBodyTemplate { get; set; }
    public bool InAppEnabled { get; set; } = true;
    public bool MailEnabled { get; set; } = false;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
}
```

```csharp
// src/ONEVO.Domain/Features/SharedPlatform/Notifications/Entities/Notification.cs
namespace ONEVO.Domain.Features.SharedPlatform.Notifications.Entities;

/// <summary>Per-user in-app notification. RecipientUserId is UserId (not EmployeeId) - notification
/// fields stay UserId per the Phase 2 identity migration's scope boundary. See spec §6.2.</summary>
public class Notification
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid RecipientUserId { get; set; }
    public string TemplateCode { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string? RelatedEntityType { get; set; }
    public Guid? RelatedEntityId { get; set; }
    public bool IsRead { get; set; }
    public DateTimeOffset? ReadAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
```

- [ ] **Step 4: Write both EF configurations**

```csharp
// src/ONEVO.Infrastructure/Persistence/Configurations/SharedPlatform/Notifications/NotificationTemplateConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.SharedPlatform.Notifications.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.SharedPlatform.Notifications;

public class NotificationTemplateConfiguration : IEntityTypeConfiguration<NotificationTemplate>
{
    public void Configure(EntityTypeBuilder<NotificationTemplate> builder)
    {
        builder.ToTable("notification_templates");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Code).HasMaxLength(100).IsRequired();
        builder.Property(t => t.InAppTitleTemplate).HasMaxLength(255).IsRequired();
        builder.HasIndex(t => t.Code).IsUnique().HasDatabaseName("ix_notification_templates_one_per_code");
    }
}
```

```csharp
// src/ONEVO.Infrastructure/Persistence/Configurations/SharedPlatform/Notifications/NotificationConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.SharedPlatform.Notifications.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.SharedPlatform.Notifications;

public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("notifications");
        builder.HasKey(n => n.Id);
        builder.Property(n => n.TemplateCode).HasMaxLength(100).IsRequired();
        builder.Property(n => n.Title).HasMaxLength(255).IsRequired();
        builder.Property(n => n.RelatedEntityType).HasMaxLength(40);

        builder.HasIndex(n => new { n.TenantId, n.RecipientUserId, n.IsRead, n.CreatedAt })
            .HasDatabaseName("ix_notifications_tenant_id_recipient_user_id_is_read_created_at");
    }
}
```

- [ ] **Step 5: Repository interface + implementation**

```csharp
// src/ONEVO.Application/Common/RepositoryInterfaces/INotificationRepository.cs
using ONEVO.Domain.Features.SharedPlatform.Notifications.Entities;

namespace ONEVO.Application.Common.RepositoryInterfaces;

public interface INotificationRepository
{
    Task AddAsync(Notification notification, CancellationToken ct = default);
    Task<IReadOnlyList<Notification>> GetByRecipientAsync(Guid tenantId, Guid recipientUserId, bool unreadOnly, int page, int pageSize, CancellationToken ct = default);
    Task<int> GetUnreadCountAsync(Guid tenantId, Guid recipientUserId, CancellationToken ct = default);
    Task<Notification?> GetTrackedByIdForRecipientAsync(Guid tenantId, Guid id, Guid recipientUserId, CancellationToken ct = default);
    Task MarkAllReadAsync(Guid tenantId, Guid recipientUserId, CancellationToken ct = default);
    Task<NotificationTemplate?> GetTemplateByCodeAsync(string code, CancellationToken ct = default);
    Task AddTemplateRangeAsync(IReadOnlyList<NotificationTemplate> templates, CancellationToken ct = default);
    Task<bool> AnyTemplatesExistAsync(CancellationToken ct = default);
}
```

```csharp
// src/ONEVO.Infrastructure/Repositories/SharedPlatform/NotificationRepository.cs
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Domain.Features.SharedPlatform.Notifications.Entities;
using ONEVO.Infrastructure.Persistence;

namespace ONEVO.Infrastructure.Repositories.SharedPlatform;

public class NotificationRepository : INotificationRepository
{
    private readonly ApplicationDbContext _db;

    public NotificationRepository(ApplicationDbContext db) => _db = db;

    public async Task AddAsync(Notification notification, CancellationToken ct = default)
        => await _db.Set<Notification>().AddAsync(notification, ct);

    public async Task<IReadOnlyList<Notification>> GetByRecipientAsync(Guid tenantId, Guid recipientUserId, bool unreadOnly, int page, int pageSize, CancellationToken ct = default)
    {
        var query = _db.Set<Notification>().AsNoTracking()
            .Where(n => n.TenantId == tenantId && n.RecipientUserId == recipientUserId);
        if (unreadOnly) query = query.Where(n => !n.IsRead);
        return await query.OrderByDescending(n => n.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
    }

    public async Task<int> GetUnreadCountAsync(Guid tenantId, Guid recipientUserId, CancellationToken ct = default)
        => await _db.Set<Notification>().CountAsync(n => n.TenantId == tenantId && n.RecipientUserId == recipientUserId && !n.IsRead, ct);

    public async Task<Notification?> GetTrackedByIdForRecipientAsync(Guid tenantId, Guid id, Guid recipientUserId, CancellationToken ct = default)
        => await _db.Set<Notification>().FirstOrDefaultAsync(n => n.TenantId == tenantId && n.Id == id && n.RecipientUserId == recipientUserId, ct);

    public async Task MarkAllReadAsync(Guid tenantId, Guid recipientUserId, CancellationToken ct = default)
        => await _db.Set<Notification>()
            .Where(n => n.TenantId == tenantId && n.RecipientUserId == recipientUserId && !n.IsRead)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(n => n.IsRead, true)
                .SetProperty(n => n.ReadAt, DateTimeOffset.UtcNow), ct);

    public async Task<NotificationTemplate?> GetTemplateByCodeAsync(string code, CancellationToken ct = default)
        => await _db.Set<NotificationTemplate>().AsNoTracking().FirstOrDefaultAsync(t => t.Code == code, ct);

    public async Task AddTemplateRangeAsync(IReadOnlyList<NotificationTemplate> templates, CancellationToken ct = default)
        => await _db.Set<NotificationTemplate>().AddRangeAsync(templates, ct);

    public async Task<bool> AnyTemplatesExistAsync(CancellationToken ct = default)
        => await _db.Set<NotificationTemplate>().AnyAsync(ct);
}
```

- [ ] **Step 6: Register in DI:** `services.AddScoped<INotificationRepository, NotificationRepository>();`

- [ ] **Step 7: Generate migration** (`dotnet ef migrations add AddNotificationFoundation --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api`), then append RLS for `notifications` only (has `tenant_id`) — **not** for `notification_templates` (global, no `tenant_id` column, per Global Constraints). Same RLS SQL block pattern as every prior migration in this plan series.

- [ ] **Step 8: Apply migration, verify RLS via `pg_policies` for `notifications` only, run `TenantIsolationArchitectureTests`, verify PASS.**

- [ ] **Step 9: Run unit test, verify PASS. Step 10: Commit.**

```bash
git add src/ONEVO.Domain/Features/SharedPlatform/Notifications/ src/ONEVO.Infrastructure/Persistence/Configurations/SharedPlatform/Notifications/ src/ONEVO.Application/Common/RepositoryInterfaces/INotificationRepository.cs src/ONEVO.Infrastructure/Repositories/SharedPlatform/NotificationRepository.cs src/ONEVO.Infrastructure/Migrations/ src/ONEVO.Infrastructure/DependencyInjection.cs tests/ONEVO.Tests.Unit/Features/SharedPlatform/Notifications/NotificationConfigurationTests.cs
git commit -m "feat(shared): NotificationTemplate/Notification entities, configs, repository, migration"
```

### Task 2: `NotificationTemplateSeeder`

**Files:**
- Create: `src/ONEVO.Infrastructure/Persistence/Seeders/NotificationTemplateSeeder.cs`
- Modify: wherever `LookupDataSeeder` is invoked at startup (search `Program.cs`/`DependencyInjection.cs` for `LookupDataSeeder` to find the exact hook point)

**Interfaces:**
- Produces: seeds exactly 4 rows into `notification_templates` if none exist yet (idempotent, matching `LookupDataSeeder`'s own idempotency pattern — read that file first for the exact idempotency check style, e.g. `if (await context.Set<T>().AnyAsync()) return;`).

- [ ] **Step 1: Read `LookupDataSeeder` (or equivalent) in full to match its exact class shape, DI lifetime, and startup-invocation convention.**

- [ ] **Step 2: Write the seeder**

```csharp
// src/ONEVO.Infrastructure/Persistence/Seeders/NotificationTemplateSeeder.cs
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Domain.Features.SharedPlatform.Notifications.Entities;

namespace ONEVO.Infrastructure.Persistence.Seeders;

public class NotificationTemplateSeeder
{
    private readonly INotificationRepository _notifications;

    public NotificationTemplateSeeder(INotificationRepository notifications) => _notifications = notifications;

    public async Task SeedAsync(CancellationToken ct = default)
    {
        if (await _notifications.AnyTemplatesExistAsync(ct))
            return;

        var templates = new List<NotificationTemplate>
        {
            new()
            {
                Id = Guid.NewGuid(), Code = "work_task_creation_request_created",
                InAppTitleTemplate = "New task request", InAppBodyTemplate = "{{requesterName}} requested a new task \"{{taskTitle}}\" on {{objectiveName}}."
            },
            new()
            {
                Id = Guid.NewGuid(), Code = "work_task_creation_request_decided",
                InAppTitleTemplate = "Task request {{decision}}", InAppBodyTemplate = "Your task request \"{{taskTitle}}\" on {{objectiveName}} was {{decision}}."
            },
            new()
            {
                Id = Guid.NewGuid(), Code = "work_allocation_extend_request_created",
                InAppTitleTemplate = "Allocation extension requested", InAppBodyTemplate = "{{requesterName}} requested {{requestedHours}} more hours for {{objectiveName}}."
            },
            new()
            {
                Id = Guid.NewGuid(), Code = "work_allocation_extend_request_decided",
                InAppTitleTemplate = "Allocation request {{decision}}", InAppBodyTemplate = "Your allocation extension request for {{objectiveName}} was {{decision}}."
            }
        };

        await _notifications.AddTemplateRangeAsync(templates, ct);
        // NOTE: uses whatever SaveChanges call LookupDataSeeder itself uses at its call site
        // (likely a direct DbContext.SaveChangesAsync since this runs at startup, outside a
        // request-scoped IUnitOfWork) - match that exact pattern once Step 1's read confirms it.
    }
}
```

- [ ] **Step 3: Wire `NotificationTemplateSeeder.SeedAsync()` into the same startup hook where `LookupDataSeeder` runs, registered in DI alongside it.**

- [ ] **Step 4: Manual verification** (seeders are typically integration-tested via the dev/smoke seed path in this codebase, not unit tests — confirm by checking whether `LookupDataSeeder` has a dedicated unit test; if not, this task matches that convention and needs no new unit test, only the manual step below).

Run: start the API locally against a fresh dev database, confirm via `SELECT code FROM notification_templates;` that all 4 codes exist, then restart the API a second time and confirm the count is still exactly 4 (idempotency).

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Infrastructure/Persistence/Seeders/NotificationTemplateSeeder.cs src/ONEVO.Api/Program.cs
git commit -m "feat(shared): seed the four Work Management notification templates"
```

### Task 3: `INotificationDispatcher` real implementation (template rendering + in-app write)

**Files:**
- Create: `src/ONEVO.Infrastructure/Services/SharedPlatform/Notifications/NotificationDispatcher.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/SharedPlatform/Notifications/NotificationDispatcherTests.cs`

**Interfaces:**
- Consumes: `INotificationRepository` (Task 1).
- Produces: `NotificationDispatcher : INotificationDispatcher` — first real implementation. Placeholder rendering uses simple `{{key}}` string replacement (no templating library dependency needed for this simple case).

- [ ] **Step 1: Read `INotificationDispatcher.cs` in full (Global Constraints already flags this). If its `SendToUserAsync` signature does not already accept a template code + placeholder dictionary + related-entity info, extend it additively:**

```csharp
// src/ONEVO.Application/Common/ServiceInterfaces/INotificationDispatcher.cs — additive change only
public interface INotificationDispatcher
{
    // existing methods unchanged...

    /// <summary>Renders `templateCode`'s in-app template against `placeholders` and writes a
    /// Notification row for `recipientUserId` if the template's InAppEnabled is true. No-op for
    /// the mail half in this slice (MailEnabled defaults false - see spec §6.4).</summary>
    Task SendTemplatedAsync(
        Guid tenantId, Guid recipientUserId, string templateCode,
        IReadOnlyDictionary<string, string> placeholders,
        string? relatedEntityType = null, Guid? relatedEntityId = null,
        CancellationToken ct = default);
}
```

- [ ] **Step 2: Write the failing test**

```csharp
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Domain.Features.SharedPlatform.Notifications.Entities;
using ONEVO.Infrastructure.Services.SharedPlatform.Notifications;
using Xunit;

namespace ONEVO.Tests.Unit.Features.SharedPlatform.Notifications;

public class NotificationDispatcherTests
{
    [Fact]
    public async Task SendTemplatedAsync_RendersPlaceholdersAndWritesNotification()
    {
        var tenantId = Guid.NewGuid();
        var recipientUserId = Guid.NewGuid();

        var repo = new Mock<INotificationRepository>();
        repo.Setup(x => x.GetTemplateByCodeAsync("work_task_creation_request_created", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NotificationTemplate
            {
                Id = Guid.NewGuid(), Code = "work_task_creation_request_created",
                InAppTitleTemplate = "New task request",
                InAppBodyTemplate = "{{requesterName}} requested \"{{taskTitle}}\".",
                InAppEnabled = true, MailEnabled = false
            });

        var dispatcher = new NotificationDispatcher(repo.Object);
        await dispatcher.SendTemplatedAsync(
            tenantId, recipientUserId, "work_task_creation_request_created",
            new Dictionary<string, string> { ["requesterName"] = "Priya", ["taskTitle"] = "Build the thing" },
            "task_creation_request", Guid.NewGuid());

        repo.Verify(x => x.AddAsync(
            It.Is<Notification>(n => n.Body == "Priya requested \"Build the thing\"." && n.TenantId == tenantId && n.RecipientUserId == recipientUserId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendTemplatedAsync_TemplateInAppDisabled_DoesNotWriteNotification()
    {
        var repo = new Mock<INotificationRepository>();
        repo.Setup(x => x.GetTemplateByCodeAsync("some_code", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NotificationTemplate { Id = Guid.NewGuid(), Code = "some_code", InAppTitleTemplate = "t", InAppBodyTemplate = "b", InAppEnabled = false });

        var dispatcher = new NotificationDispatcher(repo.Object);
        await dispatcher.SendTemplatedAsync(Guid.NewGuid(), Guid.NewGuid(), "some_code", new Dictionary<string, string>());

        repo.Verify(x => x.AddAsync(It.IsAny<Notification>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
```

- [ ] **Step 3: Run test, verify FAIL.**

- [ ] **Step 4: Write the implementation**

```csharp
// src/ONEVO.Infrastructure/Services/SharedPlatform/Notifications/NotificationDispatcher.cs
using System.Text;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.SharedPlatform.Notifications.Entities;

namespace ONEVO.Infrastructure.Services.SharedPlatform.Notifications;

public class NotificationDispatcher : INotificationDispatcher
{
    private readonly INotificationRepository _notifications;

    public NotificationDispatcher(INotificationRepository notifications) => _notifications = notifications;

    public async Task SendTemplatedAsync(
        Guid tenantId, Guid recipientUserId, string templateCode,
        IReadOnlyDictionary<string, string> placeholders,
        string? relatedEntityType = null, Guid? relatedEntityId = null,
        CancellationToken ct = default)
    {
        var template = await _notifications.GetTemplateByCodeAsync(templateCode, ct);
        if (template is null || !template.InAppEnabled)
            return;

        var notification = new Notification
        {
            Id = Guid.NewGuid(), TenantId = tenantId, RecipientUserId = recipientUserId,
            TemplateCode = templateCode, Title = Render(template.InAppTitleTemplate, placeholders),
            Body = Render(template.InAppBodyTemplate, placeholders),
            RelatedEntityType = relatedEntityType, RelatedEntityId = relatedEntityId,
            IsRead = false, CreatedAt = DateTimeOffset.UtcNow
        };

        await _notifications.AddAsync(notification, ct);
        // Mail half intentionally omitted: template.MailEnabled is false by default in this
        // slice (spec §6.4) - wiring IOutboxWriter here is explicitly deferred, not forgotten.
    }

    private static string Render(string template, IReadOnlyDictionary<string, string> placeholders)
    {
        var sb = new StringBuilder(template);
        foreach (var (key, value) in placeholders)
            sb.Replace($"{{{{{key}}}}}", value);
        return sb.ToString();
    }

    // Existing SendToUserAsync/SendToTenantAsync/SendToGroupAsync from the pre-existing interface
    // remain unimplemented placeholders (throw NotImplementedException) unless another in-flight
    // change already implements them - check before assuming this class is the sole implementer.
}
```

- [ ] **Step 5: Register in DI:** `services.AddScoped<INotificationDispatcher, NotificationDispatcher>();` — note this may already have a registration line pointing at a missing/placeholder type; search for `INotificationDispatcher` in `DependencyInjection.cs` first and replace rather than duplicate if one exists.

- [ ] **Step 6: Run tests, verify PASS. Step 7: Commit.**

```bash
git add src/ONEVO.Application/Common/ServiceInterfaces/INotificationDispatcher.cs src/ONEVO.Infrastructure/Services/SharedPlatform/Notifications/ src/ONEVO.Infrastructure/DependencyInjection.cs tests/ONEVO.Tests.Unit/Features/SharedPlatform/Notifications/NotificationDispatcherTests.cs
git commit -m "feat(shared): real INotificationDispatcher implementation - template rendering + in-app write"
```

### Task 4: Wire call sites in Parts 2-3's handlers

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/CreateTaskCreationRequest/CreateTaskCreationRequestCommandHandler.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/ApproveTaskCreationRequest/ApproveTaskCreationRequestCommandHandler.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/RejectTaskCreationRequest/RejectTaskCreationRequestCommandHandler.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/ObjectiveChangeRequests/Commands/RequestAllocationExtension/RequestAllocationExtensionCommandHandler.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/ObjectiveChangeRequests/Commands/ApproveObjectiveChangeRequest/ApproveObjectiveChangeRequestCommandHandler.cs` (extend_allocation branch only)

**Interfaces:** consumes `INotificationDispatcher.SendTemplatedAsync` (Task 3). Recipient is always resolved to a **UserId** — every one of these handlers currently has the recipient as an `EmployeeId` (Objective owner or reporting manager), so each call site needs one extra lookup: `IMilestoneMembershipCoordinator.GetActiveAssigneeAsync(...).UserId` (already used elsewhere in these same files for other purposes — reuse, don't add a second lookup mechanism).

- [ ] **Step 1: In `CreateTaskCreationRequestCommandHandler` (Part 2 Task 2), after `await _requests.AddAsync(...)` inside the transaction, add:**

```csharp
var ownerAssignee = await _membership.GetActiveAssigneeAsync(tenantId, objective.OwnerId, innerCt);
if (ownerAssignee is not null)
{
    await _notificationDispatcher.SendTemplatedAsync(
        tenantId, ownerAssignee.UserId, "work_task_creation_request_created",
        new Dictionary<string, string> { ["requesterName"] = requesterDisplayName, ["taskTitle"] = payload.Title, ["objectiveName"] = objective.Title },
        "task_creation_request", entity.Id, innerCt);
}
```

(`requesterDisplayName` needs a name lookup — reuse `ICallerIdentityResolver.ResolveDisplayNamesByEmployeeIdAsync` for the single requester id, same as other handlers resolve display names.) Inject `INotificationDispatcher` into the constructor.

- [ ] **Step 2: In `ApproveTaskCreationRequestCommandHandler` and `RejectTaskCreationRequestCommandHandler` (Part 2 Task 3), after the status update, notify the **requester** (not the owner) with `work_task_creation_request_decided`, placeholder `decision` = `"approved"` or `"rejected"`.**

- [ ] **Step 3: In `RequestAllocationExtensionCommandHandler` (Part 3 Task 2), after `AddAsync`, notify the **reporting manager** with `work_allocation_extend_request_created`.**

- [ ] **Step 4: In `ApproveObjectiveChangeRequestCommandHandler`'s `extend_allocation` case (Part 3 Task 3), after the slack check passes and `AllocatedHours` is updated, notify the **original requester** with `work_allocation_extend_request_decided`, `decision = "approved"`. In the `Reject` handler's existing generic path (already handles all request types uniformly — confirm this by reading `RejectObjectiveChangeRequestCommandHandler.cs`), add the same notification call gated to only fire when `RequestType == ExtendAllocation` (other request types don't get this notification in this slice — spec §6 call-site list is `extend_allocation` and `task_creation_requests` only).**

- [ ] **Step 5: Update each modified handler's existing unit tests to mock the new `INotificationDispatcher` dependency (Moq default no-op is sufficient — these tests don't need to assert notification content, since Task 3's `NotificationDispatcherTests` already covers rendering correctness; each handler test only needs `Times.Once`/`Times.Never` verification that the call happened under the right condition, added as one new assertion per existing happy-path test, not new test methods).**

- [ ] **Step 6: Run the full Work Management + SharedPlatform Notifications test suites, verify PASS.**

Run: `dotnet test --filter "FullyQualifiedName~WorkManagement|FullyQualifiedName~Notifications"`

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/ src/ONEVO.Application/Features/WorkManagement/ObjectiveChangeRequests/Commands/ tests/
git commit -m "feat(work): wire notification dispatch into task-creation-request and extend-allocation flows"
```

### Task 5: Notifications API + Controller

**Files:**
- Create: `src/ONEVO.Application/Features/SharedPlatform/Notifications/Queries/GetMyNotifications/{GetMyNotificationsQuery,GetMyNotificationsQueryHandler}.cs`
- Create: `src/ONEVO.Application/Features/SharedPlatform/Notifications/Queries/GetUnreadCount/{GetUnreadCountQuery,GetUnreadCountQueryHandler}.cs`
- Create: `src/ONEVO.Application/Features/SharedPlatform/Notifications/Commands/MarkNotificationRead/{MarkNotificationReadCommand,MarkNotificationReadCommandHandler}.cs`
- Create: `src/ONEVO.Application/Features/SharedPlatform/Notifications/Commands/MarkAllNotificationsRead/{MarkAllNotificationsReadCommand,MarkAllNotificationsReadCommandHandler}.cs`
- Create: `src/ONEVO.Api/Controllers/Tenant/SharedPlatform/NotificationsController.cs`
- Create: `docs/postman-request/Notifications/Get My Notifications.md`, `Get Unread Count.md`, `Mark Notification Read.md`, `Mark All Notifications Read.md` (new top-level `docs/postman-request/Notifications/` module folder, not under `Work Management/`, since this is shared infra per spec §6.5)

**Interfaces:**
- Produces: the four query/command handlers (straightforward `ICurrentUser` + `INotificationRepository` calls, following the exact pattern of Part 1 Task 7's `GetObjectiveTasksQueryHandler` for the two queries and Part 1 Task 8's `MoveTaskStatusCommandHandler` for the two commands — no new pattern to introduce), plus `NotificationsController` at route `api/v1/notifications` (spec §6.5 — deliberately not under `api/v1/work/`).

- [ ] **Step 1: Write all four handlers + tests, mirroring the cited reference patterns exactly.**

- [ ] **Step 2: Write the controller**

```csharp
// src/ONEVO.Api/Controllers/Tenant/SharedPlatform/NotificationsController.cs
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Application.Features.SharedPlatform.Notifications.Commands.MarkAllNotificationsRead;
using ONEVO.Application.Features.SharedPlatform.Notifications.Commands.MarkNotificationRead;
using ONEVO.Application.Features.SharedPlatform.Notifications.Queries.GetMyNotifications;
using ONEVO.Application.Features.SharedPlatform.Notifications.Queries.GetUnreadCount;

namespace ONEVO.Api.Controllers.Tenant.SharedPlatform;

[ApiController]
[Route("api/v1/notifications")]
[Authorize(Policy = "TenantPolicy")]
public class NotificationsController : ControllerBase
{
    private readonly IMediator _mediator;

    public NotificationsController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<IActionResult> GetMine([FromQuery] bool unreadOnly = false, [FromQuery] int page = 1, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetMyNotificationsQuery(unreadOnly, page), ct);
        return result.IsSuccess ? Ok(result.Value!.Select(n => n.ToViewModel()).ToList()) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpGet("unread-count")]
    public async Task<IActionResult> UnreadCount(CancellationToken ct)
    {
        var result = await _mediator.Send(new GetUnreadCountQuery(), ct);
        return result.IsSuccess ? Ok(new { count = result.Value }) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new MarkNotificationReadCommand(id), ct);
        return result.IsSuccess ? NoContent() : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllRead(CancellationToken ct)
    {
        await _mediator.Send(new MarkAllNotificationsReadCommand(), ct);
        return NoContent();
    }
}
```

- [ ] **Step 3: Write View Models + Contracts mappers, Postman docs, update the top-level Postman README to list the new `Notifications/` module folder alongside `Work Management/`.**

- [ ] **Step 4: Run full test suite, verify PASS. Step 5: Commit.**

```bash
git add src/ONEVO.Application/Features/SharedPlatform/Notifications/ src/ONEVO.Api/Controllers/Tenant/SharedPlatform/NotificationsController.cs src/ONEVO.Api/Contracts/SharedPlatform/ docs/postman-request/Notifications/ docs/postman-request/README.md tests/
git commit -m "feat(shared): notifications API - list, unread count, mark read/all-read"
```

## Part 4 complete
