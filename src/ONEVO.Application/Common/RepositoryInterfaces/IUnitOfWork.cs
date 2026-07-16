namespace ONEVO.Application.Common.RepositoryInterfaces;

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
