using Moq;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;

namespace ONEVO.Tests.Unit.Features.Auth;

public class GlobalEmailDirectoryRepositoryContractTests
{
    [Fact]
    public async Task UpsertAsync_CanBeMocked()
    {
        var repo = new Mock<IGlobalEmailDirectoryRepository>();
        var tenantId = Guid.NewGuid();
        repo.Setup(r => r.UpsertAsync("user@example.com", tenantId, default))
            .Returns(Task.CompletedTask);

        await repo.Object.UpsertAsync("user@example.com", tenantId, default);

        repo.Verify(r => r.UpsertAsync("user@example.com", tenantId, default), Times.Once);
    }
}
