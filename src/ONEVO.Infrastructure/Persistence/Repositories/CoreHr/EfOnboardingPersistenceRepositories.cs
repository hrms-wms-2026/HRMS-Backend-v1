using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.CoreHr.Onboarding.DTOs.Responses;
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

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}

public sealed class EfChecklistTemplateRepository(ApplicationDbContext db) : IChecklistTemplateRepository
{
    public Task AddAsync(ChecklistTemplate template, CancellationToken ct = default)
        => db.ChecklistTemplates.AddAsync(template, ct).AsTask();

    public Task<ChecklistTemplate?> GetActiveOnboardingAsync(Guid tenantId, Guid templateId, Guid? departmentId, CancellationToken ct = default)
        => db.ChecklistTemplates.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == templateId
            && x.IsActive && x.TemplateType == "onboarding"
            && (x.DepartmentId == null || x.DepartmentId == departmentId), ct);

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}

public sealed class EfEmployeeChecklistTaskRepository(ApplicationDbContext db) : IEmployeeChecklistTaskRepository
{
    public async Task<IReadOnlyList<EmployeeChecklistTask>> InstantiateAsync(
        ChecklistTemplate template, Guid employeeId, string? editedTasksJson, CancellationToken ct = default)
    {
        if (!template.IsActive || template.TemplateType != "onboarding")
            throw new ArgumentException("Only active onboarding templates can be instantiated.", nameof(template));

        var definitions = ParseDefinitions(editedTasksJson ?? template.TasksJson);
        var tasks = definitions.Select(definition => new EmployeeChecklistTask
        {
            Id = Guid.NewGuid(), TenantId = template.TenantId, EmployeeId = employeeId, TemplateId = template.Id,
            LifecycleType = template.TemplateType, TaskTitle = definition.Title, OwnerType = definition.OwnerType,
            AssignedToId = definition.AssignedToId, DueDate = definition.DueDate, Sequence = definition.Sequence,
            Status = "pending"
        }).ToList();
        await db.EmployeeChecklistTasks.AddRangeAsync(tasks, ct);
        return tasks;
    }

    public async Task<IReadOnlyList<EmployeeChecklistTask>> ListByEmployeeAsync(Guid tenantId, Guid employeeId, CancellationToken ct = default)
        => await db.EmployeeChecklistTasks.AsNoTracking().Where(x => x.TenantId == tenantId && x.EmployeeId == employeeId)
            .OrderBy(x => x.Sequence).ThenBy(x => x.Id).ToListAsync(ct);

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);

    private static IReadOnlyList<TaskDefinition> ParseDefinitions(string tasksJson)
    {
        try
        {
            using var document = JsonDocument.Parse(tasksJson);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                throw new ArgumentException("Checklist task JSON must be an array.", nameof(tasksJson));

            var definitions = new List<TaskDefinition>();
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object
                    || !item.TryGetProperty("title", out var title) || string.IsNullOrWhiteSpace(title.GetString())
                    || !item.TryGetProperty("ownerType", out var ownerType) || string.IsNullOrWhiteSpace(ownerType.GetString())
                    || !item.TryGetProperty("assignedToId", out var assignedTo) || !Guid.TryParse(assignedTo.GetString(), out var assignedToId)
                    || !item.TryGetProperty("dueDate", out var dueDate) || !DateOnly.TryParse(dueDate.GetString(), out var parsedDueDate))
                    throw new ArgumentException("Each checklist task requires title, ownerType, assignedToId, and dueDate.", nameof(tasksJson));

                int? sequence = null;
                if (item.TryGetProperty("sequence", out var sequenceElement))
                {
                    if (!sequenceElement.TryGetInt32(out var parsedSequence))
                        throw new ArgumentException("Checklist task sequence must be an integer.", nameof(tasksJson));
                    sequence = parsedSequence;
                }
                definitions.Add(new TaskDefinition(title.GetString()!, ownerType.GetString()!, assignedToId, parsedDueDate, sequence));
            }
            return definitions;
        }
        catch (JsonException ex)
        {
            throw new ArgumentException("Checklist task JSON is invalid.", nameof(tasksJson), ex);
        }
    }

    private sealed record TaskDefinition(string Title, string OwnerType, Guid AssignedToId, DateOnly DueDate, int? Sequence);
}
