using Microsoft.AspNetCore.Http;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Infrastructure.Identity.CurrentUser;
using Xunit;

namespace ONEVO.Tests.Unit.Infrastructure.Identity.CurrentUser;

public sealed class CurrentUserServiceTests
{
    [Fact]
    public void TenantId_FallsBackToTenantContext_WhenNoHttpContext()
    {
        var httpContextAccessor = new Mock<IHttpContextAccessor>();
        httpContextAccessor.Setup(a => a.HttpContext).Returns((HttpContext?)null);
        var tenantContext = new Mock<ITenantContext>();
        var expectedTenantId = Guid.NewGuid();
        tenantContext.Setup(t => t.TenantId).Returns(expectedTenantId);

        var sut = new ONEVO.Infrastructure.Identity.CurrentUser.CurrentUserService(
            httpContextAccessor.Object, tenantContext.Object);

        Assert.Equal(expectedTenantId, sut.TenantId);
    }

    [Fact]
    public void TenantId_PrefersHttpContextClaim_WhenHttpContextPresent()
    {
        var claimTenantId = Guid.NewGuid();
        var httpContext = new DefaultHttpContext();
        httpContext.User = new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity(new[]
            {
                new System.Security.Claims.Claim("tenant_id", claimTenantId.ToString())
            }));
        var httpContextAccessor = new Mock<IHttpContextAccessor>();
        httpContextAccessor.Setup(a => a.HttpContext).Returns(httpContext);
        var tenantContext = new Mock<ITenantContext>();
        tenantContext.Setup(t => t.TenantId).Returns(Guid.NewGuid()); // must be ignored

        var sut = new ONEVO.Infrastructure.Identity.CurrentUser.CurrentUserService(
            httpContextAccessor.Object, tenantContext.Object);

        Assert.Equal(claimTenantId, sut.TenantId);
    }

    [Fact]
    public void TenantId_FallsBackThroughRealTenantContextAccessor_AfterSwitchToTenant()
    {
        // Proves the actual production wiring this fix exists for: a background job calls
        // ITenantContextSwitcher.SwitchToTenantAsync, which calls IWritableTenantContext.Resolve(...)
        // on TenantContextAccessor - the same object also registered as ITenantContext. This test
        // uses the real TenantContextAccessor (not a mock of the interface) so a change to that
        // class's Resolve()/TenantId wiring would break this test too, not just an interface stub.
        var httpContextAccessor = new Mock<IHttpContextAccessor>();
        httpContextAccessor.Setup(a => a.HttpContext).Returns((HttpContext?)null);
        var tenantContextAccessor = new ONEVO.Infrastructure.Identity.Tenancy.TenantContextAccessor();
        var tenantId = Guid.NewGuid();
        tenantContextAccessor.Resolve(new ONEVO.Application.Common.ServiceInterfaces.TenantRegistryEntry(
            tenantId, "acme", ONEVO.Domain.Features.InfrastructureModule.Entities.TenantStatus.Active, PlanCode: null));

        var sut = new ONEVO.Infrastructure.Identity.CurrentUser.CurrentUserService(
            httpContextAccessor.Object, tenantContextAccessor);

        Assert.Equal(tenantId, sut.TenantId);
    }
}
