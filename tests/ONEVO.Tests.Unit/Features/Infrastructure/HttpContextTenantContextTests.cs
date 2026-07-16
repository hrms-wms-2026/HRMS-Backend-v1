using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Moq;
using ONEVO.Infrastructure.Identity;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Infrastructure;

public class HttpContextTenantContextTests
{
    [Fact]
    public void TenantId_WhenClaimPresent_ReturnsParsedGuid()
    {
        var expected = Guid.NewGuid();
        var mockAccessor = new Mock<IHttpContextAccessor>();
        var ctx = new DefaultHttpContext();
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("tenant_id", expected.ToString())
        }));
        mockAccessor.Setup(a => a.HttpContext).Returns(ctx);

        var sut = new HttpContextTenantContext(mockAccessor.Object);

        sut.TenantId.Should().Be(expected);
    }

    [Fact]
    public void TenantId_WhenClaimMissing_ThrowsInvalidOperationException()
    {
        var mockAccessor = new Mock<IHttpContextAccessor>();
        mockAccessor.Setup(a => a.HttpContext).Returns(new DefaultHttpContext());

        var sut = new HttpContextTenantContext(mockAccessor.Object);

        var act = () => sut.TenantId;
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*tenant_id*");
    }
}
