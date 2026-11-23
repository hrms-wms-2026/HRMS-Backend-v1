using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using ONEVO.Api.Controllers.Tenant.Auth;
using ONEVO.Application.Common.Tenancy;

namespace ONEVO.Tests.Unit.Features.Auth;

public sealed class TenantHintCookieWriterTests
{
    private sealed class TestController : ControllerBase { }

    private static (TestController Controller, Mock<IWebHostEnvironment> Env) Make()
    {
        var controller = new TestController
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        var env = new Mock<IWebHostEnvironment>();
        env.SetupGet(e => e.EnvironmentName).Returns("Production");
        return (controller, env);
    }

    private static string SetCookie(ControllerBase c) => c.Response.Headers["Set-Cookie"].ToString().ToLowerInvariant();

    [Fact]
    public void SingleHost_SetsHostOnlyCookie()
    {
        var (c, env) = Make();
        c.SetLastTenantHintCookie("acme", "app.example.net", env.Object, TenancyMode.SingleHost);
        var h = SetCookie(c);
        h.Should().Contain("crownus_last_tenant=acme");
        h.Should().NotContain("domain=");
    }

    [Fact]
    public void Subdomain_SetsParentDomainCookie()
    {
        var (c, env) = Make();
        c.SetLastTenantHintCookie("acme", "app.example.net", env.Object, TenancyMode.Subdomain);
        SetCookie(c).Should().Contain("domain=.app.example.net");
    }

    [Fact]
    public void SingleHost_ClearIsHostOnly_Subdomain_ClearHasDomain()
    {
        var (c, env) = Make();
        c.ClearLastTenantHintCookie("app.example.net", env.Object, TenancyMode.SingleHost);
        SetCookie(c).Should().NotContain("domain=");

        var (c2, env2) = Make();
        c2.ClearLastTenantHintCookie("app.example.net", env2.Object, TenancyMode.Subdomain);
        SetCookie(c2).Should().Contain("domain=.app.example.net");
    }
}
