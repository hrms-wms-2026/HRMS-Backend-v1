using ONEVO.Domain.Features.DevPlatform.AppCatalog.Entities;

namespace ONEVO.Application.Features.DevPlatform.AppCatalog.RepositoryInterfaces;

public interface IGlobalApplicationRepository
{
    Task<IReadOnlyList<GlobalApplication>> ListAsync(CancellationToken ct);
    Task<GlobalApplication?> GetByIdAsync(Guid id, CancellationToken ct);
    Task RecordObservedAsync(IEnumerable<string> processNames, DateTimeOffset observedAt, CancellationToken ct);
    Task AddAsync(GlobalApplication application, CancellationToken ct);
}
