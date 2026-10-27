using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Infrastructure.Services.Leave;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave;

public class LeaveCreditJobTests
{
    [Fact]
    public async Task RunAsync_NoActiveTenants_CompletesWithoutError()
    {
        var services = new ServiceCollection();
        var tenantRepoMock = new Mock<ITenantRepository>();
        tenantRepoMock
            .Setup(t => t.ListAsync(TenantStatus.Active, null, 0, int.MaxValue, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Tenant>());

        services.AddSingleton(tenantRepoMock.Object);
        services.AddSingleton(Mock.Of<IWritableTenantContext>());
        var provider = services.BuildServiceProvider();

        var job = new LeaveCreditJob(provider, NullLogger<LeaveCreditJob>.Instance);

        // Should not throw with zero tenants - proves the tenant-loop/error-isolation shape
        // without resolving the per-tenant dependency chain.
        await job.RunAsync(new DateOnly(2027, 1, 1), CancellationToken.None);
    }
}
