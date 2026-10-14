using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.Services;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.WorkManagement.ProjectMembers.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using ONEVO.Domain.Lookups;
using ONEVO.Infrastructure.Persistence;

namespace ONEVO.Infrastructure.Services.CoreHr;

public sealed class OfficeProjectReconciler(
    IServiceScopeFactory scopeFactory,
    IOfficeProjectProvisioner provisioner,
    ILogger<OfficeProjectReconciler> logger) : IOfficeProjectReconciler
{
    private const string OwnerRoleName = "Owner";

    private sealed record OwnerEmployee(Guid EmployeeId, Guid UserId, Guid LegalEntityId);

    public async Task<OfficeProjectReconcileOutcome> ReconcileAsync(Guid tenantId, CancellationToken ct = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var services = scope.ServiceProvider;

        var tenant = await services.GetRequiredService<ITenantRepository>().GetByIdAsync(tenantId, ct);
        if (tenant is null || tenant.Status is TenantStatus.Suspended or TenantStatus.Cancelled)
            return OfficeProjectReconcileOutcome.TenantInactive;

        await services.GetRequiredService<ITenantContextSwitcher>().SwitchToTenantAsync(
            new TenantRegistryEntry(tenant.Id, tenant.Slug, tenant.Status, PlanCode: null), ct);
        var db = services.GetRequiredService<ApplicationDbContext>();
        var now = DateTimeOffset.UtcNow;

        var owner = await FindOwnerEmployeeAsync(db, tenantId, now, ct);
        if (owner is null)
            return OfficeProjectReconcileOutcome.NoOwnerEmployee;

        var office = await db.Projects.AsNoTracking()
            .Where(p => p.TenantId == tenantId && p.SystemPurpose == ProjectSystemPurposes.Office)
            .Select(p => new { p.Id, p.LeadId })
            .FirstOrDefaultAsync(ct);

        if (office is null)
        {
            await provisioner.EnsureAsync(
                tenantId, owner.LegalEntityId, owner.UserId, owner.EmployeeId, DateOnly.FromDateTime(now.UtcDateTime), ct);
            logger.LogInformation("Created the Office project for tenant {TenantId}, led by the tenant owner.", tenantId);
            return OfficeProjectReconcileOutcome.Created;
        }

        var outcome = OfficeProjectReconcileOutcome.AlreadyAligned;
        if (office.LeadId != owner.EmployeeId)
        {
            // Guarded on the lead we read, so concurrent runs converge instead of flip-flopping.
            var updated = await db.Projects
                .Where(p => p.Id == office.Id && p.LeadId == office.LeadId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(p => p.LeadId, owner.EmployeeId)
                    .SetProperty(p => p.UpdatedAt, now), ct);
            if (updated == 1)
            {
                outcome = OfficeProjectReconcileOutcome.LeadSwitched;
                logger.LogInformation(
                    "Switched the Office project lead for tenant {TenantId} to the tenant owner (previous lead kept as member).",
                    tenantId);
            }
        }

        // The root module's owner carries owner-only powers (IsEffectiveOwnerAsync) and led-work
        // scope, so it follows the lead. Unconditional on the switch above, so a run interrupted
        // between the two updates converges on the next one.
        await db.Objectives
            .Where(o => o.TenantId == tenantId && o.ProjectId == office.Id && o.IsDefault && o.OwnerId != owner.EmployeeId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.OwnerId, owner.EmployeeId)
                .SetProperty(o => o.UpdatedAt, now), ct);

        await EnsureOwnerMembershipAsync(db, tenantId, office.Id, owner, now, ct);
        return outcome;
    }

    public async Task<OfficeProjectReconcileOutcome?> TryReconcileAsync(Guid tenantId, CancellationToken ct = default)
    {
        try
        {
            return await ReconcileAsync(tenantId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Office project reconciliation failed for tenant {TenantId}; it will be retried by the next trigger.", tenantId);
            return null;
        }
    }

    private static async Task<OwnerEmployee?> FindOwnerEmployeeAsync(
        ApplicationDbContext db, Guid tenantId, DateTimeOffset now, CancellationToken ct)
    {
        var ownerUserIds = await db.UserRoles.AsNoTracking()
            .Where(ur => ur.TenantId == tenantId
                && ur.Role.IsSystem && ur.Role.Name == OwnerRoleName
                && (ur.ExpiresAt == null || ur.ExpiresAt > now))
            .OrderBy(ur => ur.AssignedAt).ThenBy(ur => ur.UserId)
            .Select(ur => ur.UserId)
            .ToListAsync(ct);
        if (ownerUserIds.Count == 0)
            return null;

        var candidates = await db.Employees.AsNoTracking()
            .Where(e => e.TenantId == tenantId
                && ownerUserIds.Contains(e.UserId)
                && e.EmploymentStatusId == EmploymentStatusIds.Active)
            .Select(e => new
            {
                e.Id, e.UserId, e.LegalEntityId, e.CreatedAt,
                InPrimaryEntity = e.LegalEntityId != null
                    && db.LegalEntities.Any(le => le.Id == e.LegalEntityId && le.IsPrimary)
            })
            .ToListAsync(ct);
        if (candidates.Count == 0)
            return null;

        var primaryLegalEntityId = await db.LegalEntities.AsNoTracking()
            .Where(le => le.TenantId == tenantId && le.IsPrimary && le.IsActive)
            .OrderBy(le => le.CreatedAt)
            .Select(le => (Guid?)le.Id)
            .FirstOrDefaultAsync(ct);

        foreach (var userId in ownerUserIds)
        {
            var chosen = candidates
                .Where(c => c.UserId == userId)
                .OrderByDescending(c => c.InPrimaryEntity).ThenBy(c => c.CreatedAt).ThenBy(c => c.Id)
                .FirstOrDefault();
            if (chosen is null)
                continue;

            var legalEntityId = chosen.LegalEntityId ?? primaryLegalEntityId;
            return legalEntityId is null ? null : new OwnerEmployee(chosen.Id, chosen.UserId, legalEntityId.Value);
        }

        return null;
    }

    private async Task EnsureOwnerMembershipAsync(
        ApplicationDbContext db, Guid tenantId, Guid projectId, OwnerEmployee owner, DateTimeOffset now, CancellationToken ct)
    {
        var rootObjectiveId = await db.Objectives.AsNoTracking()
            .Where(o => o.TenantId == tenantId && o.ProjectId == projectId && o.IsDefault)
            .Select(o => o.Id)
            .SingleAsync(ct);

        var membership = await db.ProjectMembers.FirstOrDefaultAsync(m =>
            m.TenantId == tenantId && m.ProjectId == projectId
            && m.ObjectiveId == rootObjectiveId && m.EmployeeId == owner.EmployeeId, ct);

        if (membership is null)
        {
            db.ProjectMembers.Add(new ProjectMember
            {
                Id = Guid.NewGuid(), TenantId = tenantId, ProjectId = projectId, ObjectiveId = rootObjectiveId,
                EmployeeId = owner.EmployeeId, MembershipSource = ProjectMembershipSources.System,
                IsActive = true, JoinedAt = now, CreatedById = owner.UserId, CreatedAt = now
            });
        }
        else if (!membership.IsActive)
        {
            membership.IsActive = true;
            membership.RemovedAt = null;
            membership.JoinedAt = now;
        }
        else
        {
            return;
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            logger.LogInformation("Owner membership for the Office project of tenant {TenantId} was added concurrently.", tenantId);
        }
    }
}
