using Microsoft.Extensions.Configuration;
using Moq;
using ONEVO.Application.Features.AgentGateway.Commands.StartEnrollment;
using ONEVO.Application.Features.AgentGateway.RepositoryInterfaces;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Domain.Features.AgentGateway.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.AgentGateway;

public class StartEnrollmentCommandHandlerTests
{
    private readonly Mock<IAgentGatewayRepository> _repo = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    private StartEnrollmentCommandHandler CreateHandler(string appBaseUrl = "https://app.onevo.io")
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Urls:AppBaseUrl"] = appBaseUrl
            })
            .Build();
        return new StartEnrollmentCommandHandler(_repo.Object, _uow.Object, config);
    }

    [Fact]
    public async Task Handle_ValidDeviceInfo_ReturnsEnrollmentIdAndAuthUrl()
    {
        AgentEnrollmentChallenge? saved = null;
        _repo.Setup(r => r.AddChallengeAsync(It.IsAny<AgentEnrollmentChallenge>(), It.IsAny<CancellationToken>()))
             .Callback<AgentEnrollmentChallenge, CancellationToken>((c, _) => saved = c)
             .Returns(Task.CompletedTask);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = CreateHandler();
        var result = await handler.Handle(
            new StartEnrollmentCommand("device-uuid-v7", "DESKTOP-ABC123", "Windows 11 23H2", "1.0.0"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.NotEqual(Guid.Empty, result.Value!.EnrollmentId);
        Assert.Contains("enrollment_id=", result.Value.AuthUrl);
        Assert.NotNull(saved);
        Assert.Equal("pending", saved!.Status);
        Assert.True(result.Value.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(8));
        Assert.True(result.Value.ExpiresAt < DateTimeOffset.UtcNow.AddMinutes(12));
    }

    [Fact]
    public async Task Handle_EmptyDeviceId_ReturnsFailure()
    {
        var handler = CreateHandler();
        var result = await handler.Handle(
            new StartEnrollmentCommand("", "NAME", "Win11", "1.0.0"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }
}
