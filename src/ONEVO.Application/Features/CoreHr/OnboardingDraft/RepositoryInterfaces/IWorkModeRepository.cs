using ONEVO.Domain.Lookups;

namespace ONEVO.Application.Features.CoreHr.OnboardingDrafts.RepositoryInterfaces;

public interface IWorkModeRepository
{
    Task<bool> ExistsActiveAsync(int workModeId, CancellationToken ct = default);
    Task<List<WorkMode>> ListActiveAsync(CancellationToken ct = default);
}
