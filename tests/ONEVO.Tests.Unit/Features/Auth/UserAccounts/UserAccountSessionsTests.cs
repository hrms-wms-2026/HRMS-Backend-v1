using FluentAssertions;
using NSubstitute;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.UserAccounts;
using ONEVO.Application.Features.Auth.UserAccounts.Commands.RevokeUserAccountSession;
using ONEVO.Application.Features.Auth.UserAccounts.Queries.GetUserAccountSessions;
using ONEVO.Application.Features.Auth.UserAccounts.RepositoryInterfaces;

namespace ONEVO.Tests.Unit.Features.Auth.UserAccounts;

public class UserAccountSessionsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Admin = Guid.NewGuid();
    private static readonly Guid AdminSession = Guid.NewGuid();
    private static readonly Guid Target = Guid.NewGuid();

    private readonly IUserAccountRepository _accounts = Substitute.For<IUserAccountRepository>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();

    public UserAccountSessionsTests()
    {
        _currentUser.TenantId.Returns(TenantId);
        _currentUser.UserId.Returns(Admin);
        _currentUser.SessionId.Returns(AdminSession);
        _clock.UtcNow.Returns(Now);
        foreach (var id in new[] { Admin, Target })
        {
            _accounts.GetAsync(TenantId, id, Now, Arg.Any<CancellationToken>()).Returns(new UserAccountRow(
                id, "A", "B", "a@b.test", true, null, null, Now, null, 1, [], false, null, null, null, 2, 1));
        }
    }

    private RevokeUserAccountSessionCommandHandler Revoke() => new(_accounts, _currentUser, _clock);

    [Fact]
    public async Task RevokingOneSession_LeavesTheOthersAlone()
    {
        var sessionId = Guid.NewGuid();
        _accounts.RevokeSessionAsync(TenantId, Target, sessionId, Arg.Any<CancellationToken>()).Returns(true);

        var result = await Revoke().Handle(new RevokeUserAccountSessionCommand(Target, sessionId, null), default);

        result.IsSuccess.Should().BeTrue();
        await _accounts.Received(1).RevokeSessionAsync(TenantId, Target, sessionId, Arg.Any<CancellationToken>());
        await _accounts.DidNotReceiveWithAnyArgs().RevokeAllAccessAsync(default, default, default, default!, default);
        await _accounts.DidNotReceiveWithAnyArgs().RevokeDeviceAsync(default, default, default, default, default!, default);
    }

    [Fact]
    public async Task RevokingOneTrayDevice_DisconnectsOnlyThatDevice()
    {
        var deviceId = Guid.NewGuid();
        _accounts.RevokeDeviceAsync(TenantId, Target, deviceId, Now, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);

        var result = await Revoke().Handle(new RevokeUserAccountSessionCommand(Target, null, deviceId), default);

        result.IsSuccess.Should().BeTrue();
        await _accounts.DidNotReceiveWithAnyArgs().RevokeSessionAsync(default, default, default, default);
    }

    [Fact]
    public async Task YourOtherSessions_CanBeRevoked_ButNotTheOneYouAreUsing()
    {
        var otherSession = Guid.NewGuid();
        _accounts.RevokeSessionAsync(TenantId, Admin, otherSession, Arg.Any<CancellationToken>()).Returns(true);

        (await Revoke().Handle(new RevokeUserAccountSessionCommand(Admin, otherSession, null), default)).IsSuccess.Should().BeTrue();

        var current = await Revoke().Handle(new RevokeUserAccountSessionCommand(Admin, AdminSession, null), default);
        current.IsSuccess.Should().BeFalse();
        current.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task AlreadyEndedSession_IsNotFound()
    {
        var result = await Revoke().Handle(new RevokeUserAccountSessionCommand(Target, Guid.NewGuid(), null), default);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Sessions_MarkTheCallersCurrentSession_AndDescribeTheBrowser()
    {
        _accounts.ListActiveSessionsAsync(TenantId, Admin, Now, Arg.Any<CancellationToken>()).Returns(
        [
            new UserWebSessionRow(AdminSession, "10.0.0.1",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/129.0 Safari/537.36",
                Now.AddHours(-2), Now, Now.AddDays(30)),
            new UserWebSessionRow(Guid.NewGuid(), "10.0.0.2",
                "Mozilla/5.0 (Macintosh; Intel Mac OS X 14_0) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.0 Safari/605.1.15",
                Now.AddDays(-1), Now.AddHours(-3), Now.AddDays(29)),
        ]);
        _accounts.ListActiveDevicesAsync(TenantId, Admin, Arg.Any<CancellationToken>())
            .Returns([new UserTrayDeviceRow(Guid.NewGuid(), "DAPI-LAPTOP", "Windows 11", Now.AddDays(-5), Now)]);

        var result = await new GetUserAccountSessionsQueryHandler(_accounts, _currentUser, _clock)
            .Handle(new GetUserAccountSessionsQuery(Admin), default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.WebSessions.Select(s => (s.Device, s.IsCurrent))
            .Should().Equal(("Chrome on Windows", true), ("Safari on macOS", false));
        result.Value.TrayDevices.Should().ContainSingle(d => d.DeviceName == "DAPI-LAPTOP");
    }

    [Theory]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/129.0 Safari/537.36 Edg/129.0", "Edge on Windows")]
    [InlineData("Mozilla/5.0 (X11; Linux x86_64; rv:131.0) Gecko/20100101 Firefox/131.0", "Firefox on Linux")]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) CriOS/129.0 Mobile/15E148 Safari/604.1", "Chrome on iOS")]
    [InlineData("", "Unknown browser")]
    public void UserAgent_IsDescribedReadably(string userAgent, string expected) =>
        UserAgentDescription.Describe(userAgent).Should().Be(expected);
}
