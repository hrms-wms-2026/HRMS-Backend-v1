using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Services;

public class ObjectiveAllocationSlackCalculator : IObjectiveAllocationSlackCalculator
{
    private readonly IObjectiveRepository _objectives;
    private readonly IWorkTaskRepository _tasks;

    public ObjectiveAllocationSlackCalculator(IObjectiveRepository objectives, IWorkTaskRepository tasks)
    {
        _objectives = objectives;
        _tasks = tasks;
    }

    public async Task<decimal> CalculateAsync(Guid tenantId, Objective objective, Guid? excludingTaskId = null, CancellationToken ct = default)
    {
        var children = await _objectives.GetTrackedActiveDirectChildrenAsync(tenantId, objective.Id, ct);
        var childSum = children.Sum(c => c.AllocatedHours);
        var taskSum = await _tasks.GetActiveAllocationSumByObjectiveIdAsync(tenantId, objective.Id, excludingTaskId, ct);
        return objective.AllocatedHours - childSum - taskSum;
    }
}
