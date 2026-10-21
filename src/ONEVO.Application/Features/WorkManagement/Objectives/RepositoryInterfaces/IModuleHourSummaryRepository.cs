using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;

namespace ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;

/// <summary>Read-only: the rows are maintained by database triggers (see ModuleHourSummary).</summary>
public interface IModuleHourSummaryRepository
{
    /// <summary>Summaries of the given Modules, keyed by Module id. A Module with no tasks may have no row.</summary>
    Task<IReadOnlyDictionary<Guid, ModuleHourSummary>> ListByObjectiveIdsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> objectiveIds, CancellationToken ct = default);
}
