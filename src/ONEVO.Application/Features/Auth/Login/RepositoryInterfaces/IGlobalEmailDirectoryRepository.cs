namespace ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;

public interface IGlobalEmailDirectoryRepository
{
    Task UpsertAsync(string email, Guid tenantId, CancellationToken ct = default);
}
