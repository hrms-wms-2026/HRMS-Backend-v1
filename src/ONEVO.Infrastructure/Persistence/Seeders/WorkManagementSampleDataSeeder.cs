using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.ProjectMembers.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using ONEVO.Domain.Features.WorkManagement.ReleaseCalendar.Entities;
using ONEVO.Domain.Features.WorkManagement.Versions.Entities;

namespace ONEVO.Infrastructure.Persistence.Seeders;

/// <summary>
/// Development-only Work Management sample data: 5 Project Categories per tenant, and — for
/// every active user in every active tenant who has an Employee record — 2 sample Projects
/// (each with its own Default Objective/creator membership/Default Version/release reminder,
/// matching CreateProjectCommandHandler's atomic shape) plus 2 Milestones (sub-Objectives) per
/// project. Runs after DevSmokeTestTenantSeeder and generically covers every tenant/user in the
/// database, not just the hardcoded Acme/Dapi smoke tenants, so it also reaches accounts created
/// through real signup. Idempotent: re-running never creates more than the target counts.
/// </summary>
public sealed class WorkManagementSampleDataSeeder : IHostedService
{
    private const int TargetCategoriesPerTenant = 5;
    private const int TargetProjectsPerUser = 2;
    private const int TargetMilestonesPerProject = 2;
    private const string SampleIdentifierPrefix = "SMK";

    // WorkManagementDapiDemoSeeder is now the sole source of Work Management data for this
    // tenant (5 hand-designed projects, 22 named employees) - without this guard, every one of
    // those 22 employees would also pick up 2 generic "SMK..." sample projects here, burying the
    // curated dataset under 44 unrelated rows on every dev boot.
    private const string DapiTenantSlug = "dapi";

    private static readonly string[] CategoryNames =
        ["Backend", "Frontend", "Infrastructure", "Design", "Marketing"];

    private readonly IServiceProvider _services;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<WorkManagementSampleDataSeeder> _logger;

    public WorkManagementSampleDataSeeder(
        IServiceProvider services,
        IHostEnvironment environment,
        ILogger<WorkManagementSampleDataSeeder> logger)
    {
        _services = services;
        _environment = environment;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_environment.IsDevelopment() && !_environment.IsEnvironment("Test"))
        {
            return;
        }

        try
        {
            await using var scope = _services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var tenantContext = scope.ServiceProvider.GetRequiredService<IWritableTenantContext>();

            await SeedAsync(db, tenantContext, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WorkManagementSampleDataSeeder failed. Startup will stop.");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public static async Task SeedAsync(
        ApplicationDbContext db,
        IWritableTenantContext tenantContext,
        CancellationToken cancellationToken)
    {
        tenantContext.SetAdminMode();
        var tenants = await db.Tenants
            .Where(t => t.Status == TenantStatus.Active)
            .ToListAsync(cancellationToken);

        foreach (var tenant in tenants)
        {
            if (tenant.Slug == DapiTenantSlug)
            {
                continue;
            }

            // RLS is enforced per-DB-session against whichever tenant is currently Resolve()d,
            // so every tenant's staged inserts must be flushed with SaveChangesAsync before the
            // loop moves on and re-resolves to the next tenant - a single SaveChangesAsync after
            // the whole loop (saving all tenants' rows under only the last-resolved tenant) gets
            // rejected by Postgres for every other tenant's rows. Matches
            // DevSmokeTestTenantSeeder's per-tenant SetAdminMode+Resolve+SaveChanges pattern.
            tenantContext.SetAdminMode();
            tenantContext.Resolve(new TenantRegistryEntry(tenant.Id, tenant.Slug, tenant.Status, PlanCode: null));

            var categories = await EnsureProjectCategoriesAsync(db, tenant.Id, cancellationToken);

            var legalEntity = await db.LegalEntities
                .FirstOrDefaultAsync(l => l.TenantId == tenant.Id && l.IsPrimary, cancellationToken);
            if (legalEntity is null)
            {
                await db.SaveChangesAsync(cancellationToken);
                continue;
            }

            var users = await db.Users
                .Where(u => u.TenantId == tenant.Id && u.IsActive)
                .ToListAsync(cancellationToken);

            foreach (var user in users)
            {
                var employee = await db.Employees
                    .FirstOrDefaultAsync(e => e.TenantId == tenant.Id && e.UserId == user.Id, cancellationToken);
                if (employee is null)
                {
                    continue;
                }

                await EnsureUserSampleProjectsAsync(db, tenant.Id, user, employee, legalEntity.Id, categories, cancellationToken);
            }

            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private static async Task<List<ProjectCategory>> EnsureProjectCategoriesAsync(
        ApplicationDbContext db,
        Guid tenantId,
        CancellationToken ct)
    {
        var existing = await db.ProjectCategories
            .Where(c => c.TenantId == tenantId)
            .ToListAsync(ct);

        var now = DateTimeOffset.UtcNow;
        foreach (var name in CategoryNames)
        {
            if (existing.Any(c => c.Name == name))
            {
                continue;
            }

            var category = new ProjectCategory
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Name = name,
                IsActive = true,
                CreatedAt = now
            };
            db.ProjectCategories.Add(category);
            existing.Add(category);
        }

        return existing.Take(TargetCategoriesPerTenant).ToList();
    }

    private static async Task EnsureUserSampleProjectsAsync(
        ApplicationDbContext db,
        Guid tenantId,
        User user,
        Employee employee,
        Guid legalEntityId,
        List<ProjectCategory> categories,
        CancellationToken ct)
    {
        if (categories.Count == 0)
        {
            return;
        }

        // Keyed on TenantId+Identifier only (not LeadId): the unique constraint this seeder must
        // stay idempotent against is ix_projects_tenant_id_identifier, and Identifier is derived
        // from user.Id, not employee.Id. If an Employee row is ever deleted and recreated for the
        // same user (its Id changes), a LeadId-based check would stop recognizing already-seeded
        // rows and try to re-insert the same identifiers, violating the unique index.
        var shortUserId = user.Id.ToString("N")[..8].ToUpperInvariant();
        var existingSampleProjects = await db.Projects
            .Where(p => p.TenantId == tenantId && p.Identifier.StartsWith(SampleIdentifierPrefix + shortUserId))
            .ToListAsync(ct);

        var now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(now.UtcDateTime.Date);

        for (var projectIndex = existingSampleProjects.Count; projectIndex < TargetProjectsPerUser; projectIndex++)
        {
            var category = categories[projectIndex % categories.Count];
            var identifier = $"{SampleIdentifierPrefix}{shortUserId}{projectIndex + 1}";

            var project = new Project
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                OwningLegalEntityId = legalEntityId,
                CategoryId = category.Id,
                Name = $"{user.FirstName}'s Sample Project {projectIndex + 1}",
                Identifier = identifier,
                Description = "Development sample data seeded for Work Management UI testing.",
                LeadId = employee.Id,
                StartDate = today,
                TargetDate = today.AddDays(90),
                AllocatedHours = 0m,
                CompletedHours = 0m,
                IsActive = true,
                CreatedById = user.Id,
                CreatedAt = now
            };

            var defaultObjective = new Objective
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ProjectId = project.Id,
                ParentObjectiveId = null,
                IsDefault = true,
                Title = project.Name,
                Description = project.Description,
                OwnerId = employee.Id,
                IsActive = true,
                StartDate = project.StartDate,
                EndDate = project.TargetDate,
                Progress = 0m,
                AllocatedHours = 40m,
                CompletedHours = 0m,
                CreatedById = user.Id,
                CreatedAt = now
            };

            var creatorMembership = new ProjectMember
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ProjectId = project.Id,
                ObjectiveId = defaultObjective.Id,
                EmployeeId = employee.Id,
                MembershipSource = ProjectMembershipSources.System,
                IsActive = true,
                JoinedAt = now,
                CreatedById = user.Id,
                CreatedAt = now
            };

            var defaultVersion = new ProjectVersion
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ProjectId = project.Id,
                Name = "Initial Release",
                StatusId = VersionStatusIds.Planned,
                CreatedById = user.Id,
                CreatedAt = now
            };

            var releaseReminder = new ReleaseCalendarEntry
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ProjectId = project.Id,
                VersionId = defaultVersion.Id,
                RecipientUserId = user.Id,
                ScheduledDate = project.TargetDate.AddDays(-14),
                ReminderType = ReleaseReminderTypes.ProjectRelease,
                IsActive = true,
                CreatedById = user.Id,
                CreatedAt = now
            };

            db.Projects.Add(project);
            db.Objectives.Add(defaultObjective);
            db.ProjectMembers.Add(creatorMembership);
            db.ProjectVersions.Add(defaultVersion);
            db.ReleaseCalendarEntries.Add(releaseReminder);

            for (var milestoneIndex = 0; milestoneIndex < TargetMilestonesPerProject; milestoneIndex++)
            {
                var milestone = new Objective
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    ProjectId = project.Id,
                    ParentObjectiveId = defaultObjective.Id,
                    IsDefault = false,
                    Title = $"Milestone {milestoneIndex + 1}",
                    Description = "Development sample milestone seeded for Work Management UI testing.",
                    OwnerId = employee.Id,
                    ReportingManagerId = null,
                    IsActive = true,
                    StartDate = today.AddDays(milestoneIndex * 30),
                    EndDate = today.AddDays((milestoneIndex + 1) * 30),
                    Progress = 0m,
                    AllocatedHours = 20m,
                    CompletedHours = 0m,
                    CreatedById = user.Id,
                    CreatedAt = now
                };

                var milestoneMembership = new ProjectMember
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    ProjectId = project.Id,
                    ObjectiveId = milestone.Id,
                    EmployeeId = employee.Id,
                    MembershipSource = ProjectMembershipSources.System,
                    IsActive = true,
                    JoinedAt = now,
                    CreatedById = user.Id,
                    CreatedAt = now
                };

                db.Objectives.Add(milestone);
                db.ProjectMembers.Add(milestoneMembership);
            }
        }
    }
}
