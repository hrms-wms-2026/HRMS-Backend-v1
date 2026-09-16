namespace ONEVO.Application.Features.CoreHr.OnboardingDrafts.RepositoryInterfaces;

public interface IEmploymentTypeRepository
{
    Task<int?> GetIdByCodeAsync(string code, CancellationToken ct = default);

    Task<string?> GetCodeByIdAsync(int id, CancellationToken ct = default);
}
