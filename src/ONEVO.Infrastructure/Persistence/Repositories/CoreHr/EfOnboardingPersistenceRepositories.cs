using Microsoft.EntityFrameworkCore;
using Npgsql;
using ONEVO.Application.Common.Exceptions;
using ONEVO.Application.Features.CoreHr.Onboarding.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Onboarding.Models;
using ONEVO.Application.Features.CoreHr.Onboarding.Queries.ListPendingAccessGrantRequestsForMe;
using ONEVO.Application.Features.CoreHr.Onboarding.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.CoreHr;

public sealed class EfAccessGrantRequestRepository(ApplicationDbContext db) : IAccessGrantRequestRepository
{
    public Task AddAsync(AccessGrantRequest request, CancellationToken ct = default)
        => db.AccessGrantRequests.AddAsync(request, ct).AsTask();

    public Task<AccessGrantRequest?> GetPendingByDraftAsync(Guid tenantId, Guid onboardingDraftId, Guid targetPositionId, Guid positionAccessTemplateId, CancellationToken ct = default)
        => db.AccessGrantRequests.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.OnboardingDraftId == onboardingDraftId
            && x.TargetPositionId == targetPositionId && x.PositionAccessTemplateId == positionAccessTemplateId
            && x.ApprovalStatus == "Pending", ct);

    public Task<AccessGrantRequest?> GetTrackedByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default)
        => db.AccessGrantRequests.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, ct);

    public Task<bool> AnyPendingByDraftAsync(Guid tenantId, Guid onboardingDraftId, CancellationToken ct = default)
        => db.AccessGrantRequests.AnyAsync(x => x.TenantId == tenantId && x.OnboardingDraftId == onboardingDraftId
            && x.ApprovalStatus == "Pending", ct);

    public async Task<(IReadOnlyList<OnboardingAccessGrantRequestListItemResponse> Items, int TotalCount)> ListOnboardingRequestsAsync(
        Guid tenantId, string approvalStatus, string actionType, Guid? legalEntityId, Guid? requestedRoleId,
        string? search, int page, int pageSize, CancellationToken ct = default)
    {
        var requests = db.AccessGrantRequests.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.ApprovalStatus == approvalStatus && x.ActionType == actionType);
        var drafts = db.OnboardingDrafts.AsNoTracking().Where(d => d.TenantId == tenantId);
        var positions = db.Positions.AsNoTracking().Where(p => p.TenantId == tenantId);
        var departments = db.Departments.AsNoTracking().Where(d => d.TenantId == tenantId);
        var legalEntities = db.LegalEntities.AsNoTracking().Where(l => l.TenantId == tenantId);
        var roles = db.Roles.AsNoTracking().Where(r => r.TenantId == tenantId);
        var users = db.Users.AsNoTracking().Where(u => u.TenantId == tenantId);

        // Draft join is INNER: OnboardingDraftId must not be null for a row to be part of this
        // queue at all (see the interface doc comment), and this join is what enforces it.
        // Every other join is LEFT - a display name that fails to resolve must not drop the row.
        var joined =
            from x in requests
            join d in drafts on x.OnboardingDraftId equals d.Id
            join position in positions on x.TargetPositionId equals position.Id into positionJoin
            from position in positionJoin.DefaultIfEmpty()
            join department in departments on x.TargetDepartmentId equals department.Id into departmentJoin
            from department in departmentJoin.DefaultIfEmpty()
            join legalEntity in legalEntities on d.LegalEntityId equals legalEntity.Id into legalEntityJoin
            from legalEntity in legalEntityJoin.DefaultIfEmpty()
            join role in roles on x.RequestedRoleId equals role.Id into roleJoin
            from role in roleJoin.DefaultIfEmpty()
            join requester in users on x.RequestedByUserId equals requester.Id into requesterJoin
            from requester in requesterJoin.DefaultIfEmpty()
            join decider in users on x.DecidedByUserId equals decider.Id into deciderJoin
            from decider in deciderJoin.DefaultIfEmpty()
            select new { x, d, position, department, legalEntity, role, requester, decider };

        if (legalEntityId is not null)
        {
            joined = joined.Where(row => row.d.LegalEntityId == legalEntityId.Value);
        }

        if (requestedRoleId is not null)
        {
            joined = joined.Where(row => row.x.RequestedRoleId == requestedRoleId.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            // .ToLower().Contains(...) rather than EF.Functions.ILike: translates on both the
            // Npgsql provider and the EF Core InMemory provider this repository's own tests use.
            var term = search.Trim().ToLower();
            joined = joined.Where(row =>
                (row.d.FirstName + " " + row.d.LastName).ToLower().Contains(term)
                || row.d.WorkEmail.ToLower().Contains(term)
                || (row.position != null && row.position.Name.ToLower().Contains(term))
                || (row.role != null && row.role.Name.ToLower().Contains(term)));
        }

        var totalCount = await joined.CountAsync(ct);

        var items = await joined
            .OrderByDescending(row => row.x.RequestedAt).ThenBy(row => row.x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(row => new OnboardingAccessGrantRequestListItemResponse(
                row.x.Id,
                row.x.OnboardingDraftId!.Value,
                row.x.ApprovalStatus,
                row.x.RequestedAt,
                row.x.RequestedByUserId,
                row.requester != null ? row.requester.FirstName + " " + row.requester.LastName : null,
                row.x.DecidedAt,
                row.x.DecidedByUserId,
                row.decider != null ? row.decider.FirstName + " " + row.decider.LastName : null,
                row.x.DecisionNote,
                row.d.LegalEntityId,
                row.legalEntity != null ? row.legalEntity.Name : null,
                (Guid?)row.x.TargetDepartmentId,
                row.department != null ? row.department.Name : null,
                row.x.TargetPositionId,
                row.position != null ? row.position.Name : null,
                row.x.PositionAccessTemplateId,
                row.x.RequestedRoleId,
                row.role != null ? row.role.Name : null,
                row.d.FirstName + " " + row.d.LastName,
                row.d.WorkEmail,
                row.d.StartDate,
                row.d.Status,
                row.d.DraftReason,
                row.d.LastSavedStep))
            .ToListAsync(ct);

        return (items, totalCount);
    }

    public async Task<IReadOnlyList<PendingAccessGrantRequestResponse>> ListPendingAsync(
        Guid tenantId, CancellationToken ct = default)
    {
        var requests = db.AccessGrantRequests.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.ApprovalStatus == "Pending");
        var positions = db.Positions.AsNoTracking().Where(p => p.TenantId == tenantId);
        var users = db.Users.AsNoTracking().Where(u => u.TenantId == tenantId);
        var employees = db.Employees.AsNoTracking().Where(e => e.TenantId == tenantId);

        // Every join is LEFT - a display name that fails to resolve must not drop the row.
        // Employee is left-joined on EmployeeId; when EmployeeId is null, EmployeeName is null.
        var joined =
            from x in requests
            join position in positions on x.TargetPositionId equals position.Id into positionJoin
            from position in positionJoin.DefaultIfEmpty()
            join requester in users on x.RequestedByUserId equals requester.Id into requesterJoin
            from requester in requesterJoin.DefaultIfEmpty()
            join employee in employees on x.EmployeeId equals employee.Id into employeeJoin
            from employee in employeeJoin.DefaultIfEmpty()
            select new { x, position, requester, employee };

        return await joined
            .OrderByDescending(row => row.x.RequestedAt).ThenBy(row => row.x.Id)
            .Select(row => new PendingAccessGrantRequestResponse(
                row.x.Id,
                row.x.ActionType,
                row.employee != null ? row.employee.FirstName + " " + row.employee.LastName : null,
                row.position != null ? row.position.Name : string.Empty,
                row.x.ChangeReason,
                row.requester != null ? row.requester.FirstName + " " + row.requester.LastName : string.Empty,
                row.x.RequestedAt))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyDictionary<Guid, Guid>> GetApprovedByUserIdsForAssignmentsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> assignmentIds, CancellationToken ct = default)
    {
        if (assignmentIds.Count == 0)
            return new Dictionary<Guid, Guid>();

        var rows = await db.AccessGrantRequests
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId
                && x.ApprovalStatus == "Approved"
                && x.ReservedPositionAssignmentId != null
                && assignmentIds.Contains(x.ReservedPositionAssignmentId.Value)
                && x.DecidedByUserId != null)
            .Select(x => new { AssignmentId = x.ReservedPositionAssignmentId!.Value, UserId = x.DecidedByUserId!.Value })
            .ToListAsync(ct);

        return rows
            .GroupBy(row => row.AssignmentId)
            .ToDictionary(group => group.Key, group => group.First().UserId);
    }

    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        try
        {
            return await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyConflictException(ex);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new UniqueConstraintConflictException(ex);
        }
    }
}

public sealed class EfChecklistTemplateRepository(ApplicationDbContext db) : IChecklistTemplateRepository
{
    public Task AddAsync(ChecklistTemplate template, CancellationToken ct = default)
        => db.ChecklistTemplates.AddAsync(template, ct).AsTask();

    public Task<ChecklistTemplate?> GetActiveOnboardingAsync(
        Guid tenantId, Guid templateId, Guid legalEntityId, Guid? departmentId, Guid? positionId, CancellationToken ct = default)
        => db.ChecklistTemplates.AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenantId && x.Id == templateId && x.IsActive && x.TemplateType == "onboarding"
            && x.LegalEntityId == legalEntityId
            && (
                (x.PositionId != null && x.PositionId == positionId)
                || (x.PositionId == null && x.DepartmentId != null && x.DepartmentId == departmentId)
                || (x.PositionId == null && x.DepartmentId == null)
            ), ct);

    public Task<ChecklistTemplate?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default)
        => db.ChecklistTemplates.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, ct);

    public Task<ChecklistTemplate?> GetTrackedByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default)
        => db.ChecklistTemplates.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, ct);

    public async Task<(IReadOnlyList<ChecklistTemplate> Items, int TotalCount)> ListAsync(
        Guid tenantId, ChecklistTemplateListFilter filter, int page, int pageSize, CancellationToken ct = default)
    {
        var query = db.ChecklistTemplates.AsNoTracking().Where(x => x.TenantId == tenantId && x.LegalEntityId == filter.LegalEntityId);

        if (!filter.IncludeInactive) query = query.Where(x => x.IsActive);
        if (filter.TemplateType is not null) query = query.Where(x => x.TemplateType == filter.TemplateType);
        if (filter.DepartmentId is not null) query = query.Where(x => x.DepartmentId == filter.DepartmentId);
        if (filter.PositionId is not null) query = query.Where(x => x.PositionId == filter.PositionId);

        var totalCount = await query.CountAsync(ct);
        var items = await query.OrderBy(x => x.Name).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return (items, totalCount);
    }

    public async Task<IReadOnlyList<ChecklistTemplateMatch>> ListOnboardingMatchesAsync(
        Guid tenantId, Guid legalEntityId, Guid? departmentId, Guid? positionId, CancellationToken ct = default)
    {
        var candidates = await db.ChecklistTemplates.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.IsActive && x.TemplateType == "onboarding" && x.LegalEntityId == legalEntityId
                && (
                    (positionId != null && x.PositionId == positionId)
                    || (departmentId != null && x.PositionId == null && x.DepartmentId == departmentId)
                    || (x.PositionId == null && x.DepartmentId == null)
                ))
            .ToListAsync(ct);

        return candidates
            .Select(t => new ChecklistTemplateMatch(t, t.PositionId is not null
                ? ChecklistTemplateMatchLevels.Position
                : t.DepartmentId is not null ? ChecklistTemplateMatchLevels.Department : ChecklistTemplateMatchLevels.Company))
            .OrderBy(m => m.MatchLevel switch
            {
                ChecklistTemplateMatchLevels.Position => 0,
                ChecklistTemplateMatchLevels.Department => 1,
                _ => 2,
            })
            .ThenBy(m => m.Template.Name)
            .ToList();
    }

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}

public sealed class EfEmployeeChecklistTaskRepository(ApplicationDbContext db) : IEmployeeChecklistTaskRepository
{
    public async Task<IReadOnlyList<EmployeeChecklistTask>> InstantiateAsync(
        ChecklistTemplate template, Guid employeeId, Guid newHireUserId, string? editedTasksJson, DateOnly anchorDate, CancellationToken ct = default)
    {
        if (!template.IsActive || template.TemplateType != "onboarding")
            throw new ArgumentException("Only active onboarding templates can be instantiated.", nameof(template));

        var mode = editedTasksJson is not null ? ChecklistTaskDueRuleMode.AbsoluteDate : ChecklistTaskDueRuleMode.OffsetDays;
        var definitions = ChecklistTaskJsonContract.Parse(editedTasksJson ?? template.TasksJson, mode);
        var tasks = ChecklistTaskJsonContract.ToEmployeeChecklistTasks(
            definitions, template.TenantId, employeeId, template.Id, template.TemplateType, newHireUserId, anchorDate, mode);

        await db.EmployeeChecklistTasks.AddRangeAsync(tasks, ct);
        return tasks;
    }

    public async Task<IReadOnlyList<EmployeeChecklistTask>> ListByEmployeeAsync(Guid tenantId, Guid employeeId, CancellationToken ct = default)
        => await db.EmployeeChecklistTasks.AsNoTracking().Where(x => x.TenantId == tenantId && x.EmployeeId == employeeId)
            .OrderBy(x => x.Sequence).ThenBy(x => x.Id).ToListAsync(ct);

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
