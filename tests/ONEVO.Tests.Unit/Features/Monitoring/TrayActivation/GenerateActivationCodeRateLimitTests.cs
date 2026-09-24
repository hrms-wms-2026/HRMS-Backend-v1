using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.TrayActivation.Commands.GenerateActivationCode;
using ONEVO.Application.Features.Monitoring.TrayActivation.RepositoryInterfaces;

namespace ONEVO.Tests.Unit.Features.Monitoring.TrayActivation;

public sealed class GenerateActivationCodeRateLimitTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly DateTimeOffset _now = new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);

    private (GenerateActivationCodeCommandHandler Handler, Mock<ITrayActivationRepository> Repo) Build(int recentCodes)
    {
        var repo = new Mock<ITrayActivationRepository>();
        repo.Setup(r => r.CountRecentCodesForUserAsync(_userId, _tenantId, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(recentCodes);

        var user = new Mock<ICurrentUser>();
        user.Setup(u => u.UserId).Returns(_userId);
        user.Setup(u => u.TenantId).Returns(_tenantId);
        user.Setup(u => u.LegalEntityId).Returns(Guid.NewGuid());

        var clock = new Mock<IDateTimeProvider>();
        clock.Setup(c => c.UtcNow).Returns(_now);

        return (new GenerateActivationCodeCommandHandler(repo.Object, user.Object, clock.Object, new Mock<IUnitOfWork>().Object), repo);
    }

    [Fact]
    public async Task CountsCodesFromTheLastTenMinutesOnly()
    {
        var (handler, repo) = Build(recentCodes: 0);

        await handler.Handle(new GenerateActivationCodeCommand(), CancellationToken.None);

        repo.Verify(r => r.CountRecentCodesForUserAsync(
            _userId, _tenantId, _now.AddMinutes(-10), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public async Task Allows_UpToFiveCodesPerWindow(int alreadyCreated)
    {
        var (handler, repo) = Build(alreadyCreated);

        var result = await handler.Handle(new GenerateActivationCodeCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        repo.Verify(r => r.AddActivationCodeAsync(It.IsAny<ONEVO.Domain.Features.Monitoring.TrayActivation.Entities.TrayActivationCode>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Rejects_TheSixthCodeInTheWindow_With429()
    {
        var (handler, repo) = Build(recentCodes: 5);

        var result = await handler.Handle(new GenerateActivationCodeCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(429, result.StatusCode);
        repo.Verify(r => r.AddActivationCodeAsync(It.IsAny<ONEVO.Domain.Features.Monitoring.TrayActivation.Entities.TrayActivationCode>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
