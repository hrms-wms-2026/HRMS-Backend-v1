using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Services;

/// <summary>Implements spec §3.1's slack formula: AllocatedHours - SUM(active child objectives) - SUM(active tasks).</summary>
public interface IObjectiveAllocationSlackCalculator
{
    Task<decimal> CalculateAsync(Guid tenantId, Objective objective, Guid? excludingTaskId = null, CancellationToken ct = default);
}
