using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ONEVO.Api.Middleware;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
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
        => RequestDbConnectionMiddleware.AppliesTo(Request(path, "application/json")).Should().Be(expected);

    [Theory]
    [InlineData("multipart/form-data; boundary=x")]
    [InlineData("application/octet-stream")]
    public void Uploads_do_not_hold_a_connection_while_the_body_arrives(string contentType)
        => RequestDbConnectionMiddleware.AppliesTo(Request("/api/v1/files", contentType)).Should().BeFalse();

    [Fact]
    public async Task A_failed_open_does_not_fail_the_request_so_EF_opens_lazily_with_its_retry()
    {
        // Port 1 refuses at once: the open fails the way it would during a failover.
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer("Server=tcp:127.0.0.1,1;Database=x;User Id=x;Password=x;Connect Timeout=1;TrustServerCertificate=True")
            .Options;
        var clock = new Mock<IDateTimeProvider>().Object;
        await using var db = new ApplicationDbContext(options,
            new AuditableEntityInterceptor(new Mock<ICurrentUser>().Object, clock), new SoftDeleteInterceptor(clock),
            new DomainEventDispatchInterceptor(new Mock<IPublisher>().Object), new Mock<ITenantContext>().Object);
        var nextRan = false;
        var middleware = new RequestDbConnectionMiddleware(_ => { nextRan = true; return Task.CompletedTask; },
            NullLogger<RequestDbConnectionMiddleware>.Instance);
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/auth/me";

        await middleware.InvokeAsync(context, db);

        nextRan.Should().BeTrue();
    }

    private static HttpRequest Request(string path, string contentType)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Request.ContentType = contentType;
        return context.Request;
    }
}
