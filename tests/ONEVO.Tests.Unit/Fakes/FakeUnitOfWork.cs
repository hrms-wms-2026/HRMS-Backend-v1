using ONEVO.Application.Common.RepositoryInterfaces;

namespace ONEVO.Tests.Unit.Fakes;

public sealed class FakeUnitOfWork : IUnitOfWork
{
    public bool ShouldFailSave { get; set; }
    public int SaveCallCount { get; private set; }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveCallCount++;
        if (ShouldFailSave)
        {
            throw new InvalidOperationException("simulated database save failure");
        }

        return Task.FromResult(1);
    }
}
