using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using ONEVO.Api.Controllers.Admin.DevPlatform.Subscriptions;
using ONEVO.Api.Controllers.Admin.DevPlatform.SystemConfig;
using ONEVO.Api.Controllers.Admin.DevPlatform.Tenants;
using ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.DTOs;

namespace ONEVO.Tests.Unit.Features.DevPlatform.SystemConfig.RateLimits;

/// <summary>Improvements task 47: a limit name outside the six returns 400 and never reaches a handler.</summary>
public sealed class RateLimitControllersTests
{
    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private static readonly SetRateLimitRequest Body = new(600, 100, "Customer import week, approved.", null);

    private T WithContext<T>(T controller) where T : ControllerBase
    {
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        controller.ProblemDetailsFactory = new TestProblemDetailsFactory();
        return controller;
    }

    public static TheoryData<string> UnknownNames() => new()
    {
        "login", "Enabled", "PreAuthentication", "mfa", "password-reset", "7", "SignedIn2",
    };

    [Theory]
    [MemberData(nameof(UnknownNames))]
    public async Task Unknown_limit_names_return_400_on_every_write_route(string limitName)
    {
        var global = WithContext(new RateLimitsController(_mediator));
        var plan = WithContext(new SubscriptionPlanRateLimitsController(_mediator));
        var tenant = WithContext(new AdminTenantRateLimitsController(_mediator));

        var results = new[]
        {
            await global.Set(limitName, Body, default),
            await global.Remove(limitName, null, "Customer import week, approved.", null, default),
            await plan.Set(Guid.NewGuid(), limitName, Body, default),
            await plan.Remove(Guid.NewGuid(), limitName, null, "Customer import week, approved.", null, default),
            await tenant.Set(Guid.NewGuid(), limitName, Body, default),
            await tenant.Remove(Guid.NewGuid(), limitName, null, "Customer import week, approved.", null, default),
        };

        results.Should().AllSatisfy(r => r.Should().BeOfType<ObjectResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest));
        _mediator.ReceivedCalls().Should().BeEmpty();
    }

    private sealed class TestProblemDetailsFactory : Microsoft.AspNetCore.Mvc.Infrastructure.ProblemDetailsFactory
    {
        public override ProblemDetails CreateProblemDetails(
            HttpContext httpContext, int? statusCode = null, string? title = null, string? type = null,
            string? detail = null, string? instance = null) =>
            new() { Status = statusCode, Title = title, Detail = detail };

        public override ValidationProblemDetails CreateValidationProblemDetails(
            HttpContext httpContext, Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary modelStateDictionary,
            int? statusCode = null, string? title = null, string? type = null, string? detail = null, string? instance = null) =>
            new(modelStateDictionary) { Status = statusCode };
    }
}
