using ONEVO.Domain.Features.WorkManagement.Labels.Entities;

namespace ONEVO.Application.Features.WorkManagement.Labels.RepositoryInterfaces;

public interface ILabelRepository
{
    Task AddAsync(Label label, CancellationToken ct = default);
}
