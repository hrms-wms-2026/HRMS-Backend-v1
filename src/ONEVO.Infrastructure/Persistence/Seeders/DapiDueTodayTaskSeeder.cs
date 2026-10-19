using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using TaskStatusEntity = ONEVO.Domain.Features.WorkManagement.Tasks.Entities.TaskStatus;

namespace ONEVO.Infrastructure.Persistence.Seeders;

/// <summary>
/// Development/Test-only: gives the Dapi tenant owner and Kiru Balachandran 10 open tasks each,
/// due today, so the dashboard's My Tasks card (which only shows assigned tasks with a due date
/// on or before the lookahead cutoff - see GetMyActiveTasksAsync) isn't empty for a live demo.
/// Reuses an existing project/objective/category each employee (or, for the owner, the tenant)
/// already has rather than building a new work tree. Idempotent via deterministic Guids. Must run
/// after WorkManagementDapiDemoSeeder (projects/objectives/statuses/categories must exist).
/// </summary>
public sealed class DapiDueTodayTaskSeeder : IHostedService
{
    private static readonly Guid DapiTenantId = Guid.Parse("6b0874ab-71db-401f-859f-bdd50c1317fb");
    private static readonly Guid DapiOwnerUserId = Guid.Parse("cd49a0c2-e978-4055-b8be-7d46a3727e94");
    private const string KiruEmail = "kiru.balachandran@dapi.test";
    private const int TasksPerEmployee = 10;

    private readonly IServiceProvider _services;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<DapiDueTodayTaskSeeder> _logger;

    public DapiDueTodayTaskSeeder(
        IServiceProvider services,
        IHostEnvironment environment,
        ILogger<DapiDueTodayTaskSeeder> logger)
    {
        _services = services;
        _environment = environment;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Local `dotnet run` only - see DapiLeaveSampleSeeder for why Test/CI must never run this.
        if (!_environment.IsDevelopment())
            return;

        try
        {
            await using var scope = _services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var tenantContext = scope.ServiceProvider.GetRequiredService<IWritableTenantContext>();

            tenantContext.SetAdminMode();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            await SeedAsync(db, tenantContext, timeout.Token);
            await db.SaveChangesAsync(timeout.Token);
            _logger.LogInformation("Dapi due-today demo tasks seeded (owner + Kiru Balachandran).");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DapiDueTodayTaskSeeder failed. Due-today demo tasks were skipped; host continues.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    internal static async Task SeedAsync(
        ApplicationDbContext db,
        IWritableTenantContext tenantContext,
        CancellationToken ct)
    {
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == DapiTenantId, ct);
        if (tenant is null)
            return;

        tenantContext.SetAdminMode();
        tenantContext.Resolve(new TenantRegistryEntry(
            tenant.Id, tenant.Slug, tenant.Status, PlanCode: null));

        var now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(now.UtcDateTime);

        var owner = await db.Employees
            .FirstOrDefaultAsync(e => e.TenantId == DapiTenantId && e.UserId == DapiOwnerUserId, ct);
        var kiru = await db.Employees
            .FirstOrDefaultAsync(e => e.TenantId == DapiTenantId && e.Email == KiruEmail, ct);

        // Reuse a task Kiru is already assigned to as the template for his new ones - keeps his
        // 10 due-today tasks in the same project/objective he already works in.
        var kiruTemplate = kiru is null
            ? null
            : await (
                from a in db.TaskAssignments.AsNoTracking()
                join t in db.WorkTasks.AsNoTracking() on a.TaskId equals t.Id
                where a.EmployeeId == kiru.Id
                select t)
                .FirstOrDefaultAsync(ct);

        // The owner isn't assigned to any existing task, so fall back to the tenant's oldest task
        // for a valid project/objective/category to anchor the owner's new tasks to.
        var ownerTemplate = await db.WorkTasks.AsNoTracking()
            .Where(t => t.TenantId == DapiTenantId)
            .OrderBy(t => t.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (owner is not null && ownerTemplate is not null)
            await SeedTasksForEmployeeAsync(db, owner.Id, owner.UserId, "owner", ownerTemplate, today, now, ct);

        if (kiru is not null && (kiruTemplate ?? ownerTemplate) is { } kiruAnchor)
            await SeedTasksForEmployeeAsync(db, kiru.Id, kiru.UserId, "kiru", kiruAnchor, today, now, ct);
    }

    private static async Task SeedTasksForEmployeeAsync(
        ApplicationDbContext db,
        Guid employeeId,
        Guid userId,
        string personKey,
        WorkTask template,
        DateOnly today,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == template.ProjectId, ct);
        if (project is null)
            return;

        var statusId = await ResolveOpenStatusIdAsync(db, template, ct);
        if (statusId is null)
            return;

        var nextTaskNumber = project.NextTaskNumber < 1 ? 1L : project.NextTaskNumber;

        for (var i = 0; i < TasksPerEmployee; i++)
        {
            var taskId = WorkManagementDapiDemoSeeder.DeterministicGuid(
                $"dapi-demo-today:task:{personKey}:{i}");
            // IgnoreQueryFilters: a demo task soft-deleted from the app is still in the table, so
            // the default is_deleted filter would hide it and the re-insert would hit pk_tasks.
            if (db.WorkTasks.Local.Any(t => t.TenantId == DapiTenantId && t.Id == taskId)
                || await db.WorkTasks.IgnoreQueryFilters()
                    .AnyAsync(t => t.TenantId == DapiTenantId && t.Id == taskId, ct))
            {
                continue;
            }

            var shortId = $"{project.Identifier}-{nextTaskNumber}";
            nextTaskNumber++;

            db.WorkTasks.Add(new WorkTask
            {
                Id = taskId,
                TenantId = DapiTenantId,
                ProjectId = template.ProjectId,
                ObjectiveId = template.ObjectiveId,
                ShortId = shortId,
                Title = $"Due-today demo task {i + 1}",
                Description = "Seeded so the dashboard's My Tasks card has something due today for the demo.",
                CategoryId = template.CategoryId,
                StatusId = statusId.Value,
                Priority = i % 3 == 0 ? WorkTaskPriorities.High : WorkTaskPriorities.Medium,
                DueDate = today,
                EstimatedHours = 4m,
                CompletedHours = 0m,
                ProgressPercent = 0,
                StartedAt = null,
                CompletedAt = null,
                CreatedById = DapiOwnerUserId,
                CreatedAt = now
            });

            var assignmentId = WorkManagementDapiDemoSeeder.DeterministicGuid(
                $"dapi-demo-today:task-assignment:{personKey}:{i}");
            if (!db.TaskAssignments.Local.Any(a => a.Id == assignmentId)
                && !await db.TaskAssignments.AnyAsync(a => a.Id == assignmentId, ct))
            {
                db.TaskAssignments.Add(new TaskAssignment
                {
                    Id = assignmentId,
                    TaskId = taskId,
                    UserId = userId,
                    EmployeeId = employeeId,
                    AssignedById = DapiOwnerUserId,
                    AssignedAt = now
                });
            }
        }

        project.NextTaskNumber = nextTaskNumber;
    }

    /// <summary>An open (non-complete) status in the template task's project - matching its own
    /// objective scope first, then the project-wide template status set.</summary>
    private static async Task<Guid?> ResolveOpenStatusIdAsync(
        ApplicationDbContext db, WorkTask template, CancellationToken ct)
    {
        var status = await db.TaskStatuses.AsNoTracking()
            .Where(s => s.TenantId == DapiTenantId
                        && s.ProjectId == template.ProjectId
                        && (s.ObjectiveId == null || s.ObjectiveId == template.ObjectiveId)
                        && !s.MarksTaskComplete)
            .OrderBy(s => s.DisplayOrder)
            .FirstOrDefaultAsync(ct);
        return status?.Id;
    }
}
