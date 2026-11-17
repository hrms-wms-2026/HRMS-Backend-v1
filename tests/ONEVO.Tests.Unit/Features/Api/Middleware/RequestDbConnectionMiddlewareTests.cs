using FluentAssertions;
using Microsoft.AspNetCore.Http;
using ONEVO.Api.Middleware;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Api.Middleware;

public class RequestDbConnectionMiddlewareTests
{
    [Theory]
    [InlineData("/api/v1/inbox/counts", true)]
    [InlineData("/API/v1/employees/me", true)]
    [InlineData("/admin/v1/tenants", true)]
    [InlineData("/hubs/inbox/negotiate", false)]
    [InlineData("/health/ready", false)]
    [InlineData("/swagger/index.html", false)]
    [InlineData("/apidocs", false)]
    public void Keeps_a_connection_open_only_for_api_and_admin_requests(string path, bool expected)
        => RequestDbConnectionMiddleware.AppliesTo(new PathString(path)).Should().Be(expected);
}
