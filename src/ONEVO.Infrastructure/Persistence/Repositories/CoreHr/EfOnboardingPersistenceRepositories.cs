using System.Text.Json;
using Microsoft.EntityFrameworkCore;
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
