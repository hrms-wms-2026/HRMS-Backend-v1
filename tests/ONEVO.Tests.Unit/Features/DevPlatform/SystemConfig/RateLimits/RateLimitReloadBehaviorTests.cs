using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.Commands.SetGlobalRateLimit;
using ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.DTOs;
using ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.Helpers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.Services;
using Xunit;

namespace ONEVO.Tests.Unit.Features.DevPlatform.SystemConfig.RateLimits;

public sealed class RateLimitReloadBehaviorTests
{
    private readonly RateLimitReloadSignal _signal = new();
    private readonly IRateLimitSettingsInvalidator _invalidator = Substitute.For<IRateLimitSettingsInvalidator>();

    private static readonly SetGlobalRateLimitCommand Command =
        new(RateLimitName.SignedIn, 900, 150, "Customer import week, approved by ops.", null, "corr-1");

    private static readonly Result<IReadOnlyList<GlobalRateLimitDto>> Ok =
        Result<IReadOnlyList<GlobalRateLimitDto>>.Success([]);

    private RateLimitReloadBehavior<TRequest, TResponse> Behavior<TRequest, TResponse>() where TRequest : notnull =>
        new(_signal, _invalidator, NullLogger<RateLimitReloadBehavior<TRequest, TResponse>>.Instance);

    [Fact]
    public async Task Reloads_after_the_inner_pipeline_returns_when_a_change_was_saved()
    {
        // The inner pipeline stands for AuditingBehavior + handler: its transaction has committed when it returns.
        var innerReturned = false;
        var innerReturnedAtReload = false;
        _invalidator.When(i => i.InvalidateAsync(Arg.Any<CancellationToken>())).Do(_ => innerReturnedAtReload = innerReturned);

        var response = await Behavior<SetGlobalRateLimitCommand, Result<IReadOnlyList<GlobalRateLimitDto>>>()
            .Handle(Command, _ =>
            {
                _signal.Request();
                innerReturned = true;
                return Task.FromResult(Ok);
            }, default);

        response.Should().BeSameAs(Ok);
        await _invalidator.Received(1).InvalidateAsync(CancellationToken.None);
        innerReturnedAtReload.Should().BeTrue();
    }

    [Fact]
    public async Task Does_not_reload_when_nothing_was_saved()
    {
        await Behavior<SetGlobalRateLimitCommand, Result<IReadOnlyList<GlobalRateLimitDto>>>()
            .Handle(Command, _ => Task.FromResult(Ok), default);

        await _invalidator.DidNotReceive().InvalidateAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_failed_reload_still_returns_the_response()
    {
        _invalidator.InvalidateAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("db down"));

        var response = await Behavior<SetGlobalRateLimitCommand, Result<IReadOnlyList<GlobalRateLimitDto>>>()
            .Handle(Command, _ => { _signal.Request(); return Task.FromResult(Ok); }, default);

        response.Should().BeSameAs(Ok);
    }

    [Fact]
    public async Task Other_requests_pass_through_without_a_reload()
    {
        _signal.Request();

        var response = await Behavior<OtherRequest, string>().Handle(new OtherRequest(), _ => Task.FromResult("done"), default);

        response.Should().Be("done");
        await _invalidator.DidNotReceive().InvalidateAsync(Arg.Any<CancellationToken>());
    }

    public sealed record OtherRequest : IRequest<string>;
}
