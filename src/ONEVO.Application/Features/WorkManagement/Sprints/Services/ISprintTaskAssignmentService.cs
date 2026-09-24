using ONEVO.Application.Common.Models;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Sprints.Services;

public sealed record SprintTaskChangeSet(IReadOnlyList<WorkTask> ToAdd, IReadOnlyList<WorkTask> ToRemove)
{
    public static readonly SprintTaskChangeSet Empty = new(Array.Empty<WorkTask>(), Array.Empty<WorkTask>());
    public bool IsEmpty => ToAdd.Count == 0 && ToRemove.Count == 0;
}

/// <summary>Spec D2/D4. PrepareAsync validates and returns tracked tasks (no mutation); Apply
/// mutates them - call Apply inside the caller's ExecuteInTransactionAsync.</summary>
public interface ISprintTaskAssignmentService
{
    Task<Result<SprintTaskChangeSet>> PrepareAsync(
        Guid tenantId, Sprint sprint, IReadOnlyCollection<Guid> addTaskIds, IReadOnlyCollection<Guid> removeTaskIds,
        Guid callerEmployeeId, CancellationToken ct = default);

    void Apply(SprintTaskChangeSet changes, Guid sprintId);
}
