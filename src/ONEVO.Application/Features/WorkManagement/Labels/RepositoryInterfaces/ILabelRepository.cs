using ONEVO.Domain.Features.WorkManagement.Labels.Entities;

namespace ONEVO.Application.Features.WorkManagement.Labels.RepositoryInterfaces;

public interface ILabelRepository
{
    Task AddAsync(Label label, CancellationToken ct = default);

    /// <summary>Batched fetch of labels for a set of projects, capped per project (oldest first). Projects with no labels are simply absent from the result.</summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<Label>>> GetByProjectIdsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> projectIds, int takePerProject, CancellationToken ct = default);
}
