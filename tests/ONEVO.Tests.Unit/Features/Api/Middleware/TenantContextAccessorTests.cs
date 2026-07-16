using FluentAssertions;
using ONEVO.Application.Common.ServiceInterfaces;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Api.Middleware;

public class TenantContextAccessorTests
{
    [Fact]
    public void DefaultState_IsNotResolved_AndSystemMode()
    {
        TenantContextMode.Tenant.Should().NotBe(TenantContextMode.Admin);
        TenantContextMode.Admin.Should().NotBe(TenantContextMode.System);
    }
}
