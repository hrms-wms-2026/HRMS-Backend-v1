using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using ONEVO.Api.Logging;
using ONEVO.Application.Common.Configuration;
using ONEVO.Application.Common.ServiceInterfaces;
using Serilog.Events;
using Xunit;

namespace ONEVO.Tests.Unit.Api.Logging;

public class RequestLoggingTests
{
    private sealed class FakeSqlMetrics : IRequestSqlMetrics
    {
        public int CommandCount { get; init; }
        public TimeSpan TotalElapsed { get; init; }
    }

    private static DefaultHttpContext CreateContext(
        IRequestSqlMetrics? sqlMetrics = null,
        ITenantContext? tenantContext = null,
        int thresholdMs = 200)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IOptions<PerformanceOptions>>(
            Options.Create(new PerformanceOptions { SlowRequestThresholdMs = thresholdMs }));
        if (sqlMetrics is not null) services.AddSingleton(sqlMetrics);
        if (tenantContext is not null) services.AddSingleton(tenantContext);
        return new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
    }

    private static Dictionary<string, object> Enrich(HttpContext ctx)
    {
        var values = new Dictionary<string, object>();
        RequestLogging.Enrich((name, value) => values[name] = value, ctx);
        return values;
    }

    [Fact]
    public void Route_template_is_logged_instead_of_the_raw_url()
    {
        var ctx = CreateContext();
        ctx.Request.Path = "/api/v1/employees/3f2a";
        ctx.SetEndpoint(new RouteEndpoint(
            _ => Task.CompletedTask,
            RoutePatternFactory.Parse("api/v{version:apiVersion}/employees/{id:guid}"),
            0, EndpointMetadataCollection.Empty, "employees"));

        Enrich(ctx)["RouteTemplate"].Should().Be("api/v{version:apiVersion}/employees/{id:guid}");
    }

    [Fact]
    public void Request_without_a_matched_route_logs_a_fixed_placeholder()
    {
        var ctx = CreateContext();
        ctx.Request.Path = "/no/such/thing";

        Enrich(ctx)["RouteTemplate"].Should().Be(RequestLogging.UnmatchedRoute);
    }

    [Fact]
    public void Sql_count_and_time_come_from_the_request_metrics()
    {
        var ctx = CreateContext(new FakeSqlMetrics
        {
            CommandCount = 7,
            TotalElapsed = TimeSpan.FromMilliseconds(42.5),
        });

        var values = Enrich(ctx);

        values["SqlCommandCount"].Should().Be(7);
        values["SqlElapsedMs"].Should().Be(42.5);
    }

    [Fact]
    public void Request_without_sql_metrics_logs_zero_sql()
    {
        var values = Enrich(CreateContext());

        values["SqlCommandCount"].Should().Be(0);
        values["SqlElapsedMs"].Should().Be(0d);
    }

    [Fact]
    public void Tenant_and_user_come_from_claims()
    {
        var tenantId = Guid.NewGuid().ToString();
        var userId = Guid.NewGuid().ToString();
        var ctx = CreateContext();
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("tenant_id", tenantId), new Claim(ClaimTypes.NameIdentifier, userId)], "test"));

        var values = Enrich(ctx);

        values["TenantId"].Should().Be(tenantId);
        values["UserId"].Should().Be(userId);
    }

    [Fact]
    public void Anonymous_request_falls_back_to_the_host_resolved_tenant()
    {
        var tenantId = Guid.NewGuid();
        var tenant = new Mock<ITenantContext>();
        tenant.SetupGet(t => t.IsResolved).Returns(true);
        tenant.SetupGet(t => t.TenantId).Returns(tenantId);

        var values = Enrich(CreateContext(tenantContext: tenant.Object));

        values["TenantId"].Should().Be(tenantId.ToString());
        values["UserId"].Should().Be("anon");
    }

    [Fact]
    public void Anonymous_request_without_a_tenant_logs_anon()
    {
        var values = Enrich(CreateContext());

        values["TenantId"].Should().Be("anon");
        values["UserId"].Should().Be("anon");
    }

    [Theory]
    [InlineData(199.9, 200, LogEventLevel.Information)]
    [InlineData(200.0, 200, LogEventLevel.Information)]
    [InlineData(200.1, 200, LogEventLevel.Warning)]
    [InlineData(150.0, 100, LogEventLevel.Warning)]
    public void Level_is_warning_only_above_the_slow_threshold(double elapsedMs, int thresholdMs, LogEventLevel expected)
    {
        var ctx = CreateContext(thresholdMs: thresholdMs);
        ctx.Response.StatusCode = 200;

        RequestLogging.GetLevel(ctx, elapsedMs, null).Should().Be(expected);
    }

    [Fact]
    public void Server_errors_and_exceptions_are_errors_even_when_fast()
    {
        var ctx = CreateContext();
        ctx.Response.StatusCode = 500;
        RequestLogging.GetLevel(ctx, 5, null).Should().Be(LogEventLevel.Error);

        ctx.Response.StatusCode = 200;
        RequestLogging.GetLevel(ctx, 5, new InvalidOperationException()).Should().Be(LogEventLevel.Error);
    }

    [Fact]
    public void Message_template_names_route_status_time_and_sql()
    {
        RequestLogging.MessageTemplate.Should().Contain("{RouteTemplate}")
            .And.Contain("{StatusCode}")
            .And.Contain("{Elapsed")
            .And.Contain("{SqlCommandCount}")
            .And.Contain("{SqlElapsedMs");
    }
}
