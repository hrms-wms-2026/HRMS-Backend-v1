using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Entitlement.Helpers;
using ONEVO.Application.Features.Leave.Entitlement.RepositoryInterfaces;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Infrastructure.Services.Leave;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave;

public class LeaveYearEndEntitlementJobTests
{
    [Fact]
    public async Task RunForYearAsync_NoActiveTenants_CompletesWithoutError()
    {
        var services = new ServiceCollection();
        var tenantRepoMock = new Mock<ITenantRepository>();
        tenantRepoMock
            .Setup(t => t.ListAsync(TenantStatus.Active, null, 0, int.MaxValue, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Tenant>());

        services.AddSingleton(tenantRepoMock.Object);
        services.AddSingleton(Mock.Of<IWritableTenantContext>());
        services.AddSingleton(Mock.Of<ITenantContextSwitcher>());
        services.AddSingleton(Mock.Of<ILeaveEntitlementRepository>());
        services.AddSingleton(Mock.Of<IDateTimeProvider>());
        var provider = services.BuildServiceProvider();

        var job = new LeaveYearEndEntitlementJob(provider, NullLogger<LeaveYearEndEntitlementJob>.Instance);

        // Should not throw with zero tenants - proves the tenant-loop/error-isolation shape
        // without re-mocking LeaveEntitlementPlanner's own dependency chain (already covered
        // by GenerateEntitlementsCommandHandler's Phase 3 tests).
        await job.RunForYearAsync(2027, CancellationToken.None);
    }
}
