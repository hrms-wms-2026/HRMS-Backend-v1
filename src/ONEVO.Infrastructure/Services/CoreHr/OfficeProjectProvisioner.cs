using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using ONEVO.Application.Features.CoreHr.Onboarding.Services;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.ProjectMembers.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using ONEVO.Infrastructure.Persistence;
using TaskStatusEntity = ONEVO.Domain.Features.WorkManagement.Tasks.Entities.TaskStatus;

namespace ONEVO.Infrastructure.Services.CoreHr;

public sealed class OfficeProjectProvisioner(IServiceScopeFactory scopeFactory) : IOfficeProjectProvisioner
{
    public async Task<OfficeProjectContext> EnsureAsync(
        Guid tenantId, Guid initialOwningLegalEntityId, Guid actingUserId,
        Guid actingEmployeeId, DateOnly targetDate, CancellationToken ct = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var effectiveTargetDate = targetDate < today ? today : targetDate;
        var existing = await db.Projects.AsNoTracking().FirstOrDefaultAsync(
            project => project.TenantId == tenantId && project.SystemPurpose == ProjectSystemPurposes.Office, ct);
        if (existing is not null)
        {
            if (effectiveTargetDate > existing.TargetDate)
            {
                var tracked = await db.Projects.FirstAsync(project => project.Id == existing.Id, ct);
                tracked.TargetDate = effectiveTargetDate;
                tracked.UpdatedAt = DateTimeOffset.UtcNow;
                var root = await db.Objectives.FirstAsync(
                    objective => objective.ProjectId == existing.Id && objective.IsDefault, ct);
                if (effectiveTargetDate > root.EndDate)
                    root.EndDate = effectiveTargetDate;
                await db.SaveChangesAsync(ct);
            }
            return await LoadContextAsync(db, tenantId, existing.Id, ct);
        }

        var now = DateTimeOffset.UtcNow;
        var category = await db.ProjectCategories.FirstOrDefaultAsync(
            item => item.TenantId == tenantId && item.IsActive, ct);
        if (category is null)
        {
            category = new ProjectCategory
            {
                Id = Guid.NewGuid(), TenantId = tenantId, Name = "Operations", IsActive = true,
                CreatedById = actingUserId, CreatedAt = now
            };
            db.ProjectCategories.Add(category);
        }

        var identifier = "OFFICE";
        if (await db.Projects.AnyAsync(project => project.TenantId == tenantId && project.Identifier == identifier, ct))
            identifier = $"OFFICE-{Guid.NewGuid():N}"[..20].ToUpperInvariant();

        var project = new Project
        {
            Id = Guid.NewGuid(), TenantId = tenantId, SystemPurpose = ProjectSystemPurposes.Office,
            OwningLegalEntityId = initialOwningLegalEntityId, CategoryId = category.Id,
            Name = "Office", Identifier = identifier, LeadId = actingEmployeeId,
            StartDate = today, TargetDate = effectiveTargetDate,
            AllocatedHours = 0m, CompletedHours = 0m, IsActive = true, IsAchieved = false,
            CreatedById = actingUserId, CreatedAt = now
        };
        var objective = new Objective
        {
            Id = Guid.NewGuid(), TenantId = tenantId, ProjectId = project.Id, IsDefault = true,
            Title = "Office", OwnerId = actingEmployeeId, IsActive = true,
            StartDate = project.StartDate, EndDate = effectiveTargetDate, AllocatedHours = 0m,
            CompletedHours = 0m, Progress = 0m, CreatedById = actingUserId, CreatedAt = now
        };
        var statuses = new[]
        {
            Status("To Do", 0, TaskStatusCategories.NotStarted, "#94A3B8", false),
            Status("In Process", 1, TaskStatusCategories.Active, "#2563EB", false),
            Status("Done", 2, TaskStatusCategories.Done, "#16A34A", true)
        };
        var categories = new[]
        {
            TaskCategory("Onboarding", 0),
            TaskCategory("Offboarding", 1)
        };

        db.Projects.Add(project);
        db.Objectives.Add(objective);
        db.TaskStatuses.AddRange(statuses);
        db.TaskCategories.AddRange(categories);
        db.ProjectMembers.Add(new ProjectMember
        {
            Id = Guid.NewGuid(), TenantId = tenantId, ProjectId = project.Id, ObjectiveId = objective.Id,
            EmployeeId = actingEmployeeId, MembershipSource = ProjectMembershipSources.System,
            IsActive = true, JoinedAt = now, CreatedById = actingUserId, CreatedAt = now
        });

        try
        {
            await db.SaveChangesAsync(ct);
            return new OfficeProjectContext(
                project.Id, objective.Id, statuses[0].Id,
                categories.ToDictionary(item => item.Name.ToLowerInvariant(), item => item.Id));
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.ChangeTracker.Clear();
            var winner = await db.Projects.AsNoTracking().FirstOrDefaultAsync(
                item => item.TenantId == tenantId && item.SystemPurpose == ProjectSystemPurposes.Office, ct);
            if (winner is null)
                return await EnsureAsync(
                    tenantId, initialOwningLegalEntityId, actingUserId, actingEmployeeId, effectiveTargetDate, ct);
            return await LoadContextAsync(db, tenantId, winner.Id, ct);
        }

        TaskStatusEntity Status(string name, int order, string statusCategory, string color, bool complete) => new()
        {
            Id = Guid.NewGuid(), TenantId = tenantId, ProjectId = project.Id, ObjectiveId = null,
            Name = name, DisplayOrder = order, Category = statusCategory, Color = color,
            MarksTaskComplete = complete, Visibility = TaskStatusVisibilities.Public,
            CreatedById = actingUserId, CreatedAt = now
        };
        ONEVO.Domain.Features.WorkManagement.Tasks.Entities.TaskCategory TaskCategory(string name, int order) => new()
        {
            Id = Guid.NewGuid(), TenantId = tenantId, ProjectId = project.Id,
            Name = name, DisplayOrder = order, CreatedById = actingUserId, CreatedAt = now
        };
    }

    private static async Task<OfficeProjectContext> LoadContextAsync(
        ApplicationDbContext db, Guid tenantId, Guid projectId, CancellationToken ct)
    {
        var objectiveId = await db.Objectives.AsNoTracking()
            .Where(item => item.TenantId == tenantId && item.ProjectId == projectId && item.IsDefault)
            .Select(item => item.Id).SingleAsync(ct);
        var statusId = await db.TaskStatuses.AsNoTracking()
            .Where(item => item.TenantId == tenantId && item.ProjectId == projectId
                && item.ObjectiveId == null && item.Category == TaskStatusCategories.NotStarted)
            .OrderBy(item => item.DisplayOrder).Select(item => item.Id).FirstAsync(ct);
        var categories = await db.TaskCategories.AsNoTracking()
            .Where(item => item.TenantId == tenantId && item.ProjectId == projectId)
            .ToDictionaryAsync(item => item.Name.ToLower(), item => item.Id, ct);
        return new OfficeProjectContext(projectId, objectiveId, statusId, categories);
    }
}
